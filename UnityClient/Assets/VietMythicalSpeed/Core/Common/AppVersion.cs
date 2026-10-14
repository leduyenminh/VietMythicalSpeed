using System;

namespace VMS.Core
{
    /// <summary>
    /// "major.minor.patch" game versions (PlayerSettings "Version"). The parsing rules mirror
    /// server/src/version.ts, which uses them for the minimum-version check – keep both in sync.
    /// </summary>
    public static class AppVersion
    {
        /// <summary>Accepts "1.2.3", "1.2", "1", an optional leading "v" and a "-suffix"/"+suffix" that is ignored.</summary>
        public static bool TryParse(string text, out int major, out int minor, out int patch)
        {
            major = minor = patch = 0;
            if (string.IsNullOrEmpty(text)) return false;
            string s = text.Trim();
            if (s.StartsWith("v", StringComparison.Ordinal)) s = s.Substring(1);
            int cut = s.IndexOfAny(new[] { '-', '+' });
            if (cut >= 0) s = s.Substring(0, cut);
            var parts = s.Split('.');
            if (parts.Length < 1 || parts.Length > 3) return false;
            var values = new int[3];
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p.Length == 0 || p.Length > 6) return false;
                foreach (char c in p)
                    if (c < '0' || c > '9') return false;
                values[i] = int.Parse(p);
            }
            major = values[0];
            minor = values[1];
            patch = values[2];
            return true;
        }

        /// <summary>Orders versions; anything unparseable sorts before every valid version.</summary>
        public static int Compare(string a, string b)
        {
            bool okA = TryParse(a, out int a0, out int a1, out int a2);
            bool okB = TryParse(b, out int b0, out int b1, out int b2);
            if (!okA || !okB) return okA == okB ? 0 : okA ? 1 : -1;
            if (a0 != b0) return a0.CompareTo(b0);
            if (a1 != b1) return a1.CompareTo(b1);
            return a2.CompareTo(b2);
        }

        /// <summary>Next version for a release: "patch" 1.2.3→1.2.4, "minor" →1.3.0, "major" →2.0.0, "none" unchanged.</summary>
        public static string Bump(string version, string part)
        {
            if (!TryParse(version, out int major, out int minor, out int patch))
                throw new ArgumentException("Not a version like 1.2.3: \"" + version + "\"");
            switch ((part ?? "patch").ToLowerInvariant())
            {
                case "none": break;
                case "patch": patch++; break;
                case "minor": minor++; patch = 0; break;
                case "major": major++; minor = 0; patch = 0; break;
                default: throw new ArgumentException("Bump must be patch, minor, major or none, not \"" + part + "\"");
            }
            return major + "." + minor + "." + patch;
        }
    }
}
