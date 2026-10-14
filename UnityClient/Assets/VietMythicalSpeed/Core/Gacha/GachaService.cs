using System;
using System.Collections.Generic;

namespace VMS.Core
{
    public class GachaException : Exception
    {
        public readonly string Code;

        public GachaException(string code, string message) : base(message)
        {
            Code = code;
        }
    }

    /// <summary>
    /// Profile-level gacha operations (payment + roll + collection), used by the offline backend.
    /// server/src/game/gachaService.ts implements the same steps for online play.
    /// </summary>
    public sealed class GachaService
    {
        readonly GachaEngine _engine;
        readonly BannerCatalog _banners;

        public GachaService(GachaEngine engine, BannerCatalog banners)
        {
            _engine = engine;
            _banners = banners;
        }

        public GachaConfig Config => _engine.Config;

        public BannerDef RequireActiveBanner(string bannerId, DateTime utcNow)
        {
            var banner = _banners.Find(bannerId);
            if (banner == null) throw new GachaException("banner_not_found", "Unknown banner.");
            if (!banner.IsActive(utcNow)) throw new GachaException("banner_inactive", "This banner is not running.");
            return banner;
        }

        public List<PullResult> Pull(PlayerProfile profile, string bannerId, int count, IRandomSource rng, DateTime utcNow)
        {
            if (count != 1 && count != 10) throw new GachaException("bad_count", "Pull 1 or 10 at a time.");
            var banner = RequireActiveBanner(bannerId, utcNow);

            int ticketsUsed = Math.Min(profile.tickets, count);
            int jadeCost = (count - ticketsUsed) * Config.costPerPull;
            if (profile.jade < jadeCost) throw new GachaException("insufficient_jade", "Not enough jade.");

            var results = _engine.Pull(banner, profile.gacha, count, rng);
            profile.tickets -= ticketsUsed;
            profile.jade -= jadeCost;
            CollectionRules.Apply(profile, results, Config);
            return results;
        }

        public PullResult Spark(PlayerProfile profile, string bannerId, string navigatorId, DateTime utcNow)
        {
            var banner = RequireActiveBanner(bannerId, utcNow);
            PullResult result;
            try
            {
                result = _engine.Spark(banner, profile.gacha, navigatorId);
            }
            catch (InvalidOperationException e)
            {
                throw new GachaException("spark_unavailable", e.Message);
            }
            CollectionRules.Apply(profile, new[] { result }, Config);
            return result;
        }
    }

    public static class CollectionRules
    {
        public static void Apply(PlayerProfile profile, IList<PullResult> results, GachaConfig cfg)
        {
            foreach (var r in results)
            {
                var owned = profile.FindNavigator(r.navigatorId);
                if (owned == null)
                {
                    profile.navigators.Add(new OwnedNavigator { id = r.navigatorId, copies = 1 });
                    r.isNew = true;
                    r.fragments = 0;
                }
                else
                {
                    owned.copies++;
                    r.isNew = false;
                    r.fragments = cfg.dupeFragments.Get((Rarity)r.rarity);
                    owned.fragments += r.fragments;
                }
            }
        }

        public struct AwakenCost
        {
            public bool possible;
            public bool affordable;
            public int fragments;
            public int coins;
        }

        public static AwakenCost CostToAwaken(PlayerProfile profile, string navigatorId, GachaConfig cfg)
        {
            var owned = profile.FindNavigator(navigatorId);
            var cost = new AwakenCost();
            if (owned == null || owned.awakening >= cfg.maxAwaken) return cost;
            int level = owned.awakening;
            cost.possible = level < cfg.awakenFragmentCosts.Length && level < cfg.awakenCoinCosts.Length;
            if (!cost.possible) return cost;
            cost.fragments = cfg.awakenFragmentCosts[level];
            cost.coins = cfg.awakenCoinCosts[level];
            cost.affordable = owned.fragments >= cost.fragments && profile.coins >= cost.coins;
            return cost;
        }

        public static void Awaken(PlayerProfile profile, string navigatorId, GachaConfig cfg)
        {
            var cost = CostToAwaken(profile, navigatorId, cfg);
            if (!cost.possible) throw new GachaException("awaken_max", "Already fully awakened.");
            if (!cost.affordable) throw new GachaException("awaken_cost", "Not enough fragments or coins.");
            var owned = profile.FindNavigator(navigatorId);
            owned.fragments -= cost.fragments;
            profile.coins -= cost.coins;
            owned.awakening++;
        }
    }
}
