using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using VMS.Core;
using VMS.Core.Music;
using VMS.Race;
using VMS.Songs;
using VMS.UI;

namespace VMS
{
    /// <summary>
    /// Development builds only: launched with "-vmsAutoShots &lt;folder&gt;", walks through every screen and a few
    /// races (offline backend) and saves screenshots, then quits. Used for visual regression checks on CI/PC.
    /// </summary>
    public sealed class DevAutomation : MonoBehaviour
    {
        /// <summary>"-vmsAutoplay": races steer themselves (integration check that charts, timing and judging agree).</summary>
        public static bool Autoplay { get; private set; }

        string _folder;
        int _index;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (!Debug.isDebugBuild && !Application.isEditor) return;
            var args = Environment.GetCommandLineArgs();
            Autoplay = Array.IndexOf(args, "-vmsAutoplay") >= 0;
            int i = Array.IndexOf(args, "-vmsAutoShots");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("DevAutomation");
            DontDestroyOnLoad(go);
            var automation = go.AddComponent<DevAutomation>();
            automation._folder = args[i + 1];
            Directory.CreateDirectory(automation._folder);
            AudioListener.volume = 0f; // unattended runs stay silent
            Application.runInBackground = true; // ...and keep going when another window takes focus
        }

        /// <summary>True when <paramref name="name"/> is on the command line; development builds only.</summary>
        public static bool Flag(string name) =>
            (Debug.isDebugBuild || Application.isEditor) && Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        /// <summary>Value after <paramref name="name"/> on the command line; development builds only.</summary>
        public static string Arg(string name)
        {
            if (!Debug.isDebugBuild || Application.isEditor) return null;
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        IEnumerator Start()
        {
            // "-vmsScenario" runs one online scenario against "-vmsServer"; the default walks every screen offline.
            switch (Arg("-vmsScenario"))
            {
                case "offline-race": yield return OfflineRaceScenario(); break;
                case "resume-sync-delete": yield return ResumeSyncDeleteScenario(); break;
                case "outdated": yield return OutdatedScenario(); break;
                case "liveops": yield return LiveOpsScenario(); break;
                case "languages": yield return LanguagesScenario(); break;
                default: yield return ScreenTour(); break;
            }
            Debug.Log("[DevAutomation] done: " + _index + " screenshots in " + _folder);
            Application.Quit();
        }

        static IEnumerator WaitForMenus() =>
            WaitFor(() => GameApp.Instance != null && GameApp.Instance.Ui != null &&
                          (GameApp.Instance.Ui.Current is LoginScreen || GameApp.Instance.Ui.Current is HomeScreen), 120f);

        void Log(string message) => Debug.Log("[DevAutomation] " + message);

        string Status(GameApp app) =>
            "connected=" + app.Backend.IsConnected + " pending=" + app.Backend.PendingRaceCount +
            " coins=" + (app.Profile != null ? app.Profile.coins : -1) + " jade=" + (app.Profile != null ? app.Profile.jade : -1);

        /// <summary>Server up: race online, then (server taken down by the test driver) race again offline.</summary>
        IEnumerator OfflineRaceScenario()
        {
            yield return WaitForMenus();
            var app = GameApp.Instance;
            if (app.Ui.Current is LoginScreen)
            {
                yield return Shot("login_online");
                yield return Await(app.Auth.SignInGuestAsync());
            }
            yield return WaitFor(() => app.Ui.Current is HomeScreen, 30f);
            yield return new WaitForSecondsRealtime(2f);
            ClosePopups();
            Log("signed in: " + Status(app));
            yield return RaceToResults(app, "results_online");
            Log("after online race: " + Status(app));

            yield return Handshake("need_offline", "offline_ok");
            yield return RaceToResults(app, "results_offline");
            Log("after offline race: " + Status(app));
            app.Ui.Reset<HomeScreen>();
            yield return Settle("home_offline");
        }

        /// <summary>Server still down at launch: resume from the cache, then sync when it returns, then delete the account.</summary>
        IEnumerator ResumeSyncDeleteScenario()
        {
            yield return WaitForMenus();
            var app = GameApp.Instance;
            yield return new WaitForSecondsRealtime(2f);
            ClosePopups();
            Log("launched offline on " + app.Ui.Current.GetType().Name + ": " + Status(app));
            yield return Shot("home_resumed_offline");
            app.Ui.Show<GachaScreen>();
            yield return Settle("gacha_offline");
            ClosePopups();
            app.Ui.Show<SettingsScreen>();
            yield return new WaitForSecondsRealtime(1f);
            ScrollCurrent(0.12f);
            yield return Settle("settings_offline_account");

            yield return Handshake("need_online", "online_ok");
            app.TrySync();
            yield return WaitFor(() => app.Backend.IsConnected && app.Backend.PendingRaceCount == 0, 30f);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("synced");
            Log("after sync: " + Status(app));

            var settings = app.Ui.Show<SettingsScreen>();
            yield return new WaitForSecondsRealtime(4f); // let the sync toast fade
            settings.ShowDeleteAccount();
            yield return Settle("delete_account_popup");
            ClosePopups();
            var delete = app.Auth.DeleteAccountAsync(null);
            yield return Await(delete);
            yield return WaitFor(() => app.Ui.Current is LoginScreen, 10f);
            yield return Settle("login_after_delete");
            Log("account deleted: " + (delete.Status == TaskStatus.RanToCompletion) + ", signed in " + app.Auth.IsSignedIn);
        }

        /// <summary>Server requires a newer version: signing in must end at the blocking update dialog.</summary>
        IEnumerator OutdatedScenario()
        {
            yield return WaitForMenus();
            var app = GameApp.Instance;
            yield return new WaitForSecondsRealtime(1f);
            yield return Await(app.Auth.SignInGuestAsync());
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("update_required");
            Log("update prompt shown: " + FindPopups().Length + " popup(s), signed in " + app.Auth.IsSignedIn);
        }

        /// <summary>Online shop flow against a server with PAYMENTS_FAKE=1 (dev store), then the password-reset dialog.</summary>
        IEnumerator LiveOpsScenario()
        {
            yield return WaitForMenus();
            var app = GameApp.Instance;
            if (app.Ui.Current is LoginScreen)
            {
                var login = (LoginScreen)app.Ui.Current;
                login.ShowReset("player@example.com");
                yield return Settle("reset_password");
                ClosePopups();
                yield return Await(app.Auth.SignInGuestAsync());
            }
            yield return WaitFor(() => app.Ui.Current is HomeScreen, 30f);
            yield return new WaitForSecondsRealtime(2f);
            ClosePopups();
            yield return CosmeticsWalk(app);
        }

        /// <summary>Buys gold through the dev store, buys and equips a full look, and photographs every new screen.</summary>
        IEnumerator CosmeticsWalk(GameApp app)
        {
            yield return WaitFor(() => app.Purchases.Available, 20f);
            Log("store: " + (app.Purchases.Store != null ? app.Purchases.Store.Name : "none") + ", catalog items " + app.Shop.items.Count + ", events " + app.Events.Count);
            app.Ui.Show<ShopScreen>(ShopScreen.FeaturedTab);
            yield return Settle("shop_featured");
            app.Ui.Show<ShopScreen>(ShopScreen.GoldTab);
            yield return Settle("shop_gold");

            var gold = Safe(() => app.Purchases.BuyAsync(app.Shop.Product("gold_6480")));
            yield return Await(gold);
            if (gold.Status == TaskStatus.RanToCompletion)
            {
                app.SetProfile(gold.Result.profile);
                Log("bought gold: +" + gold.Result.grant.gold + " +" + gold.Result.grant.bonus + " bonus, total " + app.Profile.TotalGold);
            }
            var pass = Safe(() => app.Purchases.BuyAsync(app.Shop.Product("lantern_pass")));
            yield return Await(pass);
            if (pass.Status == TaskStatus.RanToCompletion) app.SetProfile(pass.Result.profile);

            var look = new[] { "frame_phoenix", "plate_dong_son", "bubble_lantern", "exhaust_phoenix", "garage_hoi_an", "emo_heart", "skin_vang_anh_ao_dai", "kit_rong_gold" };
            foreach (var id in look)
            {
                var buy = Safe(() => app.Backend.BuyItemAsync(id));
                yield return Await(buy);
                if (buy.Status != TaskStatus.RanToCompletion) continue;
                app.SetProfile(buy.Result.profile);
                var item = app.Shop.Item(id);
                if (!VMS.Core.ShopCategories.IsEquippable(item.category)) continue;
                var equip = Safe(() => app.Backend.EquipAsync(item.category, item.target, id));
                yield return Await(equip);
                if (equip.Status == TaskStatus.RanToCompletion) app.SetProfile(equip.Result);
            }
            Log("after shopping: gold " + app.Profile.TotalGold + ", owned " + app.Profile.cosmetics.owned.Count + ", frame " + app.Profile.cosmetics.avatarFrame + ", passes " + app.Profile.passes.Count);

            app.Ui.Show<ShopScreen>(VMS.Core.ShopCategories.AvatarFrame);
            yield return Settle("shop_frames");
            app.Ui.Show<ShopScreen>(VMS.Core.ShopCategories.VesselKit);
            yield return Settle("shop_kits");
            app.Ui.Show<ShopScreen>(ShopScreen.ItemArg("kit_rong_gold"));
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("shop_kit_detail");
            ClosePopups();
            app.Ui.Show<ShopScreen>(ShopScreen.ItemArg("skin_lan_nhi_lion"));
            yield return Settle("shop_skin_detail");
            ClosePopups();
            app.Ui.Show<ShopScreen>(ShopScreen.ItemArg("emo_pout"));
            yield return Settle("shop_emotion_detail");
            ClosePopups();
            var garage = app.Ui.Show<GarageScreen>();
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("garage");
            yield return new WaitForEndOfFrame();
            garage.Preview.SaveTo(Path.Combine(_folder, "garage_preview_raw.png"));

            app.Ui.Show<MailScreen>();
            yield return new WaitForSecondsRealtime(2f);
            yield return Shot("mail");
            var claim = Safe(() => app.Backend.ClaimMailAsync(0, true));
            yield return Await(claim);
            if (claim.Status == TaskStatus.RanToCompletion)
            {
                app.SetProfile(claim.Result.profile);
                Log("mail claimed: " + claim.Result.claimed.Length + " mail(s), owned frame_welcome " + app.Profile.cosmetics.owned.Contains("frame_welcome"));
            }
            app.RefreshLiveData();
            yield return new WaitForSecondsRealtime(1.5f);
            app.Ui.Show<EventsScreen>();
            yield return Settle("events");
            var eventClaim = Safe(() => app.Backend.ClaimEventAsync("grand_opening", 0));
            yield return Await(eventClaim);
            if (eventClaim.Status == TaskStatus.RanToCompletion)
            {
                app.SetProfile(eventClaim.Result.profile);
                Log("event reward claimed");
            }

            var home = app.Ui.Reset<HomeScreen>();
            yield return new WaitForSecondsRealtime(1.5f);
            ClosePopups();
            yield return Shot("home_cosmetics");
            home.Emote(app.Shop.Item("emo_heart"));
            yield return Settle("home_emotion");
            home.ShowEmotions();
            yield return Settle("emotions_popup");
            ClosePopups();
            ProfileCard.Show();
            yield return Settle("profile_card");
            ClosePopups();
        }

        IEnumerator RaceToResults(GameApp app, string shot)
        {
            var demo = app.Songs.Find(SongLibrary.DemoId);
            app.Ui.Show<SongSelectScreen>();
            app.StartRace(new RaceSetup
            {
                song = demo,
                mode = GameMode.Classic,
                difficulty = Difficulty.Normal,
                navigatorId = app.SelectedNavigator.id,
                vesselId = VesselDef.All[0].id,
                themeId = "huong",
            });
            yield return WaitFor(() => app.InRace, 60f);
            yield return WaitFor(() => app.Ui.Current is ResultsScreen, 240f);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot(shot);
        }

        /// <summary>Asks the external test driver to do something (e.g. stop the server) and waits for its answer.</summary>
        IEnumerator Handshake(string request, string reply)
        {
            string replyPath = Path.Combine(_folder, reply);
            File.WriteAllText(Path.Combine(_folder, request), DateTime.UtcNow.ToString("o"));
            Log("waiting for " + reply);
            yield return WaitFor(() => File.Exists(replyPath), 180f);
            if (File.Exists(replyPath)) File.Delete(replyPath);
        }

        IEnumerator ScreenTour()
        {
            yield return WaitForMenus();
            var app = GameApp.Instance;
            yield return new WaitForSecondsRealtime(1f);
            if (app.Ui.Current is LoginScreen)
            {
                yield return Shot("login");
                yield return Await(app.Auth.SignInGuestAsync());
            }
            yield return WaitFor(() => app.Ui.Current is HomeScreen, 30f);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-vmsFresh") >= 0)
            {
                ClosePopups();
                yield return Await(app.Auth.DeleteAccountAsync(null));
                yield return WaitFor(() => app.Ui.Current is LoginScreen, 10f);
                yield return Await(app.Auth.SignInGuestAsync());
                yield return WaitFor(() => app.Ui.Current is HomeScreen, 30f);
                Log("fresh guest account: " + Status(app));
            }
            yield return new WaitForSecondsRealtime(2f);
            yield return Shot("home");
            ClosePopups();
            yield return CosmeticsWalk(app);

            app.Ui.Show<SongSelectScreen>();
            yield return Settle("songs");
            app.Ui.Show<NavigatorsScreen>();
            yield return Settle("navigators");
            app.Ui.Show<NavigatorDetailScreen>(app.Data.Navigator("thanh_van"));
            yield return Settle("navigator_detail");
            app.Ui.Show<NavigatorDetailScreen>(app.Data.Navigator("vang_anh"));
            yield return new WaitForSecondsRealtime(0.5f);
            ScrollCurrent(0.35f);
            yield return Settle("navigator_outfits");
            app.Ui.Show<WorldScreen>("vanlac");
            yield return Settle("world_vanlac");
            app.Ui.Show<WorldScreen>("lumikaari");
            yield return Settle("world_lumikaari");
            ScrollCurrent(0.2f);
            yield return Settle("world_codex");
            app.Ui.Show<SongSelectScreen>();
            yield return new WaitForSecondsRealtime(0.5f);
            RoadPicker.Show(app.Settings.Theme, _ => { });
            yield return Settle("road_picker");
            ClosePopups();
            app.Ui.Show<GachaScreen>();
            yield return new WaitForSecondsRealtime(1f);
            yield return Settle("gacha");

            // Earlier tours may have spent the jade: top up from gold so the draw always runs.
            int cost = app.Data.Gacha.costPerPull * 10;
            if (app.Profile.jade < cost && app.Profile.TotalGold >= cost - app.Profile.jade)
            {
                var exchange = Safe(() => app.Backend.ExchangeGoldAsync(cost - app.Profile.jade));
                yield return Await(exchange);
                if (exchange.Status == TaskStatus.RanToCompletion) app.SetProfile(exchange.Result.profile);
            }
            var pull = Safe(() => app.Backend.PullAsync("standard", 10));
            yield return Await(pull);
            if (pull.Status == TaskStatus.RanToCompletion)
            {
                app.SetProfile(pull.Result.profile);
                app.Ui.Show<GachaRevealScreen>(new GachaRevealScreen.Args { results = pull.Result.results, bannerId = "standard", count = 10 });
                yield return new WaitForSecondsRealtime(1.0f);
                yield return Shot("gacha_shake");
                yield return new WaitForSecondsRealtime(6f);
                yield return Shot("gacha_reveal");
            }
            var settingsScreen = app.Ui.Show<SettingsScreen>();
            yield return Settle("settings");
            ScrollCurrent(0.62f);
            yield return Settle("settings_graphics");
            ScrollCurrent(0.45f);
            yield return Settle("settings_language");
            settingsScreen.ShowDeleteAccount();
            yield return Settle("delete_account");
            ClosePopups();

            var demo = app.Songs.Find(SongLibrary.DemoId);
            var themes = new[] { "halong", "huong", "hoang", "yunque", "hanayume", "dalmaru", "nebelreich", "lumikaari" };
            var modes = new[] { GameMode.Skill, GameMode.Classic, GameMode.Dodge, GameMode.Classic, GameMode.Skill, GameMode.Classic, GameMode.Dodge, GameMode.Classic };
            for (int t = 0; t < themes.Length && demo != null; t++)
            {
                app.Ui.Show<SongSelectScreen>();
                app.StartRace(new RaceSetup
                {
                    song = demo,
                    mode = modes[t % modes.Length],
                    difficulty = Difficulty.Normal,
                    navigatorId = app.SelectedNavigator.id,
                    vesselId = VesselDef.All[t % VesselDef.All.Length].id,
                    themeId = themes[t],
                });
                yield return WaitFor(() => app.InRace, 60f);
                yield return new WaitForSecondsRealtime(4.5f);
                yield return Shot("race_" + themes[t] + "_start");
                yield return new WaitForSecondsRealtime(t == 0 ? 75f : t < 3 ? 12f : 9f);
                yield return Shot("race_" + themes[t] + "_mid");
                if (t == 0)
                {
                    yield return WaitFor(() => app.Ui.Current is ResultsScreen, 60f);
                    yield return new WaitForSecondsRealtime(1.5f);
                    yield return Shot("results");
                }
                else
                {
                    app.AbortRace();
                    yield return new WaitForSecondsRealtime(1f);
                }
            }
        }

        /// <summary>Switches through every language at runtime and photographs the screens with the most text.</summary>
        IEnumerator LanguagesScenario()
        {
            yield return WaitForMenus();
            var app = GameApp.Instance;
            if (app.Ui.Current is LoginScreen) yield return Await(app.Auth.SignInGuestAsync());
            yield return WaitFor(() => app.Ui.Current is HomeScreen, 30f);
            yield return new WaitForSecondsRealtime(2f);
            ClosePopups();
            var nav = app.Data.Navigator("aava") ?? app.Data.Navigator("thanh_van");
            ShopItem sample = app.Shop.Item("garage_sauna") ?? app.Shop.Item("frame_lotus");
            foreach (string lang in Loc.Languages)
            {
                app.Settings.Language = lang;
                app.ApplySettings();
                app.Ui.Reset<HomeScreen>();
                yield return new WaitForSecondsRealtime(1.5f);
                ClosePopups();
                yield return Shot(lang + "_home");
                app.Ui.Show<NavigatorDetailScreen>(nav);
                yield return Settle(lang + "_navigator");
                app.Ui.Show<WorldScreen>("hanayume");
                yield return Settle(lang + "_world");
                app.Ui.Show<ShopScreen>(sample != null ? sample.category : ShopScreen.FeaturedTab);
                yield return Settle(lang + "_shop");
                app.Ui.Show<EventsScreen>();
                yield return Settle(lang + "_events");
                app.Ui.Show<SettingsScreen>();
                yield return Settle(lang + "_settings");
                app.Ui.Show<SongSelectScreen>();
                yield return Settle(lang + "_songs");
            }
            app.Settings.Language = Loc.Vietnamese;
            app.ApplySettings();
        }

        IEnumerator Settle(string name)
        {
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot(name);
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(_folder, (++_index).ToString("00") + "_" + name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[DevAutomation] " + path);
            yield return null;
        }

        /// <summary>Scrolls the current screen's list (1 = top, 0 = bottom).</summary>
        static void ScrollCurrent(float position)
        {
            var scroll = GameApp.Instance.Ui.Current.GetComponentInChildren<UnityEngine.UI.ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = position;
        }

        static void ClosePopups()
        {
            foreach (var p in FindPopups()) p.Close();
        }

        static Popup[] FindPopups() => UiRoot.Instance.PopupLayer.GetComponentsInChildren<Popup>();

        static Task<T> Safe<T>(Func<Task<T>> call)
        {
            try
            {
                return call();
            }
            catch (Exception e)
            {
                return Task.FromException<T>(e);
            }
        }

        static IEnumerator Await(Task task)
        {
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) Debug.LogException(task.Exception);
        }

        static IEnumerator WaitFor(Func<bool> condition, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
