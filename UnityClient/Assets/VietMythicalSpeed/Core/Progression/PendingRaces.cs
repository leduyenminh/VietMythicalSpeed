using System;
using System.Collections.Generic;

namespace VMS.Core
{
    /// <summary>A race finished while the server was unreachable, waiting to be uploaded.</summary>
    [Serializable]
    public class PendingRace
    {
        /// <summary>Client-generated id; the server grants each id once, so re-sending is always safe.</summary>
        public string id;
        /// <summary>UTC ISO-8601 time the race ended; the server applies that day's reward caps.</summary>
        public string finishedAt;
        public RaceReport report;
    }

    /// <summary>Offline race queue, oldest first. Bounded so a long offline stretch can't grow it forever.</summary>
    [Serializable]
    public class PendingRaceQueue
    {
        public const int MaxEntries = 100;

        public List<PendingRace> races = new List<PendingRace>();

        public int Count => races.Count;

        public bool Contains(string id) => races.Exists(r => r.id == id);

        public void Add(PendingRace race)
        {
            if (race == null || string.IsNullOrEmpty(race.id) || Contains(race.id)) return;
            races.Add(race);
            if (races.Count > MaxEntries) races.RemoveRange(0, races.Count - MaxEntries);
        }

        /// <summary>The oldest races, at most <paramref name="max"/>, as a copy.</summary>
        public List<PendingRace> Batch(int max) => races.GetRange(0, Math.Min(Math.Max(0, max), races.Count));

        /// <summary>Removes races the server has answered for; returns how many were removed.</summary>
        public int Settle(IEnumerable<string> ids)
        {
            var done = new HashSet<string>(ids);
            return races.RemoveAll(r => done.Contains(r.id));
        }
    }
}
