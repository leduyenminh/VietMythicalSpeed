using System;
using System.Collections.Generic;
using System.Globalization;

namespace VMS.Core
{
    [Serializable]
    public class RarityRates
    {
        /// <summary>Base chance of a Thiên (★3) navigator per pull.</summary>
        public double thien = 0.03;
        /// <summary>Base chance of a Địa (★2) navigator per pull. Nhân (★1) gets the remainder.</summary>
        public double dia = 0.185;
    }

    [Serializable]
    public class RarityInts
    {
        public int thien;
        public int dia;
        public int nhan;

        public int Get(Rarity r)
        {
            switch (r)
            {
                case Rarity.Thien: return thien;
                case Rarity.Dia: return dia;
                default: return nhan;
            }
        }
    }

    /// <summary>
    /// Tunable gacha rules (Resources/Data/gacha.json). The server reads the same file, so offline and online
    /// play follow the same rules.
    /// </summary>
    [Serializable]
    public class GachaConfig
    {
        public RarityRates rates = new RarityRates();
        /// <summary>Pull number (counted since the last Thiên) where soft pity starts. 0 disables soft pity.</summary>
        public int softPityStart;
        /// <summary>Extra Thiên chance added for every pull at or past softPityStart.</summary>
        public double softPityStep = 0.06;
        /// <summary>Pull number (since last Thiên) that is guaranteed to be Thiên. 0 disables.</summary>
        public int hardPity = 100;
        /// <summary>Minimum rarity guaranteed in the last slot of a 10-pull that has not reached it yet.</summary>
        public int tenPullMinRarity = 2;
        /// <summary>Chance that a Thiên result on a banner with a featured Thiên is the featured one.</summary>
        public double featuredShare = 0.5;
        /// <summary>After losing the featured 50/50, the next Thiên on that pity group is the featured one.</summary>
        public bool guaranteeFeaturedAfterLoss = true;
        /// <summary>Chance that a Địa result is one of the banner's featured Địa navigators.</summary>
        public double featuredDiaShare = 0.5;
        /// <summary>Points (one per pull) needed to directly pick a featured navigator. 0 disables.</summary>
        public int sparkCost = 200;
        /// <summary>Jade (Ngọc Bích) cost of one pull. A Nén Nhang ticket replaces one pull's cost.</summary>
        public int costPerPull = 150;
        public RarityInts dupeFragments = new RarityInts { thien = 100, dia = 30, nhan = 10 };
        public int[] awakenFragmentCosts = { 50, 80, 120, 160, 200 };
        public int[] awakenCoinCosts = { 2000, 4000, 8000, 12000, 20000 };
        public int maxAwaken = 5;

        /// <summary>Thiên chance for the n-th pull since the last Thiên (n starts at 1).</summary>
        public double ThienChanceAt(int n)
        {
            double p = rates.thien;
            if (softPityStart > 0 && n >= softPityStart) p += softPityStep * (n - softPityStart + 1);
            if (hardPity > 0 && n >= hardPity) p = 1.0;
            return p > 1.0 ? 1.0 : p;
        }
    }

    [Serializable]
    public class BannerDef
    {
        public string id;
        /// <summary>"standard" or "limited".</summary>
        public string type;
        public LocalizedText title;
        public LocalizedText subtitle;
        public string[] featured;
        /// <summary>Banners sharing a pity group share their pity counter. Empty means the banner id.</summary>
        public string pityGroup;
        /// <summary>ISO-8601 UTC. Empty means unbounded.</summary>
        public string startUtc;
        public string endUtc;
        public string color;
        public int sortOrder;

        public string PityGroup => string.IsNullOrEmpty(pityGroup) ? id : pityGroup;
        public bool HasFeatured => featured != null && featured.Length > 0;

        public bool IsActive(DateTime utcNow)
        {
            DateTime start, end;
            if (TryParseUtc(startUtc, out start) && utcNow < start) return false;
            if (TryParseUtc(endUtc, out end) && utcNow >= end) return false;
            return true;
        }

        public static bool TryParseUtc(string iso, out DateTime value)
        {
            value = default(DateTime);
            if (string.IsNullOrEmpty(iso)) return false;
            return DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out value);
        }
    }

    [Serializable]
    public class BannerCatalog
    {
        public BannerDef[] banners;

        public BannerDef Find(string id)
        {
            if (banners == null) return null;
            foreach (var b in banners) if (b.id == id) return b;
            return null;
        }

        public List<BannerDef> Active(DateTime utcNow)
        {
            var list = new List<BannerDef>();
            if (banners == null) return list;
            foreach (var b in banners) if (b.IsActive(utcNow)) list.Add(b);
            list.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));
            return list;
        }
    }

    [Serializable]
    public class PityCounter
    {
        public string group;
        public int pullsSinceThien;
        public bool featuredGuaranteed;
    }

    [Serializable]
    public class SparkCounter
    {
        public string bannerId;
        public int points;
    }

    /// <summary>Per-player gacha progress. Serialised as part of the player profile.</summary>
    [Serializable]
    public class GachaState
    {
        public List<PityCounter> pity = new List<PityCounter>();
        public List<SparkCounter> spark = new List<SparkCounter>();

        public PityCounter GetPity(string group)
        {
            foreach (var p in pity) if (p.group == group) return p;
            var created = new PityCounter { group = group };
            pity.Add(created);
            return created;
        }

        public SparkCounter GetSpark(string bannerId)
        {
            foreach (var s in spark) if (s.bannerId == bannerId) return s;
            var created = new SparkCounter { bannerId = bannerId };
            spark.Add(created);
            return created;
        }

        public GachaState Clone()
        {
            var c = new GachaState();
            foreach (var p in pity)
                c.pity.Add(new PityCounter { group = p.group, pullsSinceThien = p.pullsSinceThien, featuredGuaranteed = p.featuredGuaranteed });
            foreach (var s in spark)
                c.spark.Add(new SparkCounter { bannerId = s.bannerId, points = s.points });
            return c;
        }
    }

    [Serializable]
    public class PullResult
    {
        public string navigatorId;
        public int rarity;
        public bool featured;
        /// <summary>Filled in when the result is applied to a collection.</summary>
        public bool isNew;
        /// <summary>Soul fragments granted because the navigator was already owned.</summary>
        public int fragments;
    }

    /// <summary>One line of the published drop-rate table (shown in the Rates popup).</summary>
    public struct RateLine
    {
        public string navigatorId;
        public Rarity rarity;
        public bool featured;
        public double probability;
    }
}
