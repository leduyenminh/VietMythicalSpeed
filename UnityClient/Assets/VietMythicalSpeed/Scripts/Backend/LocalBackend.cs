using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using VMS.Core;

namespace VMS.Backend
{
    /// <summary>
    /// Offline backend: accounts and progress live in Application.persistentDataPath/offline.
    /// It applies exactly the same rules as the server (shared VMS.Core code), but nothing stops a player from
    /// editing local files, so offline progress is never uploaded or trusted by the server.
    /// </summary>
    public sealed class LocalBackend : IBackend
    {
        [Serializable]
        class LocalIdentity
        {
            public string provider;
            public string subject;
        }

        [Serializable]
        class LocalAccount
        {
            public string id;
            public string username;
            public string email;
            public string displayName;
            public string salt;
            public string hash;
            public int iterations;
            public List<LocalIdentity> identities = new List<LocalIdentity>();
        }

        [Serializable]
        class AccountsFile
        {
            public List<LocalAccount> accounts = new List<LocalAccount>();
        }

        readonly GameData _data;
        readonly string _dir;
        readonly GachaService _gacha;
        AccountsFile _accounts;
        LocalAccount _current;
        PlayerProfile _profile;

        public LocalBackend(GameData data, string directory)
        {
            _data = data;
            _dir = directory;
            Directory.CreateDirectory(_dir);
            _gacha = new GachaService(new GachaEngine(data.Gacha, data.Navigators), data.Banners);
            _accounts = JsonFile.Read<AccountsFile>(AccountsPath) ?? new AccountsFile();
        }

        public bool IsOnline => false;
        public string Key => "offline";
        public bool SupportsSteamOpenId => false;
        public bool IsConnected => true;
        public int PendingRaceCount => 0;

        public event Action ConnectionChanged
        {
            add { }
            remove { }
        }

        public Task<SyncResult> SyncAsync() => Task.FromResult<SyncResult>(null);

        string AccountsPath => Path.Combine(_dir, "accounts.json");
        string ProfilePath(string accountId) => Path.Combine(_dir, "profile_" + accountId + ".json");
        static DateTime Now => DateTime.UtcNow;

        // ---------------------------------------------------------------- auth

        public async Task<AuthResult> RegisterAsync(string username, string email, string password)
        {
            string error = AccountRules.ValidateRegistration(username, email, password);
            if (error != null) throw new BackendException(error, error);
            if (FindByLogin(username) != null) throw new BackendException("username_taken", "Username already exists.");
            if (FindByLogin(email) != null) throw new BackendException("email_taken", "Email already registered.");

            string salt = null;
            string hash = await Task.Run(() => Pbkdf2.Hash(password, out salt));
            var account = NewAccount(username);
            account.username = username;
            account.email = email.Trim().ToLowerInvariant();
            account.salt = salt;
            account.hash = hash;
            account.iterations = Pbkdf2.DefaultIterations;
            account.identities.Add(new LocalIdentity { provider = Providers.Password, subject = account.id });
            return SignIn(account, true);
        }

        public async Task<AuthResult> LoginPasswordAsync(string login, string password)
        {
            var account = FindByLogin(login);
            bool ok = account != null && !string.IsNullOrEmpty(account.hash) &&
                      await Task.Run(() => Pbkdf2.Verify(password ?? string.Empty, account.salt, account.hash, account.iterations));
            if (!ok) throw new BackendException("invalid_credentials", "Wrong username/email or password.");
            return SignIn(account, false);
        }

        public Task<AuthResult> LoginGuestAsync(string deviceId, string deviceSecret) =>
            Task.FromResult(SignInWithIdentity(Providers.Guest, deviceId, Loc.T("login.guestName")));

        public Task<AuthResult> LoginGooglePlayAsync(GooglePlayCredential credential)
        {
            // Offline we cannot verify the server auth code, so the Play Games player id is trusted as-is.
            if (credential == null || string.IsNullOrEmpty(credential.playerId))
                throw new BackendException("gpgs_failed", "No Google Play Games player.");
            return Task.FromResult(SignInWithIdentity(Providers.Google, credential.playerId, credential.displayName));
        }

        public Task<AuthResult> LoginSteamTicketAsync(SteamCredential credential)
        {
            if (credential == null || string.IsNullOrEmpty(credential.steamId))
                throw new BackendException("steam_failed", "No Steam user.");
            return Task.FromResult(SignInWithIdentity(Providers.Steam, credential.steamId, credential.personaName));
        }

        public Task<AuthResult> LoginSteamOpenIdAsync(string code, string codeVerifier) =>
            throw new BackendException("needs_server", "Steam sign-in through the browser needs the online server.");

        public Task<AuthResult> ResumeAsync(StoredSession session)
        {
            var account = session == null ? null : _accounts.accounts.Find(a => a.id == session.accountId);
            if (account == null) throw new BackendException("unauthorized", "Offline account not found.");
            return Task.FromResult(SignIn(account, false));
        }

        public Task LogoutAsync()
        {
            _current = null;
            _profile = null;
            return Task.CompletedTask;
        }

        public async Task DeleteAccountAsync(string password)
        {
            RequireSession();
            var account = _current;
            if (!string.IsNullOrEmpty(account.hash))
            {
                bool ok = await Task.Run(() => Pbkdf2.Verify(password ?? string.Empty, account.salt, account.hash, account.iterations));
                if (!ok) throw new BackendException("invalid_password", "Wrong password.");
            }
            _accounts.accounts.Remove(account);
            SaveAccounts();
            JsonFile.Delete(ProfilePath(account.id));
            JsonFile.Delete(MailPath(account.id));
            _current = null;
            _profile = null;
        }

        public Task<AccountInfo> LinkGooglePlayAsync(GooglePlayCredential credential) =>
            Task.FromResult(Link(Providers.Google, credential != null ? credential.playerId : null));

        public Task<AccountInfo> LinkSteamTicketAsync(SteamCredential credential) =>
            Task.FromResult(Link(Providers.Steam, credential != null ? credential.steamId : null));

        public Task<AccountInfo> LinkSteamOpenIdAsync(string code, string codeVerifier) =>
            throw new BackendException("needs_server", "Steam linking through the browser needs the online server.");

        public string SteamOpenIdStartUrl(string redirectUri, string codeChallenge, string state) => null;

        AccountInfo Link(string provider, string subject)
        {
            RequireSession();
            if (string.IsNullOrEmpty(subject)) throw new BackendException("link_failed", "Nothing to link.");
            var owner = FindByIdentity(provider, subject);
            if (owner != null && owner != _current) throw new BackendException("identity_in_use", "Already linked to another account.");
            if (owner == null) _current.identities.Add(new LocalIdentity { provider = provider, subject = subject });
            SaveAccounts();
            return Info(_current);
        }

        AuthResult SignInWithIdentity(string provider, string subject, string displayName)
        {
            if (string.IsNullOrEmpty(subject)) throw new BackendException("invalid_identity", "Missing identity.");
            var account = FindByIdentity(provider, subject);
            bool created = false;
            if (account == null)
            {
                account = NewAccount(string.IsNullOrEmpty(displayName) ? Loc.T("login.guestName") : displayName);
                account.identities.Add(new LocalIdentity { provider = provider, subject = subject });
                created = true;
            }
            return SignIn(account, created);
        }

        LocalAccount NewAccount(string displayName)
        {
            var account = new LocalAccount { id = Guid.NewGuid().ToString("N"), displayName = displayName };
            _accounts.accounts.Add(account);
            return account;
        }

        AuthResult SignIn(LocalAccount account, bool created)
        {
            _current = account;
            _profile = created ? null : JsonFile.Read<PlayerProfile>(ProfilePath(account.id));
            if (_profile == null)
            {
                _profile = EconomyRules.NewProfile(_data.Economy, account.id, account.displayName);
                SaveProfile();
                SendTemplate(account.id, "welcome");
            }
            _profile.Normalize();
            SaveAccounts();
            return new AuthResult { account = Info(account), profile = _profile };
        }

        static AccountInfo Info(LocalAccount a)
        {
            var providers = new List<string>();
            foreach (var i in a.identities) if (!providers.Contains(i.provider)) providers.Add(i.provider);
            return new AccountInfo { id = a.id, username = a.username, email = a.email, displayName = a.displayName, providers = providers.ToArray() };
        }

        LocalAccount FindByLogin(string login)
        {
            if (string.IsNullOrEmpty(login)) return null;
            string l = login.Trim();
            return _accounts.accounts.Find(a =>
                string.Equals(a.username, l, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.email, l, StringComparison.OrdinalIgnoreCase));
        }

        LocalAccount FindByIdentity(string provider, string subject) =>
            _accounts.accounts.Find(a => a.identities.Exists(i => i.provider == provider && i.subject == subject));

        void RequireSession()
        {
            if (_current == null || _profile == null) throw new BackendException("unauthorized", "Not signed in.");
        }

        // ---------------------------------------------------------------- game

        public Task<PlayerProfile> GetProfileAsync()
        {
            RequireSession();
            return Task.FromResult(_profile);
        }

        public Task<PlayerProfile> SelectNavigatorAsync(string navigatorId)
        {
            RequireSession();
            if (!_profile.Owns(navigatorId)) throw new BackendException("not_owned", "Navigator not owned.");
            _profile.selectedNavigator = navigatorId;
            SaveProfile();
            return Task.FromResult(_profile);
        }

        public Task<PlayerProfile> AwakenAsync(string navigatorId)
        {
            RequireSession();
            try
            {
                CollectionRules.Awaken(_profile, navigatorId, _data.Gacha);
            }
            catch (GachaException e)
            {
                throw new BackendException(e.Code, e.Message);
            }
            SaveProfile();
            return Task.FromResult(_profile);
        }

        public Task<List<BannerDef>> GetBannersAsync() => Task.FromResult(_data.Banners.Active(Now));

        public Task<PullResponse> PullAsync(string bannerId, int count)
        {
            RequireSession();
            List<PullResult> results;
            try
            {
                results = _gacha.Pull(_profile, bannerId, count, Xoshiro128.CreateRandomlySeeded(), Now);
            }
            catch (GachaException e)
            {
                throw new BackendException(e.Code, e.Message);
            }
            SaveProfile();
            return Task.FromResult(new PullResponse { results = results.ToArray(), profile = _profile });
        }

        public Task<PullResponse> SparkAsync(string bannerId, string navigatorId)
        {
            RequireSession();
            PullResult result;
            try
            {
                result = _gacha.Spark(_profile, bannerId, navigatorId, Now);
            }
            catch (GachaException e)
            {
                throw new BackendException(e.Code, e.Message);
            }
            SaveProfile();
            return Task.FromResult(new PullResponse { results = new[] { result }, profile = _profile });
        }

        public Task<RaceCompleteResponse> CompleteRaceAsync(RaceReport report)
        {
            RequireSession();
            var nav = _data.Navigator(report.navigatorId);
            var reward = EconomyRules.GrantRaceReward(_profile, _data.Economy, report, nav, Now);
            EventRules.OnRace(_profile, _data.Events, report, Now, _data.Economy.race.minDurationSec);
            SaveProfile();
            return Task.FromResult(new RaceCompleteResponse { reward = reward, profile = _profile });
        }

        public Task<DailyClaimResponse> ClaimDailyAsync()
        {
            RequireSession();
            var result = DailyRules.Claim(_profile, _data.Economy, _data.Shop, _data.Events, Now);
            EventRules.Prune(_profile, _data.Events);
            SaveProfile();
            return Task.FromResult(new DailyClaimResponse { result = result, profile = _profile });
        }

        // ---------------------------------------------------------------- shop, purchases, events, mail

        /// <summary>Offline the dev store stands in for real payments in the editor and development builds only.</summary>
        static bool DevStoreAllowed => Application.isEditor || Debug.isDebugBuild;

        T Rules<T>(Func<T> apply)
        {
            RequireSession();
            try
            {
                var result = apply();
                SaveProfile();
                return result;
            }
            catch (ShopException e)
            {
                throw new BackendException(e.Code, e.Message);
            }
        }

        public Task<ShopResponse> GetShopAsync() =>
            Task.FromResult(new ShopResponse { catalog = _data.Shop, stores = new StoresInfo { fake = DevStoreAllowed } });

        public Task<ShopBuyResponse> BuyItemAsync(string itemId) => Task.FromResult(Rules(() =>
        {
            var r = ShopRules.Buy(_profile, _data.Shop, itemId, Now);
            return new ShopBuyResponse { profile = _profile, itemId = itemId, rewards = r.rewards };
        }));

        public Task<PlayerProfile> EquipAsync(string category, string target, string itemId) => Task.FromResult(Rules(() =>
        {
            ShopRules.Equip(_profile, _data.Shop, category, target, itemId);
            return _profile;
        }));

        public Task<ExchangeResponse> ExchangeGoldAsync(int amount) => Task.FromResult(Rules(() =>
            new ExchangeResponse { jade = ShopRules.ExchangeGold(_profile, _data.Shop, amount), profile = _profile }));

        public Task<bool> CanBuyProductAsync(string productId)
        {
            RequireSession();
            return Task.FromResult(ShopRules.CanBuyProduct(_profile, _data.Shop.Product(productId)));
        }

        static BackendException NeedsServer() => new BackendException("needs_server", "Purchases need the online server.");

        public Task<PurchaseResponse> VerifyGooglePurchaseAsync(string productId, string purchaseToken) => throw NeedsServer();

        public Task<string> StartSteamPurchaseAsync(string productId, string language) => throw NeedsServer();

        public Task<PurchaseResponse> FinishSteamPurchaseAsync(string orderId, bool authorized) => throw NeedsServer();

        public Task<PurchaseResponse> FakePurchaseAsync(string productId)
        {
            if (!DevStoreAllowed) throw NeedsServer();
            var product = _data.Shop.Product(productId);
            if (product == null) throw new BackendException("product_not_found", "Unknown product.");
            return Task.FromResult(Rules(() =>
            {
                if (!ShopRules.CanBuyProduct(_profile, product)) throw new ShopException("limit_reached", "Already bought.");
                var g = ShopRules.ApplyProduct(_profile, _data.Shop, product, Now);
                return new PurchaseResponse
                {
                    profile = _profile,
                    productId = productId,
                    grant = new ProductGrantInfo { gold = g.gold, bonus = g.bonus, firstPurchase = g.firstPurchase, rewards = g.rewards, passJade = g.passJade },
                };
            }));
        }

        public Task<EventsResponse> GetEventsAsync() =>
            Task.FromResult(new EventsResponse { events = EventRules.Visible(_data.Events, Now), claimGraceDays = _data.Events.claimGraceDays });

        public Task<RewardsResponse> ClaimEventAsync(string eventId, int index) => Task.FromResult(Rules(() =>
            new RewardsResponse { rewards = EventRules.Claim(_profile, _data.Events, _data.Shop, eventId, index, Now), profile = _profile }));

        [Serializable]
        class LocalMailbox
        {
            public long nextId = 1;
            public List<MailItem> mail = new List<MailItem>();
        }

        string MailPath(string accountId) => Path.Combine(_dir, "mail_" + accountId + ".json");

        LocalMailbox ReadMailbox(string accountId) => JsonFile.Read<LocalMailbox>(MailPath(accountId)) ?? new LocalMailbox();

        static long UnixMs(DateTime utc) => (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

        void SendTemplate(string accountId, string templateId)
        {
            var t = _data.Mail != null ? _data.Mail.Find(templateId) : null;
            if (t == null) return;
            var box = ReadMailbox(accountId);
            long now = UnixMs(Now);
            box.mail.Insert(0, new MailItem
            {
                id = box.nextId++,
                title = t.title,
                body = t.body,
                attachments = new List<Reward>(t.attachments),
                createdAt = now,
                expiresAt = now + Math.Max(1, t.expiresDays) * 86400000L,
            });
            JsonFile.Write(MailPath(accountId), box);
        }

        public Task<MailList> GetMailAsync()
        {
            RequireSession();
            var box = ReadMailbox(_current.id);
            long now = UnixMs(Now);
            box.mail.RemoveAll(m => m.expiresAt <= now);
            var list = new MailList { mail = box.mail };
            foreach (var m in box.mail)
            {
                if (!m.read) list.unread++;
                if (!m.claimed && m.HasAttachments) list.unclaimed++;
            }
            return Task.FromResult(list);
        }

        public Task MarkMailReadAsync(long id)
        {
            RequireSession();
            var box = ReadMailbox(_current.id);
            var m = box.mail.Find(x => x.id == id);
            if (m != null && !m.read)
            {
                m.read = true;
                JsonFile.Write(MailPath(_current.id), box);
            }
            return Task.CompletedTask;
        }

        public Task<MailClaimResponse> ClaimMailAsync(long id, bool all)
        {
            RequireSession();
            var box = ReadMailbox(_current.id);
            var response = new MailClaimResponse { profile = _profile };
            var claimed = new List<long>();
            foreach (var m in box.mail)
            {
                if ((!all && m.id != id) || m.claimed || !m.HasAttachments) continue;
                Wallet.Grant(_profile, _data.Shop, m.attachments);
                response.rewards.AddRange(m.attachments);
                m.claimed = m.read = true;
                claimed.Add(m.id);
            }
            if (claimed.Count == 0 && !all) throw new BackendException("already_claimed", "Nothing to claim.");
            response.claimed = claimed.ToArray();
            SaveProfile();
            JsonFile.Write(MailPath(_current.id), box);
            return Task.FromResult(response);
        }

        public Task DeleteMailAsync(long id)
        {
            RequireSession();
            var box = ReadMailbox(_current.id);
            var m = box.mail.Find(x => x.id == id);
            if (m != null && !m.claimed && m.HasAttachments) throw new BackendException("claim_first", "Claim the gifts before deleting.");
            box.mail.RemoveAll(x => x.id == id);
            JsonFile.Write(MailPath(_current.id), box);
            return Task.CompletedTask;
        }

        public Task RequestPasswordResetAsync(string email, string language) =>
            throw new BackendException("needs_server", "Password reset needs the online server.");

        public Task<AuthResult> ResetPasswordAsync(string email, string code, string newPassword) =>
            throw new BackendException("needs_server", "Password reset needs the online server.");

        [Serializable]
        class OfflineSave
        {
            public int version = 1;
            public PlayerProfile profile;
            public LocalMailbox mailbox;
        }

        /// <summary>The offline save of the signed-in account, for cloud backups (profile + mailbox).</summary>
        public string ExportSave()
        {
            RequireSession();
            return JsonUtility.ToJson(new OfflineSave { profile = _profile, mailbox = ReadMailbox(_current.id) });
        }

        /// <summary>Replaces the signed-in account's progress with a cloud backup.</summary>
        public PlayerProfile ImportSave(string json)
        {
            RequireSession();
            OfflineSave save;
            try
            {
                save = JsonUtility.FromJson<OfflineSave>(json);
            }
            catch (ArgumentException)
            {
                save = null;
            }
            if (save == null || save.profile == null) throw new BackendException("save_invalid", "The cloud save is damaged.");
            save.profile.accountId = _current.id;
            _profile = save.profile.Normalize();
            SaveProfile();
            JsonFile.Write(MailPath(_current.id), save.mailbox ?? new LocalMailbox());
            return _profile;
        }

        /// <summary>Signed-in offline account (cloud backups are tied to it).</summary>
        public string CurrentAccountId => _current != null ? _current.id : null;


        // ---------------------------------------------------------------- storage

        void SaveAccounts() => JsonFile.Write(AccountsPath, _accounts);

        void SaveProfile()
        {
            if (_current != null && _profile != null) JsonFile.Write(ProfilePath(_current.id), _profile);
        }
    }
}
