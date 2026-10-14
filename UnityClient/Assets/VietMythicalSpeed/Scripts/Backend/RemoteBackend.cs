using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using VMS.Core;

namespace VMS.Backend
{
    /// <summary>
    /// Online backend: every rule runs on the authoritative server in server/. The last known account and profile
    /// are cached per account so the game keeps working without a connection: races are queued and synced later
    /// (the server applies the caps of the day they were played), a navigator choice is replayed, and draws,
    /// awakening, linking and account deletion wait until the server is reachable again.
    /// </summary>
    public sealed class RemoteBackend : IBackend
    {
        [Serializable] class RegisterBody { public string username; public string email; public string password; }
        [Serializable] class LoginBody { public string login; public string password; }
        [Serializable] class GuestBody { public string deviceId; public string deviceSecret; }
        [Serializable] class GoogleBody { public string serverAuthCode; }
        [Serializable] class SteamTicketBody { public string ticket; }
        [Serializable] class OpenIdBody { public string code; public string codeVerifier; }
        [Serializable] class RefreshBody { public string refreshToken; }
        [Serializable] class NavigatorBody { public string navigatorId; }
        [Serializable] class PullBody { public string bannerId; public int count; }
        [Serializable] class SparkBody { public string bannerId; public string navigatorId; }
        [Serializable] class RaceBody { public RaceReport report; public string id; }
        [Serializable] class SyncBody { public PendingRace[] races; }
        [Serializable] class SyncItem { public string id; public string status; }
        [Serializable] class SyncTotals { public int applied; public int coins; public int jade; public int bondExp; }
        [Serializable] class SyncResponse { public SyncItem[] results; public SyncTotals total; public PlayerProfile profile; }
        [Serializable] class DeleteBody { public bool confirm; public string password; }
        [Serializable] class EmptyBody { }
        [Serializable] class ProfileEnvelope { public PlayerProfile profile; }
        [Serializable] class AccountEnvelope { public AccountInfo account; }
        [Serializable] class BannersEnvelope { public BannerDef[] banners; }

        /// <summary>What the device remembers about one online account between sessions.</summary>
        [Serializable]
        class AccountCache
        {
            public AccountInfo account;
            public PlayerProfile profile;
            /// <summary>Navigator picked while offline, sent on the next sync.</summary>
            public string pendingNavigator;
            public PendingRaceQueue queue = new PendingRaceQueue();
        }

        const int SyncBatch = 50;

        readonly ApiClient _api;
        readonly string _cacheDir;
        AccountCache _cache;
        string _accountId;
        bool _connected = true;

        public RemoteBackend(string baseUrl, string cacheDirectory)
        {
            _api = new ApiClient(baseUrl);
            // One folder per server, so a development server never mixes with production data.
            _cacheDir = Path.Combine(cacheDirectory, StableHash.Fnv1a(_api.BaseUrl).ToString("x8"));
        }

        public ApiClient Api => _api;
        public bool IsOnline => true;
        public string Key => _api.BaseUrl;
        public bool SupportsSteamOpenId => true;
        public bool IsConnected => _connected;
        public int PendingRaceCount => _cache != null ? _cache.queue.Count : 0;

        public event Action ConnectionChanged;

        public event Action<ClientOutdatedException> ClientOutdated
        {
            add => _api.ClientOutdated += value;
            remove => _api.ClientOutdated -= value;
        }

        // ---------------------------------------------------------------- connection + cache

        /// <summary>Runs a server call and tracks whether the server is reachable.</summary>
        async Task<T> Call<T>(Func<Task<T>> call)
        {
            try
            {
                var result = await call();
                SetConnected(true);
                return result;
            }
            catch (BackendException e) when (e.IsConnectivity)
            {
                SetConnected(false);
                throw;
            }
        }

        void SetConnected(bool connected)
        {
            if (_connected == connected) return;
            _connected = connected;
            Debug.Log("[RemoteBackend] " + (connected ? "Connected." : "Offline."));
            ConnectionChanged?.Invoke();
        }

        bool HasCachedProfile => _cache != null && _cache.profile != null;

        string CachePath(string accountId) => Path.Combine(_cacheDir, "account_" + accountId + ".json");

        void OpenCache(string accountId)
        {
            if (_cache != null && _accountId == accountId) return;
            _accountId = accountId;
            _cache = JsonFile.Read<AccountCache>(CachePath(accountId)) ?? new AccountCache();
            if (_cache.queue == null) _cache.queue = new PendingRaceQueue();
        }

        void SaveCache()
        {
            if (_cache == null || string.IsNullOrEmpty(_accountId)) return;
            try
            {
                JsonFile.Write(CachePath(_accountId), _cache);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RemoteBackend] Could not save the offline cache: " + e.Message);
            }
        }

        AuthResult Remember(AuthResult result)
        {
            if (result == null || result.account == null) return result;
            OpenCache(result.account.id);
            _cache.account = result.account;
            if (result.profile != null) _cache.profile = result.profile;
            SaveCache();
            return result;
        }

        PlayerProfile Remember(PlayerProfile profile)
        {
            if (profile == null || _cache == null) return profile;
            _cache.profile = profile;
            SaveCache();
            return profile;
        }

        // ---------------------------------------------------------------- auth

        async Task<AuthResult> Auth(string path, object body)
        {
            var result = await Call(() => _api.Post<AuthResult>(path, body, false));
            _api.Accept(result);
            return Remember(result);
        }

        public Task<AuthResult> RegisterAsync(string username, string email, string password) =>
            Auth("/v1/auth/register", new RegisterBody { username = username, email = email, password = password });

        public Task<AuthResult> LoginPasswordAsync(string login, string password) =>
            Auth("/v1/auth/login", new LoginBody { login = login, password = password });

        public Task<AuthResult> LoginGuestAsync(string deviceId, string deviceSecret) =>
            Auth("/v1/auth/guest", new GuestBody { deviceId = deviceId, deviceSecret = deviceSecret });

        public Task<AuthResult> LoginGooglePlayAsync(GooglePlayCredential credential) =>
            Auth("/v1/auth/google", new GoogleBody { serverAuthCode = credential.serverAuthCode });

        public Task<AuthResult> LoginSteamTicketAsync(SteamCredential credential) =>
            Auth("/v1/auth/steam/ticket", new SteamTicketBody { ticket = credential.ticket });

        public Task<AuthResult> LoginSteamOpenIdAsync(string code, string codeVerifier) =>
            Auth("/v1/auth/steam/openid/exchange", new OpenIdBody { code = code, codeVerifier = codeVerifier });

        public async Task<AuthResult> ResumeAsync(StoredSession session)
        {
            if (session == null || string.IsNullOrEmpty(session.refreshToken))
                throw new BackendException("unauthorized", "No stored session.");
            _api.RefreshToken = session.refreshToken;
            try
            {
                return Remember(await Call(() => _api.RefreshAsync()));
            }
            catch (BackendException e) when (e.IsConnectivity && !string.IsNullOrEmpty(session.accountId))
            {
                // No connection: continue from the cached profile. The refresh token is kept, so the first call
                // after the server is back (a 401 without an access token) refreshes the session transparently.
                OpenCache(session.accountId);
                if (_cache.account == null || _cache.profile == null) throw;
                Debug.Log("[RemoteBackend] Offline: resuming from the cached profile.");
                return new AuthResult { account = _cache.account, profile = _cache.profile, refreshToken = session.refreshToken };
            }
        }

        public async Task LogoutAsync()
        {
            try
            {
                if (!string.IsNullOrEmpty(_api.RefreshToken))
                    await _api.Post<EmptyBody>("/v1/auth/logout", new RefreshBody { refreshToken = _api.RefreshToken }, false);
            }
            catch (BackendException)
            {
                // Logging out locally must always succeed; the server token expires on its own.
            }
            _api.Clear();
            // The cache file stays: races still queued for this account sync at its next sign-in on this device.
            _cache = null;
            _accountId = null;
            SetConnected(true);
        }

        public async Task DeleteAccountAsync(string password)
        {
            await Call(() => _api.Post<EmptyBody>("/v1/account/delete", new DeleteBody { confirm = true, password = password ?? string.Empty }));
            if (!string.IsNullOrEmpty(_accountId)) JsonFile.Delete(CachePath(_accountId));
            _cache = null;
            _accountId = null;
            _api.Clear();
        }

        public async Task<AccountInfo> LinkGooglePlayAsync(GooglePlayCredential credential) =>
            (await Call(() => _api.Post<AccountEnvelope>("/v1/account/link/google", new GoogleBody { serverAuthCode = credential.serverAuthCode }))).account;

        public async Task<AccountInfo> LinkSteamTicketAsync(SteamCredential credential) =>
            (await Call(() => _api.Post<AccountEnvelope>("/v1/account/link/steam", new SteamTicketBody { ticket = credential.ticket }))).account;

        public async Task<AccountInfo> LinkSteamOpenIdAsync(string code, string codeVerifier) =>
            (await Call(() => _api.Post<AccountEnvelope>("/v1/account/link/steam", new OpenIdBody { code = code, codeVerifier = codeVerifier }))).account;

        public string SteamOpenIdStartUrl(string redirectUri, string codeChallenge, string state) =>
            _api.BaseUrl + "/v1/auth/steam/openid/start?redirect_uri=" + Uri.EscapeDataString(redirectUri) +
            "&code_challenge=" + Uri.EscapeDataString(codeChallenge) + "&state=" + Uri.EscapeDataString(state);

        // ---------------------------------------------------------------- game

        public async Task<PlayerProfile> GetProfileAsync()
        {
            try
            {
                return Remember((await Call(() => _api.Get<ProfileEnvelope>("/v1/profile"))).profile);
            }
            catch (BackendException e) when (e.IsConnectivity && HasCachedProfile)
            {
                return _cache.profile;
            }
        }

        /// <summary>Known to be offline: skip the request (the sync loop probes the server) so the game reacts at once.</summary>
        bool PlayLocally => !_connected && HasCachedProfile;

        public async Task<PlayerProfile> SelectNavigatorAsync(string navigatorId)
        {
            if (PlayLocally) return SelectNavigatorOffline(navigatorId);
            try
            {
                var profile = (await Call(() => _api.Post<ProfileEnvelope>("/v1/profile/navigator", new NavigatorBody { navigatorId = navigatorId }))).profile;
                if (_cache != null) _cache.pendingNavigator = null;
                return Remember(profile);
            }
            catch (BackendException e) when (e.IsConnectivity && HasCachedProfile)
            {
                return SelectNavigatorOffline(navigatorId);
            }
        }

        PlayerProfile SelectNavigatorOffline(string navigatorId)
        {
            if (!_cache.profile.Owns(navigatorId)) throw new BackendException("not_owned", "Navigator not owned.");
            _cache.profile.selectedNavigator = navigatorId;
            _cache.pendingNavigator = navigatorId;
            SaveCache();
            return _cache.profile;
        }

        public async Task<PlayerProfile> AwakenAsync(string navigatorId) =>
            Remember((await Call(() => _api.Post<ProfileEnvelope>("/v1/navigators/awaken", new NavigatorBody { navigatorId = navigatorId }))).profile);

        public async Task<List<BannerDef>> GetBannersAsync()
        {
            var env = await Call(() => _api.Get<BannersEnvelope>("/v1/gacha/banners"));
            return env != null && env.banners != null ? new List<BannerDef>(env.banners) : new List<BannerDef>();
        }

        public async Task<PullResponse> PullAsync(string bannerId, int count)
        {
            var response = await Call(() => _api.Post<PullResponse>("/v1/gacha/pull", new PullBody { bannerId = bannerId, count = count }));
            Remember(response.profile);
            return response;
        }

        public async Task<PullResponse> SparkAsync(string bannerId, string navigatorId)
        {
            var response = await Call(() => _api.Post<PullResponse>("/v1/gacha/spark", new SparkBody { bannerId = bannerId, navigatorId = navigatorId }));
            Remember(response.profile);
            return response;
        }

        public async Task<RaceCompleteResponse> CompleteRaceAsync(RaceReport report)
        {
            string id = Guid.NewGuid().ToString("N");
            if (PlayLocally) return QueueRace(id, report);
            try
            {
                var response = await Call(() => _api.Post<RaceCompleteResponse>("/v1/race/complete", new RaceBody { report = report, id = id }));
                Remember(response.profile);
                return response;
            }
            catch (BackendException e) when (e.IsConnectivity && HasCachedProfile)
            {
                // The request may or may not have reached the server; its id makes the later sync safe either way.
                return QueueRace(id, report);
            }
        }

        RaceCompleteResponse QueueRace(string id, RaceReport report)
        {
            _cache.queue.Add(new PendingRace
            {
                id = id,
                finishedAt = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                report = report,
            });
            SaveCache();
            return new RaceCompleteResponse { profile = _cache.profile, pending = true };
        }

        public async Task<DailyClaimResponse> ClaimDailyAsync()
        {
            var response = await Call(() => _api.Post<DailyClaimResponse>("/v1/daily/claim", new EmptyBody()));
            Remember(response.profile);
            return response;
        }

        // ---------------------------------------------------------------- shop, purchases, events, mail

        [Serializable] class ItemBody { public string itemId; }
        [Serializable] class EquipBody { public string category; public string target; public string itemId; }
        [Serializable] class AmountBody { public int amount; }
        [Serializable] class ProductBody { public string productId; }
        [Serializable] class AllowedEnvelope { public bool allowed; }
        [Serializable] class GoogleBuyBody { public string productId; public string purchaseToken; }
        [Serializable] class SteamInitBody { public string productId; public string language; }
        [Serializable] class SteamInitEnvelope { public string orderId; }
        [Serializable] class SteamFinishBody { public string orderId; public bool authorized; }
        [Serializable] class EventClaimBody { public string eventId; public int index; }
        [Serializable] class MailIdBody { public long id; }
        [Serializable] class MailClaimBody { public long id; public bool all; }
        [Serializable] class ForgotBody { public string email; public string lang; }
        [Serializable] class ResetBody { public string email; public string code; public string newPassword; }

        T KeepProfile<T>(T response, PlayerProfile profile)
        {
            Remember(profile);
            return response;
        }

        public Task<ShopResponse> GetShopAsync() => Call(() => _api.Get<ShopResponse>("/v1/shop", false));

        public async Task<ShopBuyResponse> BuyItemAsync(string itemId)
        {
            var r = await Call(() => _api.Post<ShopBuyResponse>("/v1/shop/buy", new ItemBody { itemId = itemId }));
            return KeepProfile(r, r.profile);
        }

        public async Task<PlayerProfile> EquipAsync(string category, string target, string itemId) =>
            Remember((await Call(() => _api.Post<ProfileEnvelope>("/v1/cosmetics/equip", new EquipBody { category = category, target = target ?? string.Empty, itemId = itemId ?? string.Empty }))).profile);

        public async Task<ExchangeResponse> ExchangeGoldAsync(int amount)
        {
            var r = await Call(() => _api.Post<ExchangeResponse>("/v1/shop/exchange", new AmountBody { amount = amount }));
            return KeepProfile(r, r.profile);
        }

        public async Task<bool> CanBuyProductAsync(string productId) =>
            (await Call(() => _api.Post<AllowedEnvelope>("/v1/purchases/check", new ProductBody { productId = productId }))).allowed;

        public async Task<PurchaseResponse> VerifyGooglePurchaseAsync(string productId, string purchaseToken)
        {
            var r = await Call(() => _api.Post<PurchaseResponse>("/v1/purchases/google", new GoogleBuyBody { productId = productId, purchaseToken = purchaseToken }));
            return KeepProfile(r, r.profile);
        }

        public async Task<string> StartSteamPurchaseAsync(string productId, string language) =>
            (await Call(() => _api.Post<SteamInitEnvelope>("/v1/purchases/steam/init", new SteamInitBody { productId = productId, language = language }))).orderId;

        public async Task<PurchaseResponse> FinishSteamPurchaseAsync(string orderId, bool authorized)
        {
            var r = await Call(() => _api.Post<PurchaseResponse>("/v1/purchases/steam/finalize", new SteamFinishBody { orderId = orderId, authorized = authorized }));
            return KeepProfile(r, r.profile);
        }

        public async Task<PurchaseResponse> FakePurchaseAsync(string productId)
        {
            var r = await Call(() => _api.Post<PurchaseResponse>("/v1/purchases/fake", new ProductBody { productId = productId }));
            return KeepProfile(r, r.profile);
        }

        public Task<EventsResponse> GetEventsAsync() => Call(() => _api.Get<EventsResponse>("/v1/events", false));

        public async Task<RewardsResponse> ClaimEventAsync(string eventId, int index)
        {
            var r = await Call(() => _api.Post<RewardsResponse>("/v1/events/claim", new EventClaimBody { eventId = eventId, index = index }));
            return KeepProfile(r, r.profile);
        }

        public Task<MailList> GetMailAsync() => Call(() => _api.Get<MailList>("/v1/mail"));

        public Task MarkMailReadAsync(long id) => Call(() => _api.Post<EmptyBody>("/v1/mail/read", new MailIdBody { id = id }));

        public async Task<MailClaimResponse> ClaimMailAsync(long id, bool all)
        {
            var r = await Call(() => _api.Post<MailClaimResponse>("/v1/mail/claim", new MailClaimBody { id = all ? 0 : id, all = all }));
            return KeepProfile(r, r.profile);
        }

        public Task DeleteMailAsync(long id) => Call(() => _api.Post<EmptyBody>("/v1/mail/delete", new MailIdBody { id = id }));

        public Task RequestPasswordResetAsync(string email, string language) =>
            Call(() => _api.Post<EmptyBody>("/v1/auth/password/forgot", new ForgotBody { email = email, lang = language }, false));

        public Task<AuthResult> ResetPasswordAsync(string email, string code, string newPassword) =>
            Auth("/v1/auth/password/reset", new ResetBody { email = email, code = code, newPassword = newPassword });

        // ---------------------------------------------------------------- cloud saves (backups of offline-mode progress)

        [Serializable] class CloudPutBody { public string slot; public string data; public string summary; public string device; public int baseRevision; public bool force; }

        public Task<CloudSaveInfo> GetCloudSaveAsync(string slot) =>
            Call(() => _api.Get<CloudSaveInfo>("/v1/cloudsave?slot=" + Uri.EscapeDataString(slot)));

        public Task<CloudSaveInfo> PutCloudSaveAsync(string slot, string data, string summary, int baseRevision, bool force) =>
            Call(() => _api.Post<CloudSaveInfo>("/v1/cloudsave", new CloudPutBody
            {
                slot = slot,
                data = data,
                summary = summary,
                device = SystemInfo.deviceModel,
                baseRevision = baseRevision,
                force = force,
            }));

        public async Task<SyncResult> SyncAsync()
        {
            if (_cache == null) return null;
            var summary = new SyncResult();

            string navigator = _cache.pendingNavigator;
            if (!string.IsNullOrEmpty(navigator))
            {
                try
                {
                    summary.profile = Remember((await Call(() => _api.Post<ProfileEnvelope>("/v1/profile/navigator", new NavigatorBody { navigatorId = navigator }))).profile);
                }
                catch (BackendException e) when (e.Code == "not_owned")
                {
                    // The server knows better (e.g. progress changed elsewhere); keep its choice.
                }
                if (_cache == null) return null;
                _cache.pendingNavigator = null;
                SaveCache();
            }

            while (_cache != null && _cache.queue.Count > 0)
            {
                var batch = _cache.queue.Batch(SyncBatch);
                var response = await Call(() => _api.Post<SyncResponse>("/v1/race/sync", new SyncBody { races = batch.ToArray() }));
                if (_cache == null) return null;
                // Every race the server answered for is final (applied, duplicate, expired or rejected).
                var answered = new List<string>();
                if (response.results != null)
                    foreach (var r in response.results) answered.Add(r.id);
                int settled = _cache.queue.Settle(answered);
                if (response.total != null)
                {
                    summary.applied += response.total.applied;
                    summary.coins += response.total.coins;
                    summary.jade += response.total.jade;
                    summary.bondExp += response.total.bondExp;
                }
                summary.profile = Remember(response.profile);
                if (settled == 0) break;
            }

            // Nothing to upload: this doubles as the "is the server back?" probe and refreshes the profile.
            if (summary.profile == null && _cache != null)
                summary.profile = Remember((await Call(() => _api.Get<ProfileEnvelope>("/v1/profile"))).profile);
            return summary;
        }
    }
}
