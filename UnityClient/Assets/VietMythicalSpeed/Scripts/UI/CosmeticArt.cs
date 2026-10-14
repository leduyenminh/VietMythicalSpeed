using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VMS.Core;
using VMS.Rendering;

namespace VMS.UI
{
    /// <summary>
    /// Draws shop cosmetics from their style data (colours + a named shape/pattern), so every item works
    /// with placeholder art and new items can arrive from the server without a game update.
    /// </summary>
    public static class CosmeticArt
    {
        public static Color Hex(string hex, Color fallback) =>
            !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;

        static ShopCatalog Catalog => GameApp.Instance != null ? GameApp.Instance.Shop : null;

        /// <summary>The player's equipped item of a category (basic item when nothing is equipped).</summary>
        public static ShopItem Equipped(string category, string target = null)
        {
            var catalog = Catalog;
            if (catalog == null) return null;
            return ShopRules.Equipped(GameApp.Instance.Profile, catalog, category, target);
        }

        // ------------------------------------------------------------------ avatar frame

        /// <summary>Circular avatar (the navigator bust) inside an avatar frame.</summary>
        public static RectTransform Avatar(Transform parent, NavigatorDef nav, ShopItem frame, float size)
        {
            var root = Ui.Node("Avatar", parent);
            root.sizeDelta = new Vector2(size, size);
            var s = frame != null ? frame.style : null;
            Color a = Hex(s != null ? s.colorA : null, Color.white);
            Color b = Hex(s != null ? s.colorB : null, UiTheme.Primary);
            Color c = Hex(s != null ? s.colorC : null, UiTheme.Primary);

            var glow = Ui.Image(root, UiSprites.Glow, c.WithAlpha(0.6f), "Glow");
            glow.rectTransform.Fill(-size * 0.16f, -size * 0.16f, -size * 0.16f, -size * 0.16f);
            glow.enabled = !UiTheme.Flat;
            var disc = Ui.Image(root, UiSprites.Circle, Color.white, "Disc");
            disc.rectTransform.Fill(size * 0.1f, size * 0.1f, size * 0.1f, size * 0.1f);
            disc.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            NavigatorArt.Bust(disc.transform, nav, size * 0.8f).Fill();
            var inner = Ui.Image(root, UiSprites.Ring, b, "Inner");
            inner.rectTransform.Fill(size * 0.085f, size * 0.085f, size * 0.085f, size * 0.085f);
            var ring = Ui.Image(root, UiSprites.FrameRing(s != null ? s.shape : null), a, "Frame");
            ring.rectTransform.Fill();
            return root;
        }

        // ------------------------------------------------------------------ nameplate (profile frame)

        /// <summary>Background of a player nameplate: gradient, pattern and an accent edge.</summary>
        public static RectTransform Plate(Transform parent, ShopItem plate, float skew = 30f)
        {
            var s = plate != null ? plate.style : null;
            Color a = Hex(s != null ? s.colorA : null, Color.white);
            Color b = Hex(s != null ? s.colorB : null, UiTheme.PanelAlt);
            Color c = Hex(s != null ? s.colorC : null, UiTheme.Ink);
            var bg = Ui.Skew(parent, a, b, skew, "Plate");
            var pattern = UiSprites.Pattern(s != null ? s.pattern : null);
            if (pattern != null)
            {
                var overlay = Ui.Image(bg.rectTransform, pattern, c.WithAlpha(0.22f), "Pattern");
                overlay.type = s.pattern == "drum" ? Image.Type.Simple : Image.Type.Tiled;
                if (s.pattern == "ink") overlay.color = c.WithAlpha(0.3f);
                overlay.preserveAspect = s.pattern == "drum";
                overlay.rectTransform.Fill(skew, 4f, skew, 4f);
                overlay.raycastTarget = false;
            }
            var edge = Ui.Skew(bg.rectTransform, c, c, skew * 0.15f, "Edge");
            edge.rectTransform.At(0f, 0f, skew * 0.5f, 0f, 10f, 0f, 0f, 0f);
            edge.rectTransform.anchorMax = new Vector2(0f, 1f);
            edge.rectTransform.sizeDelta = new Vector2(10f, 0f);
            return bg.rectTransform;
        }

        public static Color PlateText(ShopItem plate) => Hex(plate != null && plate.style != null ? plate.style.colorC : null, UiTheme.Ink);

        // ------------------------------------------------------------------ speech bubble

        /// <summary>Recolours a navigator speech bubble (bg + skewed tail + text) with a bubble frame.</summary>
        public static void ApplyBubble(RectTransform bubble, Image bg, Graphic tail, Text line, ShopItem frame)
        {
            var s = frame != null ? frame.style : null;
            if (s == null || bg == null) return;
            Color fill = Hex(s.colorA, Color.white), border = Hex(s.colorB, fill), text = Hex(s.colorC, UiTheme.Ink);
            bg.color = fill.WithAlpha(0.96f);
            if (line != null) line.color = text;
            var old = bubble.Find("Border");
            if (old != null) Object.Destroy(old.gameObject);
            if (border != fill)
            {
                var edge = Ui.Image(bubble, UiSprites.RoundedOutline, border, "Border");
                edge.rectTransform.Fill(-2f, -2f, -2f, -2f);
                edge.raycastTarget = false;
                edge.transform.SetSiblingIndex(bg.transform.GetSiblingIndex() + 1);
            }
            if (tail != null)
            {
                tail.color = fill;
                tail.gameObject.SetActive(s.shape != "none" && s.shape != "round");
                var dots = bubble.Find("Dots");
                if (dots != null) Object.Destroy(dots.gameObject);
                if (s.shape == "round")
                {
                    // Thought-bubble dots instead of a pointed tail.
                    var holder = Ui.Node("Dots", bubble);
                    var tr = tail.rectTransform;
                    holder.anchorMin = tr.anchorMin;
                    holder.anchorMax = tr.anchorMax;
                    holder.pivot = tr.pivot;
                    holder.anchoredPosition = tr.anchoredPosition;
                    holder.sizeDelta = tr.sizeDelta;
                    var d1 = Ui.Image(holder, UiSprites.Circle, fill, "Dot1");
                    d1.rectTransform.At(0.5f, 0.5f, 0f, 0f, 22f, 22f);
                    var d2 = Ui.Image(holder, UiSprites.Circle, fill, "Dot2");
                    d2.rectTransform.At(0.5f, 0.5f, tr.pivot.x > 0.5f ? -24f : 24f, -14f, 12f, 12f);
                }
            }
        }

        public static Color BubbleText(ShopItem frame) => Hex(frame != null && frame.style != null ? frame.style.colorC : null, UiTheme.Ink);

        // ------------------------------------------------------------------ garage interior

        /// <summary>Full-size garage backdrop: wall gradient, floor and themed props.</summary>
        public static RectTransform Garage(Transform parent, ShopItem garage)
        {
            var s = garage != null ? garage.style : null;
            Color wall = Hex(s != null ? s.colorA : null, UiTheme.Hex("#F6EAD0"));
            Color floor = Hex(s != null ? s.colorB : null, UiTheme.Hex("#C9A877"));
            Color accent = Hex(s != null ? s.colorC : null, UiTheme.Hex("#7FBF6A"));
            var root = Ui.Node("Garage", parent).Fill();
            Ui.Gradient(root, wall, Color.Lerp(wall, floor, 0.5f), "Wall").rectTransform.Fill();
            var floorRect = Ui.Gradient(root, Color.Lerp(floor, Color.white, 0.12f), Color.Lerp(floor, Color.black, 0.35f), "Floor").rectTransform;
            floorRect.Region(0f, 0f, 1f, 0.3f);
            var line = Ui.Image(root, UiSprites.White, accent.WithAlpha(0.6f), "FloorLine");
            line.rectTransform.Region(0f, 0.3f, 1f, 0.3f);
            line.rectTransform.sizeDelta = new Vector2(0f, 4f);
            var props = Ui.Node("Props", root).Fill();
            string kind = s != null ? s.props : "bamboo";
            switch (kind)
            {
                case "lanterns":
                    for (int i = 0; i < 7; i++)
                    {
                        float x = 0.07f + i * 0.145f;
                        float y = 0.86f - (i % 2) * 0.06f;
                        var string1 = Ui.Image(props, UiSprites.White, accent.WithAlpha(0.5f), "String");
                        string1.rectTransform.At(x, 1f, 0f, 0f, 3f, (1f - y) * 1080f, 0.5f, 1f);
                        var glow = Ui.Image(props, UiSprites.Glow, accent.WithAlpha(0.45f), "Glow");
                        glow.rectTransform.At(x, y, 0f, 0f, 240f, 240f);
                        var lantern = Ui.Image(props, UiSprites.Rounded, Color.Lerp(UiTheme.Hex("#E2463C"), accent, i % 3 == 0 ? 0.6f : 0f), "Lantern");
                        lantern.rectTransform.At(x, y, 0f, 0f, 74f, 96f);
                    }
                    break;
                case "palace":
                    for (int i = 0; i < 6; i++)
                    {
                        var column = Ui.Gradient(props, Color.Lerp(wall, Color.black, 0.15f), Color.Lerp(wall, Color.black, 0.45f), "Column");
                        column.rectTransform.At(0.05f + i * 0.18f, 0.3f, 0f, 0f, 70f, 760f, 0.5f, 0f);
                        var cap = Ui.Image(props, UiSprites.RoundedSmall, accent, "Capital");
                        cap.rectTransform.At(0.05f + i * 0.18f, 0.3f, 0f, 740f, 110f, 26f, 0.5f, 0f);
                    }
                    var beam = Ui.Image(props, UiSprites.White, accent.WithAlpha(0.85f), "Beam");
                    beam.rectTransform.Region(0f, 0.95f, 1f, 0.97f);
                    var drum = Ui.Image(props, UiSprites.Pattern("drum"), accent.WithAlpha(0.25f), "Emblem");
                    drum.rectTransform.At(0.5f, 0.62f, 0f, 0f, 420f, 420f);
                    break;
                case "lotus":
                    for (int i = 0; i < 9; i++)
                    {
                        float x = (i * 0.123f + 0.05f) % 1f;
                        var pad = Ui.Image(props, UiSprites.Circle, UiTheme.Hex("#4FA36B").WithAlpha(0.8f), "Pad");
                        pad.rectTransform.At(x, 0.05f + (i % 3) * 0.08f, 0f, 0f, 150f, 60f);
                        var flower = Ui.Image(props, UiSprites.FrameRing("petal"), accent, "Lotus");
                        flower.rectTransform.At(x, 0.1f + (i % 3) * 0.08f, 0f, 0f, 70f, 70f);
                    }
                    for (int i = 0; i < 5; i++)
                    {
                        var reed = Ui.Image(props, UiSprites.White, UiTheme.Hex("#4FA36B").WithAlpha(0.7f), "Reed");
                        reed.rectTransform.At(0.85f + i * 0.03f, 0.3f, 0f, 0f, 6f, 300f + i * 40f, 0.5f, 0f);
                    }
                    break;
                case "stars":
                    var stars = Ui.Image(props, UiSprites.Pattern("stars"), accent.WithAlpha(0.7f), "Stars");
                    stars.type = Image.Type.Tiled;
                    stars.rectTransform.Region(0f, 0.32f, 1f, 1f);
                    var moon = Ui.Image(props, UiSprites.Glow, Color.white.WithAlpha(0.8f), "Moon");
                    moon.rectTransform.At(0.82f, 0.8f, 0f, 0f, 260f, 260f);
                    var torii = Ui.Image(props, UiSprites.White, accent.WithAlpha(0.35f), "Gate");
                    torii.rectTransform.At(0.5f, 0.3f, 0f, 520f, 900f, 26f, 0.5f, 0f);
                    break;
                case "cloudsea":
                    // Peaks with pavilions rising from a cloud sea, a moon gate on the back wall.
                    var gate = Ui.Image(props, UiSprites.Ring, accent.WithAlpha(0.55f), "MoonGate");
                    gate.rectTransform.At(0.5f, 0.3f, 0f, 120f, 640f, 640f, 0.5f, 0f);
                    for (int i = 0; i < 5; i++)
                    {
                        float x = new[] { 0.06f, 0.2f, 0.8f, 0.93f, 0.68f }[i];
                        float h = new[] { 620f, 460f, 540f, 700f, 360f }[i];
                        var peak = Ui.Gradient(props, Color.Lerp(accent, Color.white, 0.25f), Color.Lerp(accent, Color.black, 0.35f), "Peak");
                        peak.rectTransform.At(x, 0.22f, 0f, 0f, 120f, h, 0.5f, 0f);
                        var roof = Ui.Skew(props, Color.Lerp(accent, Color.black, 0.55f), Color.Lerp(accent, Color.black, 0.55f), 40f, "Pavilion");
                        roof.rectTransform.At(x, 0.22f, 0f, h, 170f, 34f, 0.5f, 0f);
                    }
                    for (int i = 0; i < 6; i++)
                    {
                        var cloud = Ui.Image(props, UiSprites.Glow, Color.white.WithAlpha(0.85f), "Cloud");
                        cloud.rectTransform.At(i / 5f, 0.24f + (i % 2) * 0.04f, 0f, 0f, 520f, 200f);
                    }
                    break;
                case "shrine":
                    // A vermilion gate, stone lanterns and drifting petals.
                    Color vermilion = accent, lacquer = Color.Lerp(accent, Color.black, 0.75f);
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                    {
                        var post = Ui.Image(props, UiSprites.White, vermilion, "Post");
                        post.rectTransform.At(0.5f, 0.3f, s2 * 330f, 0f, 44f, 640f, 0.5f, 0f);
                        var lantern = Ui.Image(props, UiSprites.RoundedSmall, Color.Lerp(wall, Color.white, 0.5f), "StoneLantern");
                        lantern.rectTransform.At(0.5f, 0.3f, s2 * 620f, 0f, 70f, 220f, 0.5f, 0f);
                        var light = Ui.Image(props, UiSprites.Glow, UiTheme.Hex("#FFD27A").WithAlpha(0.6f), "Light");
                        light.rectTransform.At(0.5f, 0.3f, s2 * 620f, 150f, 160f, 160f, 0.5f, 0.5f);
                    }
                    Ui.Image(props, UiSprites.White, vermilion, "Tie").rectTransform.At(0.5f, 0.3f, 0f, 500f, 760f, 34f, 0.5f, 0f);
                    Ui.Image(props, UiSprites.White, vermilion, "Beam").rectTransform.At(0.5f, 0.3f, 0f, 610f, 860f, 34f, 0.5f, 0f);
                    Ui.Skew(props, lacquer, lacquer, -40f, "Top").rectTransform.At(0.5f, 0.3f, 0f, 644f, 940f, 40f, 0.5f, 0f);
                    for (int i = 0; i < 14; i++)
                    {
                        var petal = Ui.Image(props, UiSprites.FrameRing("petal"), UiTheme.Hex("#FFB3CF").WithAlpha(0.8f), "Petal");
                        petal.rectTransform.At((i * 0.137f + 0.04f) % 1f, 0.35f + ((i * 0.29f) % 0.6f), 0f, 0f, 40f, 40f);
                        petal.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i * 37f);
                    }
                    break;
                case "hanok":
                    // Paper-lattice doors glowing under a dark tiled eave.
                    for (int i = 0; i < 4; i++)
                    {
                        float x = 0.2f + i * 0.2f;
                        var door = Ui.Image(props, UiSprites.White, UiTheme.Hex("#FFF2D6"), "Door");
                        door.rectTransform.At(x, 0.3f, 0f, 40f, 300f, 520f, 0.5f, 0f);
                        var lattice = Ui.Image(door.rectTransform, UiSprites.Pattern("lattice"), floor.WithAlpha(0.9f), "Lattice");
                        lattice.type = Image.Type.Tiled;
                        lattice.rectTransform.Fill();
                        var glow = Ui.Image(props, UiSprites.Glow, UiTheme.Hex("#FFD9A0").WithAlpha(0.35f), "Lamp");
                        glow.rectTransform.At(x, 0.3f, 0f, 300f, 420f, 420f, 0.5f, 0.5f);
                        var post = Ui.Image(props, UiSprites.White, floor, "Post");
                        post.rectTransform.At(x - 0.1f, 0.3f, 0f, 0f, 36f, 640f, 0.5f, 0f);
                    }
                    var eave = Ui.Skew(props, accent, Color.Lerp(accent, Color.black, 0.3f), 80f, "Eave");
                    eave.rectTransform.Region(-0.05f, 0.86f, 1.05f, 0.96f);
                    break;
                case "clockwork":
                    // Timber beams, a big brass gear and two little ones.
                    for (int i = 0; i < 5; i++)
                    {
                        var timber = Ui.Image(props, UiSprites.White, floor, "Beam");
                        timber.rectTransform.At(0.1f + i * 0.2f, 0.3f, 0f, 0f, 40f, 760f, 0.5f, 0f);
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        var brace = Ui.Image(props, UiSprites.White, floor, "Brace");
                        brace.rectTransform.At(0.2f + i * 0.2f, 0.62f, 0f, 0f, 30f, 420f);
                        brace.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i % 2 == 0 ? 35f : -35f);
                    }
                    var big = Ui.Image(props, UiSprites.FrameRing("gear"), accent, "Gear");
                    big.rectTransform.At(0.5f, 0.64f, 0f, 0f, 460f, 460f);
                    big.gameObject.AddComponent<Spin>().degreesPerSecond = 8f;
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                    {
                        var small = Ui.Image(props, UiSprites.FrameRing("gear"), Color.Lerp(accent, Color.white, 0.3f), "SmallGear");
                        small.rectTransform.At(0.5f, 0.64f, s2 * 290f, -160f, 200f, 200f);
                        small.gameObject.AddComponent<Spin>().degreesPerSecond = -18f * s2;
                    }
                    break;
                case "sauna":
                    // Log walls and a window full of aurora.
                    for (int i = 0; i < 10; i++)
                    {
                        var log = Ui.Gradient(props, Color.Lerp(wall, Color.white, 0.12f), Color.Lerp(wall, Color.black, 0.35f), "Log");
                        log.rectTransform.Region(0f, 0.3f + i * 0.07f, 1f, 0.3f + i * 0.07f + 0.065f);
                    }
                    var frame = Ui.Image(props, UiSprites.Rounded, Color.Lerp(wall, Color.black, 0.5f), "Window");
                    frame.rectTransform.At(0.5f, 0.62f, 0f, 0f, 760f, 400f);
                    var sky = Ui.Gradient(frame.rectTransform, UiTheme.Hex("#0D1C3A"), UiTheme.Hex("#020814"), "Sky");
                    sky.rectTransform.Fill(18f, 18f, 18f, 18f);
                    for (int i = 0; i < 3; i++)
                    {
                        var band = Ui.Image(sky.rectTransform, UiSprites.Glow, Color.Lerp(accent, UiTheme.Hex("#B57BFF"), i * 0.45f).WithAlpha(0.7f), "Aurora");
                        band.rectTransform.At(0.25f + i * 0.25f, 0.6f - i * 0.08f, 0f, 0f, 420f, 150f);
                        band.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -12f + i * 10f);
                    }
                    var lamp = Ui.Image(props, UiSprites.Glow, UiTheme.Hex("#FFB45A").WithAlpha(0.55f), "Lamp");
                    lamp.rectTransform.At(0.12f, 0.45f, 0f, 0f, 260f, 260f);
                    break;
                default: // bamboo workshop
                    for (int i = 0; i < 12; i++)
                    {
                        float x = i < 6 ? 0.02f + i * 0.035f : 0.78f + (i - 6) * 0.035f;
                        var cane = Ui.Gradient(props, accent, Color.Lerp(accent, Color.black, 0.3f), "Bamboo");
                        cane.rectTransform.At(x, 0.3f, 0f, 0f, 30f, 800f, 0.5f, 0f);
                        for (int k = 1; k < 6; k++)
                        {
                            var node = Ui.Image(cane.rectTransform, UiSprites.White, Color.Lerp(accent, Color.black, 0.45f), "Node");
                            node.rectTransform.At(0.5f, 0f, 0f, k * 140f, 34f, 6f, 0.5f, 0f);
                        }
                    }
                    break;
            }
            var shade = Ui.Image(root, UiSprites.Glow, Color.black.WithAlpha(0.25f), "Shadow");
            shade.rectTransform.At(0.5f, 0.3f, 0f, 0f, 900f, 120f);
            foreach (var g in root.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
            return root;
        }

        // ------------------------------------------------------------------ exhaust and FX cells

        static readonly Dictionary<ProceduralTextures.FxCell, Sprite> CellSprites = new Dictionary<ProceduralTextures.FxCell, Sprite>();

        /// <summary>A sprite cut from the race FX atlas (dots, flames, petals, lanterns…).</summary>
        public static Sprite CellSprite(ProceduralTextures.FxCell cell)
        {
            if (CellSprites.TryGetValue(cell, out var s) && s) return s;
            var atlas = ProceduralTextures.FxAtlas;
            var r = ProceduralTextures.AtlasRect(cell);
            s = Sprite.Create(atlas, new Rect(r.x * atlas.width, r.y * atlas.height, r.width * atlas.width, r.height * atlas.height), new Vector2(0.5f, 0.5f), 100f);
            CellSprites[cell] = s;
            return s;
        }

        public static ProceduralTextures.FxCell CellOf(ShopItem item)
        {
            var s = item != null ? item.style : null;
            return s != null && !string.IsNullOrEmpty(s.cell) && System.Enum.TryParse(s.cell, out ProceduralTextures.FxCell cell) ? cell : ProceduralTextures.FxCell.Dot;
        }

        /// <summary>Flat preview of an exhaust style: a streak from head to tail colour with its embers.</summary>
        public static RectTransform ExhaustSwatch(Transform parent, ShopItem item, float width, float height, Color fallback)
        {
            var s = item != null ? item.style : null;
            Color head = Hex(s != null ? s.colorA : null, fallback);
            Color tail = Hex(s != null ? s.colorB : null, head * 0.6f);
            var root = Ui.Node("Exhaust", parent);
            root.sizeDelta = new Vector2(width, height);
            for (int i = 0; i < 8; i++)
            {
                float k = i / 7f;
                var puff = Ui.Image(root, UiSprites.Glow, Color.Lerp(head, tail, k).WithAlpha(1f - k * 0.7f), "Puff");
                float size = height * Mathf.Lerp(0.9f, 0.35f, k) * Mathf.Lerp(0.8f, 1.4f, s != null ? Mathf.Clamp01(s.width) : 0.5f);
                puff.rectTransform.At(0f, 0.5f, width * (0.12f + 0.8f * k), 0f, size * 1.4f, size);
            }
            if (s != null && s.embers > 0f)
            {
                var sprite = CellSprite(CellOf(item));
                for (int i = 0; i < 6; i++)
                {
                    var ember = Ui.Image(root, sprite, Color.Lerp(head, tail, (i * 0.37f) % 1f), "Ember");
                    ember.rectTransform.At(0f, 0.5f, width * (0.25f + i * 0.12f), height * (((i * 0.53f) % 1f) - 0.5f) * 0.8f, height * 0.32f, height * 0.32f);
                }
            }
            foreach (var g in root.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
            return root;
        }

        // ------------------------------------------------------------------ emotions

        /// <summary>Round face icon showing an emotion's expression.</summary>
        public static RectTransform FaceIcon(Transform parent, string face, float size)
        {
            var root = Ui.Node("Face", parent);
            root.sizeDelta = new Vector2(size, size);
            Ui.Image(root, UiSprites.Circle, new Color(1f, 0.9f, 0.82f), "Skin").rectTransform.Fill();
            var rig = FaceRig.Build(root, size, 0.5f, 0.56f, size * 0.12f, size * 0.15f, size * 0.17f);
            rig.Set(face);
            return root;
        }
    }

    /// <summary>
    /// Eyes, mouth and blush of a placeholder face; <see cref="Set"/> switches between emotion expressions
    /// (smile, happy, laugh, wink, sleepy, pout, surprised, heart).
    /// </summary>
    public sealed class FaceRig : MonoBehaviour
    {
        Image _eyeL, _eyeR, _mouth, _blushL, _blushR;
        Text _extra;
        Vector2 _eyeSize;
        float _unit;
        static readonly Color Ink = new Color(0.15f, 0.12f, 0.25f);
        static readonly Color MouthColor = new Color(0.72f, 0.25f, 0.32f);

        /// <param name="cx">Face centre x as an anchor fraction of <paramref name="parent"/>.</param>
        /// <param name="eyeY">Eye line as an anchor fraction (from the bottom).</param>
        public static FaceRig Build(RectTransform parent, float faceWidth, float cx, float eyeY, float eyeW, float eyeH, float eyeGap)
        {
            var rig = parent.gameObject.AddComponent<FaceRig>();
            rig._eyeSize = new Vector2(eyeW, eyeH);
            rig._unit = faceWidth;
            rig._eyeL = Ui.Image(parent, UiSprites.Circle, Ink, "EyeL");
            rig._eyeL.rectTransform.At(cx, eyeY, -eyeGap, 0f, eyeW, eyeH);
            rig._eyeR = Ui.Image(parent, UiSprites.Circle, Ink, "EyeR");
            rig._eyeR.rectTransform.At(cx, eyeY, eyeGap, 0f, eyeW, eyeH);
            rig._blushL = Ui.Image(parent, UiSprites.Glow, new Color(1f, 0.55f, 0.6f, 0.55f), "BlushL");
            rig._blushL.rectTransform.At(cx, eyeY, -eyeGap * 1.45f, -eyeH * 0.95f, eyeW * 1.6f, eyeH * 0.8f);
            rig._blushR = Ui.Image(parent, UiSprites.Glow, new Color(1f, 0.55f, 0.6f, 0.55f), "BlushR");
            rig._blushR.rectTransform.At(cx, eyeY, eyeGap * 1.45f, -eyeH * 0.95f, eyeW * 1.6f, eyeH * 0.8f);
            rig._mouth = Ui.Image(parent, UiSprites.Smile, MouthColor, "Mouth");
            rig._mouth.rectTransform.At(cx, eyeY, 0f, -eyeH * 1.6f, eyeW * 1.6f, eyeW * 1.1f);
            rig._extra = Ui.Label(parent, string.Empty, Mathf.Max(10, Mathf.RoundToInt(eyeH * 1.6f)), Ink, TextAnchor.MiddleCenter, FontStyle.Bold, "Extra");
            rig._extra.rectTransform.At(cx, eyeY, eyeGap * 2.6f, eyeH * 2.2f, eyeW * 3f, eyeH * 2.5f);
            rig.Set(null);
            return rig;
        }

        void Eye(Image eye, Sprite sprite, Color color, float sx, float sy)
        {
            eye.sprite = sprite;
            eye.color = color;
            eye.rectTransform.sizeDelta = new Vector2(_eyeSize.x * sx, _eyeSize.y * sy);
        }

        void Mouth(Sprite sprite, float sx, float sy, bool visible = true)
        {
            _mouth.gameObject.SetActive(visible);
            _mouth.sprite = sprite;
            _mouth.rectTransform.sizeDelta = new Vector2(_eyeSize.x * 1.6f * sx, _eyeSize.x * 1.1f * sy);
        }

        public void Set(string face)
        {
            Eye(_eyeL, UiSprites.Circle, Ink, 1f, 1f);
            Eye(_eyeR, UiSprites.Circle, Ink, 1f, 1f);
            Mouth(UiSprites.Smile, 1f, 1f, false);
            _blushL.rectTransform.localScale = _blushR.rectTransform.localScale = Vector3.one;
            _extra.text = string.Empty;
            switch (face)
            {
                case "smile":
                    Mouth(UiSprites.Smile, 1f, 1f);
                    break;
                case "happy":
                    Eye(_eyeL, UiSprites.ClosedEye, Ink, 1.6f, 0.9f);
                    Eye(_eyeR, UiSprites.ClosedEye, Ink, 1.6f, 0.9f);
                    Mouth(UiSprites.Smile, 1.1f, 1.1f);
                    break;
                case "laugh":
                    Eye(_eyeL, UiSprites.ClosedEye, Ink, 1.6f, 0.9f);
                    Eye(_eyeR, UiSprites.ClosedEye, Ink, 1.6f, 0.9f);
                    Mouth(UiSprites.OpenMouth, 1.2f, 1.3f);
                    _blushL.rectTransform.localScale = _blushR.rectTransform.localScale = Vector3.one * 1.3f;
                    break;
                case "wink":
                    Eye(_eyeR, UiSprites.ClosedEye, Ink, 1.6f, 0.9f);
                    Mouth(UiSprites.Smile, 1f, 1f);
                    _extra.text = "♪";
                    break;
                case "sleepy":
                    Eye(_eyeL, UiSprites.White, Ink, 1.5f, 0.18f);
                    Eye(_eyeR, UiSprites.White, Ink, 1.5f, 0.18f);
                    Mouth(UiSprites.OpenMouth, 0.5f, 0.6f);
                    _extra.text = "z z";
                    break;
                case "pout":
                    Eye(_eyeL, UiSprites.Circle, Ink, 0.9f, 0.8f);
                    Eye(_eyeR, UiSprites.Circle, Ink, 0.9f, 0.8f);
                    Mouth(UiSprites.White, 0.5f, 0.18f);
                    _blushL.rectTransform.localScale = _blushR.rectTransform.localScale = Vector3.one * 1.5f;
                    _extra.text = "!";
                    break;
                case "surprised":
                    Eye(_eyeL, UiSprites.Circle, Ink, 1.3f, 1.3f);
                    Eye(_eyeR, UiSprites.Circle, Ink, 1.3f, 1.3f);
                    Mouth(UiSprites.Circle, 0.45f, 0.7f);
                    _extra.text = "!?";
                    break;
                case "heart":
                    var pink = new Color(1f, 0.3f, 0.45f);
                    Eye(_eyeL, UiSprites.Heart, pink, 1.7f, 1.4f);
                    Eye(_eyeR, UiSprites.Heart, pink, 1.7f, 1.4f);
                    Mouth(UiSprites.Smile, 1.1f, 1.1f);
                    _blushL.rectTransform.localScale = _blushR.rectTransform.localScale = Vector3.one * 1.4f;
                    break;
            }
        }
    }
}
