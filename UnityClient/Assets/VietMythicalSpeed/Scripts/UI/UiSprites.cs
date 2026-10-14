using UnityEngine;

namespace VMS.UI
{
    /// <summary>UI sprites generated once at startup (rounded panels, circles, rings, glows, patterns).</summary>
    public static class UiSprites
    {
        static Sprite _rounded, _roundedSmall, _roundedOutline, _roundedSmallOutline, _circle, _ring, _glow, _gradient, _hatch, _halo, _white;

        /// <summary>White rounded rectangle, 9-sliced (corner radius 28 px at 1080p).</summary>
        public static Sprite Rounded => _rounded ? _rounded : (_rounded = RoundedRect(96, 28));

        /// <summary>4 px outline with the same corners as <see cref="Rounded"/> – selection and rarity frames.</summary>
        public static Sprite RoundedOutline => _roundedOutline ? _roundedOutline : (_roundedOutline = RoundedRect(96, 28, 4f));

        public static Sprite RoundedSmallOutline => _roundedSmallOutline ? _roundedSmallOutline : (_roundedSmallOutline = RoundedRect(48, 12, 3f));

        public static Sprite RoundedSmall => _roundedSmall ? _roundedSmall : (_roundedSmall = RoundedRect(48, 12));

        public static Sprite Circle => _circle ? _circle : (_circle = Make(128, (u, v) => Edge(1f - Len(u, v), 128), "Circle"));

        public static Sprite Ring => _ring ? _ring : (_ring = Make(128, (u, v) =>
        {
            float r = Len(u, v);
            return Mathf.Min(Edge(1f - r, 128), Edge(r - 0.84f, 128));
        }, "Ring"));

        public static Sprite Glow => _glow ? _glow : (_glow = Make(128, (u, v) => Mathf.Pow(Mathf.Clamp01(1f - Len(u, v)), 2f), "Glow"));

        /// <summary>White at the top fading to transparent at the bottom; tint with Image.color.</summary>
        public static Sprite Gradient => _gradient ? _gradient : (_gradient = Make(8, 128, (x, y) => y, "Gradient"));

        /// <summary>Diagonal hatch, tiled behind headers (a Blue Archive staple).</summary>
        public static Sprite Hatch => _hatch ? _hatch : (_hatch = Make(32, 32, (x, y) =>
        {
            float d = Mathf.Repeat((x + y) * 32f, 16f);
            return d < 6f ? 0.18f : 0f;
        }, "Hatch", TextureWrapMode.Repeat));

        /// <summary>Thin broken halo ring, the emblem above navigator portraits.</summary>
        public static Sprite Halo => _halo ? _halo : (_halo = Make(256, (u, v) =>
        {
            float r = Len(u, v * 2.6f);
            float ring = Mathf.Min(Edge(0.95f - r, 256), Edge(r - 0.82f, 256));
            float a = Mathf.Atan2(v, u);
            float gaps = Mathf.Repeat(a / (Mathf.PI * 2f) * 6f + 0.25f, 1f) < 0.12f ? 0f : 1f;
            return ring * gaps;
        }, "Halo"));

        public static Sprite White => _white ? _white : (_white = Make(4, 4, (x, y) => 1f, "White"));

        // ------------------------------------------------------------------ cosmetics (shop items are drawn from these)

        static readonly System.Collections.Generic.Dictionary<string, Sprite> Shaped = new System.Collections.Generic.Dictionary<string, Sprite>();

        static Sprite Cached(string key, System.Func<Sprite> make)
        {
            if (Shaped.TryGetValue(key, out var s) && s) return s;
            s = make();
            Shaped[key] = s;
            return s;
        }

        static float Band(float r, float inner, float outer, int size) => Mathf.Min(Edge(outer - r, size), Edge(r - inner, size));

        /// <summary>Avatar frame ring: ring | double | petal | cloud | scale | flame | sakura | gear | snow. Transparent centre.</summary>
        public static Sprite FrameRing(string shape)
        {
            shape = string.IsNullOrEmpty(shape) ? "ring" : shape;
            return Cached("frame:" + shape, () => Make(256, (u, v) =>
            {
                float r = Len(u, v);
                float a = Mathf.Atan2(v, u);
                switch (shape)
                {
                    case "double": return Mathf.Max(Band(r, 0.9f, 0.99f, 256), Band(r, 0.8f, 0.85f, 256));
                    case "petal": return Band(r, 0.8f, 0.84f + 0.15f * Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 4f)), 3f), 256);
                    case "cloud": return Band(r, 0.8f, 0.87f + 0.1f * Mathf.Abs(Mathf.Sin(a * 6f)), 256);
                    case "scale": return Band(r, 0.8f, 0.9f + 0.08f * (1f - Mathf.Abs(Mathf.Sin(a * 9f))), 256);
                    case "flame": return Band(r, 0.8f, 0.85f + 0.14f * Mathf.Pow(Mathf.Abs(Mathf.Sin(a * 7f + 0.6f * Mathf.Sin(a * 3f))), 0.6f), 256);
                    case "sakura":
                    {
                        // Five round petals, each with the little notch at its tip.
                        float lobe = Mathf.Abs(Mathf.Cos(a * 2.5f));
                        return Band(r, 0.8f, 0.82f + 0.15f * Mathf.Pow(lobe, 0.7f) - 0.06f * Mathf.Pow(lobe, 40f), 256);
                    }
                    case "gear":
                    {
                        float tooth = Mathf.Repeat(a / (Mathf.PI * 2f) * 16f, 1f);
                        float lift = Mathf.Clamp01(Mathf.Min(tooth, 0.55f - tooth) * 20f) * 0.11f;
                        return Mathf.Max(Band(r, 0.8f, 0.86f + lift, 256), Band(r, 0.66f, 0.7f, 256) * 0.8f);
                    }
                    case "snow":
                        return Band(r, 0.8f, 0.84f + 0.15f * Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 3f)), 12f) + 0.05f * Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 9f)), 20f), 256);
                    default: return Band(r, 0.84f, 0.97f, 256);
                }
            }, "Frame_" + shape));
        }

        /// <summary>Nameplate pattern, white (tint it): hatch | wave | lattice | drum | stars | ink | arcs | glass | birch; null for plain.</summary>
        public static Sprite Pattern(string name)
        {
            switch (name)
            {
                case "hatch": return Hatch;
                case "wave":
                    return Cached("pattern:wave", () => Make(64, 32, (x, y) =>
                    {
                        float w = 0.5f + 0.28f * Mathf.Sin(x * Mathf.PI * 2f);
                        return Mathf.Abs(y - w) < 0.07f ? 0.6f : 0f;
                    }, "Waves", TextureWrapMode.Repeat));
                case "lattice":
                    return Cached("pattern:lattice", () => Make(32, 32, (x, y) =>
                        Mathf.Abs(x - 0.5f) < 0.07f || Mathf.Abs(y - 0.5f) < 0.07f ? 0.5f : 0f, "Lattice", TextureWrapMode.Repeat));
                case "stars":
                    return Cached("pattern:stars", () => Make(64, 64, (x, y) =>
                    {
                        float best = 0f;
                        // Four-pointed sparkles at fixed spots of the tile.
                        float[] px = { 0.2f, 0.7f, 0.45f, 0.9f };
                        float[] py = { 0.25f, 0.15f, 0.7f, 0.6f };
                        for (int i = 0; i < 4; i++)
                        {
                            float dx = Mathf.Abs(x - px[i]), dy = Mathf.Abs(y - py[i]);
                            float star = Mathf.Clamp01(1f - (dx * dy) * 900f - (dx + dy) * 9f);
                            best = Mathf.Max(best, star);
                        }
                        return best * 0.8f;
                    }, "Stars", TextureWrapMode.Repeat));
                case "ink":
                    // Ink-wash mountains: a near ridge and a paler far one.
                    return Cached("pattern:ink", () => Make(128, 32, (x, y) =>
                    {
                        float near = 0.18f + 0.3f * Mathf.Pow(Mathf.Abs(Mathf.Sin(x * Mathf.PI * 2f)), 1.6f) + 0.05f * Mathf.Sin(x * Mathf.PI * 14f);
                        float far = 0.35f + 0.4f * Mathf.Pow(Mathf.Abs(Mathf.Sin(x * Mathf.PI * 3f + 1f)), 2.2f);
                        return y < near ? 0.7f : y < far ? 0.3f : 0f;
                    }, "InkPeaks", TextureWrapMode.Repeat));
                case "arcs":
                    // Overlapping wave arcs (fish-scale waves); upper rows cover lower ones.
                    return Cached("pattern:arcs", () => Make(64, 32, (x, y) =>
                    {
                        float value = 0f;
                        float[] cx = { 0f, 1f, 0.5f, 0f, 1f };
                        float[] cy = { 0f, 0f, 0.5f, 1f, 1f };
                        for (int i = 0; i < cx.Length; i++)
                        {
                            float dx = (x - cx[i]) * 1f, dy = (y - cy[i]) * 0.5f;
                            float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                            if (d < 0.5f) value = Mathf.Repeat(d * 8f, 1f) < 0.3f ? 0.65f : 0.08f;
                        }
                        return value;
                    }, "Arcs", TextureWrapMode.Repeat));
                case "glass":
                    // Stained glass: diamond panes of two shades in dark leading.
                    return Cached("pattern:glass", () => Make(64, 64, (x, y) =>
                    {
                        float a = Mathf.Repeat(x + y, 1f), b = Mathf.Repeat(x - y, 1f);
                        bool lead = Mathf.Abs(a - 0.5f) > 0.45f || Mathf.Abs(b - 0.5f) > 0.45f;
                        if (lead) return 0.9f;
                        return Mathf.Repeat(Mathf.Floor(x + y) + Mathf.Floor(x - y + 1f), 2f) < 1f ? 0.35f : 0.12f;
                    }, "Glass", TextureWrapMode.Repeat));
                case "birch":
                    // Dark lenticels scattered across white bark.
                    return Cached("pattern:birch", () => Make(64, 64, (x, y) =>
                    {
                        float[] rows = { 0.12f, 0.31f, 0.47f, 0.66f, 0.83f };
                        float[] from = { 0.1f, 0.55f, 0.25f, 0.65f, 0.05f };
                        float[] to = { 0.4f, 0.85f, 0.45f, 0.95f, 0.3f };
                        for (int i = 0; i < rows.Length; i++)
                            if (Mathf.Abs(y - rows[i]) < 0.025f && x > from[i] && x < to[i]) return 0.85f;
                        return 0f;
                    }, "Birch", TextureWrapMode.Repeat));
                case "drum":
                    // Đông Sơn bronze drum face: a sun star in the middle, rings and a band of ticks.
                    return Cached("pattern:drum", () => Make(256, (u, v) =>
                    {
                        float r = Len(u, v);
                        if (r > 1f) return 0f;
                        float a = Mathf.Atan2(v, u) / (Mathf.PI * 2f);
                        float rings = Mathf.Repeat(r * 6f, 1f) < 0.1f ? 0.6f : 0f;
                        float ticks = r > 0.5f && r < 0.66f && Mathf.Repeat(a * 32f, 1f) < 0.35f ? 0.5f : 0f;
                        float sun = r < 0.22f ? (Mathf.Repeat(a * 14f, 1f) < 0.5f ? 0.8f : 0.35f) : 0f;
                        return Mathf.Max(rings, Mathf.Max(ticks, sun));
                    }, "Drum"));
                default: return null;
            }
        }

        /// <summary>Face parts for navigator expressions (placeholder art).</summary>
        public static Sprite Smile => Cached("face:smile", () => Make(64, (u, v) => v < 0.05f ? Band(Len(u, v * 1.2f), 0.55f, 0.85f, 64) : 0f, "Smile"));

        public static Sprite ClosedEye => Cached("face:arc", () => Make(64, (u, v) => v > -0.05f ? Band(Len(u, v * 1.2f), 0.55f, 0.88f, 64) : 0f, "ClosedEye"));

        public static Sprite OpenMouth => Cached("face:open", () => Make(64, (u, v) => v < 0.1f ? Edge(0.9f - Len(u, v * 1.4f), 64) : 0f, "OpenMouth"));

        public static Sprite Heart => Cached("face:heart", () => Make(64, (u, v) =>
        {
            float x = u * 1.25f, y = v * 1.25f + 0.2f;
            float f = Mathf.Pow(x * x + y * y - 1f, 3f) - x * x * y * y * y;
            return Mathf.Clamp01(-f * 6f);
        }, "Heart"));

        static float Len(float u, float v) => Mathf.Sqrt(u * u + v * v);

        static float Edge(float signed, int size) => Mathf.Clamp01(signed * size * 0.5f + 0.5f);

        /// <param name="stroke">0 for a filled shape, otherwise the outline width in pixels.</param>
        static Sprite RoundedRect(int size, int radius, float stroke = 0f)
        {
            var tex = NewTexture(size, size, TextureWrapMode.Clamp);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    // Distance to the outer edge, also for the straight sides (where d is 0).
                    float edge = Mathf.Min(radius - d, Mathf.Min(Mathf.Min(x + 0.5f, size - x - 0.5f), Mathf.Min(y + 0.5f, size - y - 0.5f)));
                    float alpha = Mathf.Clamp01(radius - d + 0.5f);
                    if (stroke > 0f) alpha *= Mathf.Clamp01(stroke - edge + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * alpha));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var border = new Vector4(radius, radius, radius, radius);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            sprite.name = "Rounded" + radius;
            return sprite;
        }

        delegate float Shape(float u, float v);

        static Sprite Make(int size, Shape shape, string name)
        {
            return Make(size, size, (x, y) => shape(x * 2f - 1f, y * 2f - 1f), name);
        }

        static Sprite Make(int w, int h, Shape shape, string name, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            var tex = NewTexture(w, h, wrap);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(shape((x + 0.5f) / w, (y + 0.5f) / h))));
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            return sprite;
        }

        static Texture2D NewTexture(int w, int h, TextureWrapMode wrap) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = wrap, filterMode = FilterMode.Bilinear, name = "UI Procedural" };
    }
}
