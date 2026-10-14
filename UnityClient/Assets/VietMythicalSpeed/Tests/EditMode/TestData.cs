using System.IO;
using VMS.Core;

namespace VMS.Tests
{
    /// <summary>Locates and loads repository files from tests (Unity editor or the standalone .NET runner).</summary>
    public static class TestData
    {
        static string _root;

        /// <summary>Repository root: the first ancestor of the working directory that contains "shared/".</summary>
        public static string RepoRoot
        {
            get
            {
                if (_root != null) return _root;
                var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
                while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "shared")))
                    dir = dir.Parent;
                _root = dir != null ? dir.FullName : Directory.GetCurrentDirectory();
                return _root;
            }
        }

        public static string DataDir =>
            Path.Combine(RepoRoot, "UnityClient", "Assets", "VietMythicalSpeed", "Resources", "Data");

        public static string GoldenPath => Path.Combine(RepoRoot, "shared", "gacha-golden.json");

        public static T Load<T>(string path)
        {
            string text = File.ReadAllText(path);
#if UNITY_5_3_OR_NEWER
            return UnityEngine.JsonUtility.FromJson<T>(text);
#else
            return System.Text.Json.JsonSerializer.Deserialize<T>(text,
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
#endif
        }

        public static void Save<T>(string path, T value)
        {
#if UNITY_5_3_OR_NEWER
            File.WriteAllText(path, UnityEngine.JsonUtility.ToJson(value, true) + "\n");
#else
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(value,
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true, WriteIndented = true, IgnoreReadOnlyProperties = true }) + "\n");
#endif
        }

        public static NavigatorCatalog Navigators() => Load<NavigatorCatalog>(Path.Combine(DataDir, "navigators.json"));
        public static BannerCatalog Banners() => Load<BannerCatalog>(Path.Combine(DataDir, "banners.json"));
        public static GachaConfig Gacha() => Load<GachaConfig>(Path.Combine(DataDir, "gacha.json"));
        public static EconomyConfig Economy() => Load<EconomyConfig>(Path.Combine(DataDir, "economy.json"));
        public static ShopCatalog Shop() => Load<ShopCatalog>(Path.Combine(DataDir, "shop.json"));
        public static EventCatalog Events() => Load<EventCatalog>(Path.Combine(DataDir, "events.json"));
    }

    /// <summary>Shape of shared/gacha-golden.json (also read by server/test/gacha.test.ts).</summary>
    [System.Serializable]
    public class GoldenFile
    {
        public string description;
        public uint[] xoshiro128Seed12345;
        public GachaConfig config;
        public NavigatorDef[] navigators;
        public BannerDef[] banners;
        public GoldenCase[] cases;
    }

    /// <summary>
    /// Builds the cross-language golden vectors from the C# engine (the reference implementation).
    /// Aggressive pity settings make short sequences exercise soft/hard pity, 50/50 losses and guarantees.
    /// </summary>
    public static class GoldenGenerator
    {
        public static GoldenFile Build(NavigatorCatalog catalog)
        {
            var config = new GachaConfig
            {
                rates = new RarityRates { thien = 0.06, dia = 0.2 },
                softPityStart = 12,
                softPityStep = 0.12,
                hardPity = 20,
                tenPullMinRarity = 2,
                featuredShare = 0.5,
                guaranteeFeaturedAfterLoss = true,
                featuredDiaShare = 0.5,
                sparkCost = 30,
                costPerPull = 150,
            };
            var navigators = new NavigatorDef[catalog.navigators.Length];
            for (int i = 0; i < navigators.Length; i++)
            {
                var n = catalog.navigators[i];
                navigators[i] = new NavigatorDef { id = n.id, rarity = n.rarity, limited = n.limited };
            }
            var banners = new[]
            {
                new BannerDef { id = "standard", type = "standard", featured = new string[0], pityGroup = "standard", startUtc = "", endUtc = "" },
                new BannerDef { id = "limited_son_lam", type = "limited", featured = new[] { "son_lam", "lan_nhi", "nghe_bao" }, pityGroup = "limited", startUtc = "", endUtc = "" },
                new BannerDef { id = "limited_thuy_ha", type = "limited", featured = new[] { "thuy_ha", "bach_hac", "ly_ngu" }, pityGroup = "limited", startUtc = "", endUtc = "" },
            };
            var engine = new GachaEngine(config, new NavigatorCatalog { navigators = navigators });
            var bannerCatalog = new BannerCatalog { banners = banners };

            var specs = new[]
            {
                new GoldenCase { bannerId = "standard", seed = 1u, pulls = Repeat(1, 20, 10, 3) },
                new GoldenCase { bannerId = "limited_son_lam", seed = 42u, pulls = Repeat(10, 8, 1, 15) },
                new GoldenCase { bannerId = "limited_thuy_ha", seed = 2026u, pulls = Repeat(1, 60, 0, 0) },
                new GoldenCase { bannerId = "standard", seed = 777u, pulls = Repeat(10, 5, 0, 0) },
                new GoldenCase { bannerId = "limited_son_lam", seed = 4294967295u, pulls = Repeat(10, 6, 0, 0) },
            };
            foreach (var c in specs)
            {
                var state = new GachaState();
                var rng = new Xoshiro128(c.seed);
                var results = new System.Collections.Generic.List<GoldenResult>();
                foreach (int count in c.pulls)
                    foreach (var r in engine.Pull(bannerCatalog.Find(c.bannerId), state, count, rng))
                        results.Add(new GoldenResult { navigatorId = r.navigatorId, rarity = r.rarity, featured = r.featured });
                c.expected = results.ToArray();
                c.finalState = state;
            }

            var reference = new Xoshiro128(12345u);
            return new GoldenFile
            {
                description = "Cross-language gacha contract. Generated from the C# engine (VMS.Core.GachaEngine); verified by Unity EditMode tests and server/test/gacha.test.ts.",
                xoshiro128Seed12345 = new[] { reference.NextUInt(), reference.NextUInt(), reference.NextUInt(), reference.NextUInt() },
                config = config,
                navigators = navigators,
                banners = banners,
                cases = specs,
            };
        }

        static int[] Repeat(int countA, int timesA, int countB, int timesB)
        {
            var list = new int[timesA + timesB];
            for (int i = 0; i < timesA; i++) list[i] = countA;
            for (int i = 0; i < timesB; i++) list[timesA + i] = countB;
            return list;
        }
    }

    [System.Serializable]
    public class GoldenCase
    {
        public string bannerId;
        public uint seed;
        public int[] pulls;
        public GoldenResult[] expected;
        public GachaState finalState;
    }

    [System.Serializable]
    public class GoldenResult
    {
        public string navigatorId;
        public int rarity;
        public bool featured;
    }
}
