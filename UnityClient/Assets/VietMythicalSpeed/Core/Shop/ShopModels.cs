using System;
using System.Collections.Generic;

namespace VMS.Core
{
    /// <summary>Reward and price currency ids (wire format, shared with the server).</summary>
    public static class Currencies
    {
        /// <summary>Nén Vàng – bought with real money (bonus gold from packs/events is tracked separately).</summary>
        public const string Gold = "gold";
        public const string Jade = "jade";
        public const string Coins = "coins";
        public const string Tickets = "tickets";
        /// <summary>Reward type for a shop item (cosmetic) instead of a currency.</summary>
        public const string Item = "item";
    }

    public static class ShopCategories
    {
        public const string AvatarFrame = "avatarFrame";
        public const string ProfileFrame = "profileFrame";
        public const string BubbleFrame = "bubbleFrame";
        public const string Exhaust = "exhaust";
        public const string NavigatorSkin = "navigatorSkin";
        public const string Emotion = "emotion";
        public const string Garage = "garage";
        public const string VesselKit = "vesselKit";
        /// <summary>Consumable bundles (coins, incense) bought with gold.</summary>
        public const string Special = "special";

        public static readonly string[] Cosmetic = { AvatarFrame, ProfileFrame, BubbleFrame, Exhaust, NavigatorSkin, Emotion, Garage, VesselKit };

        /// <summary>Categories with one equipped choice (per navigator / per vessel for the targeted ones).</summary>
        public static bool IsEquippable(string category) =>
            category == AvatarFrame || category == ProfileFrame || category == BubbleFrame || category == Exhaust ||
            category == Garage || category == NavigatorSkin || category == VesselKit;

        public static bool IsTargeted(string category) => category == NavigatorSkin || category == VesselKit;
    }

    [Serializable]
    public class Reward
    {
        public string type;
        public int amount;
        public string itemId;
    }

    [Serializable]
    public class Price
    {
        public string currency;
        public int amount;
    }

    /// <summary>
    /// Look of a cosmetic. One flat shape for every category (unused fields stay empty), so new items can be
    /// added to shop.json – even served by the server – without a game update, as long as they reuse known shapes.
    /// </summary>
    [Serializable]
    public class CosmeticStyle
    {
        /// <summary>Avatar frame: ring|double|petal|cloud|scale|flame. Bubble: skew|round|none.</summary>
        public string shape;
        /// <summary>Nameplate: plain|hatch|wave|lattice|drum|stars.</summary>
        public string pattern;
        public string colorA;
        public string colorB;
        public string colorC;
        /// <summary>Exhaust ember sprite (ProceduralTextures.FxCell name).</summary>
        public string cell;
        /// <summary>Emotion expression: smile|happy|laugh|wink|sleepy|pout|surprised|heart.</summary>
        public string face;
        /// <summary>Vessel kit add-on: none|lanterns|banners|horns.</summary>
        public string ornament;
        /// <summary>Garage decoration set: bamboo|lotus|lanterns|stars|palace.</summary>
        public string props;
        public float width;
        public float length;
        /// <summary>Exhaust embers per second.</summary>
        public float embers;
        public LocalizedText text;
    }

    [Serializable]
    public class ShopItem
    {
        public string id;
        public string category;
        /// <summary>Navigator id (skins) or vessel id (kits).</summary>
        public string target;
        /// <summary>Owned by every player and used when nothing else is equipped.</summary>
        public bool basic;
        /// <summary>Not sold; obtained from mail, events or bundles.</summary>
        public bool hidden;
        /// <summary>Bought for its <see cref="rewards"/> (coins, incense) rather than owned.</summary>
        public bool consumable;
        public int rarity;
        public Price price;
        public LocalizedText name;
        public LocalizedText desc;
        public CosmeticStyle style;
        public List<Reward> rewards = new List<Reward>();
        public string startUtc;
        public string endUtc;
        public int sortOrder;

        public bool ForSale => price != null && price.amount > 0 && !string.IsNullOrEmpty(price.currency);
    }

    [Serializable]
    public class SteamPrice
    {
        public string currency;
        public int cents;
    }

    /// <summary>Real-money product (Google Play product id = Steam/our id).</summary>
    [Serializable]
    public class ProductDef
    {
        public string id;
        /// <summary>gold | pass (daily jade for N days) | bundle.</summary>
        public string kind;
        /// <summary>Paid gold.</summary>
        public int gold;
        /// <summary>Bonus gold on every purchase.</summary>
        public int bonus;
        /// <summary>Bonus gold instead of <see cref="bonus"/> on the first purchase of this product.</summary>
        public int firstBonus;
        /// <summary>Maximum purchases per account (0 = unlimited).</summary>
        public int limit;
        public int passDays;
        public int passDailyJade;
        /// <summary>Fallback price label; stores show their own localised price.</summary>
        public float priceUsd;
        public string steamItemId;
        public List<SteamPrice> steamPrices = new List<SteamPrice>();
        public List<Reward> rewards = new List<Reward>();
        public LocalizedText name;
        public LocalizedText desc;
        public int sortOrder;
    }

    /// <summary>Resources/Data/shop.json – shared with the server.</summary>
    [Serializable]
    public class ShopCatalog
    {
        public int goldToJadeRate = 1;
        public int maxPassDays = 180;
        public string[] vessels = new string[0];
        public List<ProductDef> products = new List<ProductDef>();
        public List<ShopItem> items = new List<ShopItem>();

        public ShopItem Item(string id)
        {
            if (string.IsNullOrEmpty(id) || items == null) return null;
            foreach (var i in items) if (i.id == id) return i;
            return null;
        }

        public ProductDef Product(string id)
        {
            if (string.IsNullOrEmpty(id) || products == null) return null;
            foreach (var p in products) if (p.id == id) return p;
            return null;
        }

        /// <summary>The basic (default) item of a category; targeted categories have none (the base look is used).</summary>
        public ShopItem Basic(string category)
        {
            foreach (var i in items) if (i.basic && i.category == category) return i;
            return null;
        }

        public List<ShopItem> InCategory(string category)
        {
            var list = new List<ShopItem>();
            foreach (var i in items) if (i.category == category) list.Add(i);
            list.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));
            return list;
        }

        /// <summary>Data mistakes that would break purchases; checked by tests and at load.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            var ids = new HashSet<string>();
            foreach (var i in items)
            {
                if (!ids.Add(i.id)) problems.Add("Duplicate item id " + i.id);
                if (Array.IndexOf(ShopCategories.Cosmetic, i.category) < 0 && i.category != ShopCategories.Special)
                    problems.Add(i.id + ": unknown category " + i.category);
                if (ShopCategories.IsTargeted(i.category) && string.IsNullOrEmpty(i.target)) problems.Add(i.id + ": needs a target");
                if (i.category == ShopCategories.VesselKit && Array.IndexOf(vessels, i.target) < 0) problems.Add(i.id + ": unknown vessel " + i.target);
                if (i.consumable && (i.rewards == null || i.rewards.Count == 0)) problems.Add(i.id + ": consumable without rewards");
                if (i.price != null && i.price.amount > 0 && i.price.currency != Currencies.Gold && i.price.currency != Currencies.Jade && i.price.currency != Currencies.Coins)
                    problems.Add(i.id + ": price currency must be gold, jade or coins");
            }
            foreach (var p in products)
            {
                if (!ids.Add(p.id)) problems.Add("Duplicate product id " + p.id);
                if (p.kind == "pass" && (p.passDays <= 0 || p.passDailyJade <= 0)) problems.Add(p.id + ": pass needs passDays and passDailyJade");
                if (p.rewards != null)
                    foreach (var r in p.rewards)
                        if (r.type == Currencies.Item && Item(r.itemId) == null) problems.Add(p.id + ": rewards unknown item " + r.itemId);
            }
            return problems;
        }
    }

    // ------------------------------------------------------------------ player state

    [Serializable]
    public class TargetedChoice
    {
        public string target;
        public string item;
    }

    /// <summary>Owned cosmetics and what is equipped. Empty choices mean the basic look.</summary>
    [Serializable]
    public class CosmeticLoadout
    {
        public List<string> owned = new List<string>();
        public string avatarFrame;
        public string profileFrame;
        public string bubbleFrame;
        public string exhaust;
        public string garage;
        public List<TargetedChoice> navigatorSkins = new List<TargetedChoice>();
        public List<TargetedChoice> vesselKits = new List<TargetedChoice>();

        public string Get(string category, string target = null)
        {
            switch (category)
            {
                case ShopCategories.AvatarFrame: return avatarFrame;
                case ShopCategories.ProfileFrame: return profileFrame;
                case ShopCategories.BubbleFrame: return bubbleFrame;
                case ShopCategories.Exhaust: return exhaust;
                case ShopCategories.Garage: return garage;
                case ShopCategories.NavigatorSkin: return Find(navigatorSkins, target);
                case ShopCategories.VesselKit: return Find(vesselKits, target);
                default: return null;
            }
        }

        public void Set(string category, string target, string itemId)
        {
            switch (category)
            {
                case ShopCategories.AvatarFrame: avatarFrame = itemId; break;
                case ShopCategories.ProfileFrame: profileFrame = itemId; break;
                case ShopCategories.BubbleFrame: bubbleFrame = itemId; break;
                case ShopCategories.Exhaust: exhaust = itemId; break;
                case ShopCategories.Garage: garage = itemId; break;
                case ShopCategories.NavigatorSkin: Put(navigatorSkins, target, itemId); break;
                case ShopCategories.VesselKit: Put(vesselKits, target, itemId); break;
            }
        }

        static string Find(List<TargetedChoice> list, string target)
        {
            if (list == null) return null;
            foreach (var c in list) if (c.target == target) return c.item;
            return null;
        }

        static void Put(List<TargetedChoice> list, string target, string itemId)
        {
            list.RemoveAll(c => c.target == target);
            if (!string.IsNullOrEmpty(itemId)) list.Add(new TargetedChoice { target = target, item = itemId });
        }
    }

    [Serializable]
    public class ProductCount
    {
        public string id;
        public int count;
    }

    [Serializable]
    public class PassState
    {
        public string id;
        /// <summary>Last UTC day (inclusive) the pass pays out.</summary>
        public string untilDay;
        public string lastClaimDay;
    }

    [Serializable]
    public class EventProgress
    {
        public string id;
        public long value;
        public string lastDay;
        public List<int> claimed = new List<int>();
    }

    // ------------------------------------------------------------------ events and mail data

    [Serializable]
    public class EventMilestone
    {
        public long target;
        public List<Reward> rewards = new List<Reward>();
    }

    [Serializable]
    public class EventDef
    {
        public string id;
        /// <summary>login (days checked in) | races (rewarded races) | score (total race score).</summary>
        public string type;
        public string startUtc;
        public string endUtc;
        public string color;
        public int sortOrder;
        public LocalizedText title;
        public LocalizedText desc;
        public List<EventMilestone> milestones = new List<EventMilestone>();
    }

    /// <summary>Resources/Data/events.json – the server serves its own copy, so events can change without an update.</summary>
    [Serializable]
    public class EventCatalog
    {
        /// <summary>Days after an event ends during which earned rewards can still be claimed.</summary>
        public int claimGraceDays = 3;
        public List<EventDef> events = new List<EventDef>();

        public EventDef Find(string id)
        {
            if (events == null) return null;
            foreach (var e in events) if (e.id == id) return e;
            return null;
        }
    }

    [Serializable]
    public class MailTemplate
    {
        public string id;
        public int expiresDays;
        public LocalizedText title;
        public LocalizedText body;
        public List<Reward> attachments = new List<Reward>();
    }

    /// <summary>Resources/Data/mail.json.</summary>
    [Serializable]
    public class MailTemplates
    {
        public List<MailTemplate> templates = new List<MailTemplate>();

        public MailTemplate Find(string id)
        {
            foreach (var t in templates) if (t.id == id) return t;
            return null;
        }
    }

    /// <summary>One message in the player's mailbox (wire format of GET /v1/mail).</summary>
    [Serializable]
    public class MailItem
    {
        public long id;
        public LocalizedText title;
        public LocalizedText body;
        public List<Reward> attachments = new List<Reward>();
        /// <summary>Unix milliseconds.</summary>
        public long createdAt;
        public long expiresAt;
        public bool read;
        public bool claimed;

        public bool HasAttachments => attachments != null && attachments.Count > 0;
    }
}
