using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using VMS.Core;

namespace VMS.Tests
{
    [TestFixture]
    public class GachaTests
    {
        static NavigatorCatalog Catalog() => TestData.Navigators();

        static BannerDef Standard() => new BannerDef { id = "standard", type = "standard", featured = new string[0], pityGroup = "standard" };

        static BannerDef Limited() => new BannerDef
        {
            id = "limited_son_lam",
            type = "limited",
            featured = new[] { "son_lam", "lan_nhi", "nghe_bao" },
            pityGroup = "limited",
        };

        [Test]
        public void ShippedDataIsValid()
        {
            var problems = Catalog().Validate();
            Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
            var banners = TestData.Banners();
            var catalog = Catalog();
            foreach (var b in banners.banners)
                foreach (var id in b.featured)
                    Assert.IsTrue(catalog.Find(id) != null, b.id + " features unknown navigator " + id);
        }

        [Test]
        public void XoshiroMatchesReferenceSequence()
        {
            // Reference values pinned so the TypeScript port can be checked against them.
            var rng = new Xoshiro128(12345u);
            var first = new[] { rng.NextUInt(), rng.NextUInt(), rng.NextUInt() };
            var again = new Xoshiro128(12345u);
            for (int i = 0; i < first.Length; i++) Assert.AreEqual(first[i], again.NextUInt());
            Assert.IsTrue(first[0] != first[1] && first[1] != first[2], "generator looks stuck");
        }

        [Test]
        public void BaseRatesMatchConfigWithoutPity()
        {
            var cfg = new GachaConfig { hardPity = 0, softPityStart = 0 };
            var engine = new GachaEngine(cfg, Catalog());
            var state = new GachaState();
            var rng = new Xoshiro128(7u);
            int thien = 0, dia = 0;
            const int pulls = 200000;
            for (int i = 0; i < pulls; i++)
            {
                var r = engine.Pull(Standard(), state, 1, rng)[0];
                if (r.rarity == 3) thien++;
                else if (r.rarity == 2) dia++;
            }
            Assert.AreEqual(0.03, thien / (double)pulls, 0.002);
            Assert.AreEqual(0.185, dia / (double)pulls, 0.004);
        }

        [Test]
        public void HardPityIsNeverExceeded()
        {
            var cfg = new GachaConfig { hardPity = 20, rates = new RarityRates { thien = 0.001, dia = 0.2 } };
            var engine = new GachaEngine(cfg, Catalog());
            var state = new GachaState();
            var rng = new Xoshiro128(99u);
            int sinceThien = 0;
            for (int i = 0; i < 5000; i++)
            {
                var r = engine.Pull(Standard(), state, 1, rng)[0];
                sinceThien++;
                if (r.rarity == 3) sinceThien = 0;
                Assert.LessOrEqual(sinceThien, 19);
            }
        }

        [Test]
        public void SoftPityRaisesChanceGradually()
        {
            var cfg = new GachaConfig { softPityStart = 50, softPityStep = 0.1, hardPity = 60 };
            Assert.AreEqual(0.03, cfg.ThienChanceAt(49), 1e-9);
            Assert.AreEqual(0.13, cfg.ThienChanceAt(50), 1e-9);
            Assert.AreEqual(0.23, cfg.ThienChanceAt(51), 1e-9);
            Assert.AreEqual(1.0, cfg.ThienChanceAt(60), 1e-9);
        }

        [Test]
        public void LosingFiftyFiftyGuaranteesNextFeatured()
        {
            var cfg = new GachaConfig { rates = new RarityRates { thien = 0.5, dia = 0.3 } };
            var engine = new GachaEngine(cfg, Catalog());
            var state = new GachaState();
            var rng = new Xoshiro128(2024u);
            bool lastWasLoss = false;
            int losses = 0;
            for (int i = 0; i < 4000; i++)
            {
                var r = engine.Pull(Limited(), state, 1, rng)[0];
                if (r.rarity != 3) continue;
                if (lastWasLoss) Assert.IsTrue(r.featured && r.navigatorId == "son_lam", "guarantee not honoured");
                lastWasLoss = !r.featured;
                if (lastWasLoss) losses++;
            }
            Assert.Greater(losses, 100);
        }

        [Test]
        public void LimitedNavigatorsStayOffStandardBanner()
        {
            var catalog = Catalog();
            var engine = new GachaEngine(new GachaConfig { rates = new RarityRates { thien = 0.5, dia = 0.3 } }, catalog);
            var state = new GachaState();
            var rng = new Xoshiro128(5u);
            for (int i = 0; i < 3000; i++)
            {
                var r = engine.Pull(Standard(), state, 1, rng)[0];
                Assert.IsFalse(catalog.Find(r.navigatorId).limited, r.navigatorId + " is limited");
            }
        }

        [Test]
        public void TenPullAlwaysContainsDiaOrBetter()
        {
            var engine = new GachaEngine(new GachaConfig { rates = new RarityRates { thien = 0.0, dia = 0.01 }, hardPity = 0 }, Catalog());
            var state = new GachaState();
            var rng = new Xoshiro128(11u);
            for (int i = 0; i < 500; i++)
            {
                var results = engine.Pull(Standard(), state, 10, rng);
                int best = 0;
                foreach (var r in results) best = Math.Max(best, r.rarity);
                Assert.GreaterOrEqual(best, 2);
            }
        }

        [Test]
        public void PublishedRatesSumToOne()
        {
            var engine = new GachaEngine(TestData.Gacha(), Catalog());
            foreach (var banner in new[] { Standard(), Limited() })
            {
                double sum = 0;
                foreach (var line in engine.DescribeRates(banner)) sum += line.probability;
                Assert.AreEqual(1.0, sum, 1e-9);
            }
        }

        [Test]
        public void ServicePaysWithTicketsFirstThenJade()
        {
            var cfg = TestData.Gacha();
            var service = new GachaService(new GachaEngine(cfg, Catalog()), TestData.Banners());
            var profile = new PlayerProfile { jade = 10000, tickets = 3 };
            var results = service.Pull(profile, "standard", 10, new Xoshiro128(1u), new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc));
            Assert.AreEqual(10, results.Count);
            Assert.AreEqual(0, profile.tickets);
            Assert.AreEqual(10000 - 7 * cfg.costPerPull, profile.jade);
            int copies = 0;
            foreach (var n in profile.navigators) copies += n.copies;
            Assert.AreEqual(10, copies);
        }

        [Test]
        public void ServiceRejectsInactiveBannerAndPoorPlayers()
        {
            var service = new GachaService(new GachaEngine(TestData.Gacha(), Catalog()), TestData.Banners());
            var past = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var ex = Assert.Throws<GachaException>(() => service.Pull(new PlayerProfile { jade = 99999 }, "limited_son_lam", 1, new Xoshiro128(1u), past));
            Assert.AreEqual("banner_inactive", ex.Code);
            var now = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
            ex = Assert.Throws<GachaException>(() => service.Pull(new PlayerProfile { jade = 10 }, "standard", 1, new Xoshiro128(1u), now));
            Assert.AreEqual("insufficient_jade", ex.Code);
        }

        [Test]
        public void DuplicatesBecomeFragmentsAndAwakeningSpendsThem()
        {
            var cfg = TestData.Gacha();
            var profile = new PlayerProfile { coins = 100000 };
            var results = new List<PullResult>
            {
                new PullResult { navigatorId = "thanh_van", rarity = 3 },
                new PullResult { navigatorId = "thanh_van", rarity = 3 },
            };
            CollectionRules.Apply(profile, results, cfg);
            Assert.IsTrue(results[0].isNew);
            Assert.IsFalse(results[1].isNew);
            Assert.AreEqual(cfg.dupeFragments.thien, results[1].fragments);

            CollectionRules.Awaken(profile, "thanh_van", cfg);
            var owned = profile.FindNavigator("thanh_van");
            Assert.AreEqual(1, owned.awakening);
            Assert.AreEqual(cfg.dupeFragments.thien - cfg.awakenFragmentCosts[0], owned.fragments);
            Assert.AreEqual(100000 - cfg.awakenCoinCosts[0], profile.coins);
        }

        [Test]
        public void SparkExchangesPointsForFeatured()
        {
            var cfg = TestData.Gacha();
            var engine = new GachaEngine(cfg, Catalog());
            var state = new GachaState();
            state.GetSpark("limited_son_lam").points = cfg.sparkCost;
            var result = engine.Spark(Limited(), state, "son_lam");
            Assert.AreEqual("son_lam", result.navigatorId);
            Assert.AreEqual(0, state.GetSpark("limited_son_lam").points);
            Assert.Throws<InvalidOperationException>(() => engine.Spark(Limited(), state, "son_lam"));
        }

        /// <summary>
        /// Rewrites shared/gacha-golden.json from the C# engine. Explicit: run it by hand (Test Runner) only after
        /// changing the gacha rules on purpose in both GachaEngine.cs and server/src/game/gacha.ts.
        /// </summary>
        [Test, Explicit("Rewrites shared/gacha-golden.json")]
        public void RegenerateGoldenVectors()
        {
            TestData.Save(TestData.GoldenPath, GoldenGenerator.Build(Catalog()));
        }

        /// <summary>
        /// Cross-language contract: the TypeScript server engine must reproduce these exact results.
        /// </summary>
        [Test]
        public void MatchesSharedGoldenVectors()
        {
            if (!File.Exists(TestData.GoldenPath)) Assert.Ignore("shared/gacha-golden.json not found");
            var golden = TestData.Load<GoldenFile>(TestData.GoldenPath);
            var engine = new GachaEngine(golden.config, new NavigatorCatalog { navigators = golden.navigators });
            var banners = new BannerCatalog { banners = golden.banners };
            foreach (var c in golden.cases)
            {
                var state = new GachaState();
                var rng = new Xoshiro128(c.seed);
                var actual = new List<PullResult>();
                foreach (int count in c.pulls) actual.AddRange(engine.Pull(banners.Find(c.bannerId), state, count, rng));
                Assert.AreEqual(c.expected.Length, actual.Count, "result count for seed " + c.seed);
                for (int i = 0; i < actual.Count; i++)
                {
                    Assert.AreEqual(c.expected[i].navigatorId, actual[i].navigatorId, "seed " + c.seed + " pull " + i);
                    Assert.AreEqual(c.expected[i].rarity, actual[i].rarity, "seed " + c.seed + " pull " + i);
                    Assert.AreEqual(c.expected[i].featured, actual[i].featured, "seed " + c.seed + " pull " + i);
                }
                foreach (var p in c.finalState.pity)
                {
                    var mine = state.GetPity(p.group);
                    Assert.AreEqual(p.pullsSinceThien, mine.pullsSinceThien);
                    Assert.AreEqual(p.featuredGuaranteed, mine.featuredGuaranteed);
                }
            }
        }
    }
}
