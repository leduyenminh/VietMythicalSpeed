using UnityEngine;

namespace VMS.Rendering
{
    /// <summary>
    /// Shared materials built from the shaders in Resources/Shaders. All shaders are unlit, take colours from
    /// vertex colours and apply a distance fog driven by global uniforms, so they render identically in the
    /// Built-in pipeline and URP.
    /// </summary>
    public static class VmsGraphics
    {
        public static readonly int FogColorId = Shader.PropertyToID("_VmsFogColor");
        public static readonly int FogRangeId = Shader.PropertyToID("_VmsFogRange");
        public static readonly int PulseId = Shader.PropertyToID("_VmsPulse");
        public static readonly int EnergyId = Shader.PropertyToID("_VmsEnergy");
        public static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        public static readonly int TintId = Shader.PropertyToID("_Tint");

        static Material _opaque, _additive, _fx, _bars, _road, _sky;

        public static Shader Find(string name)
        {
            var shader = Shader.Find(name);
            if (shader == null)
            {
                Debug.LogError("[VmsGraphics] Shader '" + name + "' not found – is Resources/Shaders imported?");
                shader = Shader.Find("Sprites/Default");
            }
            return shader;
        }

        /// <summary>Opaque, vertex-coloured silhouettes (props, vessels).</summary>
        public static Material Opaque => _opaque ? _opaque : (_opaque = new Material(Find("VMS/UnlitVertex")) { name = "VMS Opaque" });

        /// <summary>Additive glow strips (lane lines, rails, trims) using the soft strip profile.</summary>
        public static Material Additive
        {
            get
            {
                if (_additive) return _additive;
                _additive = new Material(Find("VMS/Additive")) { name = "VMS Additive" };
                _additive.SetTexture(MainTexId, ProceduralTextures.GlowStrip);
                return _additive;
            }
        }

        /// <summary>Additive billboards from the FX atlas (pearls, sparks, lanterns).</summary>
        public static Material Fx
        {
            get
            {
                if (_fx) return _fx;
                _fx = new Material(Find("VMS/Additive")) { name = "VMS Fx" };
                _fx.SetTexture(MainTexId, ProceduralTextures.FxAtlas);
                return _fx;
            }
        }

        /// <summary>Additive striped bars for the spectrum towers.</summary>
        public static Material Bars
        {
            get
            {
                if (_bars) return _bars;
                _bars = new Material(Find("VMS/Additive")) { name = "VMS Bars" };
                _bars.SetTexture(MainTexId, ProceduralTextures.Stripes);
                return _bars;
            }
        }

        public static Material Road => _road ? _road : (_road = new Material(Find("VMS/Road")) { name = "VMS Road" });

        public static Material Sky => _sky ? _sky : (_sky = new Material(Find("VMS/Sky")) { name = "VMS Sky" });

        public static Material NewAdditive(Texture texture, string name)
        {
            var m = new Material(Find("VMS/Additive")) { name = name };
            m.SetTexture(MainTexId, texture);
            return m;
        }

        public static void SetFog(Color color, float start, float end)
        {
            Shader.SetGlobalColor(FogColorId, color);
            Shader.SetGlobalVector(FogRangeId, new Vector4(start, end, 1f / Mathf.Max(1f, end - start), 0f));
        }

        public static void SetPulse(float pulse, float energy)
        {
            Shader.SetGlobalFloat(PulseId, pulse);
            Shader.SetGlobalFloat(EnergyId, energy);
        }
    }

    /// <summary>Textures generated at runtime so the project ships without binary art for effects.</summary>
    public static class ProceduralTextures
    {
        public const int AtlasCells = 4;

        public enum FxCell
        {
            Dot = 0,
            Ring = 1,
            Flame = 2,
            Sparkle = 3,
            Petal = 4,
            Diamond = 5,
            Crescent = 6,
            Square = 7,
            Lantern = 8,
            Wing = 9,
        }

        static Texture2D _glowStrip, _atlas, _stripes;

        /// <summary>Horizontal profile: bright core with soft falloff to both edges (u across a strip).</summary>
        public static Texture2D GlowStrip
        {
            get
            {
                if (_glowStrip) return _glowStrip;
                const int w = 64, h = 4;
                _glowStrip = NewTexture(w, h, TextureWrapMode.Clamp, false);
                var px = new Color32[w * h];
                for (int x = 0; x < w; x++)
                {
                    byte v = GlowProfile((x + 0.5f) / w);
                    for (int y = 0; y < h; y++) px[y * w + x] = new Color32(v, v, v, v);
                }
                _glowStrip.SetPixels32(px);
                _glowStrip.Apply(false, true);
                return _glowStrip;
            }
        }

        static Texture2D _glowStripV;

        /// <summary>Same profile as <see cref="GlowStrip"/> but across v – TrailRenderer maps width to v.</summary>
        public static Texture2D GlowStripVertical
        {
            get
            {
                if (_glowStripV) return _glowStripV;
                const int w = 4, h = 64;
                _glowStripV = NewTexture(w, h, TextureWrapMode.Clamp, false);
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    byte v = GlowProfile((y + 0.5f) / h);
                    for (int x = 0; x < w; x++) px[y * w + x] = new Color32(v, v, v, v);
                }
                _glowStripV.SetPixels32(px);
                _glowStripV.Apply(false, true);
                return _glowStripV;
            }
        }

        /// <summary>Bright core with a soft halo; t is 0..1 across the strip.</summary>
        static byte GlowProfile(float t)
        {
            float u = Mathf.Abs(t * 2f - 1f);
            float core = Mathf.Clamp01(1f - u * 3.2f);
            float halo = Mathf.Pow(1f - u, 2.4f);
            return (byte)(255 * Mathf.Clamp01(core * 0.9f + halo * 0.55f));
        }

        /// <summary>Vertical repeating blocks for spectrum bars (v in "segments").</summary>
        public static Texture2D Stripes
        {
            get
            {
                if (_stripes) return _stripes;
                const int w = 16, h = 32;
                _stripes = NewTexture(w, h, TextureWrapMode.Repeat, false);
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float fy = (y + 0.5f) / h;
                        float fx = Mathf.Abs((x + 0.5f) / w * 2f - 1f);
                        float block = fy > 0.18f ? 1f : 0.08f;
                        float edge = Mathf.Lerp(1f, 0.55f, fx * fx);
                        byte v = (byte)(255 * block * edge);
                        px[y * w + x] = new Color32(v, v, v, v);
                    }
                _stripes.SetPixels32(px);
                _stripes.Apply(false, true);
                return _stripes;
            }
        }

        public static Rect AtlasRect(FxCell cell)
        {
            int i = (int)cell;
            float s = 1f / AtlasCells;
            return new Rect((i % AtlasCells) * s, 1f - (i / AtlasCells + 1) * s, s, s);
        }

        /// <summary>256² atlas of soft white shapes; tinted by vertex colour.</summary>
        public static Texture2D FxAtlas
        {
            get
            {
                if (_atlas) return _atlas;
                const int size = 256, cell = size / AtlasCells;
                _atlas = NewTexture(size, size, TextureWrapMode.Clamp, true);
                var px = new Color32[size * size];
                for (int c = 0; c < AtlasCells * AtlasCells; c++)
                {
                    var rect = AtlasRect((FxCell)c);
                    int ox = Mathf.RoundToInt(rect.x * size), oy = Mathf.RoundToInt(rect.y * size);
                    for (int y = 0; y < cell; y++)
                        for (int x = 0; x < cell; x++)
                        {
                            float u = (x + 0.5f) / cell * 2f - 1f;
                            float v = (y + 0.5f) / cell * 2f - 1f;
                            byte b = (byte)(255 * Mathf.Clamp01(Shape((FxCell)c, u, v)));
                            px[(oy + y) * size + ox + x] = new Color32(b, b, b, b);
                        }
                }
                _atlas.SetPixels32(px);
                _atlas.Apply(true, true);
                return _atlas;
            }
        }

        static float Shape(FxCell cell, float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            switch (cell)
            {
                case FxCell.Dot:
                    return Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f) + Mathf.Clamp01(1f - r * 4f);
                case FxCell.Ring:
                    return Mathf.Clamp01(1f - Mathf.Abs(r - 0.72f) * 9f) + 0.25f * Mathf.Clamp01(1f - Mathf.Abs(r - 0.72f) * 3f);
                case FxCell.Flame:
                {
                    // Teardrop pointing up: narrow tip at top, round belly below.
                    float y = (v + 1f) * 0.5f;
                    float width = Mathf.Lerp(0.55f, 0.02f, Mathf.Pow(y, 1.3f)) * (y < 0.3f ? Mathf.Sqrt(y / 0.3f) : 1f);
                    float d = Mathf.Abs(u) / Mathf.Max(0.001f, width);
                    float body = Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - Mathf.Abs(v) * 0.9f);
                    return Mathf.Pow(body, 0.8f) + 0.6f * Mathf.Clamp01(1f - Mathf.Sqrt(u * u * 6f + (v + 0.35f) * (v + 0.35f) * 4f));
                }
                case FxCell.Sparkle:
                {
                    float cross = Mathf.Clamp01(1f - Mathf.Abs(u) * 14f) * Mathf.Clamp01(1f - Mathf.Abs(v)) +
                                  Mathf.Clamp01(1f - Mathf.Abs(v) * 14f) * Mathf.Clamp01(1f - Mathf.Abs(u));
                    return cross + Mathf.Pow(Mathf.Clamp01(1f - r * 1.6f), 3f);
                }
                case FxCell.Petal:
                {
                    float a = Mathf.Atan2(v, u);
                    float petals = 0.55f + 0.35f * Mathf.Abs(Mathf.Cos(a * 2.5f));
                    return Mathf.Clamp01((petals - r) * 6f) * 0.85f + Mathf.Clamp01(1f - r * 5f);
                }
                case FxCell.Diamond:
                {
                    float d = Mathf.Abs(u) + Mathf.Abs(v);
                    return Mathf.Clamp01(1f - Mathf.Abs(d - 0.75f) * 10f) + 0.35f * Mathf.Clamp01(1f - d);
                }
                case FxCell.Crescent:
                {
                    float outer = Mathf.Clamp01((0.8f - r) * 12f);
                    float r2 = Mathf.Sqrt((u - 0.3f) * (u - 0.3f) + (v - 0.15f) * (v - 0.15f));
                    return outer * Mathf.Clamp01((r2 - 0.62f) * 12f);
                }
                case FxCell.Square:
                {
                    float d = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v));
                    return Mathf.Pow(Mathf.Clamp01(1f - d), 1.5f) * 1.4f;
                }
                case FxCell.Lantern:
                {
                    // Round paper lantern with darker ribs and a bright core.
                    float body = Mathf.Clamp01((1f - Mathf.Sqrt(u * u * 1.6f + v * v * 2.2f)) * 5f);
                    float ribs = 0.75f + 0.25f * Mathf.Cos(u * 18f);
                    float caps = (Mathf.Abs(v) > 0.62f && Mathf.Abs(v) < 0.75f && Mathf.Abs(u) < 0.3f) ? 0.7f : 0f;
                    return body * ribs + caps + 0.5f * Mathf.Pow(Mathf.Clamp01(1f - r), 3f);
                }
                case FxCell.Wing:
                {
                    // Swept feather shape for the Lạc-bird flock.
                    float x = (u + 1f) * 0.5f;
                    float top = 0.25f * Mathf.Sin(x * Mathf.PI) + 0.1f;
                    float d = Mathf.Abs(v - (0.35f * x - 0.2f)) - top * (1f - x * 0.6f);
                    return Mathf.Clamp01(-d * 10f);
                }
                default:
                    return 0f;
            }
        }

        /// <summary>A big moon disc for the sky; <paramref name="bands"/> adds synthwave-style cuts.</summary>
        public static Texture2D Moon(Color inner, Color outer, bool bands)
        {
            const int size = 256;
            var tex = NewTexture(size, size, TextureWrapMode.Clamp, true);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float disc = Mathf.Clamp01((0.62f - r) * 60f);
                    float glow = Mathf.Pow(Mathf.Clamp01(1f - r), 3f) * 0.6f;
                    if (bands && v < 0f)
                    {
                        float stripe = Mathf.Repeat(-v * 9f, 1f);
                        float gap = Mathf.Lerp(0.05f, 0.45f, -v / 0.62f);
                        if (stripe < gap) disc = 0f;
                    }
                    var c = Color.Lerp(outer, inner, Mathf.Clamp01(v * 0.8f + 0.6f));
                    float crater = 0.08f * Mathf.PerlinNoise(u * 6f + 3f, v * 6f + 7f);
                    px[y * size + x] = new Color(c.r - crater, c.g - crater, c.b - crater, 1f) * (disc + glow);
                }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static Texture2D NewTexture(int w, int h, TextureWrapMode wrap, bool mipmaps)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, mipmaps)
            {
                wrapMode = wrap,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
                name = "VMS Procedural",
            };
        }
    }
}
