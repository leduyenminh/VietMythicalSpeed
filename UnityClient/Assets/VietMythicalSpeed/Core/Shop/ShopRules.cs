using System;
using System.Collections.Generic;
using System.Globalization;

namespace VMS.Core
{
    public sealed class ShopException : Exception
    {
        public readonly string Code;

        public ShopException(string code, string message) : base(message)
        {
            Code = code;
        }
    }

    public static class TimeWindow
    {
        /// <summary>start ≤ now &lt; end; empty bounds are open.</summary>
        public static bool Contains(string startUtc, string endUtc, DateTime now, int extraDays = 0)
        {
            DateTime start, end;
            if (BannerDef.TryParseUtc(startUtc, out start) && now < start) return false;
            if (BannerDef.TryParseUtc(endUtc, out end) && now >= end.AddDays(extraDays)) return false;
            return true;
        }

        public static DateTime ParseDay(string day) =>
            DateTime.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    /// <summary>Currency balances. Gold spends its free (bonus) part first, so refunds can only take back paid gold.</summary>
    public static class Wallet
    {
        public static int Balance(PlayerProfile p, string currency)
        {
            switch (currency)
            {
                case Currencies.Gold: return p.gold + p.goldBonus;
                case Currencies.Jade: return p.jade;
                case Currencies.Coins: return p.coins;
                case Currencies.Tickets: return p.tickets;
                default: return 0;
            }
        }

        public static void Spend(PlayerProfile p, string currency, int amount)
        {
            if (amount < 0) throw new ShopException("invalid_request", "Negative amount.");
            if (Balance(p, currency) < amount) throw new ShopException("insufficient_" + currency, "Not enough " + currency + ".");
            switch (currency)
            {
                case Currencies.Gold:
                    int fromBonus = Math.Min(p.goldBonus, amount);
                    p.goldBonus -= fromBonus;
                    p.gold -= amount - fromBonus;
                    break;
                case Currencies.Jade: p.jade -= amount; break;
                case Currencies.Coins: p.coins -= amount; break;
                case Currencies.Tickets: p.tickets -= amount; break;
                default: throw new ShopException("invalid_request", "Unknown currency " + currency + ".");
            }
        }

        /// <summary>Applies one reward. Gold given as a reward is bonus gold. Already-owned items are skipped.</summary>
        public static void Grant(PlayerProfile p, ShopCatalog catalog, Reward r)
        {
            if (r == null) return;
            int amount = Math.Max(0, r.amount);
            switch (r.type)
            {
                case Currencies.Gold: p.goldBonus += amount; break;
                case Currencies.Jade: p.jade += amount; break;
                case Currencies.Coins: p.coins += amount; break;
                case Currencies.Tickets: p.tickets += amount; break;
                case Currencies.Item:
                    var item = catalog != null ? catalog.Item(r.itemId) : null;
                    if (item == null || item.basic) break;
                    if (item.consumable)
                    {
                        Grant(p, catalog, item.rewards);
                        break;
                    }
                    if (!p.cosmetics.owned.Contains(item.id)) p.cosmetics.owned.Add(item.id);
                    break;
            }
        }

        public static void Grant(PlayerProfile p, ShopCatalog catalog, List<Reward> rewards)
        {
            if (rewards == null) return;
            foreach (var r in rewards) Grant(p, catalog, r);
        }
    }

    public sealed class BuyResult
    {
        public ShopItem item;
        public List<Reward> rewards = new List<Reward>();
    }

    public sealed class ProductGrant
    {
        public int gold;
        public int bonus;
        public bool firstPurchase;
        public List<Reward> rewards = new List<Reward>();
        public int passJade;
    }

    /// <summary>Shop, equipment and real-money product rules. Port: server/src/game/shop.ts – keep both in sync.</summary>
    public static class ShopRules
    {
        public const int MaxExchange = 100000;

        public static bool Owns(PlayerProfile p, ShopItem item) =>
            item != null && (item.basic || (p != null && p.cosmetics != null && p.cosmetics.owned != null && p.cosmetics.owned.Contains(item.id)));

        public static bool IsOnSale(ShopItem item, DateTime now) =>
            item != null && !item.hidden && item.ForSale && TimeWindow.Contains(item.startUtc, item.endUtc, now);

        public static BuyResult Buy(PlayerProfile p, ShopCatalog catalog, string itemId, DateTime now)
        {
            p.Normalize();
            var item = catalog.Item(itemId);
            if (item == null) throw new ShopException("item_not_found", "Unknown item.");
            if (!IsOnSale(item, now)) throw new ShopException("item_unavailable", "This item isn't for sale.");
            if (!item.consumable && Owns(p, item)) throw new ShopException("already_owned", "Already owned.");
            Wallet.Spend(p, item.price.currency, item.price.amount);
            var result = new BuyResult { item = item };
            if (item.consumable)
            {
                Wallet.Grant(p, catalog, item.rewards);
                result.rewards.AddRange(item.rewards);
            }
            else
            {
                p.cosmetics.owned.Add(item.id);
                result.rewards.Add(new Reward { type = Currencies.Item, amount = 1, itemId = item.id });
            }
            return result;
        }

        /// <summary>Equips an owned item; an empty id goes back to the basic look.</summary>
        public static void Equip(PlayerProfile p, ShopCatalog catalog, string category, string target, string itemId)
        {
            p.Normalize();
            if (!ShopCategories.IsEquippable(category)) throw new ShopException("invalid_request", "Nothing to equip in " + category + ".");
            bool targeted = ShopCategories.IsTargeted(category);
            if (targeted && string.IsNullOrEmpty(target)) throw new ShopException("invalid_request", "A target is required.");
            if (string.IsNullOrEmpty(itemId))
            {
                p.cosmetics.Set(category, targeted ? target : null, null);
                return;
            }
            var item = catalog.Item(itemId);
            if (item == null || item.category != category) throw new ShopException("item_not_found", "Unknown item.");
            if (targeted && item.target != target) throw new ShopException("wrong_target", "That item is for something else.");
            if (!Owns(p, item)) throw new ShopException("not_owned", "Not owned.");
            p.cosmetics.Set(category, targeted ? target : null, item.basic ? null : item.id);
        }

        /// <summary>Equipped item of a category, falling back to the basic one (null for targeted categories).</summary>
        public static ShopItem Equipped(PlayerProfile p, ShopCatalog catalog, string category, string target = null)
        {
            string id = p != null && p.cosmetics != null ? p.cosmetics.Get(category, target) : null;
            var item = catalog.Item(id);
            if (item != null && item.category == category && Owns(p, item)) return item;
            return ShopCategories.IsTargeted(category) ? null : catalog.Basic(category);
        }

        /// <summary>Gold → jade (for fortune-stick draws). Returns the jade received.</summary>
        public static int ExchangeGold(PlayerProfile p, ShopCatalog catalog, int amount)
        {
            if (amount <= 0 || amount > MaxExchange) throw new ShopException("invalid_request", "Amount out of range.");
            Wallet.Spend(p, Currencies.Gold, amount);
            int jade = amount * Math.Max(1, catalog.goldToJadeRate);
            p.jade += jade;
            return jade;
        }

        public static int PurchaseCount(PlayerProfile p, string productId)
        {
            if (p.purchases == null) return 0;
            foreach (var c in p.purchases) if (c.id == productId) return c.count;
            return 0;
        }

        public static bool CanBuyProduct(PlayerProfile p, ProductDef product) =>
            product != null && (product.limit <= 0 || PurchaseCount(p, product.id) < product.limit);

        /// <summary>
        /// Grants a verified real-money purchase. Never refuses: the player has already paid, so limits are
        /// checked before the store charges (<see cref="CanBuyProduct"/>), not here.
        /// </summary>
        public static ProductGrant ApplyProduct(PlayerProfile p, ShopCatalog catalog, ProductDef product, DateTime now)
        {
            p.Normalize();
            int count = PurchaseCount(p, product.id);
            var grant = new ProductGrant { gold = Math.Max(0, product.gold), firstPurchase = count == 0 };
            grant.bonus = grant.firstPurchase && product.firstBonus > 0 ? product.firstBonus : Math.Max(0, product.bonus);
            p.gold += grant.gold;
            p.goldBonus += grant.bonus;
            if (product.rewards != null)
            {
                Wallet.Grant(p, catalog, product.rewards);
                grant.rewards.AddRange(product.rewards);
            }
            if (product.kind == "pass" && product.passDays > 0)
            {
                ExtendPass(p, catalog, product, now);
                grant.passJade = ClaimPassJade(p, catalog, now);
            }
            var entry = p.purchases.Find(c => c.id == product.id);
            if (entry == null) p.purchases.Add(new ProductCount { id = product.id, count = 1 });
            else entry.count++;
            return grant;
        }

        static void ExtendPass(PlayerProfile p, ShopCatalog catalog, ProductDef product, DateTime now)
        {
            DateTime today = now.Date;
            var pass = p.passes.Find(x => x.id == product.id);
            if (pass == null)
            {
                pass = new PassState { id = product.id };
                p.passes.Add(pass);
            }
            DateTime start = today;
            if (!string.IsNullOrEmpty(pass.untilDay))
            {
                DateTime until = TimeWindow.ParseDay(pass.untilDay);
                if (until >= today) start = until.AddDays(1);
            }
            DateTime end = start.AddDays(product.passDays - 1);
            DateTime cap = today.AddDays(Math.Max(product.passDays, catalog.maxPassDays) - 1);
            if (end > cap) end = cap;
            pass.untilDay = EconomyRules.DayKey(end);
        }

        /// <summary>Pays today's jade for every active pass, once per UTC day.</summary>
        public static int ClaimPassJade(PlayerProfile p, ShopCatalog catalog, DateTime now)
        {
            p.Normalize();
            string today = EconomyRules.DayKey(now);
            int total = 0;
            foreach (var pass in p.passes)
            {
                var product = catalog.Product(pass.id);
                if (product == null || string.IsNullOrEmpty(pass.untilDay)) continue;
                if (string.CompareOrdinal(pass.untilDay, today) < 0 || pass.lastClaimDay == today) continue;
                pass.lastClaimDay = today;
                total += product.passDailyJade;
            }
            p.jade += total;
            return total;
        }

        /// <summary>Days of pass left including today (0 when inactive).</summary>
        public static int PassDaysLeft(PlayerProfile p, string passId, DateTime now)
        {
            var pass = p.passes != null ? p.passes.Find(x => x.id == passId) : null;
            if (pass == null || string.IsNullOrEmpty(pass.untilDay)) return 0;
            int days = (int)(TimeWindow.ParseDay(pass.untilDay) - now.Date).TotalDays + 1;
            return Math.Max(0, days);
        }
    }

    /// <summary>Time-limited events. Port: server/src/game/events.ts – keep both in sync.</summary>
    public static class EventRules
    {
        public const string Login = "login";
        public const string Races = "races";
        public const string Score = "score";

        public static bool IsRunning(EventDef e, DateTime now) => TimeWindow.Contains(e.startUtc, e.endUtc, now);

        /// <summary>Visible and claimable: running, or ended less than claimGraceDays ago.</summary>
        public static bool IsVisible(EventDef e, EventCatalog catalog, DateTime now) =>
            TimeWindow.Contains(e.startUtc, e.endUtc, now, catalog.claimGraceDays);

        public static List<EventDef> Visible(EventCatalog catalog, DateTime now)
        {
            var list = new List<EventDef>();
            if (catalog == null || catalog.events == null) return list;
            foreach (var e in catalog.events) if (IsVisible(e, catalog, now)) list.Add(e);
            list.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));
            return list;
        }

        public static EventProgress Progress(PlayerProfile p, string eventId, bool create)
        {
            p.Normalize();
            var progress = p.events.Find(x => x.id == eventId);
            if (progress == null && create)
            {
                progress = new EventProgress { id = eventId };
                p.events.Add(progress);
            }
            return progress;
        }

        /// <summary>Counts today for every running login event (called with the daily login reward).</summary>
        public static void CheckIn(PlayerProfile p, EventCatalog catalog, DateTime now)
        {
            if (catalog == null || catalog.events == null) return;
            string today = EconomyRules.DayKey(now);
            foreach (var e in catalog.events)
            {
                if (e.type != Login || !IsRunning(e, now)) continue;
                var progress = Progress(p, e.id, true);
                if (progress.lastDay == today) continue;
                progress.lastDay = today;
                progress.value++;
            }
        }

        /// <summary>Counts a rewarded race for events running when it was played (offline races sync later).</summary>
        public static void OnRace(PlayerProfile p, EventCatalog catalog, RaceReport report, DateTime playedAt, float minDurationSec)
        {
            if (catalog == null || catalog.events == null || report == null) return;
            if (report.mode == 3 || report.durationSec < minDurationSec) return; // Zen and very short songs don't count
            foreach (var e in catalog.events)
            {
                if (!IsRunning(e, playedAt)) continue;
                if (e.type == Races) Progress(p, e.id, true).value += 1;
                else if (e.type == Score) Progress(p, e.id, true).value += Math.Max(0, report.score);
            }
        }

        public static List<Reward> Claim(PlayerProfile p, EventCatalog catalog, ShopCatalog shop, string eventId, int index, DateTime now)
        {
            var e = catalog.Find(eventId);
            if (e == null) throw new ShopException("event_not_found", "Unknown event.");
            if (!IsVisible(e, catalog, now)) throw new ShopException("event_ended", "This event has ended.");
            if (index < 0 || index >= e.milestones.Count) throw new ShopException("invalid_request", "No such reward.");
            var progress = Progress(p, eventId, true);
            if (progress.claimed.Contains(index)) throw new ShopException("already_claimed", "Already claimed.");
            if (progress.value < e.milestones[index].target) throw new ShopException("not_reached", "Not reached yet.");
            progress.claimed.Add(index);
            Wallet.Grant(p, shop, e.milestones[index].rewards);
            return e.milestones[index].rewards;
        }

        /// <summary>Drops progress of events that no longer exist, so profiles don't grow forever.</summary>
        public static void Prune(PlayerProfile p, EventCatalog catalog)
        {
            if (catalog == null || catalog.events == null) return;
            p.Normalize();
            p.events.RemoveAll(x => catalog.Find(x.id) == null);
        }
    }

    /// <summary>Everything the once-a-day login gives: the base reward, pass jade and event check-ins.</summary>
    public static class DailyRules
    {
        public static DailyClaimResult Claim(PlayerProfile p, EconomyConfig economy, ShopCatalog shop, EventCatalog events, DateTime now)
        {
            p.Normalize();
            var result = EconomyRules.ClaimDailyLogin(p, economy, now);
            if (shop != null) result.passJade = ShopRules.ClaimPassJade(p, shop, now);
            EventRules.CheckIn(p, events, now);
            return result;
        }
    }
}
