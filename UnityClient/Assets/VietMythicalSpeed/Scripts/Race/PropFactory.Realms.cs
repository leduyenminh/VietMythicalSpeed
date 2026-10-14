using UnityEngine;
using VMS.Core;
using VMS.Rendering;

namespace VMS.Race
{
    /// <summary>
    /// Scenery of the five realms beyond the Five Gates (docs/World.md). Same rules as the home roads: merged
    /// opaque silhouettes plus an additive glow submesh, counts scaled by <see cref="Density"/>, glow-only
    /// decorations and far landmarks skipped when <see cref="Minimal"/>.
    /// </summary>
    public sealed partial class PropFactory
    {
        /// <summary>The big gate at a section change, in the style of the road's realm.</summary>
        void SectionGate(MeshBuilder mb, float t)
        {
            switch (_theme.props)
            {
                case PropStyle.CloudSea: MoonGate(mb, t); break;
                case PropStyle.ShrineGates: Torii(mb, t, true); break;
                case PropStyle.Hanok: RedArrowGate(mb, t); break;
                case PropStyle.Fogwood: StoneArch(mb, t); break;
                case PropStyle.Boreal: IceArch(mb, t); break;
                default: Gate(mb, t, true); break;
            }
        }

        // ------------------------------------------------------------------ shared pieces

        static Matrix4x4 At(Vector3 p) => Matrix4x4.Translate(p);

        static Matrix4x4 Up(Vector3 centre) => Matrix4x4.TRS(centre, Quaternion.Euler(-90f, 0f, 0f), Vector3.one);

        /// <summary>Hip roof sitting on <paramref name="baseY"/>, optionally with upswept eave corners.</summary>
        void HipRoof(MeshBuilder mb, Matrix4x4 m, float baseY, float width, float depth, float height, Color32 color, bool upswept)
        {
            mb.AddTaper(m * Up(new Vector3(0f, baseY + height * 0.5f, 0f)), new Vector2(width, depth), new Vector2(width * 0.45f, depth * 0.12f), height, color, Opaque);
            if (!upswept) return;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var dir = new Vector3(sx, 0.9f, sz).normalized;
                    var corner = new Vector3(sx * width * 0.5f, baseY + 0.1f, sz * depth * 0.5f) + dir * width * 0.05f;
                    mb.AddTaper(m * Matrix4x4.TRS(corner, Quaternion.LookRotation(dir), Vector3.one),
                        new Vector2(0.35f, 0.2f), new Vector2(0.08f, 0.06f), Mathf.Max(0.6f, width * 0.12f), color, Opaque);
                }
        }

        /// <summary>Ring of boxes in the local XY plane (moon gates, ice arches, clock faces).</summary>
        void Ring(MeshBuilder mb, Matrix4x4 m, Vector3 centre, float radius, float thickness, int segments, Color32 color, float fromDeg = 0f, float toDeg = 360f)
        {
            float step = (toDeg - fromDeg) / segments;
            float length = 2f * Mathf.PI * radius * (step / 360f) * 1.08f;
            for (int i = 0; i < segments; i++)
            {
                float a = fromDeg + step * (i + 0.5f);
                float rad = a * Mathf.Deg2Rad;
                var p = centre + new Vector3(Mathf.Cos(rad) * radius, Mathf.Sin(rad) * radius, 0f);
                mb.AddBox(m * Matrix4x4.TRS(p, Quaternion.Euler(0f, 0f, a), Vector3.one), new Vector3(thickness, length, thickness * 0.8f), color, Opaque);
            }
        }

        /// <summary>Conifer: a short trunk and three stacked cones shading from <paramref name="low"/> to <paramref name="high"/>.</summary>
        void Pine(MeshBuilder mb, Matrix4x4 m, float height, Color low, Color high)
        {
            Color32 trunk = Color.Lerp(_theme.propDark, new Color(0.3f, 0.2f, 0.14f), 0.6f);
            mb.AddCylinder(m, 0.3f, 0.2f, height * 0.25f, 5, trunk, trunk, Opaque);
            for (int i = 0; i < 3; i++)
            {
                float y = height * (0.18f + i * 0.24f);
                float r = height * (0.28f - i * 0.07f);
                mb.AddCylinder(m * At(new Vector3(0f, y, 0f)), r, 0.05f, height * 0.42f, 7,
                    Color.Lerp(low, high, i / 3f), Color.Lerp(low, high, (i + 1) / 3f), Opaque);
            }
        }

        void Pagoda(MeshBuilder mb, Matrix4x4 m, int tiers, float width, Color32 body, Color32 roof, Color glow)
        {
            mb.AddBox(m * At(new Vector3(0f, 0.8f, 0f)), new Vector3(width + 2f, 1.6f, width + 2f), roof, Opaque);
            float y = 1.6f, w = width;
            for (int i = 0; i < tiers; i++)
            {
                const float th = 3.4f;
                mb.AddBox(m * At(new Vector3(0f, y + th * 0.5f, 0f)), new Vector3(w * 0.78f, th, w * 0.78f), body, Opaque);
                HipRoof(mb, m, y + th - 0.2f, w + 2.4f, w + 2.4f, 1.3f, roof, true);
                if (!Minimal) GlowCross(mb, m, new Vector3(0f, y + th * 0.5f, -w * 0.4f - 0.1f), 1.8f, glow, ProceduralTextures.FxCell.Dot, 0.5f);
                y += th + 1f;
                w *= 0.86f;
            }
            mb.AddCylinder(m * At(new Vector3(0f, y, 0f)), 0.25f, 0.05f, 4f, 5, roof, roof, Opaque);
        }

        // ------------------------------------------------------------------ Vân Khuyết: the cloud sea

        void CloudSea(MeshBuilder mb, IRandomSource rng, float t0, float t1)
        {
            Color stone = Color.Lerp(_theme.propDark, _theme.propLight, 0.45f);
            int peaks = Scaled(3 + rng.NextInt(3));
            for (int k = 0; k < peaks; k++)
            {
                float t = Range(rng, t0, t1);
                float lateral = Side(rng) * Range(rng, 24f, 115f);
                float height = Range(rng, 20f, 58f);
                float w = Range(rng, 6f, 12f);
                var m = Frame(t, lateral, -34f, Range(rng, 0f, 360f));
                // Two stacked, slightly twisted tapers: a weathered stone pillar rising out of the clouds.
                mb.AddTaper(m * Up(new Vector3(0f, height * 0.3f, 0f)), new Vector2(w * 1.25f, w), new Vector2(w, w * 0.85f), height * 0.6f,
                    Color.Lerp(_theme.propDark, stone, 0.5f), Opaque);
                mb.AddTaper(m * Matrix4x4.TRS(new Vector3(0f, height * 0.8f, 0f), Quaternion.Euler(-90f, 15f, 0f), Vector3.one),
                    new Vector2(w, w * 0.85f), new Vector2(w * 0.75f, w * 0.7f), height * 0.4f, stone, Opaque);
                if (rng.NextInt(3) != 0) Pavilion(mb, m * At(new Vector3(0f, height, 0f)), w * 0.55f);
                if (!Minimal) GlowCross(mb, m, new Vector3(0f, 30f, 0f), w * 3f, _theme.Accent * 0.35f, ProceduralTextures.FxCell.Dot, 0.2f);
            }
            if (rng.NextInt(3) == 0 && !Minimal)
            {
                // A pagoda on its own crag, far across the cloud sea.
                var m = Frame(Range(rng, t0, t1), Side(rng) * Range(rng, 70f, 150f), -40f, Range(rng, 0f, 360f));
                mb.AddTaper(m * Up(new Vector3(0f, 16f, 0f)), new Vector2(16f, 14f), new Vector2(11f, 10f), 32f, stone, Opaque);
                Pagoda(mb, m * At(new Vector3(0f, 32f, 0f)), 5, 8f, _theme.propLight, _theme.propDark, _theme.propGlow);
            }
            // Red lanterns hung in pairs from posts at the roadside.
            int posts = Scaled(2 + rng.NextInt(3));
            for (int k = 0; k < posts; k++)
            {
                var m = Frame(Range(rng, t0, t1), Side(rng) * (TrackPath.RoadHalfWidth + 2.6f), 0f);
                mb.AddBox(m * At(new Vector3(0f, 2.2f, 0f)), new Vector3(0.25f, 4.4f, 0.25f), _theme.propDark, Opaque);
                mb.AddBox(m * At(new Vector3(0f, 4.3f, 0f)), new Vector3(1.8f, 0.18f, 0.18f), _theme.propDark, Opaque);
                for (int s = -1; s <= 1; s += 2)
                {
                    mb.AddBox(m * At(new Vector3(s * 0.7f, 3.6f, 0f)), new Vector3(0.5f, 0.7f, 0.5f), Color.Lerp(_theme.propGlow, Color.red, 0.4f), Opaque);
                    GlowCross(mb, m, new Vector3(s * 0.7f, 3.6f, 0f), 1.9f, _theme.propGlow, ProceduralTextures.FxCell.Lantern, 0.8f);
                }
            }
        }

        /// <summary>Open pavilion: four posts, a floor and a roof with flying eaves; <paramref name="m"/> is its floor.</summary>
        void Pavilion(MeshBuilder mb, Matrix4x4 m, float size)
        {
            Color32 wood = Color.Lerp(_theme.propGlow, _theme.propDark, 0.55f);
            mb.AddBox(m * At(new Vector3(0f, 0.3f, 0f)), new Vector3(size * 1.3f, 0.6f, size * 1.3f), _theme.propDark, Opaque);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    mb.AddBox(m * At(new Vector3(sx * size * 0.5f, 2f, sz * size * 0.5f)), new Vector3(0.3f, 3.2f, 0.3f), wood, Opaque);
            HipRoof(mb, m, 3.4f, size * 1.6f, size * 1.6f, size * 0.55f, _theme.propDark, true);
            if (!Minimal) GlowCross(mb, m, new Vector3(0f, 2.6f, 0f), 1.6f, _theme.propGlow, ProceduralTextures.FxCell.Lantern, 0.7f);
        }

        /// <summary>A round moon gate set in a short wall, spanning the road.</summary>
        void MoonGate(MeshBuilder mb, float t)
        {
            var m = Frame(t, 0f, 0f);
            float radius = TrackPath.RoadHalfWidth + 2.2f;
            float cy = radius * 0.82f;
            Color32 wall = _theme.propLight, cap = _theme.propDark;
            Ring(mb, m, new Vector3(0f, cy, 0f), radius, 1.1f, 24, Color.Lerp(_theme.propLight, Color.white, 0.25f));
            float top = cy + radius;
            for (int side = -1; side <= 1; side += 2)
                mb.AddBox(m * At(new Vector3(side * (radius + 3.2f), top * 0.5f, 0f)), new Vector3(6f, top, 1f), wall, Opaque);
            mb.AddBox(m * At(new Vector3(0f, top + 0.4f, 0f)), new Vector3(radius * 2f + 13f, 0.8f, 1.6f), cap, Opaque);
            HipRoof(mb, m, top + 0.8f, radius * 2f + 13f, 2.4f, 1.1f, cap, true);
            GlowCross(mb, m, new Vector3(0f, cy, 0f), radius * 2.1f, _theme.bigPearl * 0.5f, ProceduralTextures.FxCell.Ring, 0.6f);
            if (!Minimal)
                for (int side = -1; side <= 1; side += 2)
                    GlowCross(mb, m, new Vector3(side * (radius + 1.2f), top - 1.6f, -0.8f), 2.2f, _theme.propGlow, ProceduralTextures.FxCell.Lantern, 0.8f);
        }

        // ------------------------------------------------------------------ Hanayume: the thousand gates

        void ShrineGates(MeshBuilder mb, IRandomSource rng, float t0, float t1)
        {
            // A tunnel of vermilion gates in time with the music: one every two beats (four on the low tiers).
            float step = _beatPeriod * (Density < 0.6f ? 4f : 2f);
            float first = Mathf.Ceil((t0 - _beatOffset) / step) * step + _beatOffset;
            for (float t = first; t < t1; t += step) Torii(mb, t, false);

            int lanterns = Scaled(3 + rng.NextInt(3));
            for (int k = 0; k < lanterns; k++)
            {
                int side = Side(rng);
                StoneLantern(mb, Frame(Range(rng, t0, t1), side * (TrackPath.RoadHalfWidth + 3.4f), 0f));
            }
            int trees = Scaled(3 + rng.NextInt(3));
            for (int k = 0; k < trees; k++)
                BlossomTree(mb, rng, Frame(Range(rng, t0, t1), Side(rng) * Range(rng, 9f, 32f), -0.5f, Range(rng, 0f, 360f)), Range(rng, 1f, 1.6f));
            if (rng.NextInt(3) == 0 && !Minimal)
            {
                var m = Frame(Range(rng, t0, t1), Side(rng) * Range(rng, 60f, 130f), -6f, Range(rng, 0f, 360f));
                Pagoda(mb, m, 5, 7f, _theme.propLight, _theme.propDark, _theme.Accent);
            }
        }

        void Torii(MeshBuilder mb, float t, bool grand)
        {
            var m = Frame(t, 0f, 0f);
            float half = TrackPath.RoadHalfWidth + 1.2f;
            float h = grand ? 11f : 8.5f;
            Color32 red = _theme.propLight, black = _theme.propDark;
            for (int side = -1; side <= 1; side += 2)
            {
                mb.AddCylinder(m * At(new Vector3(side * half, -0.5f, 0f)), 0.42f, 0.36f, h + 0.5f, 8, red, red, Opaque);
                mb.AddBox(m * At(new Vector3(side * half, 0.2f, 0f)), new Vector3(1.1f, 1f, 1.1f), black, Opaque);
                // Upswept ends of the top beam.
                mb.AddBox(m * Matrix4x4.TRS(new Vector3(side * (half + 1.9f), h + 0.5f, 0f), Quaternion.Euler(0f, 0f, side * 14f), Vector3.one),
                    new Vector3(1.4f, 0.4f, 0.95f), black, Opaque);
            }
            mb.AddBox(m * At(new Vector3(0f, h - 1.9f, 0f)), new Vector3(half * 2f + 1.6f, 0.45f, 0.5f), red, Opaque);
            mb.AddBox(m * At(new Vector3(0f, h - 0.2f, 0f)), new Vector3(half * 2f + 3.2f, 0.5f, 0.75f), red, Opaque);
            mb.AddBox(m * At(new Vector3(0f, h + 0.3f, 0f)), new Vector3(half * 2f + 3f, 0.45f, 0.95f), black, Opaque);
            mb.AddBox(m * At(new Vector3(0f, h - 1.05f, 0f)), new Vector3(1.1f, 1.3f, 0.25f), black, Opaque);
            if (!grand) return;
            GlowCross(mb, m, new Vector3(0f, h - 1.05f, -0.2f), 2.6f, _theme.Accent, ProceduralTextures.FxCell.Dot, 1f);
            if (!Minimal)
                for (int side = -1; side <= 1; side += 2)
                    GlowCross(mb, m, new Vector3(side * (half - 1.4f), h - 3f, -0.6f), 2.4f, _theme.Accent, ProceduralTextures.FxCell.Lantern, 0.7f);
        }

        void StoneLantern(MeshBuilder mb, Matrix4x4 m)
        {
            Color32 stone = Color.Lerp(_theme.propDark, new Color(0.55f, 0.55f, 0.6f), 0.55f);
            mb.AddBox(m * At(new Vector3(0f, 0.25f, 0f)), new Vector3(1.2f, 0.5f, 1.2f), stone, Opaque);
            mb.AddBox(m * At(new Vector3(0f, 1.3f, 0f)), new Vector3(0.45f, 1.6f, 0.45f), stone, Opaque);
            mb.AddBox(m * At(new Vector3(0f, 2.4f, 0f)), new Vector3(0.95f, 0.8f, 0.95f), stone, Opaque);
            HipRoof(mb, m, 2.8f, 1.6f, 1.6f, 0.7f, stone, true);
            GlowCross(mb, m, new Vector3(0f, 2.4f, 0f), 1.6f, _theme.Accent, ProceduralTextures.FxCell.Lantern, 0.8f);
        }

        void BlossomTree(MeshBuilder mb, IRandomSource rng, Matrix4x4 m, float scale)
        {
            Color32 bark = Color.Lerp(_theme.propDark, new Color(0.35f, 0.2f, 0.15f), 0.6f);
            mb.AddCylinder(m, 0.35f * scale, 0.2f * scale, 4.5f * scale, 6, bark, bark, Opaque);
            Color canopy = Color.Lerp(_theme.propGlow, Color.white, 0.15f);
            for (int i = 0; i < 3; i++)
            {
                var p = new Vector3(Range(rng, -1.6f, 1.6f), 4.2f + Range(rng, 0f, 1.6f), Range(rng, -1.6f, 1.6f)) * scale;
                mb.AddDome(m * At(p), Range(rng, 1.6f, 2.4f) * scale, Range(rng, 1.4f, 2f) * scale, 7, Color.Lerp(canopy, _theme.propLight, i * 0.08f), Opaque);
            }
            if (!Minimal) GlowCross(mb, m, new Vector3(0f, 5.2f * scale, 0f), 6f * scale, _theme.propGlow * 0.5f, ProceduralTextures.FxCell.Petal, 0.4f);
        }

        // ------------------------------------------------------------------ Dalmaru: tile roofs under the moon

        void Hanok(MeshBuilder mb, IRandomSource rng, float t0, float t1)
        {
            int houses = Scaled(2 + rng.NextInt(3));
            for (int k = 0; k < houses; k++)
            {
                int side = Side(rng);
                var m = Frame(Range(rng, t0, t1), side * Range(rng, 13f, 32f), -0.5f, -side * 90f + Range(rng, -10f, 10f));
                HanokHouse(mb, m, Range(rng, 7f, 11f), Range(rng, 4.5f, 6f));
            }
            int totems = Scaled(1 + rng.NextInt(2));
            for (int k = 0; k < totems; k++)
            {
                float t = Range(rng, t0, t1);
                // Guardian posts stand in pairs, one either side of the way.
                for (int side = -1; side <= 1; side += 2) Totem(mb, Frame(t, side * (TrackPath.RoadHalfWidth + 2.8f), 0f, -side * 90f));
            }
            int pines = Scaled(4 + rng.NextInt(4));
            for (int k = 0; k < pines; k++)
                Pine(mb, Frame(Range(rng, t0, t1), Side(rng) * Range(rng, 38f, 110f), -6f, Range(rng, 0f, 360f)), Range(rng, 10f, 18f),
                    _theme.propDark, Color.Lerp(_theme.propDark, new Color(0.25f, 0.4f, 0.35f), 0.6f));
            if (Minimal) return;
            // Goblin fires bobbing among the houses.
            int fires = 2 + rng.NextInt(3);
            for (int k = 0; k < fires; k++)
            {
                var m = Frame(Range(rng, t0, t1), Side(rng) * Range(rng, 8f, 26f), Range(rng, 1f, 5f));
                GlowCross(mb, m, Vector3.zero, Range(rng, 1f, 1.6f), _theme.Accent, ProceduralTextures.FxCell.Flame, 1f);
            }
            if (rng.NextInt(3) == 0) LanternString(mb, Range(rng, t0, t1));
        }

        void HanokHouse(MeshBuilder mb, Matrix4x4 m, float width, float depth)
        {
            Color32 wall = _theme.propLight, tile = _theme.propDark;
            Color32 wood = Color.Lerp(_theme.propDark, new Color(0.35f, 0.22f, 0.14f), 0.7f);
            mb.AddBox(m * At(new Vector3(0f, 0.3f, 0f)), new Vector3(width + 1f, 0.6f, depth + 1f), Color.Lerp(_theme.propDark, _theme.propLight, 0.35f), Opaque);
            mb.AddBox(m * At(new Vector3(0f, 1.9f, 0f)), new Vector3(width, 2.6f, depth), wall, Opaque);
            for (int i = -1; i <= 1; i++)
                mb.AddBox(m * At(new Vector3(i * width * 0.45f, 1.9f, depth * 0.5f + 0.05f)), new Vector3(0.3f, 2.6f, 0.3f), wood, Opaque);
            // Paper doors glowing warm from inside.
            for (int i = -1; i <= 1; i += 2)
                GlowCross(mb, m, new Vector3(i * width * 0.22f, 1.9f, depth * 0.5f + 0.2f), 1.5f, _theme.propGlow, ProceduralTextures.FxCell.Square, 0.7f);
            HipRoof(mb, m, 3.2f, width + 2.6f, depth + 2.4f, 2.2f, tile, true);
            mb.AddBox(m * At(new Vector3(0f, 5.45f, 0f)), new Vector3(width * 0.55f, 0.3f, 0.4f), Color.Lerp(_theme.propDark, _theme.propLight, 0.3f), Opaque);
        }

        void Totem(MeshBuilder mb, Matrix4x4 m)
        {
            Color32 wood = Color.Lerp(_theme.propDark, new Color(0.45f, 0.3f, 0.2f), 0.75f), hat = _theme.propDark;
            mb.AddCylinder(m, 0.38f, 0.34f, 4.2f, 7, wood, wood, Opaque);
            mb.AddBox(m * At(new Vector3(0f, 4.6f, 0f)), new Vector3(0.95f, 1.1f, 0.9f), wood, Opaque);
            mb.AddCylinder(m * At(new Vector3(0f, 5.1f, 0f)), 0.95f, 0.9f, 0.15f, 8, hat, hat, Opaque);
            mb.AddCylinder(m * At(new Vector3(0f, 5.25f, 0f)), 0.4f, 0.38f, 0.7f, 8, hat, hat, Opaque);
            // Painted eyes catch the goblin light.
            for (int s = -1; s <= 1; s += 2)
                GlowCross(mb, m, new Vector3(s * 0.22f, 4.75f, 0.5f), 0.35f, _theme.Accent, ProceduralTextures.FxCell.Dot, 1f);
        }

        /// <summary>Strings of coloured lotus lanterns across the road.</summary>
        void LanternString(MeshBuilder mb, float t)
        {
            var m = Frame(t, 0f, 0f);
            float half = TrackPath.RoadHalfWidth + 2f;
            GlowBar(mb, m.MultiplyPoint3x4(new Vector3(-half, 9f, 0f)), m.MultiplyPoint3x4(new Vector3(half, 9f, 0f)), 0.08f, _theme.propGlow * 0.6f, 0.2f);
            for (int i = 0; i < 7; i++)
            {
                float k = (i + 0.5f) / 7f;
                Color c = Color.HSVToRGB(Mathf.Repeat(0.02f + i * 0.13f, 1f), 0.55f, 1f);
                GlowCross(mb, m, new Vector3(Mathf.Lerp(-half, half, k), 8.2f - Mathf.Sin(k * Mathf.PI) * 1.2f, 0f), 1.6f, c, ProceduralTextures.FxCell.Lantern, 0.7f);
            }
        }

        /// <summary>Red gate of spiked uprights between two round pillars, a guardian jewel on top.</summary>
        void RedArrowGate(MeshBuilder mb, float t)
        {
            var m = Frame(t, 0f, 0f);
            float half = TrackPath.RoadHalfWidth + 1.4f;
            const float h = 10f;
            Color32 red = Color.Lerp(new Color(0.75f, 0.12f, 0.1f), _theme.propDark, 0.15f);
            for (int side = -1; side <= 1; side += 2)
                mb.AddCylinder(m * At(new Vector3(side * half, -0.5f, 0f)), 0.45f, 0.4f, h + 0.5f, 8, red, red, Opaque);
            mb.AddBox(m * At(new Vector3(0f, h - 2.4f, 0f)), new Vector3(half * 2f + 1f, 0.4f, 0.4f), red, Opaque);
            mb.AddBox(m * At(new Vector3(0f, h - 0.3f, 0f)), new Vector3(half * 2f + 1f, 0.4f, 0.4f), red, Opaque);
            const int spikes = 13;
            for (int i = 0; i < spikes; i++)
            {
                float x = Mathf.Lerp(-half + 0.6f, half - 0.6f, i / (spikes - 1f));
                mb.AddBox(m * At(new Vector3(x, h - 1.3f, 0f)), new Vector3(0.12f, 2.6f, 0.12f), red, Opaque);
                mb.AddBox(m * At(new Vector3(x, h + 0.3f, 0f)), new Vector3(0.1f, 0.8f, 0.1f), _theme.propDark, Opaque);
            }
            GlowCross(mb, m, new Vector3(0f, h + 1.4f, 0f), 2.8f, _theme.bigPearl, ProceduralTextures.FxCell.Sparkle, 1f);
            GlowCross(mb, m, new Vector3(0f, h + 1.4f, 0f), 4.4f, _theme.Accent * 0.6f, ProceduralTextures.FxCell.Ring, 0.6f);
        }

        // ------------------------------------------------------------------ Nebelreich: the misty forest

        void Fogwood(MeshBuilder mb, IRandomSource rng, float t0, float t1)
        {
            Color needles = _theme.propDark, needlesLight = Color.Lerp(_theme.propDark, new Color(0.18f, 0.32f, 0.24f), 0.8f);
            int firs = Scaled(7 + rng.NextInt(5));
            for (int k = 0; k < firs; k++)
                Pine(mb, Frame(Range(rng, t0, t1), Side(rng) * Range(rng, 8.5f, 40f), -1f, Range(rng, 0f, 360f)), Range(rng, 9f, 19f), needles, needlesLight);
            int houses = Scaled(1 + rng.NextInt(3));
            for (int k = 0; k < houses; k++)
            {
                int side = Side(rng);
                TimberHouse(mb, Frame(Range(rng, t0, t1), side * Range(rng, 14f, 30f), -0.5f, -side * 90f + Range(rng, -8f, 8f)),
                    Range(rng, 6f, 9f), Range(rng, 5f, 7f), Range(rng, 5.5f, 7.5f));
            }
            if (rng.NextInt(3) == 0)
            {
                int side = Side(rng);
                ClockTower(mb, Frame(Range(rng, t0, t1), side * Range(rng, 18f, 30f), -0.5f, -side * 90f), Range(rng, 16f, 22f));
            }
            if (rng.NextInt(3) == 0 && !Minimal)
                Castle(mb, Frame(Range(rng, t0, t1), Side(rng) * Range(rng, 70f, 140f), -20f, Range(rng, 0f, 360f)));
            if (Minimal) return;
            // Will-o'-wisps low among the trees.
            int wisps = 3 + rng.NextInt(3);
            for (int k = 0; k < wisps; k++)
            {
                var m = Frame(Range(rng, t0, t1), Side(rng) * Range(rng, 8f, 25f), Range(rng, 0.5f, 3f));
                GlowCross(mb, m, Vector3.zero, Range(rng, 0.6f, 1f), _theme.Accent, ProceduralTextures.FxCell.Dot, 1f);
            }
        }

        void TimberHouse(MeshBuilder mb, Matrix4x4 m, float width, float depth, float wallH)
        {
            Color32 plaster = _theme.propLight;
            Color32 beam = Color.Lerp(_theme.propDark, new Color(0.3f, 0.18f, 0.1f), 0.8f);
            Color32 roof = Color.Lerp(_theme.propDark, new Color(0.5f, 0.2f, 0.12f), 0.65f);
            mb.AddBox(m * At(new Vector3(0f, wallH * 0.5f, 0f)), new Vector3(width, wallH, depth), plaster, Opaque);
            float fz = depth * 0.5f + 0.06f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                mb.AddBox(m * At(new Vector3(sx * (width * 0.5f - 0.15f), wallH * 0.5f, fz)), new Vector3(0.3f, wallH, 0.12f), beam, Opaque);
                mb.AddBox(m * Matrix4x4.TRS(new Vector3(sx * width * 0.25f, wallH * 0.25f, fz), Quaternion.Euler(0f, 0f, sx * 40f), Vector3.one),
                    new Vector3(0.25f, wallH * 0.6f, 0.12f), beam, Opaque);
            }
            mb.AddBox(m * At(new Vector3(0f, wallH * 0.5f, fz)), new Vector3(width, 0.3f, 0.12f), beam, Opaque);
            mb.AddBox(m * At(new Vector3(0f, wallH - 0.15f, fz)), new Vector3(width, 0.3f, 0.12f), beam, Opaque);
            for (int sx = -1; sx <= 1; sx += 2)
                GlowCross(mb, m, new Vector3(sx * width * 0.25f, wallH * 0.75f, fz + 0.06f), 1.1f, _theme.propGlow, ProceduralTextures.FxCell.Square, 0.7f);
            // Steep gable roof: a taper narrowing to a ridge, and a chimney.
            float roofH = width * 0.55f;
            mb.AddTaper(m * Up(new Vector3(0f, wallH + roofH * 0.5f, 0f)), new Vector2(width + 0.8f, depth + 0.8f), new Vector2(width + 0.8f, 0.15f), roofH, roof, Opaque);
            mb.AddBox(m * At(new Vector3(width * 0.25f, wallH + roofH * 0.6f, 0f)), new Vector3(0.6f, roofH * 0.8f, 0.6f), beam, Opaque);
        }

        void ClockTower(MeshBuilder mb, Matrix4x4 m, float height)
        {
            Color32 stone = Color.Lerp(_theme.propLight, _theme.propDark, 0.45f);
            Color32 roof = Color.Lerp(_theme.propDark, new Color(0.25f, 0.4f, 0.35f), 0.5f);
            mb.AddBox(m * At(new Vector3(0f, height * 0.5f, 0f)), new Vector3(4f, height, 4f), stone, Opaque);
            var face = new Vector3(0f, height * 0.82f, 2.05f);
            Ring(mb, m, face, 1.2f, 0.18f, 16, _theme.propDark);
            mb.AddBox(m * Matrix4x4.TRS(face + new Vector3(0f, 0f, 0.05f), Quaternion.Euler(0f, 0f, 30f), Vector3.one) * At(new Vector3(0f, 0.4f, 0f)), new Vector3(0.12f, 0.8f, 0.05f), _theme.propDark, Opaque);
            mb.AddBox(m * Matrix4x4.TRS(face + new Vector3(0f, 0f, 0.05f), Quaternion.Euler(0f, 0f, -110f), Vector3.one) * At(new Vector3(0f, 0.3f, 0f)), new Vector3(0.12f, 0.6f, 0.05f), _theme.propDark, Opaque);
            GlowCross(mb, m, face + new Vector3(0f, 0f, 0.1f), 2.6f, _theme.propGlow, ProceduralTextures.FxCell.Dot, 0.6f);
            mb.AddCylinder(m * At(new Vector3(0f, height, 0f)), 3.2f, 0.05f, height * 0.45f, 4, roof, roof, Opaque);
        }

        void Castle(MeshBuilder mb, Matrix4x4 m)
        {
            Color32 rock = Color.Lerp(_theme.propDark, _theme.fog, 0.35f);
            Color32 stone = Color.Lerp(_theme.propLight, _theme.propDark, 0.55f);
            Color32 roof = Color.Lerp(_theme.propDark, new Color(0.2f, 0.25f, 0.45f), 0.6f);
            mb.AddTaper(m * Up(new Vector3(0f, 10f, 0f)), new Vector2(40f, 30f), new Vector2(26f, 20f), 20f, rock, Opaque);
            mb.AddBox(m * At(new Vector3(0f, 28f, 0f)), new Vector3(16f, 16f, 12f), stone, Opaque);
            HipRoof(mb, m, 36f, 17f, 13f, 6f, roof, false);
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3(i % 2 == 0 ? -9f : 9f, 20f, i < 2 ? -7f : 7f);
                float th = 18f + i * 3f;
                mb.AddCylinder(m * At(p), 2.6f, 2.4f, th, 8, stone, stone, Opaque);
                mb.AddCylinder(m * At(p + Vector3.up * th), 3.2f, 0.05f, 8f, 8, roof, roof, Opaque);
                GlowCross(mb, m, p + new Vector3(0f, th * 0.75f, -2.6f), 1.4f, _theme.propGlow, ProceduralTextures.FxCell.Square, 0.6f);
            }
        }

        /// <summary>Pointed stone arch with a glowing rose window.</summary>
        void StoneArch(MeshBuilder mb, float t)
        {
            var m = Frame(t, 0f, 0f);
            float half = TrackPath.RoadHalfWidth + 1.4f;
            const float h = 9f;
            float rise = half * 0.8f;
            Color32 stone = Color.Lerp(_theme.propLight, _theme.propDark, 0.55f);
            for (int side = -1; side <= 1; side += 2)
            {
                mb.AddBox(m * At(new Vector3(side * half, h * 0.5f - 0.5f, 0f)), new Vector3(1.4f, h + 1f, 1.4f), stone, Opaque);
                float angle = Mathf.Atan2(rise, half) * Mathf.Rad2Deg;
                float length = Mathf.Sqrt(half * half + rise * rise) + 0.6f;
                mb.AddBox(m * Matrix4x4.TRS(new Vector3(side * half * 0.5f, h + rise * 0.5f, 0f), Quaternion.Euler(0f, 0f, -side * angle), Vector3.one),
                    new Vector3(length, 0.9f, 1.2f), stone, Opaque);
            }
            GlowCross(mb, m, new Vector3(0f, h + rise * 0.35f, 0f), 3.2f, _theme.propGlow, ProceduralTextures.FxCell.Ring, 0.9f);
            GlowCross(mb, m, new Vector3(0f, h + rise * 0.35f, 0f), 1.6f, _theme.Accent, ProceduralTextures.FxCell.Diamond, 1f);
            if (!Minimal)
                for (int side = -1; side <= 1; side += 2)
                    GlowCross(mb, m, new Vector3(side * (half - 1.2f), h - 1.5f, -0.6f), 2.2f, _theme.propGlow, ProceduralTextures.FxCell.Lantern, 0.8f);
        }

        // ------------------------------------------------------------------ Lumikaari: lakes under the aurora

        void Boreal(MeshBuilder mb, IRandomSource rng, float t0, float t1)
        {
            int pines = Scaled(4 + rng.NextInt(4));
            for (int k = 0; k < pines; k++)
                Pine(mb, Frame(Range(rng, t0, t1), Side(rng) * Range(rng, 10f, 60f), -1.5f, Range(rng, 0f, 360f)), Range(rng, 8f, 17f), _theme.propDark, _theme.propLight);
            int birches = Scaled(4 + rng.NextInt(4));
            for (int k = 0; k < birches; k++)
                Birch(mb, rng, Frame(Range(rng, t0, t1), Side(rng) * Range(rng, 8.5f, 30f), -1f), Range(rng, 9f, 15f));
            if (rng.NextInt(2) == 0)
            {
                // A frozen lake on the low ground.
                var m = Frame(Range(rng, t0, t1), Side(rng) * Range(rng, 30f, 60f), -3f, Range(rng, 0f, 360f));
                mb.AddBox(m, new Vector3(Range(rng, 20f, 40f), 0.2f, Range(rng, 14f, 30f)), Color.Lerp(_theme.Accent, _theme.propDark, 0.55f), Opaque);
            }
            if (rng.NextInt(3) == 0)
            {
                int side = Side(rng);
                Sauna(mb, Frame(Range(rng, t0, t1), side * Range(rng, 14f, 26f), -0.8f, -side * 90f));
            }
            if (Minimal) return;
            int glints = 2 + rng.NextInt(3);
            for (int k = 0; k < glints; k++)
            {
                var m = Frame(Range(rng, t0, t1), Side(rng) * Range(rng, 7f, 14f), Range(rng, 0.3f, 1.5f));
                GlowCross(mb, m, Vector3.zero, Range(rng, 0.6f, 1f), _theme.Accent, ProceduralTextures.FxCell.Sparkle, 1f);
            }
        }

        void Birch(MeshBuilder mb, IRandomSource rng, Matrix4x4 m, float height)
        {
            Color32 bark = Color.Lerp(_theme.propLight, Color.white, 0.2f), mark = _theme.propDark;
            var lean = m * Matrix4x4.Rotate(Quaternion.Euler(Range(rng, -4f, 4f), 0f, Range(rng, -5f, 5f)));
            mb.AddCylinder(lean, 0.22f, 0.15f, height, 5, bark, bark, Opaque);
            for (int i = 0; i < 4; i++)
                mb.AddBox(lean * At(new Vector3(0f, height * (0.15f + i * 0.2f) + Range(rng, -0.4f, 0.4f), 0.17f)), new Vector3(0.3f, 0.12f, 0.12f), mark, Opaque);
            // Bare winter branches reaching up and out from the upper trunk.
            Color32 twig = Color.Lerp(_theme.propLight, _theme.propDark, 0.45f);
            for (int i = 0; i < 5; i++)
            {
                float y = height * (0.55f + i * 0.09f);
                float yaw = i * 137f + Range(rng, -20f, 20f);
                float length = height * (0.28f - i * 0.035f);
                var branch = lean * Matrix4x4.TRS(new Vector3(0f, y, 0f), Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-50f, 0f, 0f), Vector3.one);
                mb.AddTaper(branch * At(new Vector3(0f, 0f, length * 0.5f)), new Vector2(0.09f, 0.09f), new Vector2(0.03f, 0.03f), length, twig, Opaque);
            }
        }

        void Sauna(MeshBuilder mb, Matrix4x4 m)
        {
            Color32 logs = Color.Lerp(_theme.propDark, new Color(0.4f, 0.25f, 0.15f), 0.75f);
            mb.AddBox(m * At(new Vector3(0f, 1.5f, 0f)), new Vector3(5f, 3f, 4f), logs, Opaque);
            for (int i = 0; i < 4; i++)
                mb.AddBox(m * At(new Vector3(0f, 0.35f + i * 0.7f, 2.05f)), new Vector3(5.2f, 0.12f, 0.1f), MeshBuilder.Shade(logs, 0.6f), Opaque);
            mb.AddTaper(m * Up(new Vector3(0f, 3.9f, 0f)), new Vector2(6f, 5f), new Vector2(6f, 0.2f), 1.8f, _theme.propLight, Opaque);
            GlowCross(mb, m, new Vector3(1.2f, 1.6f, 2.1f), 1.2f, _theme.propGlow, ProceduralTextures.FxCell.Square, 0.9f);
            mb.AddBox(m * At(new Vector3(-1.6f, 4.2f, 0f)), new Vector3(0.5f, 1.6f, 0.5f), MeshBuilder.Shade(logs, 0.5f), Opaque);
            if (!Minimal) GlowCross(mb, m, new Vector3(-1.6f, 5.4f, 0f), 1.2f, _theme.propGlow * 0.6f, ProceduralTextures.FxCell.Flame, 0.6f);
            mb.AddBox(m * At(new Vector3(0f, 0.2f, -4f)), new Vector3(1.4f, 0.15f, 5f), logs, Opaque);
        }

        /// <summary>Half-ring of ice over the road, glinting.</summary>
        void IceArch(MeshBuilder mb, float t)
        {
            var m = Frame(t, 0f, 0f);
            float radius = TrackPath.RoadHalfWidth + 2f;
            Color32 ice = Color.Lerp(_theme.Accent, Color.white, 0.35f);
            Ring(mb, m, Vector3.zero, radius, 1f, 16, ice, 0f, 180f);
            Ring(mb, m, Vector3.zero, radius + 1.6f, 0.45f, 18, Color.Lerp(_theme.Accent, _theme.propDark, 0.3f), 0f, 180f);
            for (int i = 0; i < 7; i++)
            {
                float a = (i + 0.5f) / 7f * Mathf.PI;
                GlowCross(mb, m, new Vector3(Mathf.Cos(a) * (radius - 0.6f), Mathf.Sin(a) * (radius - 0.6f), -0.4f), 1.6f, _theme.Accent, ProceduralTextures.FxCell.Sparkle, 0.9f);
            }
        }
    }
}
