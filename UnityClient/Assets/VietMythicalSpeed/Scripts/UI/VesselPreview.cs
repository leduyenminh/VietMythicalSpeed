using UnityEngine;
using UnityEngine.UI;
using VMS.Race;
using VMS.Rendering;

namespace VMS.UI
{
    /// <summary>
    /// A slowly turning vessel with its kit and exhaust, rendered by a private camera into a texture shown in the
    /// UI (garage, shop previews). Lives far below the world on its own layer; costs nothing once destroyed.
    /// </summary>
    public sealed class VesselPreview : MonoBehaviour
    {
        const int Layer = 31;
        static int _slot;

        Camera _camera;
        RenderTexture _texture;
        Transform _stage, _model;
        FxBatch _fx;
        Mesh _body, _glow;
        Vector3[] _trailPoints = new Vector3[0];
        ExhaustStyle _exhaust;
        float _yaw = 200f, _time;
        // Flat UI / Low / Ultra Low: half-resolution texture redrawn ~15 times a second instead of every frame.
        bool _lowCost;
        float _sinceRender;
        const float LowCostInterval = 1f / 15f;
        static Material _composite, _glowMaterial;

        /// <summary>The render texture holds premultiplied colour; this blends it over the UI behind it.</summary>
        static Material Composite
        {
            get
            {
                if (_composite == null) _composite = new Material(VmsGraphics.Find("VMS/UIPremultiplied")) { name = "VMS Preview Composite" };
                return _composite;
            }
        }

        /// <summary>FX atlas glow that leaves alpha alone, so glows read as light added on top of the backdrop.</summary>
        static Material Glow
        {
            get
            {
                if (_glowMaterial != null) return _glowMaterial;
                _glowMaterial = new Material(VmsGraphics.Find("VMS/AdditiveRgb")) { name = "VMS Preview Glow" };
                _glowMaterial.SetTexture("_MainTex", ProceduralTextures.FxAtlas);
                return _glowMaterial;
            }
        }

        public static VesselPreview Create(RectTransform parent, int width, int height)
        {
            var image = new GameObject("VesselPreview", typeof(RectTransform)).AddComponent<RawImage>();
            image.transform.SetParent(parent, false);
            image.rectTransform.Fill();
            image.raycastTarget = false;
            var preview = image.gameObject.AddComponent<VesselPreview>();
            preview.Setup(image, width, height);
            return preview;
        }

        void Setup(RawImage image, int width, int height)
        {
            var settings = GameApp.Instance != null ? GameApp.Instance.Settings : null;
            _lowCost = UiTheme.Flat || (settings != null && settings.Economy);
            if (_lowCost)
            {
                width = Mathf.Max(64, width / 2);
                height = Mathf.Max(64, height / 2);
            }
            _texture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32) { name = "VesselPreview", antiAliasing = QualitySettings.antiAliasing > 1 ? 2 : 1 };
            image.texture = _texture;
            image.material = Composite;
            _stage = new GameObject("VesselPreviewStage").transform;
            _stage.position = new Vector3(0f, -1000f - 40f * (_slot++ % 8), 0f);
            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(_stage, false);
            camGo.transform.localPosition = new Vector3(0f, 3.6f, -11f);
            camGo.transform.LookAt(_stage.position + new Vector3(0f, 0.8f, -0.8f));
            _camera = camGo.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.cullingMask = 1 << Layer;
            _camera.fieldOfView = 34f;
            _camera.nearClipPlane = 0.5f;
            _camera.farClipPlane = 40f;
            _camera.allowHDR = false;
            _camera.allowMSAA = _texture.antiAliasing > 1;
            _camera.targetTexture = _texture;
            _camera.depth = -20;
            var fxGo = new GameObject("PreviewFx") { layer = Layer };
            fxGo.transform.SetParent(_stage, false);
            _fx = fxGo.AddComponent<FxBatch>();
            _fx.Init(_camera);
            fxGo.GetComponent<MeshRenderer>().sharedMaterial = Glow;
        }

        public void Show(VesselDef vessel, ExhaustStyle exhaust)
        {
            if (_model != null) Destroy(_model.gameObject);
            if (_body != null) Destroy(_body);
            if (_glow != null) Destroy(_glow);
            var built = VesselFactory.Build(vessel);
            _body = built.body;
            _glow = built.glow;
            _trailPoints = built.trailPoints;
            _exhaust = exhaust ?? ExhaustStyle.From(null, vessel);
            _model = new GameObject("PreviewModel") { layer = Layer }.transform;
            _model.SetParent(_stage, false);
            AddPart("Body", _body, VmsGraphics.Opaque);
            AddPart("Glow", _glow, Glow);
            _model.localRotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        void AddPart(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(_model, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        void LateUpdate()
        {
            if (_model == null) return;
            float dt = Time.unscaledDeltaTime;
            _time += dt;
            _yaw += dt * 22f;
            _model.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            if (_lowCost)
            {
                // Rendering happens after LateUpdate, so this decides whether the camera draws this frame.
                _sinceRender += dt;
                bool draw = _sinceRender >= LowCostInterval;
                if (draw) _sinceRender = 0f;
                _camera.enabled = draw;
                if (!draw) return;
            }
            // Menus leave the race's fog in place; keep the preview clear.
            VmsGraphics.SetFog(Color.clear, 500f, 1000f);

            // FxBatch vertices are in its own (stage-local) space; the stage is unrotated, so directions carry over.
            _fx.Begin();
            Vector3 back = -_model.forward;
            Vector3 right = _model.right;
            foreach (var p in _trailPoints)
            {
                Vector3 origin = _stage.InverseTransformPoint(_model.TransformPoint(p));
                // Exhaust plume: a fading row of glows streaming backwards.
                int puffs = 9;
                float reach = 1.2f + _exhaust.length * 9f;
                for (int i = 0; i < puffs; i++)
                {
                    float k = (i + Mathf.Repeat(_time * 3f, 1f)) / puffs;
                    var c = Color.Lerp(_exhaust.head, _exhaust.tail, k) * (1f - k);
                    _fx.Add(origin + back * reach * k, Mathf.Lerp(_exhaust.width * 2.2f, _exhaust.width * 0.8f, k), c, ProceduralTextures.FxCell.Dot, 0f, 0.6f);
                }
                if (_exhaust.embers <= 0f) continue;
                int embers = Mathf.Clamp(Mathf.RoundToInt(_exhaust.embers * 0.4f), 2, 8);
                for (int i = 0; i < embers; i++)
                {
                    float phase = Mathf.Repeat(_time * 0.8f + i / (float)embers + p.x * 0.37f, 1f);
                    float wobble = Mathf.Sin((phase + i) * 9f) * 0.35f;
                    var pos = origin + back * (0.4f + phase * reach * 1.3f) + right * wobble + Vector3.up * phase * 0.9f;
                    var color = Color.Lerp(_exhaust.head, _exhaust.tail, phase) * (1f - phase * 0.8f);
                    _fx.Add(pos, 0.45f * (1f - phase * 0.5f), color, _exhaust.cell, phase * 4f, 0f);
                }
            }
            _fx.End();
        }

        /// <summary>Development: writes the raw render texture (with alpha) to a PNG.</summary>
        public void SaveTo(string path)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = _texture;
            var tex = new Texture2D(_texture.width, _texture.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, _texture.width, _texture.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
        }

        void OnDestroy()
        {
            if (_stage != null) Destroy(_stage.gameObject);
            if (_body != null) Destroy(_body);
            if (_glow != null) Destroy(_glow);
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
        }
    }
}
