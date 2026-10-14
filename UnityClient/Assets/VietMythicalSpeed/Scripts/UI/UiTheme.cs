using UnityEngine;
using VMS.Core;

namespace VMS.UI
{
    /// <summary>
    /// Visual language: Blue Archive-style clean white panels, sky-blue primary, yellow highlight and
    /// slanted buttons, with Vietnamese lacquer red and gold as festive accents.
    /// </summary>
    public static class UiTheme
    {
        public static readonly Color Ink = Hex("#22314F");
        public static readonly Color InkSoft = Hex("#5B6B88");
        public static readonly Color InkFaint = Hex("#9AA8BF");
        public static readonly Color Primary = Hex("#2E9BFF");
        public static readonly Color PrimaryDark = Hex("#1773D6");
        public static readonly Color PrimaryLight = Hex("#DDF0FF");
        public static readonly Color Accent = Hex("#FFD23F");
        public static readonly Color AccentDark = Hex("#E8A800");
        public static readonly Color Lacquer = Hex("#D7263D");
        public static readonly Color Gold = Hex("#F2C94C");
        public static readonly Color Panel = Hex("#FFFFFF");
        public static readonly Color PanelAlt = Hex("#F2F7FC");
        public static readonly Color Line = Hex("#D5E3F2");
        public static readonly Color SkyTop = Hex("#BFE3FF");
        public static readonly Color SkyBottom = Hex("#F7FBFF");
        public static readonly Color Scrim = new Color(0.04f, 0.08f, 0.16f, 0.6f);
        public static readonly Color Good = Hex("#2BB673");
        public static readonly Color Bad = Hex("#E5484D");

        /// <summary>
        /// Flat UI (Settings → Graphics): solid colours instead of gradients, no drop shadows, no decorative glows,
        /// patterns or idle animations. Read by the builders, so screens built afterwards follow it.
        /// </summary>
        public static bool Flat;

        public const int TitleSize = 64;
        public const int HeadingSize = 44;
        public const int BodySize = 32;
        public const int SmallSize = 26;

        public static Color RarityColor(int rarity)
        {
            switch (rarity)
            {
                case 3: return Hex("#FFB800");
                case 2: return Hex("#B57BFF");
                default: return Hex("#6FA8DC");
            }
        }

        public static string RarityStars(int rarity) => new string('★', Mathf.Clamp(rarity, 1, 3));

        public static string RarityName(int rarity) => Loc.T("rarity." + Mathf.Clamp(rarity, 1, 3));

        public static Color NavigatorColor(NavigatorDef def) => def != null ? Hex(def.color) : Primary;

        public static Color NavigatorAccent(NavigatorDef def) => def != null && !string.IsNullOrEmpty(def.accentColor) ? Hex(def.accentColor) : Color.white;

        public static Color Hex(string hex)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.magenta;
        }

        public static Color WithAlpha(this Color c, float a) => new Color(c.r, c.g, c.b, a);

        static Font _font;
        static string _fontGroup;

        /// <summary>
        /// UI font for the current language. A bundled font wins: Resources/Fonts/Main (Vietnamese and other
        /// Latin-script languages, e.g. Be Vietnam Pro) and Resources/Fonts/Main_zh / Main_ja / Main_ko (e.g. Noto
        /// Sans SC / JP / KR) for release builds. Otherwise the OS fonts are used: Latin UI fonts first, then the CJK
        /// font of the language, so Japanese, Chinese and Korean each get their own glyph shapes for shared characters.
        /// </summary>
        public static Font Font
        {
            get
            {
                string group = FontGroup(Loc.Language);
                if (_font != null && _fontGroup == group) return _font;
                _fontGroup = group;
                _font = Resources.Load<Font>(group == "latin" ? "Fonts/Main" : "Fonts/Main_" + group);
                if (_font == null)
                {
                    try
                    {
                        _font = Font.CreateDynamicFontFromOSFont(OsFonts(group), 32);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning("[UiTheme] OS font unavailable: " + e.Message);
                    }
                }
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        static string FontGroup(string language)
        {
            switch (language)
            {
                case Loc.Chinese: return "zh";
                case Loc.Japanese: return "ja";
                case Loc.Korean: return "ko";
                default: return "latin";
            }
        }

        static readonly string[] Latin = { "Roboto", "Noto Sans", "Segoe UI", "Arial" };
        static readonly string[] Zh = { "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC", "Noto Sans SC", "Source Han Sans SC", "PingFang SC" };
        static readonly string[] Ja = { "Yu Gothic UI", "Meiryo UI", "Meiryo", "Noto Sans CJK JP", "Noto Sans JP", "Hiragino Sans" };
        static readonly string[] Ko = { "Malgun Gothic", "Noto Sans CJK KR", "Noto Sans KR", "Apple SD Gothic Neo" };

        static string[] OsFonts(string group)
        {
            var names = new System.Collections.Generic.List<string>(Latin);
            // The language's own CJK font comes first among the fallbacks; the others still cover names shown in
            // their own script (the language picker).
            switch (group)
            {
                case "zh": names.AddRange(Zh); names.AddRange(Ja); names.AddRange(Ko); break;
                case "ja": names.AddRange(Ja); names.AddRange(Zh); names.AddRange(Ko); break;
                case "ko": names.AddRange(Ko); names.AddRange(Ja); names.AddRange(Zh); break;
                default: names.AddRange(Ja); names.AddRange(Ko); names.AddRange(Zh); break;
            }
            names.Add("Segoe UI Symbol");
            return names.ToArray();
        }
    }
}
