using UnityEngine;
using VMS.Core;
using VMS.Rendering;

namespace VMS.Race
{
    /// <summary>
    /// Populates track chunks with themed scenery. Geometry goes into two submeshes: 0 = opaque silhouettes,
    /// 1 = additive glow (FX atlas). Everything is merged per chunk so a chunk costs two draw calls.
    /// </summary>
    public sealed partial class PropFactory
    {
        const int Opaque = 0, Glow = 1;

        readonly RaceTheme _theme;
        readonly TrackPath _path;
        readonly float[] _sectionTimes;
        readonly float _beatPeriod;
        readonly float _beatOffset;
        readonly uint _seed;

        /// <summary>Scenery amount (quality tier): 1 = full, 0.5 = half.</summary>
        public float Density = 1f;

        /// <summary>Ultra Low: no glow-only decorations (fireflies, candles, boat lights) and no far landmarks.</summary>
        public bool Minimal;

        int Scaled(int n) => Mathf.Max(n > 0 ? 1 : 0, Mathf.RoundToInt(n * Density));

        public PropFactory(RaceTheme theme, TrackPath path, float[] sectionTimes, float bpm, float beatOffset, uint seed)
        {
            _theme = theme;
            _path = path;
            _sectionTimes = sectionTimes ?? new float[0];
            _beatPeriod = bpm > 30f ? 60f / bpm : 0.5f;
            _beatOffset = beatOffset;
            _seed = seed;
        }

        public void Populate(MeshBuilder mb, int chunkIndex, float t0, float t1)
        {
            var rng = new Xoshiro128(_seed ^ (uint)(chunkIndex * 7919 + 17));
            if (t1 <= 0.5f) return;

            foreach (float s in _sectionTimes)
                if (s >= t0 && s < t1) SectionGate(mb, s);

            switch (_theme.props)
            {
                case PropStyle.Karst: Karsts(mb, rng, t0, t1); break;
                case PropStyle.Lanterns: Lanterns(mb, rng, t0, t1); break;
                case PropStyle.Bamboo: Bamboo(mb, rng, t0, t1); break;
                case PropStyle.Citadel: Citadel(mb, rng, t0, t1); break;
                case PropStyle.CloudSea: CloudSea(mb, rng, t0, t1); break;
                case PropStyle.ShrineGates: ShrineGates(mb, rng, t0, t1); break;
                case PropStyle.Hanok: Hanok(mb, rng, t0, t1); break;
                case PropStyle.Fogwood: Fogwood(mb, rng, t0, t1); break;
                case PropStyle.Boreal: Boreal(mb, rng, t0, t1); break;
            }
        }

        Matrix4x4 Frame(float t, float lateral, float height, float yawOffset = 0f)
        {
            Vector3 p;
            Quaternion q;
            _path.Sample(t, out p, out q);
            // Props stand upright (ignore the road's bank) but follow its heading.
            var fwd = q * Vector3.forward;
            fwd.y = 0f;
            var upright = fwd.sqrMagnitude > 0.001f ? Quaternion.LookRotation(fwd.normalized, Vector3.up) : Quaternion.identity;
            var right = q * Vector3.right;
            return Matrix4x4.TRS(p + right * lateral + Vector3.up * height, upright * Quaternion.Euler(0f, yawOffset, 0f), Vector3.one);
        }

        static float Range(IRandomSource rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);
        static int Side(IRandomSource rng) => rng.NextInt(2) == 0 ? -1 : 1;

        void GlowCross(MeshBuilder mb, Matrix4x4 m, Vector3 local, float size, Color color, ProceduralTextures.FxCell cell, float pulse)
        {
            var c = (Color32)color;
            c.a = (byte)(255 * pulse);
            var r = ProceduralTextures.AtlasRect(cell);
            Vector3 center = m.MultiplyPoint3x4(local);
            Vector3 ax = m.MultiplyVector(Vector3.right) * size * 0.5f;
            Vector3 az = m.MultiplyVector(Vector3.forward) * size * 0.5f;
            Vector3 ay = Vector3.up * size * 0.5f;
            foreach (var h in new[] { ax, az })
            {
                int i = mb.AddVertex(center - h - ay, c, new Vector2(r.xMin, r.yMin));
                mb.AddVertex(center - h + ay, c, new Vector2(r.xMin, r.yMax));
                mb.AddVertex(center + h + ay, c, new Vector2(r.xMax, r.yMax));
                mb.AddVertex(center + h - ay, c, new Vector2(r.xMax, r.yMin));
                mb.AddTriangle(i, i + 1, i + 2, Glow);
                mb.AddTriangle(i, i + 2, i + 3, Glow);
            }
        }

        void GlowBar(MeshBuilder mb, Vector3 a, Vector3 b, float width, Color color, float pulse)
        {
            var c = (Color32)color;
            c.a = (byte)(255 * pulse);
            var r = ProceduralTextures.AtlasRect(ProceduralTextures.FxCell.Square);
            Vector3 dir = b - a;
            Vector3 side = Vector3.Cross(dir.normalized, Vector3.up);
            if (side.sqrMagnitude < 0.01f) side = Vector3.Cross(dir.normalized, Vector3.forward);
            Vector3 w = side.normalized * width * 0.5f;
            Vector3 w2 = Vector3.Cross(dir.normalized, w).normalized * width * 0.5f;
            foreach (var off in new[] { w, w2 })
            {
                int i = mb.AddVertex(a - off, c, new Vector2(r.xMin, r.yMin));
                mb.AddVertex(b - off, c, new Vector2(r.xMin, r.yMax));
                mb.AddVertex(b + off, c, new Vector2(r.xMax, r.yMax));
                mb.AddVertex(a + off, c, new Vector2(r.xMax, r.yMin));
                mb.AddTriangle(i, i + 1, i + 2, Glow);
                mb.AddTriangle(i, i + 2, i + 3, Glow);
            }
        }

        /// <summary>A cổng tam quan style gate spanning the road; section changes get the grand version.</summary>
        void Gate(MeshBuilder mb, float t, bool grand)
        {
            var m = Frame(t, 0f, 0f);
            float half = TrackPath.RoadHalfWidth + 1.4f;
            float h = grand ? 10f : 8f;
            Color32 dark = _theme.propDark, light = _theme.propLight;
            mb.AddBox(m * Matrix4x4.Translate(new Vector3(-half, h * 0.5f, 0f)), new Vector3(0.9f, h, 0.9f), light, Opaque);
            mb.AddBox(m * Matrix4x4.Translate(new Vector3(half, h * 0.5f, 0f)), new Vector3(0.9f, h, 0.9f), light, Opaque);
            mb.AddBox(m * Matrix4x4.Translate(new Vector3(0f, h - 1.2f, 0f)), new Vector3(half * 2f + 1.2f, 0.5f, 0.7f), dark, Opaque);
            // Roof with upturned eaves.
            mb.AddTaper(m * Matrix4x4.TRS(new Vector3(0f, h + 0.25f, 0f), Quaternion.Euler(0f, 90f, 0f), Vector3.one),
                new Vector2(1.6f, 0.5f), new Vector2(1.6f, 0.5f), half * 2f + 3f, dark, Opaque);
            mb.AddTaper(m * Matrix4x4.TRS(new Vector3(-half - 1.9f, h + 0.75f, 0f), Quaternion.Euler(-35f, 90f, 0f), Vector3.one),
                new Vector2(1.2f, 0.3f), new Vector2(0.4f, 0.2f), 1.4f, dark, Opaque);
            mb.AddTaper(m * Matrix4x4.TRS(new Vector3(half + 1.9f, h + 0.75f, 0f), Quaternion.Euler(-35f, -90f, 0f), Vector3.one),
                new Vector2(1.2f, 0.3f), new Vector2(0.4f, 0.2f), 1.4f, dark, Opaque);
            mb.AddBox(m * Matrix4x4.Translate(new Vector3(0f, h - 2.4f, 0f)), new Vector3(3.2f, 1.4f, 0.3f), light, Opaque);

            Color glow = _theme.propGlow;
            GlowBar(mb, m.MultiplyPoint3x4(new Vector3(-half + 0.5f, 0f, 0f)), m.MultiplyPoint3x4(new Vector3(-half + 0.5f, h - 1.4f, 0f)), 0.35f, glow, 0.9f);
            GlowBar(mb, m.MultiplyPoint3x4(new Vector3(half - 0.5f, 0f, 0f)), m.MultiplyPoint3x4(new Vector3(half - 0.5f, h - 1.4f, 0f)), 0.35f, glow, 0.9f);
            GlowBar(mb, m.MultiplyPoint3x4(new Vector3(-half, h - 1.5f, -0.4f)), m.MultiplyPoint3x4(new Vector3(half, h - 1.5f, -0.4f)), 0.3f, glow, 1f);
            GlowCross(mb, m, new Vector3(0f, h - 2.4f, -0.3f), grand ? 4.5f : 3f, _theme.bigPearl, ProceduralTextures.FxCell.Dot, 1f);
            if (grand && !Minimal)
            {
                GlowCross(mb, m, new Vector3(-half, h - 3f, -0.6f), 2.6f, glow, ProceduralTextures.FxCell.Lantern, 0.6f);
                GlowCross(mb, m, new Vector3(half, h - 3f, -0.6f), 2.6f, glow, ProceduralTextures.FxCell.Lantern, 0.6f);
            }
        }

        void Karsts(MeshBuilder mb, IRandomSource rng, float t0, float t1)
        {
            int count = Scaled(3 + rng.NextInt(3));
            for (int k = 0; k < count; k++)
            {
                float t = Range(rng, t0, t1);
                int side = Side(rng);
                float lateral = side * Range(rng, 26f, 130f);
                float height = Range(rng, 22f, 85f) * (Mathf.Abs(lateral) > 70f ? 1.3f : 1f);
                float baseW = Range(rng, 10f, 22f);
                var m = Frame(t, lateral, -18f, Range(rng, 0f, 360f));
                float y = 0f;
                float w = baseW;
                int tiers = 3 + rng.NextInt(2);
                for (int tier = 0; tier < tiers; tier++)
                {
                    float th = height / tiers * Range(rng, 0.8f, 1.2f);
                    float w2 = w * Range(rng, 0.62f, 0.85f);
                    var tierM = m * Matrix4x4.TRS(new Vector3(Range(rng, -1.5f, 1.5f), y + th * 0.5f, Range(rng, -1.5f, 1.5f)),
                        Quaternion.Euler(-90f, Range(rng, -15f, 15f), 0f), Vector3.one);
                    Color32 c = Color.Lerp(_theme.propDark, _theme.propLight, (tier + 1f) / tiers);
                    mb.AddTaper(tierM, new Vector2(w, w * 0.9f), new Vector2(w2, w2 * 0.9f), th, c, Opaque);
                    y += th * 0.92f;
                    w = w2;
                }
                // A thin glowing mist line along the water at the karst foot.
                if (rng.NextInt(2) == 0 && !Minimal)
                    GlowBar(mb, m.MultiplyPoint3x4(new Vector3(-baseW, 1f, 0f)), m.MultiplyPoint3x4(new Vector3(baseW, 1f, 0f)), 1.2f, _theme.propGlow * 0.5f, 0.3f);
            }
            // Fishing-boat lights drifting on the bay.
            int boats = Minimal ? 0 : Scaled(rng.NextInt(3));
            for (int k = 0; k < boats; k++)
            {
                var m = Frame(Range(rng, t0, t1), Side(rng) * Range(rng, 12f, 40f), -6f);
                mb.AddBox(m * Matrix4x4.Translate(new Vector3(0f, 0.3f, 0f)), new Vector3(1.4f, 0.6f, 3.2f), _theme.propLight, Opaque);
                GlowCross(mb, m, new Vector3(0f, 1.6f, 0f), 2.4f, _theme.bigPearl, ProceduralTextures.FxCell.Lantern, 0.5f);
            }
        }

        void Lanterns(MeshBuilder mb, IRandomSource rng, float t0, float t1)
        {
            int count = Scaled(6 + rng.NextInt(5));
            for (int k = 0; k < count; k++)
            {
                float t = Range(rng, t0, t1);
                float lateral = Side(rng) * Range(rng, 7.5f, 34f);
                var m = Frame(t, lateral, Range(rng, 0.5f, 13f));
                Color glow = Color.Lerp(_theme.propGlow, _theme.edgeLine, (float)rng.NextDouble());
                mb.AddBox(m, new Vector3(0.55f, 0.75f, 0.55f), _theme.propLight, Opaque);
                GlowCross(mb, m, Vector3.zero, Range(rng, 1.8f, 2.8f), glow, ProceduralTextures.FxCell.Lantern, 0.8f);
            }
            if (rng.NextInt(3) == 0 && !Minimal)
            {
                // Seven-tiered pagoda on the far bank.
                var m = Frame(Range(rng, t0, t1), Side(rng) * Range(rng, 60f, 140f), -10f);
                float y = 0f, w = 12f;
                for (int tier = 0; tier < 7; tier++)
                {
                    float th = 5f;
                    mb.AddBox(m * Matrix4x4.Translate(new Vector3(0f, y + th * 0.5f, 0f)), new Vector3(w, th, w), _theme.propDark, Opaque);
                    mb.AddBox(m * Matrix4x4.Translate(new Vector3(0f, y + th, 0f)), new Vector3(w + 3f, 0.6f, w + 3f), _theme.propLight, Opaque);
                    GlowCross(mb, m, new Vector3(0f, y + th * 0.5f, -w * 0.5f - 0.2f), 2.2f, _theme.propGlow, ProceduralTextures.FxCell.Dot, 0.5f);
                    y += th;
                    w *= 0.86f;
                }
            }
            // Floating lotus candles on the river surface.
            int lotus = Minimal ? 0 : Scaled(2 + rng.NextInt(3));
            for (int k = 0; k < lotus; k++)
            {
                var m = Frame(Range(rng, t0, t1), Side(rng) * Range(rng, 7f, 22f), -2.5f);
                GlowCross(mb, m, Vector3.zero, 1.6f, _theme.roadGrid, ProceduralTextures.FxCell.Petal, 0.6f);
            }
        }

        void Bamboo(MeshBuilder mb, IRandomSource rng, float t0, float t1)
        {
            int clusters = Scaled(4 + rng.NextInt(3));
            for (int k = 0; k < clusters; k++)
            {
                float t = Range(rng, t0, t1);
                int side = Side(rng);
                float lateral = side * Range(rng, 8.5f, 45f);
                int stalks = 3 + rng.NextInt(4);
                for (int s = 0; s < stalks; s++)
                {
                    var m = Frame(t + Range(rng, -0.2f, 0.2f), lateral + Range(rng, -3f, 3f), -2f) *
                            Matrix4x4.Rotate(Quaternion.Euler(Range(rng, -6f, 6f), 0f, Range(rng, -8f, 8f) - side * 4f));
                    float height = Range(rng, 14f, 30f);
                    float r = Range(rng, 0.16f, 0.3f);
                    int segments = 5;
                    float segH = height / segments;
                    for (int seg = 0; seg < segments; seg++)
                    {
                        Color32 c = Color.Lerp(_theme.propDark, _theme.propLight, seg / (float)segments);
                        mb.AddCylinder(m * Matrix4x4.Translate(new Vector3(0f, seg * segH, 0f)), r, r * 0.93f, segH * 0.97f, 5, c, c, Opaque);
                    }
                    // Leaf clumps: flat blades fanning out near the top.
                    for (int leaf = 0; leaf < 4; leaf++)
                    {
                        var lm = m * Matrix4x4.TRS(new Vector3(0f, height * Range(rng, 0.65f, 0.95f), 0f),
                            Quaternion.Euler(Range(rng, 20f, 60f), Range(rng, 0f, 360f), 0f), Vector3.one);
                        mb.AddTaper(lm * Matrix4x4.Translate(new Vector3(0f, 0f, 1.6f)), new Vector2(0.5f, 0.05f), new Vector2(0.05f, 0.02f), 3.2f, _theme.propLight, Opaque);
                    }
                }
            }
            int flies = Minimal ? 0 : Scaled(3 + rng.NextInt(4));
            for (int k = 0; k < flies; k++)
            {
                var m = Frame(Range(rng, t0, t1), Side(rng) * Range(rng, 6.5f, 20f), Range(rng, 0.5f, 6f));
                GlowCross(mb, m, Vector3.zero, Range(rng, 0.5f, 0.9f), _theme.propGlow, ProceduralTextures.FxCell.Dot, 1f);
            }
        }

        void Citadel(MeshBuilder mb, IRandomSource rng, float t0, float t1)
        {
            // Continuous crenellated walls on both sides, built from short segments along the chunk.
            const float wallOffset = 17f;
            for (float t = t0; t < t1; t += 0.25f)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    var m = Frame(t, side * wallOffset, -1f);
                    mb.AddBox(m * Matrix4x4.Translate(new Vector3(0f, 3f, 0f)), new Vector3(2.2f, 6f, 10.5f), _theme.propDark, Opaque);
                    mb.AddBox(m * Matrix4x4.Translate(new Vector3(0f, 6.6f, 0f)), new Vector3(2.4f, 1.2f, 2.5f), _theme.propLight, Opaque);
                }
            }
            // Gates on every fourth bar line inside the chunk.
            float bar = _beatPeriod * 16f;
            float first = Mathf.Ceil((t0 - _beatOffset) / bar) * bar + _beatOffset;
            for (float t = first; t < t1; t += bar) Gate(mb, t, false);

            int posts = Scaled(2 + rng.NextInt(2));
            for (int k = 0; k < posts; k++)
            {
                var m = Frame(Range(rng, t0, t1), Side(rng) * (TrackPath.RoadHalfWidth + 2.5f), 0f);
                mb.AddBox(m * Matrix4x4.Translate(new Vector3(0f, 1.6f, 0f)), new Vector3(0.3f, 3.2f, 0.3f), _theme.propLight, Opaque);
                GlowCross(mb, m, new Vector3(0f, 3.5f, 0f), 2f, _theme.propGlow, ProceduralTextures.FxCell.Lantern, 0.9f);
            }
            if (rng.NextInt(2) == 0 && !Minimal)
            {
                var m = Frame(Range(rng, t0, t1), Side(rng) * Range(rng, 45f, 90f), -1f);
                mb.AddBox(m * Matrix4x4.Translate(new Vector3(0f, 6f, 0f)), new Vector3(24f, 12f, 14f), _theme.propDark, Opaque);
                mb.AddTaper(m * Matrix4x4.TRS(new Vector3(0f, 14f, 0f), Quaternion.Euler(0f, 90f, 0f), Vector3.one),
                    new Vector2(18f, 4f), new Vector2(8f, 3f), 30f, _theme.propLight, Opaque);
                GlowBar(mb, m.MultiplyPoint3x4(new Vector3(-12f, 12.2f, -7.2f)), m.MultiplyPoint3x4(new Vector3(12f, 12.2f, -7.2f)), 0.6f, _theme.propGlow, 0.7f);
            }
        }
    }
}
