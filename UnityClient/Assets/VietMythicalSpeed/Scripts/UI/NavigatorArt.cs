using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VMS.Core;

namespace VMS.UI
{
    /// <summary>
    /// Navigator visuals. Real illustrations are picked up automatically from
    /// Resources/Navigators/{id}/portrait.png (square bust) and standing.png (full body, transparent), and for
    /// outfit skins from Resources/Navigators/{id}/skins/{skinId}/portrait.png and standing.png. Until they exist a
    /// stylised placeholder (halo, silhouette, Hán-Nôm emblem) is drawn in the navigator's – or the skin's – colours.
    /// See docs/ArtDirection.md for the art briefs.
    /// </summary>
    public static class NavigatorArt
    {
        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        static Sprite LoadArt(string path, string kind)
        {
            string key = path + "/" + kind;
            Sprite sprite;
            if (Cache.TryGetValue(key, out sprite)) return sprite;
            var tex = Resources.Load<Texture2D>("Navigators/" + path + "/" + kind);
            sprite = tex != null ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0f), 100f) : null;
            Cache[key] = sprite;
            return sprite;
        }

        public static AudioClip Voice(string navigatorId, string lineId) =>
            Resources.Load<AudioClip>("Navigators/" + navigatorId + "/voice/" + lineId);

        /// <summary>The player's equipped outfit for this navigator (null = the base look).</summary>
        public static ShopItem EquippedSkin(NavigatorDef def) =>
            def != null ? CosmeticArt.Equipped(ShopCategories.NavigatorSkin, def.id) : null;

        struct Palette
        {
            public Color main, accent, hair;
            public bool hasHair;
        }

        static Palette Colours(NavigatorDef def, ShopItem skin)
        {
            var p = new Palette { main = UiTheme.NavigatorColor(def), accent = UiTheme.Accent };
            if (skin != null && skin.style != null)
            {
                p.main = CosmeticArt.Hex(skin.style.colorA, p.main);
                p.accent = CosmeticArt.Hex(skin.style.colorB, p.accent);
                p.hasHair = !string.IsNullOrEmpty(skin.style.colorC);
                p.hair = CosmeticArt.Hex(skin.style.colorC, Color.black);
            }
            return p;
        }

        static Sprite SkinArt(NavigatorDef def, ShopItem skin, string kind) =>
            def == null ? null : skin != null ? LoadArt(def.id + "/skins/" + skin.id, kind) ?? LoadArt(def.id, kind) : LoadArt(def.id, kind);

        /// <summary>Square bust card (gacha results, lists, HUD). Uses the equipped outfit unless one is given.</summary>
        public static RectTransform Portrait(Transform parent, NavigatorDef def, float size, bool locked = false, ShopItem skin = null)
        {
            var root = Ui.Node("Portrait " + (def != null ? def.id : "?"), parent);
            root.sizeDelta = new Vector2(size, size);
            var bg = Ui.Image(root, UiSprites.Rounded, Color.white, "Bg");
            bg.rectTransform.Fill();
            // Square inner window inside the rounded card; no stencil mask so card grids still batch.
            var window = Ui.Node("Window", bg.rectTransform).Fill(10f, 10f, 10f, 10f);
            BustContent(window, def, size - 20f, locked, skin ?? (locked ? null : EquippedSkin(def)));
            var frame = Ui.Image(root, UiSprites.RoundedOutline, Color.white, "Frame");
            frame.rectTransform.Fill(-2f, -2f, -2f, -2f);
            frame.color = locked ? UiTheme.Line : UiTheme.RarityColor(def != null ? def.rarity : 1);
            return root;
        }

        /// <summary>Frameless bust (colour wash + art or silhouette), e.g. inside a round avatar frame.</summary>
        public static RectTransform Bust(Transform parent, NavigatorDef def, float size, ShopItem skin = null)
        {
            var root = Ui.Node("Bust " + (def != null ? def.id : "?"), parent);
            root.sizeDelta = new Vector2(size, size);
            BustContent(root, def, size, false, skin ?? EquippedSkin(def));
            return root;
        }

        static void BustContent(RectTransform window, NavigatorDef def, float size, bool locked, ShopItem skin)
        {
            var pal = Colours(def, skin);
            Color main = pal.main, accent = UiTheme.NavigatorAccent(def);
            if (skin != null) accent = Color.Lerp(pal.accent, Color.white, 0.55f);
            if (locked)
            {
                main = new Color(0.55f, 0.6f, 0.7f);
                accent = new Color(0.85f, 0.88f, 0.92f);
            }
            var wash = Ui.Gradient(window, accent, Color.Lerp(main, Color.white, 0.25f), "Wash");
            wash.rectTransform.Fill();
            var art = !locked ? SkinArt(def, skin, "portrait") : null;
            if (art != null)
            {
                var img = Ui.Image(window, art, Color.white, "Art");
                img.preserveAspect = true;
                img.rectTransform.Fill();
            }
            else
            {
                Silhouette(window, def, main, size, locked, pal);
            }
        }

        static void Silhouette(RectTransform parent, NavigatorDef def, Color main, float size, bool locked, Palette pal)
        {
            Color ink = Color.Lerp(main, new Color(0.1f, 0.12f, 0.25f), 0.55f);
            var halo = Ui.Image(parent, UiSprites.Halo, locked ? Color.white.WithAlpha(0.5f) : pal.accent, "Halo");
            halo.rectTransform.At(0.5f, 1f, 0f, -size * 0.1f, size * 0.5f, size * 0.5f, 0.5f, 1f);
            var head = Ui.Image(parent, UiSprites.Circle, pal.hasHair && !locked ? pal.hair : ink, "Head");
            head.rectTransform.At(0.5f, 0f, 0f, size * 0.38f, size * 0.36f, size * 0.36f, 0.5f, 0f);
            var body = Ui.Image(parent, UiSprites.Rounded, ink, "Body");
            body.rectTransform.At(0.5f, 0f, 0f, 0f, size * 0.66f, size * 0.42f, 0.5f, 0f);
            if (locked) return;
            var ribbon = Ui.Skew(parent, pal.accent, Color.Lerp(pal.accent, Color.black, 0.25f), 14f, "Collar");
            ribbon.rectTransform.At(0.5f, 0f, 0f, size * 0.33f, size * 0.3f, size * 0.06f, 0.5f, 0f);
            if (def != null && !string.IsNullOrEmpty(def.glyph))
            {
                // Han-Nom emblem in the corner, on top so it stays readable over any colour.
                var glyph = Ui.Label(parent, def.glyph, Mathf.RoundToInt(size * 0.2f), Color.white, TextAnchor.UpperRight, FontStyle.Bold, "Glyph");
                glyph.rectTransform.Fill(0f, size * 0.03f, size * 0.05f, 0f);
                glyph.gameObject.AddComponent<Shadow>().effectColor = ink.WithAlpha(0.6f);
            }
        }

        /// <summary>
        /// Full-body lobby figure (home screen, navigator details, gacha reveal). Placeholder figures carry a
        /// <see cref="FaceRig"/> so emotions can change their expression.
        /// </summary>
        public static RectTransform Standing(Transform parent, NavigatorDef def, float height, ShopItem skin = null, bool equipped = true)
        {
            if (skin == null && equipped) skin = EquippedSkin(def);
            var root = Ui.Node("Standing " + (def != null ? def.id : "?"), parent);
            root.sizeDelta = new Vector2(height * 0.62f, height);
            var art = SkinArt(def, skin, "standing");
            if (art != null)
            {
                var img = Ui.Image(root, art, Color.white, "Art");
                img.preserveAspect = true;
                img.rectTransform.Fill();
                return root;
            }

            var pal = Colours(def, skin);
            Color main = pal.main;
            Color ink = Color.Lerp(main, new Color(0.1f, 0.12f, 0.25f), 0.45f);
            float w = height * 0.62f;

            var glow = Ui.Image(root, UiSprites.Glow, main.WithAlpha(0.55f), "Aura");
            glow.rectTransform.At(0.5f, 0.5f, 0f, height * 0.1f, w * 1.6f, w * 1.6f);
            glow.enabled = !UiTheme.Flat;
            if (def != null)
            {
                var glyph = Ui.Label(root, def.glyph, Mathf.RoundToInt(height * 0.42f), main.WithAlpha(0.22f), TextAnchor.MiddleCenter, FontStyle.Bold, "Emblem");
                glyph.rectTransform.At(0.5f, 0.5f, 0f, height * 0.12f, w * 1.4f, height * 0.6f);
            }
            var halo = Ui.Image(root, UiSprites.Halo, pal.accent, "Halo");
            halo.rectTransform.At(0.5f, 1f, 0f, -height * 0.02f, w * 0.5f, w * 0.5f, 0.5f, 1f);
            halo.gameObject.AddComponent<Bob>().amplitude = new Vector2(0f, 6f);

            // Chibi-ish head: long hair behind, face lower than the hair cap, fringe, then the expressive face.
            Color hairColor = pal.hasHair ? pal.hair : Color.Lerp(ink, new Color(0.08f, 0.07f, 0.12f), 0.5f);
            var locks = Ui.Image(root, UiSprites.Rounded, hairColor, "HairBack");
            locks.rectTransform.At(0.5f, 1f, 0f, -height * 0.16f, w * 0.52f, height * 0.3f, 0.5f, 1f);
            var hair = Ui.Image(root, UiSprites.Circle, hairColor, "Hair");
            hair.rectTransform.At(0.5f, 1f, 0f, -height * 0.1f, w * 0.52f, w * 0.52f, 0.5f, 1f);
            var face = Ui.Image(root, UiSprites.Circle, new Color(1f, 0.9f, 0.82f), "Face");
            face.rectTransform.At(0.5f, 1f, 0f, -height * 0.165f, w * 0.4f, w * 0.4f, 0.5f, 1f);
            var fringe = Ui.Skew(root, hairColor, hairColor, w * 0.06f, "Fringe");
            fringe.rectTransform.At(0.5f, 1f, 0f, -height * 0.16f, w * 0.38f, height * 0.045f, 0.5f, 1f);
            FaceRig.Build(face.rectTransform, w * 0.4f, 0.5f, 0.58f, w * 0.05f, w * 0.065f, w * 0.075f);

            // Áo giao lĩnh-like robe: wide sleeves and a long tunic, two slanted panels.
            var robe = Ui.Skew(root, main, Color.Lerp(main, ink, 0.6f), -w * 0.18f, "Robe");
            robe.rectTransform.At(0.5f, 0f, -w * 0.02f, 0f, w * 0.62f, height * 0.62f, 0.5f, 0f);
            var robe2 = Ui.Skew(root, Color.Lerp(main, Color.white, 0.25f), main, w * 0.18f, "RobeFront");
            robe2.rectTransform.At(0.5f, 0f, w * 0.06f, 0f, w * 0.34f, height * 0.6f, 0.5f, 0f);
            var sash = Ui.Skew(root, pal.accent, Color.Lerp(pal.accent, Color.black, 0.25f), 10f, "Sash");
            sash.rectTransform.At(0.5f, 0f, 0f, height * 0.42f, w * 0.6f, height * 0.035f, 0.5f, 0f);
            var sleeveL = Ui.Image(root, UiSprites.Rounded, Color.Lerp(main, ink, 0.3f), "SleeveL");
            sleeveL.rectTransform.At(0.5f, 0f, -w * 0.32f, height * 0.25f, w * 0.26f, height * 0.3f, 0.5f, 0f);
            var sleeveR = Ui.Image(root, UiSprites.Rounded, Color.Lerp(main, ink, 0.3f), "SleeveR");
            sleeveR.rectTransform.At(0.5f, 0f, w * 0.32f, height * 0.25f, w * 0.26f, height * 0.3f, 0.5f, 0f);
            var bob = root.gameObject.AddComponent<Bob>();
            bob.amplitude = new Vector2(0f, 5f);
            bob.speed = 1.1f;
            bob.scalePulse = 0.006f;
            return root;
        }

        /// <summary>Shows an expression on a figure made by <see cref="Standing"/> (no-op for real artwork).</summary>
        public static void SetEmotion(RectTransform figure, string face)
        {
            var rig = figure != null ? figure.GetComponentInChildren<FaceRig>() : null;
            if (rig != null) rig.Set(face);
        }

        /// <summary>Rarity stars plus name, under portraits.</summary>
        public static void NamePlate(Transform parent, NavigatorDef def, float width, bool locked = false)
        {
            var plate = Ui.Node("NamePlate", parent);
            plate.sizeDelta = new Vector2(width, 78f);
            var stars = Ui.Label(plate, UiTheme.RarityStars(def.rarity), 24, locked ? UiTheme.InkFaint : UiTheme.RarityColor(def.rarity), TextAnchor.UpperCenter, FontStyle.Bold, "Stars");
            stars.rectTransform.Fill(0f, 0f, 0f, 44f);
            var name = Ui.FitLabel(plate, locked ? "???" : Loc.Pick(def.name), 28, UiTheme.Ink, TextAnchor.LowerCenter);
            name.rectTransform.Fill(0f, 30f, 0f, 0f);
        }
    }
}
