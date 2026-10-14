using System;
using System.Collections.Generic;

namespace VMS.Core
{
    /// <summary>Rarity tiers, named after the Thiên – Địa – Nhân (Heaven – Earth – Human) triad.</summary>
    public enum Rarity
    {
        Nhan = 1,
        Dia = 2,
        Thien = 3,
    }

    public static class SkillTypes
    {
        /// <summary>Pearls in neighbouring lanes are pulled in. value = extra lanes of reach.</summary>
        public const string Magnet = "magnet";
        /// <summary>Absorbs hazard hits and misses without breaking combo. value = charges.</summary>
        public const string Shield = "shield";
        /// <summary>Score multiplier. value = multiplier (e.g. 1.5).</summary>
        public const string ScoreMult = "scoreMult";
        /// <summary>Spirit gain multiplier. value = multiplier.</summary>
        public const string SpiritBoost = "spiritBoost";
        /// <summary>Misses don't break combo. value = number of misses forgiven.</summary>
        public const string ComboGuard = "comboGuard";
        /// <summary>Perfect window widened. value = fraction added (0.5 = +50%).</summary>
        public const string WidePerfect = "widePerfect";
        /// <summary>Vessel steers itself to the right lane.</summary>
        public const string Autopilot = "autopilot";
        /// <summary>Hazards become pearls and pearls become big pearls.</summary>
        public const string Transmute = "transmute";
    }

    public static class PassiveTypes
    {
        public const string Score = "score";
        public const string Spirit = "spirit";
        public const string PerfectWindow = "perfectWindow";
        public const string Coins = "coins";
        public const string Bond = "bond";
    }

    public static class VoiceTriggers
    {
        public const string Greet = "greet";
        public const string Idle = "idle";
        public const string RaceStart = "raceStart";
        public const string Combo = "combo";
        public const string MissStreak = "missStreak";
        public const string SkillReady = "skillReady";
        public const string SkillCast = "skillCast";
        public const string FinishHigh = "finishHigh";
        public const string FinishMid = "finishMid";
        public const string FinishLow = "finishLow";
        public const string Obtained = "obtained";
        public const string BondUp = "bondUp";
    }

    [Serializable]
    public class SkillDef
    {
        public string type;
        public LocalizedText name;
        public LocalizedText desc;
        public float value;
        public float valuePerAwaken;
        public float duration;
        public float durationPerAwaken;
        /// <summary>Spirit needed to cast (gauge max is 100).</summary>
        public float cost = 100f;

        public float ValueAt(int awakening) => value + valuePerAwaken * awakening;
        public float DurationAt(int awakening) => duration + durationPerAwaken * awakening;
    }

    [Serializable]
    public class PassiveDef
    {
        public string type;
        public LocalizedText desc;
        public float value;
        public float valuePerAwaken;

        public float ValueAt(int awakening) => value + valuePerAwaken * awakening;
    }

    [Serializable]
    public class VoiceLine
    {
        public string id;
        public string trigger;
        public int minBond;
        public LocalizedText text;
    }

    [Serializable]
    public class NavigatorDef
    {
        public string id;
        public int rarity;
        /// <summary>Limited navigators only appear on their own banners, never in the standard pool.</summary>
        public bool limited;
        public LocalizedText name;
        public LocalizedText title;
        public LocalizedText creature;
        public LocalizedText region;
        /// <summary>Realm the navigator comes from (world.json); the home realm when empty.</summary>
        public string realm;
        public LocalizedText bio;
        /// <summary>Hán-Nôm character used as the emblem on placeholder art.</summary>
        public string glyph;
        public string color;
        public string accentColor;
        public SkillDef skill;
        public PassiveDef passive;
        public VoiceLine[] voice;

        public Rarity Rarity => (Rarity)rarity;

        public IEnumerable<VoiceLine> LinesFor(string trigger, int bondLevel)
        {
            if (voice == null) yield break;
            foreach (var line in voice)
                if (line.trigger == trigger && line.minBond <= bondLevel)
                    yield return line;
        }
    }

    [Serializable]
    public class NavigatorCatalog
    {
        public NavigatorDef[] navigators;

        Dictionary<string, NavigatorDef> _byId;

        public NavigatorDef Find(string id)
        {
            if (string.IsNullOrEmpty(id) || navigators == null) return null;
            if (_byId == null)
            {
                _byId = new Dictionary<string, NavigatorDef>(StringComparer.Ordinal);
                foreach (var n in navigators) _byId[n.id] = n;
            }
            NavigatorDef def;
            return _byId.TryGetValue(id, out def) ? def : null;
        }

        /// <summary>Returns a list of human-readable problems with the data, empty when valid.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (navigators == null || navigators.Length == 0)
            {
                problems.Add("No navigators defined.");
                return problems;
            }
            var seen = new HashSet<string>();
            foreach (var n in navigators)
            {
                if (string.IsNullOrEmpty(n.id)) problems.Add("Navigator without id.");
                else if (!seen.Add(n.id)) problems.Add("Duplicate navigator id: " + n.id);
                if (n.rarity < 1 || n.rarity > 3) problems.Add(n.id + ": rarity must be 1..3.");
                if (n.skill == null || string.IsNullOrEmpty(n.skill.type)) problems.Add(n.id + ": missing skill.");
            }
            for (int r = 1; r <= 3; r++)
            {
                bool any = false;
                foreach (var n in navigators) if (n.rarity == r && !n.limited) any = true;
                if (!any) problems.Add("Standard pool has no navigator of rarity " + r + ".");
            }
            return problems;
        }
    }
}
