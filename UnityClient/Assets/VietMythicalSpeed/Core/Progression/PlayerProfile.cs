using System;
using System.Collections.Generic;

namespace VMS.Core
{
    [Serializable]
    public class OwnedNavigator
    {
        public string id;
        public int copies;
        public int awakening;
        public int fragments;
        public int bondExp;
    }

    [Serializable]
    public class DailyState
    {
        /// <summary>UTC day (yyyy-MM-dd) the counters below belong to.</summary>
        public string day;
        public int racesRewarded;
        public int coinsEarned;
        public string lastLoginClaimDay;
        public int loginStreak;
    }

    /// <summary>
    /// The player's game state. The server stores the same shape and returns it from GET /v1/profile,
    /// so field names here are the wire format too.
    /// </summary>
    [Serializable]
    public class PlayerProfile
    {
        public string accountId;
        public string displayName;
        /// <summary>Ngọc Bích – premium currency spent on pulls.</summary>
        public int jade;
        /// <summary>Xu – soft currency earned by racing, spent on awakening.</summary>
        public int coins;
        /// <summary>Nén Nhang – single-pull tickets.</summary>
        public int tickets;
        /// <summary>Nén Vàng bought with real money. Refunds take these back.</summary>
        public int gold;
        /// <summary>Nén Vàng given for free (pack bonuses, events, mail). Spent before <see cref="gold"/>.</summary>
        public int goldBonus;
        public string selectedNavigator;
        public List<OwnedNavigator> navigators = new List<OwnedNavigator>();
        public GachaState gacha = new GachaState();
        public DailyState daily = new DailyState();
        public CosmeticLoadout cosmetics = new CosmeticLoadout();
        /// <summary>How often each real-money product was bought (first-purchase bonuses, limits).</summary>
        public List<ProductCount> purchases = new List<ProductCount>();
        public List<PassState> passes = new List<PassState>();
        public List<EventProgress> events = new List<EventProgress>();

        public int TotalGold => gold + goldBonus;

        /// <summary>Fills fields that older saves don't have (JsonUtility leaves missing lists null in some cases).</summary>
        public PlayerProfile Normalize()
        {
            if (navigators == null) navigators = new List<OwnedNavigator>();
            if (gacha == null) gacha = new GachaState();
            if (daily == null) daily = new DailyState();
            if (cosmetics == null) cosmetics = new CosmeticLoadout();
            if (cosmetics.owned == null) cosmetics.owned = new List<string>();
            if (cosmetics.navigatorSkins == null) cosmetics.navigatorSkins = new List<TargetedChoice>();
            if (cosmetics.vesselKits == null) cosmetics.vesselKits = new List<TargetedChoice>();
            if (purchases == null) purchases = new List<ProductCount>();
            if (passes == null) passes = new List<PassState>();
            if (events == null) events = new List<EventProgress>();
            foreach (var e in events) if (e.claimed == null) e.claimed = new List<int>();
            return this;
        }

        public OwnedNavigator FindNavigator(string id)
        {
            if (navigators == null) return null;
            foreach (var n in navigators) if (n.id == id) return n;
            return null;
        }

        public bool Owns(string id) => FindNavigator(id) != null;
    }

    [Serializable]
    public class RaceReport
    {
        public string songId;
        public float durationSec;
        public int mode;
        public int difficulty;
        public int score;
        public int maxCombo;
        public int perfect;
        public int good;
        public int miss;
        public float accuracy;
        public string navigatorId;
    }

    [Serializable]
    public class RaceReward
    {
        public int coins;
        public int jade;
        public int bondExp;
        public int bondLevelBefore;
        public int bondLevelAfter;
    }

    [Serializable]
    public class DailyClaimResult
    {
        public bool claimed;
        public int jade;
        public int tickets;
        public int streak;
        /// <summary>Jade from an active Lantern Pass (paid once per day).</summary>
        public int passJade;
    }
}
