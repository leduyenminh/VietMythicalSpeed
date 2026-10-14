using System;
using System.Linq;
using NUnit.Framework;
using VMS.Core;

namespace VMS.Tests
{
    /// <summary>Same scenarios as server/test/shop.test.ts – the C# and TypeScript rules must agree.</summary>
    [TestFixture]
    public class ShopTests
    {
        static readonly DateTime At = new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);
        ShopCatalog _shop;
        EventCatalog _events;
        EconomyConfig _economy;

        [OneTimeSetUp]
        public void Load()
        {
            _shop = TestData.Shop();
            _events = TestData.Events();
            _economy = TestData.Economy();
        }

        PlayerProfile Fresh() => EconomyRules.NewProfile(_economy, "acc", "Tester").Normalize();

        static DateTime Utc(int y, int mo, int d, int h = 8) => new DateTime(y, mo, d, h, 0, 0, DateTimeKind.Utc);

        static void Fails(string code, TestDelegate action)
        {
            var e = Assert.Throws<ShopException>(action);
            Assert.AreEqual(code, e.Code);
        }

        [Test]
        public void ShippedShopDataIsConsistent()
        {
            CollectionAssert.IsEmpty(_shop.Validate());
            foreach (var category in new[] { ShopCategories.AvatarFrame, ShopCategories.ProfileFrame, ShopCategories.BubbleFrame, ShopCategories.Exhaust, ShopCategories.Garage })
                Assert.AreEqual(1, _shop.items.Count(i => i.category == category && i.basic), category + " needs exactly one basic item");
            foreach (var e in _events.events)
            foreach (var m in e.milestones)
            foreach (var r in m.rewards)
                if (r.type == Currencies.Item) Assert.IsNotNull(_shop.Item(r.itemId), e.id + " rewards unknown item " + r.itemId);
        }

        [Test]
        public void GoldSpendsBonusFirstAndOwnedItemsCannotBeBoughtTwice()
        {
            var p = Fresh();
            p.gold = 500;
            p.goldBonus = 100;
            p.coins = 50000;
            ShopRules.Buy(p, _shop, "frame_lotus", At);
            Assert.AreEqual(0, p.goldBonus);
            Assert.AreEqual(300, p.gold);
            Assert.IsTrue(p.cosmetics.owned.Contains("frame_lotus"));
            Fails("already_owned", () => ShopRules.Buy(p, _shop, "frame_lotus", At));
            Fails("insufficient_gold", () => ShopRules.Buy(p, _shop, "frame_phoenix", At));
            Fails("item_unavailable", () => ShopRules.Buy(p, _shop, "frame_welcome", At));
            Fails("item_unavailable", () => ShopRules.Buy(p, _shop, "frame_basic", At));

            ShopRules.Buy(p, _shop, "frame_jade_cloud", At);
            Assert.AreEqual(25000, p.coins);
            ShopRules.Buy(p, _shop, "coin_pouch_s", At);
            ShopRules.Buy(p, _shop, "coin_pouch_s", At);
            Assert.AreEqual(25000 + 12000, p.coins);
            Assert.AreEqual(180, p.gold);
        }

        [Test]
        public void EquippingNeedsOwnershipAndTheRightTarget()
        {
            var p = Fresh();
            Fails("not_owned", () => ShopRules.Equip(p, _shop, ShopCategories.AvatarFrame, null, "frame_lotus"));
            p.cosmetics.owned.AddRange(new[] { "frame_lotus", "kit_rong_jade", "skin_vang_anh_ao_dai" });
            ShopRules.Equip(p, _shop, ShopCategories.AvatarFrame, null, "frame_lotus");
            Assert.AreEqual("frame_lotus", p.cosmetics.avatarFrame);
            Assert.AreEqual("frame_lotus", ShopRules.Equipped(p, _shop, ShopCategories.AvatarFrame).id);
            ShopRules.Equip(p, _shop, ShopCategories.AvatarFrame, null, "frame_basic");
            Assert.IsNull(p.cosmetics.avatarFrame);
            Assert.AreEqual("frame_basic", ShopRules.Equipped(p, _shop, ShopCategories.AvatarFrame).id);
            ShopRules.Equip(p, _shop, ShopCategories.VesselKit, "thuyen_rong", "kit_rong_jade");
            Assert.AreEqual("kit_rong_jade", p.cosmetics.Get(ShopCategories.VesselKit, "thuyen_rong"));
            Fails("wrong_target", () => ShopRules.Equip(p, _shop, ShopCategories.VesselKit, "xe_lan", "kit_rong_jade"));
            Fails("item_not_found", () => ShopRules.Equip(p, _shop, ShopCategories.AvatarFrame, null, "kit_rong_jade"));
            Fails("invalid_request", () => ShopRules.Equip(p, _shop, ShopCategories.Emotion, null, "emo_smile"));
            ShopRules.Equip(p, _shop, ShopCategories.NavigatorSkin, "vang_anh", "skin_vang_anh_ao_dai");
            ShopRules.Equip(p, _shop, ShopCategories.NavigatorSkin, "vang_anh", "");
            CollectionAssert.IsEmpty(p.cosmetics.navigatorSkins);
            Assert.IsNull(ShopRules.Equipped(p, _shop, ShopCategories.NavigatorSkin, "vang_anh"), "no skin = base look");
        }

        [Test]
        public void GoldExchangesOneToOneIntoJade()
        {
            var p = Fresh();
            p.gold = 10;
            p.goldBonus = 150;
            Assert.AreEqual(150, ShopRules.ExchangeGold(p, _shop, 150));
            Assert.AreEqual(3000 + 150, p.jade);
            Assert.AreEqual(0, p.goldBonus);
            Assert.AreEqual(10, p.gold);
            Fails("insufficient_gold", () => ShopRules.ExchangeGold(p, _shop, 11));
            Fails("invalid_request", () => ShopRules.ExchangeGold(p, _shop, 0));
        }

        [Test]
        public void ProductsDoubleOnFirstPurchaseAndRespectLimits()
        {
            var p = Fresh();
            var first = ShopRules.ApplyProduct(p, _shop, _shop.Product("gold_300"), At);
            Assert.AreEqual(new[] { 300, 300 }, new[] { first.gold, first.bonus });
            Assert.IsTrue(first.firstPurchase);
            var second = ShopRules.ApplyProduct(p, _shop, _shop.Product("gold_300"), At);
            Assert.AreEqual(new[] { 300, 30 }, new[] { second.gold, second.bonus });
            Assert.AreEqual(600, p.gold);
            Assert.AreEqual(330, p.goldBonus);

            var starter = _shop.Product("starter_pack");
            Assert.IsTrue(ShopRules.CanBuyProduct(p, starter));
            ShopRules.ApplyProduct(p, _shop, starter, At);
            Assert.IsFalse(ShopRules.CanBuyProduct(p, starter));
            Assert.IsTrue(p.cosmetics.owned.Contains("frame_starter"));
            Assert.AreEqual(20, p.tickets);
        }

        [Test]
        public void LanternPassPaysJadeOncePerDayAndStacksToHalfAYear()
        {
            var p = Fresh();
            var grant = ShopRules.ApplyProduct(p, _shop, _shop.Product("lantern_pass"), At);
            Assert.AreEqual(90, grant.passJade);
            Assert.AreEqual("2026-11-10", p.passes[0].untilDay);
            int jade = p.jade;
            Assert.AreEqual(0, DailyRules.Claim(p, _economy, _shop, _events, Utc(2026, 10, 12, 20)).passJade);
            Assert.AreEqual(90, DailyRules.Claim(p, _economy, _shop, _events, Utc(2026, 10, 13)).passJade);
            Assert.AreEqual(jade + 120 + 120 + 90, p.jade);
            Assert.AreEqual(0, DailyRules.Claim(p, _economy, _shop, _events, Utc(2026, 11, 11)).passJade);
            Assert.AreEqual(0, ShopRules.PassDaysLeft(p, "lantern_pass", Utc(2026, 11, 11)));

            var q = Fresh();
            for (int i = 0; i < 8; i++) ShopRules.ApplyProduct(q, _shop, _shop.Product("lantern_pass"), At);
            Assert.AreEqual("2027-04-09", q.passes[0].untilDay);
            Assert.AreEqual(180, ShopRules.PassDaysLeft(q, "lantern_pass", At));
        }

        static RaceReport Report(int mode = 0, float duration = 95f) => new RaceReport
        {
            songId = "demo", durationSec = duration, mode = mode, difficulty = 1, score = 72480, maxCombo = 163, perfect = 163, accuracy = 1f, navigatorId = "vang_anh",
        };

        [Test]
        public void EventsCountLoginsRacesAndScore()
        {
            var p = Fresh();
            CollectionAssert.AreEqual(new[] { "grand_opening", "spirit_road", "score_rush" }, EventRules.Visible(_events, At).Select(e => e.id).ToArray());
            DailyRules.Claim(p, _economy, _shop, _events, At);
            DailyRules.Claim(p, _economy, _shop, _events, Utc(2026, 10, 12, 22));
            Assert.AreEqual(1, EventRules.Progress(p, "grand_opening", false).value);
            DailyRules.Claim(p, _economy, _shop, _events, Utc(2026, 10, 13, 1));

            EventRules.OnRace(p, _events, Report(), At, 30f);
            EventRules.OnRace(p, _events, Report(mode: 3), At, 30f);
            EventRules.OnRace(p, _events, Report(duration: 10f), At, 30f);
            EventRules.OnRace(p, _events, Report(), Utc(2026, 10, 5), 30f);
            Assert.AreEqual(2, EventRules.Progress(p, "spirit_road", false).value);
            Assert.AreEqual(72480, EventRules.Progress(p, "score_rush", false).value);

            int jade = p.jade;
            var rewards = EventRules.Claim(p, _events, _shop, "grand_opening", 1, At);
            Assert.AreEqual(Currencies.Coins, rewards[0].type);
            Assert.AreEqual(5000, rewards[0].amount);
            Fails("already_claimed", () => EventRules.Claim(p, _events, _shop, "grand_opening", 1, At));
            Fails("not_reached", () => EventRules.Claim(p, _events, _shop, "grand_opening", 2, At));
            EventRules.Claim(p, _events, _shop, "grand_opening", 0, At);
            Assert.AreEqual(jade + 120, p.jade);

            EventRules.Progress(p, "spirit_road", false).value = 25;
            Assert.DoesNotThrow(() => EventRules.Claim(p, _events, _shop, "spirit_road", 0, Utc(2026, 11, 30, 12)));
            Assert.DoesNotThrow(() => EventRules.Claim(p, _events, _shop, "spirit_road", 1, Utc(2026, 12, 3, 12)));
            Fails("event_ended", () => EventRules.Claim(p, _events, _shop, "spirit_road", 2, new DateTime(2026, 12, 4, 0, 0, 1, DateTimeKind.Utc)));
        }

        [Test]
        public void RewardGrantsTreatGoldAsBonusAndItemsOnce()
        {
            var p = Fresh();
            Wallet.Grant(p, _shop, new System.Collections.Generic.List<Reward>
            {
                new Reward { type = Currencies.Gold, amount = 60 },
                new Reward { type = Currencies.Item, itemId = "emo_wink" },
                new Reward { type = Currencies.Item, itemId = "emo_wink" },
                new Reward { type = Currencies.Item, itemId = "incense_10" },
                new Reward { type = Currencies.Item, itemId = "frame_basic" },
            });
            Assert.AreEqual(new[] { 0, 60, 20 }, new[] { p.gold, p.goldBonus, p.tickets });
            CollectionAssert.AreEqual(new[] { "emo_wink" }, p.cosmetics.owned);
        }

        [Test]
        public void OldSavesWithoutShopFieldsLoadSafely()
        {
            var p = UnityEngine.JsonUtility.FromJson<PlayerProfile>("{\"accountId\":\"a\",\"jade\":5}").Normalize();
            Assert.IsNotNull(p.cosmetics.owned);
            Assert.AreEqual("frame_basic", ShopRules.Equipped(p, _shop, ShopCategories.AvatarFrame).id);
            Assert.AreEqual(0, p.TotalGold);
        }
    }
}
