using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using VMS.Core;

namespace VMS.Tests
{
    /// <summary>Language packs and the world data: every language complete, placeholders intact, realms consistent.</summary>
    [TestFixture]
    public class LocalizationTests
    {
        static readonly string[] Languages = { "vi", "en", "zh", "ja", "ko", "es", "de", "fi" };

        static string LocDir => Path.Combine(TestData.DataDir, "..", "Localization");

        static Dictionary<string, string> Parse(string language)
        {
            var table = new Dictionary<string, string>();
            foreach (var raw in File.ReadAllLines(Path.Combine(LocDir, language + ".txt")))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                table[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return table;
        }

        static string Placeholders(string s) => string.Join(" ", Regex.Matches(s, @"\{\d+\}").Cast<Match>().Select(m => m.Value).OrderBy(x => x));

        [Test]
        public void EveryLanguageHasEveryKeyWithTheSamePlaceholders()
        {
            var en = Parse("en");
            Assert.That(en.Count, Is.GreaterThan(400));
            foreach (var language in Languages)
            {
                var table = Parse(language);
                var missing = en.Keys.Where(k => !table.ContainsKey(k)).ToList();
                var extra = table.Keys.Where(k => !en.ContainsKey(k)).ToList();
                Assert.That(missing, Is.Empty, language + " is missing keys");
                Assert.That(extra, Is.Empty, language + " has unknown keys");
                foreach (var kv in en)
                    Assert.That(Placeholders(table[kv.Key]), Is.EqualTo(Placeholders(kv.Value)), language + ": " + kv.Key);
            }
        }

        [Test]
        public void LocalizedTextFallsBackToEnglishThenVietnamese()
        {
            var text = new LocalizedText("Xin chào", "Hello") { ja = "こんにちは" };
            Assert.That(text.Get("ja"), Is.EqualTo("こんにちは"));
            Assert.That(text.Get("de"), Is.EqualTo("Hello"), "missing translation falls back to English");
            Assert.That(text.Get("vi"), Is.EqualTo("Xin chào"));
            Assert.That(new LocalizedText("Chỉ tiếng Việt", null).Get("fi"), Is.EqualTo("Chỉ tiếng Việt"));
            Assert.That(text.Get("xx"), Is.EqualTo("Hello"));
        }

        [Test]
        public void GameDataIsTranslatedIntoEveryLanguage()
        {
            var navigators = TestData.Navigators();
            var world = TestData.Load<WorldCatalog>(Path.Combine(TestData.DataDir, "world.json"));
            var texts = new List<KeyValuePair<string, LocalizedText>>();
            foreach (var n in navigators.navigators)
            {
                texts.Add(Pair(n.id + ".name", n.name));
                texts.Add(Pair(n.id + ".bio", n.bio));
                texts.Add(Pair(n.id + ".skill", n.skill.desc));
                foreach (var v in n.voice) texts.Add(Pair(n.id + ".voice." + v.id, v.text));
            }
            foreach (var r in world.realms)
            {
                texts.Add(Pair(r.id + ".name", r.name));
                texts.Add(Pair(r.id + ".desc", r.desc));
                foreach (var l in r.lore) texts.Add(Pair(r.id + "." + l.id, l.text));
            }
            foreach (var item in TestData.Shop().items) texts.Add(Pair(item.id, item.name));
            foreach (var kv in texts)
                foreach (var language in Languages)
                    Assert.That(kv.Value.Raw(language), Is.Not.Null.And.Not.Empty, kv.Key + " has no " + language);
        }

        static KeyValuePair<string, LocalizedText> Pair(string key, LocalizedText text) => new KeyValuePair<string, LocalizedText>(key, text);

        [Test]
        public void NavigatorsComeFromRealmsOfTheWorldMap()
        {
            var world = TestData.Load<WorldCatalog>(Path.Combine(TestData.DataDir, "world.json"));
            Assert.That(world.realms.Select(r => r.id), Is.EquivalentTo(new[] { "vanlac", "yunque", "hanayume", "dalmaru", "nebelreich", "lumikaari" }));
            foreach (var r in world.realms) Assert.That(r.lore.Count, Is.GreaterThanOrEqualTo(4), r.id + " codex");
            foreach (var n in TestData.Navigators().navigators)
            {
                string realm = string.IsNullOrEmpty(n.realm) ? "vanlac" : n.realm;
                Assert.That(world.Find(realm), Is.Not.Null, n.id + " comes from unknown realm " + realm);
            }
            // Each neighbouring realm sends at least one navigator.
            foreach (var r in world.realms)
                Assert.That(TestData.Navigators().navigators.Any(n => (string.IsNullOrEmpty(n.realm) ? "vanlac" : n.realm) == r.id), r.id + " has no navigator");
        }
    }
}
