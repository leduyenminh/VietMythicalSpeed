using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VMS.Core;

namespace VMS.Backend
{
    /// <summary>
    /// Everything the client needs from "the server". <see cref="LocalBackend"/> implements it on-device for
    /// offline play and development; <see cref="RemoteBackend"/> talks to server/ over HTTPS.
    /// </summary>
    public interface IBackend
    {
        bool IsOnline { get; }
        /// <summary>Identifies the backend a stored session belongs to.</summary>
        string Key { get; }
        bool SupportsSteamOpenId { get; }

        /// <summary>False while an online backend can't reach its server; play continues from the cached profile.</summary>
        bool IsConnected { get; }
        /// <summary>Races finished offline that the server hasn't rewarded yet.</summary>
        int PendingRaceCount { get; }
        event Action ConnectionChanged;
        /// <summary>Uploads what was played offline and refreshes the profile; null when there is nothing to sync.</summary>
        Task<SyncResult> SyncAsync();
        /// <summary>Permanently deletes the signed-in account. Password accounts must confirm their password.</summary>
        Task DeleteAccountAsync(string password);

        Task<AuthResult> RegisterAsync(string username, string email, string password);
        Task<AuthResult> LoginPasswordAsync(string login, string password);
        Task<AuthResult> LoginGuestAsync(string deviceId, string deviceSecret);
        Task<AuthResult> LoginGooglePlayAsync(GooglePlayCredential credential);
        Task<AuthResult> LoginSteamTicketAsync(SteamCredential credential);
        Task<AuthResult> LoginSteamOpenIdAsync(string code, string codeVerifier);
        Task<AuthResult> ResumeAsync(StoredSession session);
        Task LogoutAsync();

        Task<AccountInfo> LinkGooglePlayAsync(GooglePlayCredential credential);
        Task<AccountInfo> LinkSteamTicketAsync(SteamCredential credential);
        Task<AccountInfo> LinkSteamOpenIdAsync(string code, string codeVerifier);
        string SteamOpenIdStartUrl(string redirectUri, string codeChallenge, string state);

        Task<PlayerProfile> GetProfileAsync();
        Task<PlayerProfile> SelectNavigatorAsync(string navigatorId);
        Task<PlayerProfile> AwakenAsync(string navigatorId);
        Task<List<BannerDef>> GetBannersAsync();
        Task<PullResponse> PullAsync(string bannerId, int count);
        Task<PullResponse> SparkAsync(string bannerId, string navigatorId);
        Task<RaceCompleteResponse> CompleteRaceAsync(RaceReport report);
        Task<DailyClaimResponse> ClaimDailyAsync();

        // ---- shop and cosmetics
        /// <summary>The live catalog (server copy online, the game's own data offline) and which stores the server supports.</summary>
        Task<ShopResponse> GetShopAsync();
        Task<ShopBuyResponse> BuyItemAsync(string itemId);
        Task<PlayerProfile> EquipAsync(string category, string target, string itemId);
        Task<ExchangeResponse> ExchangeGoldAsync(int amount);

        // ---- real-money purchases (the server verifies every one with the store)
        Task<bool> CanBuyProductAsync(string productId);
        Task<PurchaseResponse> VerifyGooglePurchaseAsync(string productId, string purchaseToken);
        Task<string> StartSteamPurchaseAsync(string productId, string language);
        Task<PurchaseResponse> FinishSteamPurchaseAsync(string orderId, bool authorized);
        /// <summary>Development only (server PAYMENTS_FAKE=1, or offline development builds).</summary>
        Task<PurchaseResponse> FakePurchaseAsync(string productId);

        // ---- events and mail
        Task<EventsResponse> GetEventsAsync();
        Task<RewardsResponse> ClaimEventAsync(string eventId, int index);
        Task<MailList> GetMailAsync();
        Task MarkMailReadAsync(long id);
        Task<MailClaimResponse> ClaimMailAsync(long id, bool all);
        Task DeleteMailAsync(long id);

        // ---- password reset by email (online only)
        Task RequestPasswordResetAsync(string email, string language);
        Task<AuthResult> ResetPasswordAsync(string email, string code, string newPassword);
    }

    [Serializable]
    public class StoresInfo
    {
        public bool google;
        public bool steam;
        public bool fake;
    }

    [Serializable]
    public class ShopResponse
    {
        public ShopCatalog catalog;
        public StoresInfo stores = new StoresInfo();
    }

    [Serializable]
    public class ShopBuyResponse
    {
        public PlayerProfile profile;
        public string itemId;
        public List<Reward> rewards = new List<Reward>();
    }

    [Serializable]
    public class ExchangeResponse
    {
        public PlayerProfile profile;
        public int jade;
    }

    [Serializable]
    public class ProductGrantInfo
    {
        public int gold;
        public int bonus;
        public bool firstPurchase;
        public List<Reward> rewards = new List<Reward>();
        public int passJade;
    }

    [Serializable]
    public class PurchaseResponse
    {
        public PlayerProfile profile;
        public string productId;
        public ProductGrantInfo grant = new ProductGrantInfo();
        /// <summary>The store re-delivered a purchase the server had already granted (nothing new was added).</summary>
        public bool alreadyGranted;
    }

    [Serializable]
    public class EventsResponse
    {
        public List<EventDef> events = new List<EventDef>();
        public int claimGraceDays = 3;
    }

    [Serializable]
    public class RewardsResponse
    {
        public PlayerProfile profile;
        public List<Reward> rewards = new List<Reward>();
    }

    [Serializable]
    public class MailList
    {
        public List<MailItem> mail = new List<MailItem>();
        public int unread;
        public int unclaimed;
    }

    [Serializable]
    public class CloudSaveInfo
    {
        public bool exists;
        public string slot;
        public int revision;
        public long updatedAt;
        public string device;
        public string summary;
        public string data;
    }

    [Serializable]
    public class MailClaimResponse
    {
        public PlayerProfile profile;
        public List<Reward> rewards = new List<Reward>();
        public long[] claimed = new long[0];
    }

    public class BackendException : Exception
    {
        public readonly string Code;
        public readonly int Status;

        public BackendException(string code, string message, int status = 0) : base(message)
        {
            Code = code;
            Status = status;
        }

        /// <summary>The server couldn't be reached (no network, timeout, or a proxy reporting the server down).</summary>
        public bool IsConnectivity => Code == "network" || Status == 502 || Status == 503 || Status == 504;

        /// <summary>Localised message for the error code, falling back to the server's message.</summary>
        public string Friendly
        {
            get
            {
                string key = "err." + Code;
                string text = Loc.T(key);
                return text == key ? (string.IsNullOrEmpty(Message) ? Code : Message) : text;
            }
        }
    }

    /// <summary>HTTP 426 from the server: this build is older than MIN_CLIENT_VERSION.</summary>
    public sealed class ClientOutdatedException : BackendException
    {
        public readonly string MinVersion;
        /// <summary>Store page for this platform, when the server has one configured.</summary>
        public readonly string UpdateUrl;

        public ClientOutdatedException(string message, string minVersion, string updateUrl) : base("client_outdated", message, 426)
        {
            MinVersion = minVersion;
            UpdateUrl = updateUrl;
        }
    }

    [Serializable]
    public class AccountInfo
    {
        public string id;
        public string username;
        public string email;
        public string displayName;
        public string[] providers;

        public bool HasProvider(string provider) => providers != null && Array.IndexOf(providers, provider) >= 0;
    }

    [Serializable]
    public class AuthResult
    {
        public string accessToken;
        public string refreshToken;
        public int expiresIn;
        public AccountInfo account;
        public PlayerProfile profile;
    }

    [Serializable]
    public class GooglePlayCredential
    {
        public string serverAuthCode;
        public string playerId;
        public string displayName;
    }

    [Serializable]
    public class SteamCredential
    {
        public string ticket;
        public string steamId;
        public string personaName;
    }

    [Serializable]
    public class StoredSession
    {
        public string backendKey;
        public string accountId;
        public string refreshToken;
        public string provider;
    }

    [Serializable]
    public class PullResponse
    {
        public PullResult[] results;
        public PlayerProfile profile;
    }

    [Serializable]
    public class RaceCompleteResponse
    {
        public RaceReward reward;
        public PlayerProfile profile;
        /// <summary>Saved offline; the reward arrives when the race is synced.</summary>
        public bool pending;
        /// <summary>The server had already rewarded this race id (a retry).</summary>
        public bool duplicate;
    }

    /// <summary>Outcome of <see cref="IBackend.SyncAsync"/>.</summary>
    public class SyncResult
    {
        public int applied;
        public int coins;
        public int jade;
        public int bondExp;
        public PlayerProfile profile;
    }

    [Serializable]
    public class DailyClaimResponse
    {
        public DailyClaimResult result;
        public PlayerProfile profile;
    }

    public static class Providers
    {
        public const string Password = "password";
        public const string Guest = "device";
        public const string Google = "google";
        public const string Steam = "steam";
    }
}
