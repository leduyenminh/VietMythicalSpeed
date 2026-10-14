using System.Threading.Tasks;
using UnityEngine;
using VMS.Backend;
#if VMS_STEAMWORKS && (UNITY_STANDALONE || UNITY_EDITOR)
using Steamworks;
#endif

namespace VMS.Auth
{
    /// <summary>
    /// Steam sign-in for PC builds via Steamworks.NET (GetAuthTicketForWebApi; the server verifies it with
    /// ISteamUserAuth/AuthenticateUserTicket). Android builds use <see cref="SteamOpenIdFlow"/> instead.
    /// </summary>
    public static class SteamworksBridge
    {
        public const string WebApiIdentity = "vms-backend";

#if VMS_STEAMWORKS && (UNITY_STANDALONE || UNITY_EDITOR)
        static bool _initialised;
        static bool _initAttempted;
        static Callback<GetTicketForWebApiResponse_t> _ticketCallback;
        static Callback<MicroTxnAuthorizationResponse_t> _microTxnCallback;
        static TaskCompletionSource<SteamCredential> _pending;

        /// <summary>The player approved (true) or declined an in-game purchase in the Steam overlay: (orderId, authorized).</summary>
        public static event System.Action<ulong, bool> MicroTxnAuthorization;

        public static bool IsAvailable
        {
            get
            {
                TryInit();
                return _initialised;
            }
        }

        static void TryInit()
        {
            if (_initAttempted) return;
            _initAttempted = true;
            try
            {
                if (!Packsize.Test() || !DllCheck.Test())
                {
                    Debug.LogWarning("[Steam] Steamworks binaries mismatch; Steam sign-in disabled.");
                    return;
                }
                _initialised = SteamAPI.Init();
                if (!_initialised) Debug.Log("[Steam] SteamAPI.Init failed (is Steam running / steam_appid.txt present?).");
                else _microTxnCallback = Callback<MicroTxnAuthorizationResponse_t>.Create(r => MicroTxnAuthorization?.Invoke(r.m_ulOrderID, r.m_bAuthorized != 0));
            }
            catch (System.DllNotFoundException e)
            {
                Debug.LogWarning("[Steam] steam_api library missing: " + e.Message);
            }
        }

        public static void RunCallbacks()
        {
            if (_initialised) SteamAPI.RunCallbacks();
        }

        public static void Shutdown()
        {
            if (_initialised) SteamAPI.Shutdown();
            _initialised = false;
        }

        public static Task<SteamCredential> GetWebApiTicketAsync()
        {
            TryInit();
            if (!_initialised)
                return Task.FromException<SteamCredential>(new BackendException("steam_unavailable", "Steam is not running."));
            if (_ticketCallback == null) _ticketCallback = Callback<GetTicketForWebApiResponse_t>.Create(OnTicket);
            _pending = new TaskCompletionSource<SteamCredential>();
            SteamUser.GetAuthTicketForWebApi(WebApiIdentity);
            return _pending.Task;
        }

        static void OnTicket(GetTicketForWebApiResponse_t response)
        {
            var pending = _pending;
            _pending = null;
            if (pending == null) return;
            if (response.m_eResult != EResult.k_EResultOK)
            {
                pending.TrySetException(new BackendException("steam_failed", response.m_eResult.ToString()));
                return;
            }
            var hex = new System.Text.StringBuilder(response.m_cubTicket * 2);
            for (int i = 0; i < response.m_cubTicket; i++) hex.Append(response.m_rgubTicket[i].ToString("X2"));
            pending.TrySetResult(new SteamCredential
            {
                ticket = hex.ToString(),
                steamId = SteamUser.GetSteamID().m_SteamID.ToString(),
                personaName = SteamFriends.GetPersonaName(),
            });
        }
#else
        public static bool IsAvailable => false;

        public static event System.Action<ulong, bool> MicroTxnAuthorization
        {
            add { }
            remove { }
        }

        public static void RunCallbacks() { }

        public static void Shutdown() { }

        public static Task<SteamCredential> GetWebApiTicketAsync()
        {
            var tcs = new TaskCompletionSource<SteamCredential>();
            tcs.SetException(new BackendException("steam_unavailable", "Steamworks.NET is not installed in this build."));
            return tcs.Task;
        }
#endif
    }
}
