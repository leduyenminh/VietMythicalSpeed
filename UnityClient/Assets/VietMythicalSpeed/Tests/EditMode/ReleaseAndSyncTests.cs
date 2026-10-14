using System;
using NUnit.Framework;
using VMS.Core;

namespace VMS.Tests
{
    [TestFixture]
    public class ReleaseAndSyncTests
    {
        [TestCase("1.2.3", 1, 2, 3)]
        [TestCase("1.2", 1, 2, 0)]
        [TestCase("7", 7, 0, 0)]
        [TestCase(" v0.10.2-dev ", 0, 10, 2)]
        [TestCase("2.0.0+build.5", 2, 0, 0)]
        public void ParsesVersions(string text, int major, int minor, int patch)
        {
            Assert.IsTrue(AppVersion.TryParse(text, out int a, out int b, out int c));
            Assert.AreEqual(new[] { major, minor, patch }, new[] { a, b, c });
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("1.2.3.4")]
        [TestCase("one.two")]
        [TestCase("1..2")]
        [TestCase("1.2.3a")]
        public void RejectsNonVersions(string text)
        {
            Assert.IsFalse(AppVersion.TryParse(text, out _, out _, out _));
        }

        [Test]
        public void ComparesNumericallyLikeTheServer()
        {
            // Same expectations as server/test/account.test.ts (minimum 1.2.0).
            Assert.Less(AppVersion.Compare("1.1.9", "1.2.0"), 0);
            Assert.AreEqual(0, AppVersion.Compare("1.2.0", "1.2"));
            Assert.Greater(AppVersion.Compare("1.10.0", "1.2.0"), 0);
            Assert.Greater(AppVersion.Compare("2.0", "1.99.99"), 0);
            Assert.Greater(AppVersion.Compare("v1.2.1-beta", "1.2.0"), 0);
            Assert.Less(AppVersion.Compare("garbage", "0.0.1"), 0);
        }

        [Test]
        public void BumpsReleaseVersions()
        {
            Assert.AreEqual("0.1.1", AppVersion.Bump("0.1.0", "patch"));
            Assert.AreEqual("0.2.0", AppVersion.Bump("0.1.7", "minor"));
            Assert.AreEqual("1.0.0", AppVersion.Bump("0.9.3", "major"));
            Assert.AreEqual("0.9.3", AppVersion.Bump("0.9.3", "none"));
            Assert.AreEqual("1.0.1", AppVersion.Bump("1.0", "PATCH"));
            Assert.Throws<ArgumentException>(() => AppVersion.Bump("1.0.0", "sideways"));
            Assert.Throws<ArgumentException>(() => AppVersion.Bump("latest", "patch"));
        }

        static PendingRace Race(string id) => new PendingRace { id = id, finishedAt = "2026-10-12T08:00:00Z", report = new RaceReport { songId = "demo" } };

        [Test]
        public void OfflineQueueIgnoresDuplicatesAndKeepsTheNewest()
        {
            var q = new PendingRaceQueue();
            q.Add(Race("a"));
            q.Add(Race("a"));
            q.Add(null);
            q.Add(new PendingRace());
            Assert.AreEqual(1, q.Count);

            for (int i = 0; i < PendingRaceQueue.MaxEntries + 5; i++) q.Add(Race("r" + i));
            Assert.AreEqual(PendingRaceQueue.MaxEntries, q.Count);
            Assert.IsFalse(q.Contains("a"), "oldest entries make room");
            Assert.IsTrue(q.Contains("r" + (PendingRaceQueue.MaxEntries + 4)));
        }

        [Test]
        public void OfflineQueueSettlesOnlyAnsweredRaces()
        {
            var q = new PendingRaceQueue();
            foreach (var id in new[] { "a", "b", "c", "d" }) q.Add(Race(id));
            var batch = q.Batch(3);
            Assert.AreEqual(new[] { "a", "b", "c" }, batch.ConvertAll(r => r.id).ToArray());
            batch.RemoveAt(0);
            Assert.AreEqual(4, q.Count, "Batch returns a copy");

            Assert.AreEqual(2, q.Settle(new[] { "a", "c", "zzz" }));
            Assert.AreEqual(new[] { "b", "d" }, q.races.ConvertAll(r => r.id).ToArray());
            Assert.AreEqual(0, q.Batch(-1).Count);
        }
    }
}
