using System;
using System.Collections.Generic;

namespace VMS.Core
{
    /// <summary>
    /// Pure gacha ("Xin Xăm") rules. Rolls never touch ownership – apply results with <see cref="CollectionRules"/>.
    ///
    /// The draw order below is part of the contract shared with server/src/game/gachaEngine.ts:
    ///   1. rarity roll (NextDouble)
    ///   2. Thiên on a banner with featured Thiên: 50/50 roll (NextDouble) unless guaranteed, then pick (NextInt)
    ///      Địa on a banner with featured Địa: featured roll (NextDouble), then pick (NextInt)
    ///      otherwise: pick (NextInt)
    /// Keep both implementations in sync; shared/gacha-golden.json catches drift.
    /// </summary>
    public sealed class GachaEngine
    {
        public sealed class BannerPools
        {
            public readonly List<string> FeaturedThien = new List<string>();
            public readonly List<string> OffThien = new List<string>();
            public readonly List<string> AllThien = new List<string>();
            public readonly List<string> FeaturedDia = new List<string>();
            public readonly List<string> OffDia = new List<string>();
            public readonly List<string> AllDia = new List<string>();
            public readonly List<string> AllNhan = new List<string>();
        }

        readonly GachaConfig _config;
        readonly NavigatorCatalog _catalog;

        public GachaEngine(GachaConfig config, NavigatorCatalog catalog)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public GachaConfig Config => _config;

        /// <summary>
        /// Pools are built in catalog order so both implementations index identically.
        /// Off-banner pools contain only standard (non-limited) navigators that are not featured.
        /// </summary>
        public BannerPools BuildPools(BannerDef banner)
        {
            var featured = new HashSet<string>(banner.featured ?? new string[0]);
            var pools = new BannerPools();
            foreach (var nav in _catalog.navigators)
            {
                bool isFeatured = featured.Contains(nav.id);
                if (nav.limited && !isFeatured) continue;
                switch (nav.Rarity)
                {
                    case Rarity.Thien:
                        pools.AllThien.Add(nav.id);
                        (isFeatured ? pools.FeaturedThien : pools.OffThien).Add(nav.id);
                        break;
                    case Rarity.Dia:
                        pools.AllDia.Add(nav.id);
                        (isFeatured ? pools.FeaturedDia : pools.OffDia).Add(nav.id);
                        break;
                    default:
                        pools.AllNhan.Add(nav.id);
                        break;
                }
            }
            if (pools.AllThien.Count == 0 || pools.AllDia.Count == 0 || pools.AllNhan.Count == 0)
                throw new InvalidOperationException("Banner " + banner.id + " has an empty rarity pool.");
            return pools;
        }

        public List<PullResult> Pull(BannerDef banner, GachaState state, int count, IRandomSource rng)
        {
            if (count < 1 || count > 10) throw new ArgumentOutOfRangeException(nameof(count));
            var pools = BuildPools(banner);
            var pity = state.GetPity(banner.PityGroup);
            bool sparkEnabled = _config.sparkCost > 0 && banner.HasFeatured;
            var results = new List<PullResult>(count);
            int bestRarity = 0;

            for (int i = 0; i < count; i++)
            {
                int n = pity.pullsSinceThien + 1;
                double pThien = _config.ThienChanceAt(n);
                double pDia = Math.Min(_config.rates.dia, 1.0 - pThien);

                double roll = rng.NextDouble();
                Rarity rarity = roll < pThien ? Rarity.Thien : roll < pThien + pDia ? Rarity.Dia : Rarity.Nhan;

                bool guaranteeSlot = count >= 10 && i == count - 1 && bestRarity < _config.tenPullMinRarity;
                if (guaranteeSlot && (int)rarity < _config.tenPullMinRarity)
                    rarity = (Rarity)Math.Min(3, Math.Max(1, _config.tenPullMinRarity));

                var result = new PullResult { rarity = (int)rarity };

                if (rarity == Rarity.Thien)
                {
                    if (pools.FeaturedThien.Count > 0)
                    {
                        bool win = pity.featuredGuaranteed || rng.NextDouble() < _config.featuredShare;
                        if (pools.OffThien.Count == 0) win = true;
                        if (win)
                        {
                            result.navigatorId = pools.FeaturedThien[rng.NextInt(pools.FeaturedThien.Count)];
                            result.featured = true;
                            pity.featuredGuaranteed = false;
                        }
                        else
                        {
                            result.navigatorId = pools.OffThien[rng.NextInt(pools.OffThien.Count)];
                            if (_config.guaranteeFeaturedAfterLoss) pity.featuredGuaranteed = true;
                        }
                    }
                    else
                    {
                        result.navigatorId = pools.AllThien[rng.NextInt(pools.AllThien.Count)];
                    }
                    pity.pullsSinceThien = 0;
                }
                else
                {
                    pity.pullsSinceThien++;
                    if (rarity == Rarity.Dia && pools.FeaturedDia.Count > 0)
                    {
                        bool win = rng.NextDouble() < _config.featuredDiaShare;
                        if (pools.OffDia.Count == 0) win = true;
                        var pool = win ? pools.FeaturedDia : pools.OffDia;
                        result.navigatorId = pool[rng.NextInt(pool.Count)];
                        result.featured = win;
                    }
                    else
                    {
                        var pool = rarity == Rarity.Dia ? pools.AllDia : pools.AllNhan;
                        result.navigatorId = pool[rng.NextInt(pool.Count)];
                    }
                }

                if (sparkEnabled) state.GetSpark(banner.id).points++;
                if ((int)rarity > bestRarity) bestRarity = (int)rarity;
                results.Add(result);
            }
            return results;
        }

        /// <summary>Exchanges spark points for a featured navigator of the player's choice.</summary>
        public PullResult Spark(BannerDef banner, GachaState state, string navigatorId)
        {
            if (_config.sparkCost <= 0 || !banner.HasFeatured)
                throw new InvalidOperationException("Spark is not available on this banner.");
            if (Array.IndexOf(banner.featured, navigatorId) < 0)
                throw new InvalidOperationException("Only featured navigators can be chosen with spark.");
            var spark = state.GetSpark(banner.id);
            if (spark.points < _config.sparkCost)
                throw new InvalidOperationException("Not enough spark points.");
            var nav = _catalog.Find(navigatorId);
            if (nav == null) throw new InvalidOperationException("Unknown navigator " + navigatorId);
            spark.points -= _config.sparkCost;
            return new PullResult { navigatorId = navigatorId, rarity = nav.rarity, featured = true };
        }

        /// <summary>
        /// Published per-navigator probabilities for a single pull at base rates (no pity, no 10-pull guarantee),
        /// as required for loot-box odds disclosure.
        /// </summary>
        public List<RateLine> DescribeRates(BannerDef banner)
        {
            var pools = BuildPools(banner);
            var lines = new List<RateLine>();
            double pThien = _config.rates.thien;
            double pDia = _config.rates.dia;
            double pNhan = Math.Max(0.0, 1.0 - pThien - pDia);

            AddSplit(lines, Rarity.Thien, pThien, pools.FeaturedThien, pools.OffThien, pools.AllThien, _config.featuredShare);
            AddSplit(lines, Rarity.Dia, pDia, pools.FeaturedDia, pools.OffDia, pools.AllDia, _config.featuredDiaShare);
            foreach (var id in pools.AllNhan)
                lines.Add(new RateLine { navigatorId = id, rarity = Rarity.Nhan, probability = pNhan / pools.AllNhan.Count });
            return lines;
        }

        static void AddSplit(List<RateLine> lines, Rarity rarity, double total, List<string> featured, List<string> off,
            List<string> all, double share)
        {
            if (featured.Count == 0)
            {
                foreach (var id in all)
                    lines.Add(new RateLine { navigatorId = id, rarity = rarity, probability = total / all.Count });
                return;
            }
            double featuredTotal = off.Count == 0 ? total : total * share;
            double offTotal = total - featuredTotal;
            foreach (var id in featured)
                lines.Add(new RateLine { navigatorId = id, rarity = rarity, featured = true, probability = featuredTotal / featured.Count });
            foreach (var id in off)
                lines.Add(new RateLine { navigatorId = id, rarity = rarity, probability = offTotal / off.Count });
        }
    }
}
