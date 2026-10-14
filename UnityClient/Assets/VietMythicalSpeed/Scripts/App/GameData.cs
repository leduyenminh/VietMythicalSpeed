using System;
using System.Collections.Generic;
using UnityEngine;
using VMS.Core;

namespace VMS
{
    /// <summary>Static game data loaded from Resources/Data (the server reads the same JSON files).</summary>
    public sealed class GameData
    {
        public NavigatorCatalog Navigators { get; private set; }
        public BannerCatalog Banners { get; private set; }
        public GachaConfig Gacha { get; private set; }
        public EconomyConfig Economy { get; private set; }
        public ShopCatalog Shop { get; private set; }
        public EventCatalog Events { get; private set; }
        public MailTemplates Mail { get; private set; }
        public WorldCatalog World { get; private set; }
        public BackendConfig Backend { get; private set; }

        public static GameData Load()
        {
            var data = new GameData
            {
                Navigators = LoadJson<NavigatorCatalog>("Data/navigators"),
                Banners = LoadJson<BannerCatalog>("Data/banners"),
                Gacha = LoadJson<GachaConfig>("Data/gacha"),
                Economy = LoadJson<EconomyConfig>("Data/economy"),
                Shop = LoadJson<ShopCatalog>("Data/shop") ?? new ShopCatalog(),
                Events = LoadJson<EventCatalog>("Data/events") ?? new EventCatalog(),
                Mail = LoadJson<MailTemplates>("Data/mail") ?? new MailTemplates(),
                World = LoadJson<WorldCatalog>("Data/world") ?? new WorldCatalog(),
                Backend = LoadJson<BackendConfig>("Config/backend") ?? new BackendConfig(),
            };
            var problems = data.Navigators.Validate();
            problems.AddRange(data.Shop.Validate());
            foreach (var p in problems) Debug.LogError("[GameData] " + p);
            return data;
        }

        static T LoadJson<T>(string resourcePath) where T : class
        {
            var asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null)
            {
                Debug.LogError("[GameData] Missing Resources/" + resourcePath + ".json");
                return null;
            }
            try
            {
                return JsonUtility.FromJson<T>(asset.text);
            }
            catch (Exception e)
            {
                Debug.LogError("[GameData] Could not parse " + resourcePath + ": " + e.Message);
                return null;
            }
        }

        public NavigatorDef Navigator(string id) => Navigators.Find(id);

        public IEnumerable<NavigatorDef> AllNavigators() => Navigators.navigators;

        /// <summary>Realm id of a navigator ("vanlac" for the home realm).</summary>
        public static string RealmOf(NavigatorDef def) => def == null || string.IsNullOrEmpty(def.realm) ? "vanlac" : def.realm;
    }

    /// <summary>Resources/Config/backend.json.</summary>
    [Serializable]
    public class BackendConfig
    {
        /// <summary>"offline" keeps everything on the device; "online" talks to the server at baseUrl.</summary>
        public string mode = "offline";
        public string baseUrl = "http://10.0.2.2:8787";
        /// <summary>Custom URL scheme registered in Plugins/Android/AndroidManifest.xml for OAuth-style returns.</summary>
        public string deepLinkScheme = "vietmythicalspeed";
        /// <summary>Offline mode only: server that keeps cloud backups of the offline save ("" hides cloud save).</summary>
        public string cloudSaveUrl = "";
    }
}
