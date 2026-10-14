using System.Collections.Generic;
using UnityEngine;

namespace VMS
{
    /// <summary>
    /// Key/value localisation loaded from Resources/Localization/{lang}.txt.
    /// File format: one "key = value" per line, '#' starts a comment, "\n" in a value is a line break.
    /// Missing keys fall back to English, then to the key itself. Game data texts (navigators, shop, events…)
    /// carry their translations inline and are read with <see cref="Pick"/>.
    /// </summary>
    public static class Loc
    {
        public const string Vietnamese = "vi";
        public const string English = "en";
        public const string Chinese = "zh";
        public const string Japanese = "ja";
        public const string Korean = "ko";
        public const string Spanish = "es";
        public const string German = "de";
        public const string Finnish = "fi";

        /// <summary>Supported languages in menu order.</summary>
        public static readonly string[] Languages = { Vietnamese, English, Chinese, Japanese, Korean, Spanish, German, Finnish };

        static readonly Dictionary<string, string> Table = new Dictionary<string, string>();
        static readonly Dictionary<string, string> Fallback = new Dictionary<string, string>();

        public static string Language { get; private set; } = Vietnamese;

        public static event System.Action LanguageChanged;

        public static bool IsSupported(string language) => System.Array.IndexOf(Languages, language) >= 0;

        /// <summary>The language's own name, as shown in the language picker.</summary>
        public static string NativeName(string language)
        {
            switch (language)
            {
                case Vietnamese: return "Tiếng Việt";
                case Chinese: return "简体中文";
                case Japanese: return "日本語";
                case Korean: return "한국어";
                case Spanish: return "Español";
                case German: return "Deutsch";
                case Finnish: return "Suomi";
                default: return "English";
            }
        }

        /// <summary>First-launch language from the device setting (English when unsupported).</summary>
        public static string FromSystemLanguage(SystemLanguage system)
        {
            switch (system)
            {
                case SystemLanguage.Vietnamese: return Vietnamese;
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.ChineseTraditional: return Chinese;
                case SystemLanguage.Japanese: return Japanese;
                case SystemLanguage.Korean: return Korean;
                case SystemLanguage.Spanish: return Spanish;
                case SystemLanguage.German: return German;
                case SystemLanguage.Finnish: return Finnish;
                default: return English;
            }
        }

        /// <summary>CJK text has no spaces between words.</summary>
        public static bool IsCjk => Language == Chinese || Language == Japanese || Language == Korean;

        public static void SetLanguage(string language)
        {
            if (!IsSupported(language)) language = English;
            Language = language;
            Table.Clear();
            Parse(Resources.Load<TextAsset>("Localization/" + language), Table);
            if (Fallback.Count == 0) Parse(Resources.Load<TextAsset>("Localization/" + English), Fallback);
            LanguageChanged?.Invoke();
        }

        public static string T(string key)
        {
            string value;
            if (Table.TryGetValue(key, out value)) return value;
            if (Fallback.TryGetValue(key, out value)) return value;
            return key;
        }

        public static string F(string key, params object[] args)
        {
            try
            {
                return string.Format(T(key), args);
            }
            catch (System.FormatException)
            {
                return T(key);
            }
        }

        public static string Pick(VMS.Core.LocalizedText text) => text == null ? string.Empty : text.Get(Language);

        static void Parse(TextAsset asset, Dictionary<string, string> into)
        {
            if (asset == null) return;
            foreach (var raw in asset.text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim().Replace("\\n", "\n");
                into[key] = value;
            }
        }
    }
}
