using UnityEngine;
using VMS.Rendering;

namespace VMS.Race
{
    /// <summary>The player's mount: procedural mesh, glow trims, engine trails, banking and hover bob.</summary>
    public sealed class VesselRig : MonoBehaviour
    {
        Transform _model;
        Mesh _body, _glow;
        Material _trailMaterial;
        TrailRenderer[] _trails;
        VesselDef _def;
        ExhaustStyle _exhaust;
        float _bank;
        float _emberClock;

        public void Build(VesselDef def, ExhaustStyle exhaust = null)
        {
            _def = def;
            _exhaust = exhaust ?? ExhaustStyle.From(null, def);
            var result = VesselFactory.Build(def);
            _body = result.body;
            _glow = result.glow;

            _model = new GameObject("Model").transform;
            _model.SetParent(transform, false);
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(_model, false);
            bodyGo.AddComponent<MeshFilter>().sharedMesh = _body;
            var br = bodyGo.AddComponent<MeshRenderer>();
            br.sharedMaterial = VmsGraphics.Opaque;
            br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(_model, false);
            glowGo.AddComponent<MeshFilter>().sharedMesh = _glow;
            var gr = glowGo.AddComponent<MeshRenderer>();
            gr.sharedMaterial = VmsGraphics.Fx;
            gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _trailMaterial = VmsGraphics.NewAdditive(ProceduralTextures.GlowStripVertical, "Trail");
            _trails = new TrailRenderer[result.trailPoints.Length];
            for (int i = 0; i < _trails.Length; i++)
            {
                var go = new GameObject("Trail" + i);
                go.transform.SetParent(_model, false);
                go.transform.localPosition = result.trailPoints[i];
                var trail = go.AddComponent<TrailRenderer>();
                trail.sharedMaterial = _trailMaterial;
                trail.time = _exhaust.length;
                trail.minVertexDistance = 0.8f;
                trail.widthMultiplier = _exhaust.width;
                trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                // Additive: fade by darkening RGB (vertex alpha drives the beat pulse in our shader).
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(_exhaust.head, 0f), new GradientColorKey(_exhaust.tail * 0.6f, 0.55f), new GradientColorKey(Color.black, 1f) },
                    new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0f, 1f) });
                trail.colorGradient = gradient;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.numCapVertices = 0;
                _trails[i] = trail;
            }
        }

        public VesselDef Def => _def;
        public ExhaustStyle Exhaust => _exhaust;

        /// <summary>Exhaust embers (shop styles) drift back from the trail points; drawn by the race's spark system.</summary>
        public void EmitEmbers(Sparks sparks, float dt)
        {
            if (_exhaust == null || _exhaust.embers <= 0f || _trails == null || _trails.Length == 0) return;
            _emberClock += dt * _exhaust.embers * _trails.Length;
            while (_emberClock >= 1f)
            {
                _emberClock -= 1f;
                var origin = _trails[Random.Range(0, _trails.Length)].transform;
                var velocity = -transform.forward * Random.Range(3f, 6f) + Random.insideUnitSphere * 1.2f + transform.up * 0.6f;
                sparks.Ember(origin.position, velocity, Color.Lerp(_exhaust.head, _exhaust.tail, Random.value), _exhaust.cell, Random.Range(0.3f, 0.55f));
            }
        }

        /// <summary>Places the vessel on the road at <paramref name="lateral"/> metres from the centre line.</summary>
        public void Place(TrackPath path, float songTime, float lateral, float lateralVelocity, float clock, float dt)
        {
            Vector3 p;
            Quaternion q;
            path.Sample(songTime, out p, out q);
            float hover = 0.35f + 0.08f * Mathf.Sin(clock * 3.1f);
            transform.SetPositionAndRotation(p + q * new Vector3(lateral, hover, 0f), q);

            float targetBank = Mathf.Clamp(-lateralVelocity * 2.2f, -28f, 28f);
            _bank = Mathf.Lerp(_bank, targetBank, 1f - Mathf.Exp(-dt * 14f));
            float yaw = Mathf.Clamp(lateralVelocity * 0.9f, -12f, 12f);
            _model.localRotation = Quaternion.Euler(0f, yaw, _bank);
        }

        /// <summary>Teleports without leaving trail streaks (restart, lead-in).</summary>
        public void ClearTrails()
        {
            if (_trails == null) return;
            foreach (var t in _trails) t.Clear();
        }

        void OnDestroy()
        {
            Destroy(_body);
            Destroy(_glow);
            Destroy(_trailMaterial);
        }
    }
}
