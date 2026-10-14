using UnityEngine;
using VMS.Rendering;

namespace VMS.Race
{
    /// <summary>Sky dome with procedural stars plus a big moon that stays put in the world while the track winds.</summary>
    public sealed class SkyRig : MonoBehaviour
    {
        const float Radius = 900f;

        Transform _camera;
        Material _skyMaterial, _moonMaterial;
        Texture2D _moonTexture;
        Transform _moon;
        Mesh _sphere, _quad;
        Vector3 _moonDirection;

        public void Init(Transform cameraTransform, RaceTheme theme, float starScale = 1f)
        {
            _camera = cameraTransform;
            _skyMaterial = new Material(VmsGraphics.Sky) { name = "Sky (" + theme.id + ")" };
            _skyMaterial.SetColor("_TopColor", theme.skyTop);
            _skyMaterial.SetColor("_HorizonColor", theme.skyHorizon);
            _skyMaterial.SetColor("_BottomColor", theme.skyBottom);
            _skyMaterial.SetColor("_StarColor", theme.stars);
            _skyMaterial.SetFloat("_StarDensity", theme.starDensity * starScale);
            _skyMaterial.SetFloat("_Aurora", theme.aurora);
            if (theme.aurora > 0f)
            {
                _skyMaterial.SetColor("_AuroraA", theme.auroraA);
                _skyMaterial.SetColor("_AuroraB", theme.auroraB);
            }

            _sphere = BuildSphere(24, 12);
            var dome = new GameObject("SkyDome");
            dome.transform.SetParent(transform, false);
            dome.transform.localScale = Vector3.one * Radius;
            dome.AddComponent<MeshFilter>().sharedMesh = _sphere;
            var r = dome.AddComponent<MeshRenderer>();
            r.sharedMaterial = _skyMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _moonTexture = ProceduralTextures.Moon(theme.moonInner, theme.moonOuter, theme.moonBands);
            _moonMaterial = VmsGraphics.NewAdditive(_moonTexture, "Moon");
            _quad = BuildQuad();
            var moon = new GameObject("Moon");
            moon.transform.SetParent(transform, false);
            moon.AddComponent<MeshFilter>().sharedMesh = _quad;
            var mr = moon.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _moonMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _moon = moon.transform;
            _moonDirection = new Vector3(0.25f, 0.22f, 1f).normalized;

            VmsGraphics.SetFog(theme.fog, 70f, 280f);
        }

        void LateUpdate()
        {
            if (_camera == null) return;
            transform.position = _camera.position;
            _moon.position = _camera.position + _moonDirection * (Radius * 0.8f);
            _moon.rotation = Quaternion.LookRotation(_moonDirection);
            _moon.localScale = Vector3.one * 240f;
        }

        static Mesh BuildSphere(int segments, int rings)
        {
            var mb = new MeshBuilder(1);
            for (int r = 0; r <= rings; r++)
            {
                float v = r / (float)rings;
                float lat = Mathf.Lerp(-Mathf.PI / 2f, Mathf.PI / 2f, v);
                for (int s = 0; s <= segments; s++)
                {
                    float lon = s / (float)segments * Mathf.PI * 2f;
                    var p = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                    mb.AddVertex(p, Color.white, new Vector2(s / (float)segments, v));
                }
            }
            int row = segments + 1;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    int a = r * row + s, b = a + row;
                    mb.AddTriangle(a, b, a + 1);
                    mb.AddTriangle(a + 1, b, b + 1);
                }
            var mesh = mb.ToMesh("SkySphere");
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return mesh;
        }

        static Mesh BuildQuad()
        {
            var mb = new MeshBuilder(1);
            Color32 white = new Color32(255, 255, 255, 0);
            mb.AddQuad(new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), white);
            return mb.ToMesh("Quad");
        }

        void OnDestroy()
        {
            Destroy(_skyMaterial);
            Destroy(_moonMaterial);
            Destroy(_moonTexture);
            Destroy(_sphere);
            Destroy(_quad);
        }
    }

    /// <summary>
    /// Weather of a realm drifting past the camera (blossom petals, snow, fog motes, goblin-fire sparks, low clouds):
    /// a fixed set of particles in a box around the camera that wraps as the vessel moves, so it costs no state.
    /// </summary>
    public sealed class AmbientFx
    {
        readonly string _kind;
        readonly Color _color;
        readonly int _count;

        public AmbientFx(RaceTheme theme, bool minimal)
        {
            _kind = theme.ambient ?? string.Empty;
            _color = theme.ambientColor;
            int full = _kind == "clouds" ? 10 : 36;
            _count = _kind.Length == 0 ? 0 : (minimal ? full / 3 : full);
        }

        static float Hash(int i) => Mathf.Repeat(Mathf.Sin(i * 12.9898f) * 43758.5453f, 1f);

        public void Draw(FxBatch fx, Transform camera, float time)
        {
            if (_count == 0 || camera == null) return;
            Vector3 fwd = camera.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 origin = camera.position;
            bool clouds = _kind == "clouds";
            for (int i = 0; i < _count; i++)
            {
                float h1 = Hash(i * 3 + 1), h2 = Hash(i * 3 + 2), h3 = Hash(i * 3 + 3);
                // Streams towards the camera (the vessel overtakes it) and wraps back to the far end.
                float depth = clouds ? 140f : 60f;
                float z = depth - Mathf.Repeat(time * (clouds ? 34f : 22f) + h1 * depth, depth) + 4f;
                float x = (h2 - 0.5f) * (clouds ? 120f : 44f);
                float y, size;
                ProceduralTextures.FxCell cell;
                float spin = 0f, alpha = 1f;
                switch (_kind)
                {
                    case "petals":
                        y = 16f - Mathf.Repeat(time * 1.3f + h3 * 18f, 18f);
                        x += Mathf.Sin(time * 1.7f + i) * 1.2f;
                        size = 0.45f + h3 * 0.3f;
                        cell = ProceduralTextures.FxCell.Petal;
                        spin = time * (1f + h2 * 2f) + i;
                        break;
                    case "snow":
                        y = 16f - Mathf.Repeat(time * 1.6f + h3 * 18f, 18f);
                        x += Mathf.Sin(time * 0.9f + i) * 0.6f;
                        size = 0.22f + h3 * 0.22f;
                        cell = ProceduralTextures.FxCell.Dot;
                        break;
                    case "sparks":
                        y = Mathf.Repeat(time * 1.8f + h3 * 14f, 14f) - 1f;
                        x += Mathf.Sin(time * 2.3f + i) * 0.5f;
                        size = 0.35f + h3 * 0.25f;
                        cell = ProceduralTextures.FxCell.Sparkle;
                        spin = time * 3f + i;
                        alpha = 0.6f + 0.4f * Mathf.Sin(time * 6f + i);
                        break;
                    case "clouds":
                        y = -9f + h3 * 6f;
                        size = 9f + h3 * 7f;
                        cell = ProceduralTextures.FxCell.Dot;
                        alpha = 0.16f;
                        break;
                    default: // motes: fog-lit specks rising slowly
                        y = Mathf.Repeat(time * 0.4f + h3 * 10f, 10f) - 1f;
                        x += Mathf.Sin(time * 0.6f + i) * 0.8f;
                        size = 0.25f + h3 * 0.2f;
                        cell = ProceduralTextures.FxCell.Dot;
                        alpha = 0.45f + 0.35f * Mathf.Sin(time * 2f + i * 1.3f);
                        break;
                }
                // Fade in at the far end and out right in front of the lens.
                float fade = Mathf.Clamp01((depth - z + 4f) / 12f) * Mathf.Clamp01((z - 3f) / 6f);
                var color = _color * (alpha * fade);
                fx.Add(origin + fwd * z + right * x + Vector3.up * y, size, color, cell, spin, 0f);
            }
        }
    }

    /// <summary>8 log-spaced bands from the playing music, smoothed with fast attack and slow release.</summary>
    public sealed class AudioBands
    {
        public const int BandCount = 8;

        readonly float[] _spectrum = new float[256];
        public readonly float[] Bands = new float[BandCount];
        public float Loudness { get; private set; }

        public void Update(AudioSource source, float dt)
        {
            if (source == null || !source.isPlaying)
            {
                for (int b = 0; b < BandCount; b++) Bands[b] = Mathf.Max(0f, Bands[b] - dt * 1.5f);
                Loudness = Mathf.Max(0f, Loudness - dt);
                return;
            }
            source.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);
            float total = 0f;
            for (int b = 0; b < BandCount; b++)
            {
                int lo = 1 << b, hi = Mathf.Min(_spectrum.Length, 2 << b);
                float sum = 0f;
                for (int i = lo; i < hi; i++) sum += _spectrum[i];
                float v = Mathf.Clamp01(Mathf.Sqrt(sum * (1.5f + b * 1.2f)) * 1.6f);
                Bands[b] = v > Bands[b] ? Mathf.Lerp(Bands[b], v, 0.6f) : Mathf.Max(v, Bands[b] - dt * 1.8f);
                total += Bands[b];
            }
            Loudness = Mathf.Lerp(Loudness, total / BandCount, 0.2f);
        }
    }

    /// <summary>Music-reactive light towers along both sides of the road, one dynamic mesh / one draw call.</summary>
    public sealed class SpectrumBars : MonoBehaviour
    {
        const float Slot = 0.25f;
        const float Width = 1.1f;
        const float SegmentHeight = 0.8f;

        readonly MeshBuilder _builder = new MeshBuilder(1);
        Mesh _mesh;
        TrackPath _path;
        RaceTheme _theme;
        int _perSide;

        public void Init(TrackPath path, RaceTheme theme, int perSide)
        {
            _path = path;
            _theme = theme;
            _perSide = perSide;
            _mesh = new Mesh { name = "SpectrumBars" };
            _mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = VmsGraphics.Bars;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static readonly Bounds Everywhere = new Bounds(Vector3.zero, Vector3.one * 1e7f);

        public void Rebuild(float songTime, float[] bands)
        {
            if (_perSide <= 0) return;
            _builder.Clear();
            int startSlot = Mathf.FloorToInt((songTime - 0.3f) / Slot);
            float offset = TrackPath.RoadHalfWidth + 3.4f;
            for (int k = 0; k < _perSide; k++)
            {
                int slot = startSlot + k;
                float ts = slot * Slot;
                Vector3 p;
                Quaternion q;
                _path.Sample(ts, out p, out q);
                Vector3 right = q * Vector3.right, fwd = q * Vector3.forward;
                int band = Mathf.Abs(slot) % (AudioBands.BandCount * 2);
                if (band >= AudioBands.BandCount) band = AudioBands.BandCount * 2 - 1 - band;
                float h = 0.6f + bands[band] * 11f;
                Color color = Color.Lerp(_theme.spectrumLow, _theme.spectrumHigh, band / (float)(AudioBands.BandCount - 1));
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 basePos = p + right * side * offset - Vector3.up * 0.5f;
                    AddBar(basePos, right, fwd, h, color);
                }
            }
            _builder.ApplyTo(_mesh, Everywhere, false);
        }

        void AddBar(Vector3 basePos, Vector3 right, Vector3 fwd, float height, Color color)
        {
            Color32 c = color * 0.85f;
            c.a = 90;
            Vector3 up = Vector3.up * height;
            float v = height / SegmentHeight;
            AddFace(basePos, right * (Width * 0.5f), up, v, c);
            AddFace(basePos, fwd * (Width * 0.5f), up, v, c);
        }

        void AddFace(Vector3 basePos, Vector3 halfWidth, Vector3 up, float v, Color32 c)
        {
            int i = _builder.AddVertex(basePos - halfWidth, c, new Vector2(0f, 0f));
            _builder.AddVertex(basePos - halfWidth + up, c, new Vector2(0f, v));
            _builder.AddVertex(basePos + halfWidth + up, c, new Vector2(1f, v));
            _builder.AddVertex(basePos + halfWidth, c, new Vector2(1f, 0f));
            _builder.AddTriangle(i, i + 1, i + 2);
            _builder.AddTriangle(i, i + 2, i + 3);
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }

    /// <summary>
    /// A glowing serpentine Vietnamese dragon that weaves alongside the road during loud sections,
    /// drawn as ribbon segments into the shared FX batch.
    /// </summary>
    public sealed class SpiritDragon
    {
        readonly int _segments;
        readonly Vector3[] _points;
        float _visibility;

        public SpiritDragon(int segments)
        {
            _segments = Mathf.Max(0, segments);
            _points = new Vector3[_segments + 1];
        }

        public void Draw(FxBatch fx, TrackPath path, RaceTheme theme, float songTime, float energy, float dt)
        {
            if (_segments == 0) return;
            float target = energy > 0.6f ? 1f : 0f;
            _visibility = Mathf.MoveTowards(_visibility, target, dt * 0.5f);
            if (_visibility < 0.01f) return;

            for (int s = 0; s <= _segments; s++)
            {
                float ts = songTime + 2.4f - s * 0.035f;
                float lateral = 14f * Mathf.Sin(ts * 0.85f) + 3f * Mathf.Sin(ts * 2.3f);
                float height = 8f + 3.5f * Mathf.Sin(ts * 1.6f + 1f);
                _points[s] = path.PointOnRoad(ts, lateral, height);
            }
            for (int s = 0; s < _segments; s++)
            {
                float k0 = s / (float)_segments, k1 = (s + 1) / (float)_segments;
                float w0 = Mathf.Lerp(2.3f, 0.2f, k0), w1 = Mathf.Lerp(2.3f, 0.2f, k1);
                Color c0 = Color.Lerp(theme.dragonHead, theme.dragonTail, k0) * (0.75f * _visibility * (1f - k0 * 0.5f));
                Color c1 = Color.Lerp(theme.dragonHead, theme.dragonTail, k1) * (0.75f * _visibility * (1f - k1 * 0.5f));
                fx.AddRibbon(_points[s], _points[s + 1], w0, w1, c0, c1);
                if (s % 4 == 2) fx.Add(_points[s], w0 * 1.2f, c0 * 0.6f, ProceduralTextures.FxCell.Petal, s * 0.7f, 0.5f);
            }
            Vector3 head = _points[0];
            fx.Add(head, 4.2f, theme.dragonHead * (0.6f * _visibility), ProceduralTextures.FxCell.Dot, 0f, 1f);
            fx.Add(head + (head - _points[2]).normalized * 2.5f, 2.2f, theme.bigPearl * _visibility, ProceduralTextures.FxCell.Sparkle, songTime * 2f, 1f);
        }
    }
}
