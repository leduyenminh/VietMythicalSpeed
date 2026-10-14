using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using VMS.Auth;
using VMS.Backend;
using VMS.Core;
using VMS.Core.Music;
using VMS.Race;
using System.Collections.Generic;
using VMS.Payments;
using VMS.Songs;
using VMS.UI;

namespace VMS
{
    /// <summary>Creates the game in the Main scene; the whole game is built from code, so the scene stays empty.</summary>
    static class Bootstrap
    {
        public const string MainScene = "Main";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (GameApp.Instance != null) return;
            if (SceneManager.GetActiveScene().name != MainScene) return;
            new GameObject("GameApp").AddComponent<GameApp>();
        }
    }

    /// <summary>Application root: services, profile, navigation between menus and races.</summary>
    public sealed class GameApp : MonoBehaviour
    {
        public static GameApp Instance { get; private set; }

        public GameSettings Settings { get; private set; }
        public GameData Data { get; private set; }
        public IBackend Backend { get; private set; }
        public AuthService Auth { get; private set; }
        public SongLibrary Songs { get; private set; }
        public AudioService Audio { get; private set; }
        public UiRoot Ui { get; private set; }
        public PlayerProfile Profile { get; private set; }
        public PurchaseService Purchases { get; private set; }
        /// <summary>Cloud backups of offline-mode progress (null when online, or no cloud server is configured).</summary>
        public CloudSave Cloud { get; private set; }

        /// <summary>Live shop catalog: the server's copy once fetched (new items without an update), else the game's data.</summary>
        public ShopCatalog Shop { get; private set; }
        public StoresInfo Stores { get; private set; } = new StoresInfo();
        /// <summary>Events visible now (server copy online, game data offline).</summary>
        public List<EventDef> Events { get; private set; } = new List<EventDef>();
        public int MailUnread { get; private set; }
        public int MailUnclaimed { get; private set; }
        /// <summary>Shop, events or mail counts changed (badges on the lobby).</summary>
        public event Action LiveDataChanged;

        public event Action ProfileChanged;

        Camera _menuCamera;
        RaceDirector _race;
        RaceSetup _lastSetup;
        AudioClip _menuClip;
        bool _starting;
        bool _syncing;

        /// <summary>How often an online game without a connection (or with races to upload) retries the server.</summary>
        const float SyncRetrySeconds = 20f;

        public bool InRace => _race != null;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            ConfigureAudio();

            Settings = GameSettings.Load();
            // Development: screenshot runs in another language or graphics mode (-vmsLang ja, -vmsGraphics UltraLow, -vmsFlat).
            string devLanguage = DevAutomation.Arg("-vmsLang");
            if (devLanguage != null) Settings.Language = devLanguage;
            string devTier = DevAutomation.Arg("-vmsGraphics");
            if (devTier != null && Enum.TryParse(devTier, true, out GraphicsTier tier)) Settings.Graphics = tier;
            if (DevAutomation.Flag("-vmsFlat")) Settings.FlatUi = true;
            Loc.SetLanguage(Settings.Language);
            Settings.Apply();
            ApplyMenuFrameRate();

            Data = GameData.Load();
            Backend = CreateBackend();
            Auth = new AuthService(Backend, Data.Backend);
            Auth.SignedIn += OnSignedIn;
            Auth.SignedOut += OnSignedOut;
            Backend.ConnectionChanged += OnConnectionChanged;
            if (Backend is RemoteBackend remote)
            {
                remote.ClientOutdated += e => UpdatePrompt.Show(e.MinVersion, e.UpdateUrl);
                StartCoroutine(SyncLoop());
            }
            Songs = new SongLibrary(Path.Combine(Application.persistentDataPath, "songs"));
            Audio = gameObject.AddComponent<AudioService>();
            Audio.Init(Settings);
            Shop = Data.Shop;
            string cloudUrl = DevAutomation.Arg("-vmsCloud") ?? Data.Backend.cloudSaveUrl;
            if (Backend is LocalBackend local && !string.IsNullOrEmpty(cloudUrl) && ApiClient.IsAllowedUrl(cloudUrl))
                Cloud = new CloudSave(cloudUrl, Path.Combine(Application.persistentDataPath, "cloud"), local);
            Purchases = new PurchaseService(Backend);
            Purchases.DeliveredLater += r =>
            {
                SetProfile(r.profile);
                if (!r.alreadyGranted) Ui.Toast(Loc.F("shop.delivered", r.grant.gold + r.grant.bonus), 4f);
            };

            CreateMenuCamera();
            Ui = UiRoot.Create(transform);
        }

        async void Start()
        {
            Ui.Show<SplashScreen>(null, false);
            try
            {
                if (Songs.Find(SongLibrary.DemoId) == null)
                {
                    Ui.Loading.Show(Loc.T("boot.firstRun"));
                    await Songs.EnsureDemoAsync(Ui.Loading.Report);
                }
                else
                {
                    await Songs.EnsureDemoAsync(null);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                Ui.Loading.Hide();
            }
            PlayMenuMusic();

            if (Cloud != null) _ = Cloud.ResumeAsync();
            bool resumed = await Auth.TryResumeAsync();
            if (!resumed) Ui.Reset<LoginScreen>();
        }

        /// <summary>Smaller DSP buffer = less delay between the clock and what the player hears.</summary>
        static void ConfigureAudio()
        {
            var config = AudioSettings.GetConfiguration();
            if (Application.isMobilePlatform && config.dspBufferSize > 512)
            {
                config.dspBufferSize = 512;
                AudioSettings.Reset(config);
            }
        }

        IBackend CreateBackend()
        {
            string mode = Data.Backend.mode;
            string url = Data.Backend.baseUrl;
            string over = DevAutomation.Arg("-vmsServer") ?? Settings.ServerOverride;
            if (!string.IsNullOrEmpty(over))
            {
                if (over == "offline") mode = "offline";
                else
                {
                    mode = "online";
                    url = over;
                }
            }
            if (mode == "online")
            {
                Debug.Log("[GameApp] Online backend: " + url);
                return new RemoteBackend(url, Path.Combine(Application.persistentDataPath, "online"));
            }
            return new LocalBackend(Data, Path.Combine(Application.persistentDataPath, "offline"));
        }

        void CreateMenuCamera()
        {
            var go = new GameObject("MenuCamera");
            go.transform.SetParent(transform, false);
            _menuCamera = go.AddComponent<Camera>();
            _menuCamera.clearFlags = CameraClearFlags.SolidColor;
            _menuCamera.backgroundColor = UiTheme.SkyBottom;
            _menuCamera.cullingMask = 0;
            _menuCamera.depth = -10;
            _menuCamera.allowHDR = false;
            _menuCamera.allowMSAA = false;
            go.AddComponent<AudioListener>();
        }

        void ApplyMenuFrameRate()
        {
            // Menus don't need more than 60 fps; the race uses the player's chosen target.
            Application.targetFrameRate = Settings.Economy || Settings.LowPowerMenus ? 30 : Mathf.Min(60, Settings.TargetFps);
        }

        async void PlayMenuMusic()
        {
            if (_menuClip == null)
            {
                var demo = Songs.Find(SongLibrary.DemoId);
                if (demo == null) return;
                try
                {
                    _menuClip = await AudioLoader.LoadClipAsync(Songs.AudioPath(demo));
                }
                catch (BackendException e)
                {
                    Debug.LogWarning("[GameApp] Menu music unavailable: " + e.Message);
                    return;
                }
            }
            if (!InRace) Audio.PlayMenuMusic(_menuClip);
        }

        void Update()
        {
            SteamworksBridge.RunCallbacks();
        }

        void OnApplicationQuit()
        {
            SteamworksBridge.Shutdown();
        }

        // ------------------------------------------------------------------ connection

        bool NeedsSync => Auth.IsSignedIn && !InRace && (!Backend.IsConnected || Backend.PendingRaceCount > 0);

        IEnumerator SyncLoop()
        {
            var wait = new WaitForSecondsRealtime(SyncRetrySeconds);
            while (true)
            {
                yield return wait;
                if (NeedsSync) TrySync();
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && Cloud != null && Auth != null && Auth.IsSignedIn) Cloud.AutoBackupAsync();
        }

        void OnApplicationFocus(bool focused)
        {
            if (focused && Auth != null && NeedsSync) TrySync();
        }

        void OnConnectionChanged()
        {
            if (!Backend.IsConnected)
            {
                if (!InRace) Ui.Toast(Loc.T("net.lost"), 3.5f);
            }
            else
            {
                if (NeedsSync) TrySync();
                RefreshLiveData();
            }
        }

        /// <summary>Uploads races played offline and refreshes the profile once the server answers again.</summary>
        public async void TrySync()
        {
            if (_syncing || !Auth.IsSignedIn) return;
            _syncing = true;
            try
            {
                var result = await Backend.SyncAsync();
                if (result == null || !Auth.IsSignedIn) return;
                SetProfile(result.profile);
                if (result.applied > 0) Ui.Toast(Loc.F("net.synced", result.applied, result.coins, result.jade), 4f);
            }
            catch (BackendException e)
            {
                if (e.Code == "unauthorized")
                {
                    // Revoked or expired while offline: queued races stay on the device for the next sign-in.
                    Popup.Message(Loc.T("common.error"), e.Friendly);
                    await Auth.SignOutAsync();
                }
                else if (!e.IsConnectivity && !(e is ClientOutdatedException))
                {
                    Debug.LogWarning("[GameApp] Sync failed: " + e.Code + " " + e.Message);
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        // ------------------------------------------------------------------ profile

        public void SetProfile(PlayerProfile profile)
        {
            if (profile == null) return;
            Profile = profile.Normalize();
            ProfileChanged?.Invoke();
            Ui.NotifyProfileChanged();
        }

        public NavigatorDef SelectedNavigator =>
            Data.Navigator(Profile != null ? Profile.selectedNavigator : null) ?? Data.Navigator(Data.Economy.starterNavigator);

        public int BondLevel(string navigatorId)
        {
            var owned = Profile != null ? Profile.FindNavigator(navigatorId) : null;
            return owned != null ? EconomyRules.BondLevel(Data.Economy.bond, owned.bondExp) : 1;
        }

        void OnSignedIn(AuthResult result)
        {
            SetProfile(result.profile);
            Ui.Reset<HomeScreen>();
            RefreshLiveData();
        }

        /// <summary>Fetches the shop catalog, events and mail counts; falls back to the game's own data offline.</summary>
        public async void RefreshLiveData()
        {
            try
            {
                var shop = await Backend.GetShopAsync();
                if (shop != null && shop.catalog != null && shop.catalog.items != null && shop.catalog.items.Count > 0)
                {
                    var problems = shop.catalog.Validate();
                    if (problems.Count == 0) Shop = shop.catalog;
                    else Debug.LogWarning("[GameApp] Server shop catalog rejected: " + string.Join("; ", problems));
                }
                if (shop != null && shop.stores != null) Stores = shop.stores;
            }
            catch (BackendException e)
            {
                Debug.Log("[GameApp] Shop catalog unavailable (" + e.Code + "); using the game's data.");
            }
            Purchases.Configure(Shop, Stores);
            try
            {
                var events = await Backend.GetEventsAsync();
                Events = events != null && events.events != null ? events.events : new List<EventDef>();
            }
            catch (BackendException)
            {
                Events = EventRules.Visible(Data.Events, DateTime.UtcNow);
            }
            await RefreshMailCountsAsync();
            LiveDataChanged?.Invoke();
        }

        public async Task RefreshMailCountsAsync()
        {
            if (!Auth.IsSignedIn) return;
            try
            {
                var mail = await Backend.GetMailAsync();
                MailUnread = mail.unread;
                MailUnclaimed = mail.unclaimed;
            }
            catch (BackendException)
            {
                // Offline: keep the last counts.
            }
            LiveDataChanged?.Invoke();
        }

        /// <summary>Events with a reward ready to claim (lobby badge).</summary>
        public int ClaimableEventRewards
        {
            get
            {
                if (Profile == null) return 0;
                int count = 0;
                foreach (var e in Events)
                {
                    var progress = Profile.events.Find(x => x.id == e.id);
                    if (progress == null) continue;
                    for (int i = 0; i < e.milestones.Count; i++)
                        if (progress.value >= e.milestones[i].target && !progress.claimed.Contains(i)) count++;
                }
                return count;
            }
        }

        void OnSignedOut()
        {
            Profile = null;
            Ui.Reset<LoginScreen>();
        }

        public void ApplySettings()
        {
            Settings.Save();
            Settings.Apply();
            ApplyMenuFrameRate();
            Audio.RefreshVolumes();
        }

        // ------------------------------------------------------------------ races

        public async void StartRace(RaceSetup setup)
        {
            if (_race != null || _starting) return;
            _starting = true;
            _lastSetup = setup;
            setup.vesselKit = CosmeticArt.Equipped(ShopCategories.VesselKit, setup.vesselId);
            setup.exhaust = CosmeticArt.Equipped(ShopCategories.Exhaust);
            setup.bubbleFrame = CosmeticArt.Equipped(ShopCategories.BubbleFrame);
            Audio.StopMenuMusic(0.3f);
            Ui.Loading.Show(Loc.T("race.loading"));
            try
            {
                _race = await RaceDirector.CreateAsync(setup, Ui.Loading);
            }
            catch (Exception e)
            {
                Ui.Loading.Hide();
                _starting = false;
                var be = e as BackendException;
                if (be == null) Debug.LogException(e);
                Popup.Message(Loc.T("common.error"), be != null ? be.Friendly : e.Message);
                PlayMenuMusic();
                return;
            }
            Ui.Loading.Hide();
            _starting = false;

            _race.Finished += OnRaceFinished;
            _race.Restarted += RestartRace;
            _race.Quit += QuitRace;
            _menuCamera.enabled = false;
            Ui.SetMenusVisible(false);
            Application.targetFrameRate = Settings.TargetFps;
            _race.Begin();
        }

        void EndRace()
        {
            if (_race != null) Destroy(_race.gameObject);
            _race = null;
            _menuCamera.enabled = true;
            Ui.SetMenusVisible(true);
            ApplyMenuFrameRate();
        }

        void RestartRace()
        {
            var setup = _lastSetup;
            EndRace();
            StartRace(setup);
        }

        void QuitRace()
        {
            EndRace();
            PlayMenuMusic();
        }

        /// <summary>Leaves the current race without results (pause menu "Quit", automation).</summary>
        public void AbortRace()
        {
            if (_race != null) QuitRace();
        }

        async void OnRaceFinished(RaceResult result)
        {
            if (result.setup.mode != GameMode.Zen && result.completed)
            {
                try
                {
                    var response = await Backend.CompleteRaceAsync(result.report);
                    result.reward = response.reward;
                    result.rewardPending = response.pending;
                    SetProfile(response.profile);
                    if (Cloud != null) Cloud.AutoBackupAsync();
                }
                catch (BackendException e)
                {
                    Debug.LogWarning("[GameApp] Race reward failed: " + e.Code + " " + e.Message);
                    Ui.Toast(e.Friendly);
                }
            }
            await Task.Delay(400);
            EndRace();
            Ui.Show<ResultsScreen>(result);
            PlayMenuMusic();
        }
    }
}
