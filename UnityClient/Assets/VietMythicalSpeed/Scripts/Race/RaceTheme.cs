using UnityEngine;

namespace VMS.Race
{
    public enum PropStyle
    {
        /// <summary>Hạ Long-style limestone karsts rising from a night bay.</summary>
        Karst,
        /// <summary>Lantern festival on the Perfume River.</summary>
        Lanterns,
        /// <summary>Bamboo forest at dawn.</summary>
        Bamboo,
        /// <summary>Imperial citadel walls and gates.</summary>
        Citadel,
        /// <summary>Vân Khuyết: stone peaks rising from a cloud sea, crowned with pavilions; moon gates.</summary>
        CloudSea,
        /// <summary>Hanayume: a tunnel of vermilion gates, stone lanterns and blossoming trees.</summary>
        ShrineGates,
        /// <summary>Dalmaru: tile-roofed villages, totem guardians and goblin fires under pine ridges.</summary>
        Hanok,
        /// <summary>Nebelreich: fir forests, half-timbered houses, clock towers and castles in the fog.</summary>
        Fogwood,
        /// <summary>Lumikaari: birch groves, snowy pines, frozen lakes and saunas under the aurora.</summary>
        Boreal,
    }

    /// <summary>Colour script and prop style for one environment.</summary>
    public sealed class RaceTheme
    {
        public string id;
        public string nameKey;
        public PropStyle props;
        public Color skyTop, skyHorizon, skyBottom, stars;
        public Color fog;
        public Color roadBase, roadGrid, roadBeat;
        public Color laneLine, edgeLine, rail;
        public Color pearl, bigPearl, hazard;
        public Color spectrumLow, spectrumHigh;
        public Color dragonHead, dragonTail;
        public Color moonInner, moonOuter;
        public bool moonBands;
        public Color propDark, propLight, propGlow;
        public float starDensity = 0.6f;
        /// <summary>Realm (world.json) this road belongs to.</summary>
        public string realm = "vanlac";
        /// <summary>Second prop light (goblin fires, wisps, ice); <see cref="Accent"/> falls back to propGlow.</summary>
        public Color propAccent;
        /// <summary>Particles drifting past the camera: "" (none), petals, snow, motes, sparks or clouds.</summary>
        public string ambient = string.Empty;
        public Color ambientColor = Color.white;
        /// <summary>Aurora curtains painted by the sky shader (0 = none).</summary>
        public float aurora;
        public Color auroraA, auroraB;

        public Color Accent => propAccent.a > 0f ? propAccent : propGlow;

        public static readonly RaceTheme[] All =
        {
            new RaceTheme
            {
                id = "halong", nameKey = "theme.halong", props = PropStyle.Karst,
                skyTop = Hex("#050B1F"), skyHorizon = Hex("#2B2F7A"), skyBottom = Hex("#02030A"), stars = Hex("#CFE6FF"),
                fog = Hex("#11163F"),
                roadBase = Hex("#070B1C"), roadGrid = Hex("#2E8BFF"), roadBeat = Hex("#7CF3FF"),
                laneLine = Hex("#3FA9FF"), edgeLine = Hex("#58F2FF"), rail = Hex("#2E6BFF"),
                pearl = Hex("#BFF6FF"), bigPearl = Hex("#FFE38A"), hazard = Hex("#5BFFB0"),
                spectrumLow = Hex("#1F6BFF"), spectrumHigh = Hex("#7CF3FF"),
                dragonHead = Hex("#7CF3FF"), dragonTail = Hex("#1F3BFF"),
                moonInner = Hex("#FFF6D8"), moonOuter = Hex("#9FD3FF"), moonBands = false,
                propDark = Hex("#0A1230"), propLight = Hex("#1B2C5E"), propGlow = Hex("#58C8FF"),
                starDensity = 0.8f,
            },
            new RaceTheme
            {
                id = "huong", nameKey = "theme.huong", props = PropStyle.Lanterns,
                skyTop = Hex("#140827"), skyHorizon = Hex("#B0386A"), skyBottom = Hex("#07030E"), stars = Hex("#FFE2C2"),
                fog = Hex("#3A1236"),
                roadBase = Hex("#120714"), roadGrid = Hex("#FF6FA8"), roadBeat = Hex("#FFC36B"),
                laneLine = Hex("#FF7AB6"), edgeLine = Hex("#FFB347"), rail = Hex("#FF4F8B"),
                pearl = Hex("#FFE9C9"), bigPearl = Hex("#FFD24A"), hazard = Hex("#6BFFD8"),
                spectrumLow = Hex("#FF3D7F"), spectrumHigh = Hex("#FFC36B"),
                dragonHead = Hex("#FFD24A"), dragonTail = Hex("#FF3D7F"),
                moonInner = Hex("#FFE7A3"), moonOuter = Hex("#FF5C8A"), moonBands = true,
                propDark = Hex("#1E0A1E"), propLight = Hex("#5A1A33"), propGlow = Hex("#FF9F45"),
                starDensity = 0.4f,
            },
            new RaceTheme
            {
                id = "truc", nameKey = "theme.truc", props = PropStyle.Bamboo,
                skyTop = Hex("#0E2A3A"), skyHorizon = Hex("#E8A35C"), skyBottom = Hex("#071510"), stars = Hex("#FFF2D6"),
                fog = Hex("#35473A"),
                roadBase = Hex("#08140F"), roadGrid = Hex("#7DFF9A"), roadBeat = Hex("#FFE07A"),
                laneLine = Hex("#62F59A"), edgeLine = Hex("#D9FF7A"), rail = Hex("#2FD27A"),
                pearl = Hex("#F1FFE3"), bigPearl = Hex("#FFD86B"), hazard = Hex("#7AE7FF"),
                spectrumLow = Hex("#2FD27A"), spectrumHigh = Hex("#E7FF7A"),
                dragonHead = Hex("#E7FF7A"), dragonTail = Hex("#2FD27A"),
                moonInner = Hex("#FFF4D6"), moonOuter = Hex("#FFB36B"), moonBands = false,
                propDark = Hex("#0B1F14"), propLight = Hex("#1E4A2A"), propGlow = Hex("#B6FF7A"),
                starDensity = 0.2f,
            },
            new RaceTheme
            {
                id = "hoang", nameKey = "theme.hoang", props = PropStyle.Citadel,
                skyTop = Hex("#12060A"), skyHorizon = Hex("#9C2A1E"), skyBottom = Hex("#050203"), stars = Hex("#FFE6B0"),
                fog = Hex("#3A120E"),
                roadBase = Hex("#140806"), roadGrid = Hex("#FF5A3C"), roadBeat = Hex("#FFD36B"),
                laneLine = Hex("#FF6A3D"), edgeLine = Hex("#FFD36B"), rail = Hex("#FF3B2F"),
                pearl = Hex("#FFF1D0"), bigPearl = Hex("#FFD36B"), hazard = Hex("#6BE6FF"),
                spectrumLow = Hex("#FF3B2F"), spectrumHigh = Hex("#FFD36B"),
                dragonHead = Hex("#FFD36B"), dragonTail = Hex("#FF3B2F"),
                moonInner = Hex("#FFE9B0"), moonOuter = Hex("#FF6A3D"), moonBands = true,
                propDark = Hex("#22090A"), propLight = Hex("#6E1B14"), propGlow = Hex("#FFC14D"),
                starDensity = 0.5f,
            },
            new RaceTheme
            {
                id = "yunque", nameKey = "theme.yunque", props = PropStyle.CloudSea, realm = "yunque",
                skyTop = Hex("#0E2F45"), skyHorizon = Hex("#FFC9A8"), skyBottom = Hex("#F4E6F0"), stars = Hex("#FFFFFF"),
                fog = Hex("#8F87A8"),
                roadBase = Hex("#0B1E26"), roadGrid = Hex("#4FE3B0"), roadBeat = Hex("#FFD36B"),
                laneLine = Hex("#5CF0C0"), edgeLine = Hex("#FFE08A"), rail = Hex("#2FB58A"),
                pearl = Hex("#F0FFF8"), bigPearl = Hex("#FFD36B"), hazard = Hex("#FF6B8A"),
                spectrumLow = Hex("#2FB58A"), spectrumHigh = Hex("#FFE08A"),
                dragonHead = Hex("#FFE08A"), dragonTail = Hex("#4FE3B0"),
                moonInner = Hex("#FFF6E0"), moonOuter = Hex("#FFB88A"), moonBands = false,
                propDark = Hex("#1C2B36"), propLight = Hex("#3E6B66"), propGlow = Hex("#FF7A45"), propAccent = Hex("#FFF4E0"),
                ambient = "clouds", ambientColor = Hex("#FFFFFF"),
                starDensity = 0.15f,
            },
            new RaceTheme
            {
                id = "hanayume", nameKey = "theme.hanayume", props = PropStyle.ShrineGates, realm = "hanayume",
                skyTop = Hex("#1A0B2E"), skyHorizon = Hex("#FF7FA8"), skyBottom = Hex("#0A0414"), stars = Hex("#FFE6F0"),
                fog = Hex("#3B1638"),
                roadBase = Hex("#12081A"), roadGrid = Hex("#FF7FB8"), roadBeat = Hex("#FFE08A"),
                laneLine = Hex("#FF8FC4"), edgeLine = Hex("#FFD0E4"), rail = Hex("#FF4F8B"),
                pearl = Hex("#FFF0F6"), bigPearl = Hex("#FFD36B"), hazard = Hex("#6BFFE0"),
                spectrumLow = Hex("#FF4F8B"), spectrumHigh = Hex("#FFD0E4"),
                dragonHead = Hex("#FFD0E4"), dragonTail = Hex("#FF4F8B"),
                moonInner = Hex("#FFF2F6"), moonOuter = Hex("#FF8FB8"), moonBands = false,
                propDark = Hex("#1A0A12"), propLight = Hex("#E8432E"), propGlow = Hex("#FFB3CF"), propAccent = Hex("#FFD27A"),
                ambient = "petals", ambientColor = Hex("#FFB3CF"),
                starDensity = 0.5f,
            },
            new RaceTheme
            {
                id = "dalmaru", nameKey = "theme.dalmaru", props = PropStyle.Hanok, realm = "dalmaru",
                skyTop = Hex("#070C24"), skyHorizon = Hex("#4B5BB0"), skyBottom = Hex("#03050F"), stars = Hex("#E0E8FF"),
                fog = Hex("#121A3A"),
                roadBase = Hex("#070B1A"), roadGrid = Hex("#7FD8C8"), roadBeat = Hex("#FFD27A"),
                laneLine = Hex("#8FE8D8"), edgeLine = Hex("#C8F5EC"), rail = Hex("#3FAF9F"),
                pearl = Hex("#EFFFFB"), bigPearl = Hex("#FFD27A"), hazard = Hex("#FF7A6B"),
                spectrumLow = Hex("#3FAF9F"), spectrumHigh = Hex("#C8F5EC"),
                dragonHead = Hex("#C8F5EC"), dragonTail = Hex("#4B5BB0"),
                moonInner = Hex("#FFFBEA"), moonOuter = Hex("#A9B8FF"), moonBands = true,
                propDark = Hex("#10152A"), propLight = Hex("#C9C2B0"), propGlow = Hex("#FFD9A0"), propAccent = Hex("#5FD0FF"),
                ambient = "sparks", ambientColor = Hex("#5FD0FF"),
                starDensity = 0.7f,
            },
            new RaceTheme
            {
                id = "nebelreich", nameKey = "theme.nebelreich", props = PropStyle.Fogwood, realm = "nebelreich",
                skyTop = Hex("#071216"), skyHorizon = Hex("#4E7A78"), skyBottom = Hex("#030807"), stars = Hex("#DDEEE8"),
                fog = Hex("#1A2E2E"),
                roadBase = Hex("#0A1210"), roadGrid = Hex("#FFC46B"), roadBeat = Hex("#E8F4FF"),
                laneLine = Hex("#FFCF85"), edgeLine = Hex("#FFE6B8"), rail = Hex("#D9902F"),
                pearl = Hex("#FFF6E6"), bigPearl = Hex("#FFD36B"), hazard = Hex("#8AFFC8"),
                spectrumLow = Hex("#D9902F"), spectrumHigh = Hex("#FFE6B8"),
                dragonHead = Hex("#FFE6B8"), dragonTail = Hex("#6FA88F"),
                moonInner = Hex("#F2FFF8"), moonOuter = Hex("#8FC8B8"), moonBands = false,
                propDark = Hex("#0C1713"), propLight = Hex("#D8D0BE"), propGlow = Hex("#FFB45A"), propAccent = Hex("#8AFFC8"),
                ambient = "motes", ambientColor = Hex("#CFEFE4"),
                starDensity = 0.3f,
            },
            new RaceTheme
            {
                id = "lumikaari", nameKey = "theme.lumikaari", props = PropStyle.Boreal, realm = "lumikaari",
                skyTop = Hex("#020814"), skyHorizon = Hex("#1E4466"), skyBottom = Hex("#01040A"), stars = Hex("#E8F4FF"),
                fog = Hex("#0D1C2C"),
                roadBase = Hex("#050C16"), roadGrid = Hex("#7FE8FF"), roadBeat = Hex("#5BFFB0"),
                laneLine = Hex("#8FEFFF"), edgeLine = Hex("#D8FBFF"), rail = Hex("#3FB8E0"),
                pearl = Hex("#F0FBFF"), bigPearl = Hex("#FFE08A"), hazard = Hex("#FF8A6B"),
                spectrumLow = Hex("#3FB8E0"), spectrumHigh = Hex("#5BFFB0"),
                dragonHead = Hex("#5BFFB0"), dragonTail = Hex("#B57BFF"),
                moonInner = Hex("#FFF8E8"), moonOuter = Hex("#FFD8A0"), moonBands = false,
                propDark = Hex("#0E1A24"), propLight = Hex("#E6EEF4"), propGlow = Hex("#FFB45A"), propAccent = Hex("#9FE8FF"),
                ambient = "snow", ambientColor = Hex("#F0F8FF"),
                aurora = 1f, auroraA = Hex("#5BFFB0"), auroraB = Hex("#B57BFF"),
                starDensity = 0.9f,
            },
        };

        /// <summary>The roads of one realm, in display order.</summary>
        public static System.Collections.Generic.List<RaceTheme> OfRealm(string realm)
        {
            var list = new System.Collections.Generic.List<RaceTheme>();
            foreach (var t in All) if (t.realm == realm) list.Add(t);
            return list;
        }

        /// <summary>"auto" picks a theme from the song id so each song keeps its own look.</summary>
        public static RaceTheme Resolve(string id, string songId)
        {
            foreach (var t in All) if (t.id == id) return t;
            uint h = VMS.Core.StableHash.Fnv1a(songId ?? string.Empty);
            return All[h % (uint)All.Length];
        }

        public static Color Hex(string hex)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.magenta;
        }
    }
}
