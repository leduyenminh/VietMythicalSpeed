using System;

namespace VMS.Core
{
    /// <summary>
    /// A translated string as stored in the game data JSON files. Vietnamese and English are the source
    /// languages; the others fall back to English, then Vietnamese, when a translation is missing.
    /// </summary>
    [Serializable]
    public class LocalizedText
    {
        public string vi;
        public string en;
        public string zh;
        public string ja;
        public string ko;
        public string es;
        public string de;
        public string fi;

        public LocalizedText() { }

        public LocalizedText(string vi, string en)
        {
            this.vi = vi;
            this.en = en;
        }

        /// <summary>The text in one language ("vi", "en", "zh", "ja", "ko", "es", "de", "fi"); null when absent.</summary>
        public string Raw(string language)
        {
            switch (language)
            {
                case "vi": return vi;
                case "en": return en;
                case "zh": return zh;
                case "ja": return ja;
                case "ko": return ko;
                case "es": return es;
                case "de": return de;
                case "fi": return fi;
                default: return null;
            }
        }

        public string Get(string language)
        {
            string text = Raw(language);
            if (!string.IsNullOrEmpty(text)) return text;
            if (language != "vi" && !string.IsNullOrEmpty(en)) return en;
            if (!string.IsNullOrEmpty(vi)) return vi;
            return en ?? string.Empty;
        }

        public override string ToString() => Get("vi");
    }
}
