using System;
using System.Globalization;

namespace VMS.Core
{
    [Serializable]
    public class RaceRewardConfig
    {
        public double coinsPerScore = 0.02;
        public int minCoins = 50;
        public int maxCoinsPerRace = 3000;
        public int dailyCoinCap = 30000;
        public int jadePerRace = 30;
        public int jadeRacesPerDay = 5;
        public float minDurationSec = 30f;
    }

    [Serializable]
    public class BondConfig
    {
        /// <summary>Cumulative bond EXP needed for level index+1. levelExp[0] must be 0.</summary>
        public int[] levelExp = { 0, 100, 250, 450, 700, 1000, 1400, 1900, 2500, 3200 };
        public int baseExp = 15;
        public int accuracyExp = 25;
    }

    /// <summary>Resources/Data/economy.json – shared with the server.</summary>
    [Serializable]
    public class EconomyConfig
    {
        public int startingJade = 3000;
        public int startingCoins = 5000;
        public int startingTickets = 10;
        public string starterNavigator = "vang_anh";
        public int dailyLoginJade = 120;
        public int streakBonusEvery = 7;
        public int streakBonusTickets = 3;
        public RaceRewardConfig race = new RaceRewardConfig();
        public BondConfig bond = new BondConfig();
    }

    public static class EconomyRules
    {
        public static string DayKey(DateTime utc) => utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public static PlayerProfile NewProfile(EconomyConfig cfg, string accountId, string displayName)
        {
            var p = new PlayerProfile
            {
                accountId = accountId,
                displayName = displayName,
                jade = cfg.startingJade,
                coins = cfg.startingCoins,
                tickets = cfg.startingTickets,
                selectedNavigator = cfg.starterNavigator,
            };
            if (!string.IsNullOrEmpty(cfg.starterNavigator))
                p.navigators.Add(new OwnedNavigator { id = cfg.starterNavigator, copies = 1 });
            return p;
        }

        /// <summary>Resets the per-day counters when the UTC day changes.</summary>
        public static void RollDay(PlayerProfile p, DateTime utcNow)
        {
            if (p.daily == null) p.daily = new DailyState();
            string today = DayKey(utcNow);
            if (p.daily.day == today) return;
            p.daily.day = today;
            p.daily.racesRewarded = 0;
            p.daily.coinsEarned = 0;
        }

        public static DailyClaimResult ClaimDailyLogin(PlayerProfile p, EconomyConfig cfg, DateTime utcNow)
        {
            RollDay(p, utcNow);
            string today = DayKey(utcNow);
            var result = new DailyClaimResult { streak = p.daily.loginStreak };
            if (p.daily.lastLoginClaimDay == today) return result;

            string yesterday = DayKey(utcNow.AddDays(-1));
            p.daily.loginStreak = p.daily.lastLoginClaimDay == yesterday ? p.daily.loginStreak + 1 : 1;
            p.daily.lastLoginClaimDay = today;

            result.claimed = true;
            result.streak = p.daily.loginStreak;
            result.jade = cfg.dailyLoginJade;
            if (cfg.streakBonusEvery > 0 && p.daily.loginStreak % cfg.streakBonusEvery == 0)
                result.tickets = cfg.streakBonusTickets;
            p.jade += result.jade;
            p.tickets += result.tickets;
            return result;
        }

        public static int BondLevel(BondConfig cfg, int exp)
        {
            int level = 1;
            for (int i = 1; i < cfg.levelExp.Length; i++)
                if (exp >= cfg.levelExp[i]) level = i + 1;
            return level;
        }

        public static int MaxBondLevel(BondConfig cfg) => cfg.levelExp.Length;

        /// <summary>
        /// Grants rewards for a finished race. Rewards are capped per race and per day so a forged report
        /// cannot mint unlimited currency.
        /// </summary>
        public static RaceReward GrantRaceReward(PlayerProfile p, EconomyConfig cfg, RaceReport report,
            NavigatorDef navigatorDef, DateTime utcNow)
        {
            RollDay(p, utcNow);
            var reward = new RaceReward();
            var owned = report == null ? null : p.FindNavigator(report.navigatorId);
            if (owned != null)
                reward.bondLevelBefore = reward.bondLevelAfter = BondLevel(cfg.bond, owned.bondExp);

            if (report == null || report.durationSec < cfg.race.minDurationSec || report.score < 0) return reward;

            float accuracy = Clamp01(report.accuracy);
            double coinBonus = 0, bondBonus = 0;
            if (navigatorDef != null && navigatorDef.passive != null && owned != null)
            {
                float v = navigatorDef.passive.ValueAt(owned.awakening);
                if (navigatorDef.passive.type == PassiveTypes.Coins) coinBonus = v;
                if (navigatorDef.passive.type == PassiveTypes.Bond) bondBonus = v;
            }

            int coins = (int)Math.Round(report.score * cfg.race.coinsPerScore * (1.0 + coinBonus));
            coins = Math.Max(cfg.race.minCoins, Math.Min(cfg.race.maxCoinsPerRace, coins));
            coins = Math.Max(0, Math.Min(coins, cfg.race.dailyCoinCap - p.daily.coinsEarned));
            reward.coins = coins;
            p.coins += coins;
            p.daily.coinsEarned += coins;

            if (p.daily.racesRewarded < cfg.race.jadeRacesPerDay)
            {
                reward.jade = cfg.race.jadePerRace;
                p.jade += reward.jade;
            }
            p.daily.racesRewarded++;

            if (owned != null)
            {
                float lengthFactor = report.durationSec >= 90f ? 1f : 0.6f;
                int exp = (int)Math.Round((cfg.bond.baseExp + cfg.bond.accuracyExp * accuracy) * lengthFactor * (1.0 + bondBonus));
                int cap = cfg.bond.levelExp[cfg.bond.levelExp.Length - 1];
                owned.bondExp = Math.Min(cap, owned.bondExp + exp);
                reward.bondExp = exp;
                reward.bondLevelAfter = BondLevel(cfg.bond, owned.bondExp);
            }
            return reward;
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
