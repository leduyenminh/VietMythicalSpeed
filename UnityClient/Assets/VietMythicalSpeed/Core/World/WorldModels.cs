using System;
using System.Collections.Generic;

namespace VMS.Core
{
    /// <summary>
    /// One realm of the world (world.json): Vạn Lạc, home of the Speed Envoys, and the realms beyond the Five Gates
    /// that its music-veins now reach. Everything here is fiction; see docs/World.md.
    /// </summary>
    [Serializable]
    public class RealmDef
    {
        public string id;
        public LocalizedText name;
        /// <summary>Short epithet under the name ("Land of the Cloud Sea").</summary>
        public LocalizedText title;
        public LocalizedText desc;
        /// <summary>Codex entries: places, architecture, artifacts, myths and customs.</summary>
        public List<LoreEntry> lore = new List<LoreEntry>();
        public string color;
        /// <summary>Emblem character on placeholder art.</summary>
        public string glyph;
        public int sortOrder;
    }

    [Serializable]
    public class LoreEntry
    {
        public string id;
        /// <summary>place | architecture | artifact | myth | custom.</summary>
        public string kind;
        public LocalizedText name;
        public LocalizedText text;
    }

    [Serializable]
    public class WorldCatalog
    {
        public List<RealmDef> realms = new List<RealmDef>();

        public RealmDef Find(string id)
        {
            foreach (var r in realms) if (r.id == id) return r;
            return null;
        }

        public List<RealmDef> Sorted()
        {
            var list = new List<RealmDef>(realms);
            list.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));
            return list;
        }
    }
}
