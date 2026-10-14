using System;
using NUnit.Framework;
using VMS.Core;

namespace VMS.Tests
{
    [TestFixture]
    public class EconomyAndScoringTests
    {
        static readonly DateTime Day1 = new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);

        [Test]
        public void NewProfileGetsStarterKit()
        {
            var cfg = TestData.Economy();
            var p = EconomyRules.NewProfile(cfg, "acc", "Tester");
            Assert.AreEqual(cfg.startingJade, p.jade);
            Assert.AreEqual(cfg.starterNavigator, p.selectedNavigator);
            Assert.IsTrue(p.Owns(cfg.starterNavigator));
        }

        [Test]
        public void DailyLoginOncePerDayAndStreakBonus()
        {
            var cfg = TestData.Economy();
            var p = EconomyRules.NewProfile(cfg, "acc", "Tester");
            int jade = p.jade, tickets = p.tickets;
            for (int day = 0; day < cfg.streakBonusEvery; day++)
            {
                var now = Day1.AddDays(day);
                Assert.IsTrue(EconomyRules.ClaimDailyLogin(p, cfg, now).claimed);
                Assert.IsFalse(EconomyRules.ClaimDailyLogin(p, cfg, now.AddHours(2)).claimed);
            }
            Assert.AreEqual(jade + cfg.dailyLoginJade * cfg.streakBonusEvery, p.jade);
            Assert.AreEqual(tickets + cfg.streakBonusTickets, p.tickets);

            // Skipping a day resets the streak.
            var r = EconomyRules.ClaimDailyLogin(p, cfg, Day1.AddDays(cfg.streakBonusEvery + 1));
            Assert.AreEqual(1, r.streak);
        }

        [Test]
        public void RaceRewardsAreCapped()
        {
            var cfg = TestData.Economy();
            var p = EconomyRules.NewProfile(cfg, "acc", "Tester");
            int coins = p.coins, jade = p.jade;
            var report = new RaceReport { durationSec = 120, score = 999999999, accuracy = 1f, navigatorId = cfg.starterNavigator };
            var first = EconomyRules.GrantRaceReward(p, cfg, report, null, Day1);
            Assert.AreEqual(cfg.race.maxCoinsPerRace, first.coins);
            Assert.AreEqual(cfg.race.jadePerRace, first.jade);
            for (int i = 0; i < 50; i++) EconomyRules.GrantRaceReward(p, cfg, report, null, Day1);
            Assert.AreEqual(coins + cfg.race.dailyCoinCap, p.coins);
            Assert.AreEqual(jade + cfg.race.jadePerRace * cfg.race.jadeRacesPerDay, p.jade);

            var tooShort = EconomyRules.GrantRaceReward(p, cfg, new RaceReport { durationSec = 5, score = 100 }, null, Day1.AddDays(1));
            Assert.AreEqual(0, tooShort.coins);
        }

        [Test]
        public void BondLevelsFollowThresholds()
        {
            var bond = new BondConfig();
            Assert.AreEqual(1, EconomyRules.BondLevel(bond, 0));
            Assert.AreEqual(2, EconomyRules.BondLevel(bond, 100));
            Assert.AreEqual(10, EconomyRules.BondLevel(bond, 99999));
        }

        [Test]
        public void ScoreKeeperComboShieldAndGuard()
        {
            var s = new ScoreKeeper();
            for (int i = 0; i < 25; i++) s.Register(Judgement.Perfect, false);
            Assert.AreEqual(25, s.Combo);
            Assert.AreEqual(1.1f, s.ComboMultiplier, 1e-5f);

            s.ShieldCharges = 1;
            var o = s.Register(Judgement.Miss, false);
            Assert.IsTrue(o.shieldUsed);
            Assert.AreEqual(26, s.Combo);

            s.ComboGuardCharges = 1;
            o = s.Register(Judgement.Miss, false);
            Assert.IsTrue(o.guardUsed);
            Assert.AreEqual(26, s.Combo);
            Assert.AreEqual(1, s.Miss);

            o = s.Register(Judgement.HazardHit, false);
            Assert.IsTrue(o.comboBroken);
            Assert.AreEqual(0, s.Combo);
            Assert.AreEqual(26, s.MaxCombo);
        }

        [Test]
        public void JudgeWindows()
        {
            Assert.AreEqual(Judgement.Perfect, JudgeRules.JudgePearl(0.1f, 1f, 0f));
            Assert.AreEqual(Judgement.Good, JudgeRules.JudgePearl(0.5f, 1f, 0f));
            Assert.AreEqual(Judgement.Miss, JudgeRules.JudgePearl(1f, 1f, 0f));
            Assert.AreEqual(Judgement.Perfect, JudgeRules.JudgePearl(1f, 1f, 1f), "magnet reach");
            Assert.AreEqual(Judgement.HazardHit, JudgeRules.JudgeHazard(0.2f));
            Assert.AreEqual(Judgement.HazardDodge, JudgeRules.JudgeHazard(1f));
        }

        [Test]
        public void Pbkdf2MatchesRfcVectorAndVerifies()
        {
            // RFC 7914 §11 / widely published PBKDF2-HMAC-SHA256 vector: P="password", S="salt", c=1.
            var dk = Pbkdf2.DeriveSha256(System.Text.Encoding.ASCII.GetBytes("password"), System.Text.Encoding.ASCII.GetBytes("salt"), 1, 32);
            Assert.AreEqual("120fb6cffcf8b32c43e7225256c4f837a86548c92ccc35480805987cb70be17b", BitConverter.ToString(dk).Replace("-", "").ToLowerInvariant());
            string salt;
            string hash = Pbkdf2.Hash("Lạc Hồng 123", out salt, 1000);
            Assert.IsTrue(Pbkdf2.Verify("Lạc Hồng 123", salt, hash, 1000));
            Assert.IsFalse(Pbkdf2.Verify("lac hong 123", salt, hash, 1000));
        }

        [Test]
        public void RankRequiresCleanRunForSPlus()
        {
            var s = new ScoreKeeper();
            for (int i = 0; i < 100; i++) s.Register(Judgement.Perfect, false);
            Assert.AreEqual("S+", s.Rank);
            s.Register(Judgement.Miss, false);
            Assert.AreEqual("S", s.Rank);
        }
    }
}
