using System;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;
using VMS.Auth;
using VMS.Core;

namespace VMS.Backend
{
    /// <summary>
    /// Cloud backups of offline-mode progress. The player signs in to an online account on the cloud server
    /// (backend.json "cloudSaveUrl"); the offline save is stored there as an opaque backup that can be restored on
    /// any device. It is never merged into that account's online profile: offline progress can be edited on the
    /// device, so the online economy (and real-money purchases) never trusts it.
    /// </summary>
    public sealed class CloudSave
    {
        public const string Slot = "offline";
        const string SessionKey = "vms.cloud.session";
        const string AutoKey = "vms.cloud.auto";

        readonly RemoteBackend _remote;
        readonly LocalBackend _local;
        float _lastAutoBackup = -999f;

        public CloudSave(string url, string cacheDirectory, LocalBackend local)
        {
            _remote = new RemoteBackend(url, cacheDirectory);
            _local = local;
            _remote.Api.TokensChanged += r =>
            {
                var session = LoadSession();
                if (session == null || string.IsNullOrEmpty(r.refreshToken)) return;
                session.refreshToken = r.refreshToken;
                SaveSession(session);
            };
        }

        public AccountInfo Account { get; private set; }
        public bool SignedIn => Account != null;
        public bool SupportsGoogle => GooglePlayGamesBridge.IsAvailable;
        public bool SupportsSteam => SteamworksBridge.IsAvailable;

        public bool AutoBackup
        {
            get => PlayerPrefs.GetInt(AutoKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(AutoKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        static StoredSession LoadSession()
        {
            string json = PlayerPrefs.GetString(SessionKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonUtility.FromJson<StoredSession>(json);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        static void SaveSession(StoredSession s)
        {
            PlayerPrefs.SetString(SessionKey, JsonUtility.ToJson(s));
            PlayerPrefs.Save();
        }

        void Signed(AuthResult r, string provider)
        {
            Account = r.account;
            SaveSession(new StoredSession { backendKey = _remote.Key, accountId = r.account != null ? r.account.id : null, refreshToken = r.refreshToken, provider = provider });
        }

        public async Task<bool> ResumeAsync()
        {
            var session = LoadSession();
            if (session == null || session.backendKey != _remote.Key) return false;
            try
            {
                var r = await _remote.ResumeAsync(session);
                Account = r.account;
                return true;
            }
            catch (BackendException e)
            {
                if (e.Code == "unauthorized") PlayerPrefs.DeleteKey(SessionKey);
                return false;
            }
        }

        public async Task SignInAsync(string login, string password) => Signed(await _remote.LoginPasswordAsync(login.Trim(), password), Providers.Password);

        public async Task RegisterAsync(string username, string email, string password) =>
            Signed(await _remote.RegisterAsync(username.Trim(), email.Trim(), password), Providers.Password);

        public async Task SignInGoogleAsync() => Signed(await _remote.LoginGooglePlayAsync(await GooglePlayGamesBridge.SignInAsync(true)), Providers.Google);

        public async Task SignInSteamAsync() => Signed(await _remote.LoginSteamTicketAsync(await SteamworksBridge.GetWebApiTicketAsync()), Providers.Steam);

        public async Task SignOutAsync()
        {
            await _remote.LogoutAsync();
            PlayerPrefs.DeleteKey(SessionKey);
            PlayerPrefs.Save();
            Account = null;
        }

        string RevisionKey => "vms.cloud.rev." + _local.CurrentAccountId + "." + (Account != null ? Account.id : "-");

        int BaseRevision
        {
            get => PlayerPrefs.GetInt(RevisionKey, 0);
            set
            {
                PlayerPrefs.SetInt(RevisionKey, value);
                PlayerPrefs.Save();
            }
        }

        public Task<CloudSaveInfo> GetAsync() => _remote.GetCloudSaveAsync(Slot);

        /// <summary>Uploads the offline save. A "save_conflict" error means another device backed up since.</summary>
        public async Task<CloudSaveInfo> BackupAsync(bool force)
        {
            if (!SignedIn) throw new BackendException("unauthorized", "Not signed in to the cloud.");
            var profile = await _local.GetProfileAsync();
            var saved = await _remote.PutCloudSaveAsync(Slot, _local.ExportSave(), Summary(profile), BaseRevision, force);
            BaseRevision = saved.revision;
            return saved;
        }

        /// <summary>Replaces this device's offline progress with the cloud backup.</summary>
        public async Task<PlayerProfile> RestoreAsync()
        {
            var save = await GetAsync();
            if (!save.exists) throw new BackendException("save_missing", "There is no cloud backup yet.");
            var profile = _local.ImportSave(save.data);
            BaseRevision = save.revision;
            return profile;
        }

        /// <summary>After races and purchases: back up quietly, at most every few minutes.</summary>
        public async void AutoBackupAsync()
        {
            if (!SignedIn || !AutoBackup || Time.realtimeSinceStartup - _lastAutoBackup < 180f) return;
            _lastAutoBackup = Time.realtimeSinceStartup;
            try
            {
                await BackupAsync(false);
            }
            catch (BackendException e)
            {
                Debug.Log("[CloudSave] Automatic backup skipped: " + e.Code);
            }
        }

        /// <summary>"navigators|coins|jade|gold" – shown before a restore, so the player knows what they get.</summary>
        static string Summary(PlayerProfile p) =>
            string.Join("|", p.navigators.Count.ToString(CultureInfo.InvariantCulture), p.coins.ToString(CultureInfo.InvariantCulture),
                p.jade.ToString(CultureInfo.InvariantCulture), p.TotalGold.ToString(CultureInfo.InvariantCulture));

        public static string DescribeSummary(string summary)
        {
            var parts = (summary ?? string.Empty).Split('|');
            if (parts.Length < 3) return string.Empty;
            return Loc.F("cloud.summary", parts[0], FormatNumber(parts[1]), FormatNumber(parts[2]));
        }

        static string FormatNumber(string s) => long.TryParse(s, out var n) ? n.ToString("N0") : s;
    }
}
