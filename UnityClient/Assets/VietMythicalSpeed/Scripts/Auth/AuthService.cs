using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VMS.Backend;

namespace VMS.Auth
{
    /// <summary>Sign-in orchestration across providers; owns session persistence.</summary>
    public sealed class AuthService
    {
        readonly IBackend _backend;
        readonly BackendConfig _config;

        public AccountInfo Account { get; private set; }
        public string Provider { get; private set; }
        public bool IsSignedIn => Account != null;

        public event Action<AuthResult> SignedIn;
        public event Action SignedOut;

        public AuthService(IBackend backend, BackendConfig config)
        {
            _backend = backend;
            _config = config;
            var remote = backend as RemoteBackend;
            if (remote != null) remote.Api.TokensChanged += OnTokensChanged;
        }

        /// <summary>Password accounts confirm their password before deletion.</summary>
        public bool HasPassword => Account != null && Account.HasProvider(Providers.Password);

        public bool GooglePlayAvailable => GooglePlayGamesBridge.IsAvailable;
        public bool SteamAvailable => SteamworksBridge.IsAvailable || _backend.SupportsSteamOpenId;

        public async Task<bool> TryResumeAsync()
        {
            var session = SessionStore.Load();
            if (session == null || session.backendKey != _backend.Key) return false;
            try
            {
                var result = await _backend.ResumeAsync(session);
                Complete(result, session.provider);
                return true;
            }
            catch (BackendException e)
            {
                Debug.Log("[Auth] Could not resume session: " + e.Code + " " + e.Message);
                if (e.Code != "network") SessionStore.Clear();
                return false;
            }
        }

        public Task SignInGuestAsync() =>
            Run(Providers.Guest, () => _backend.LoginGuestAsync(DeviceIdentity.Id, DeviceIdentity.Secret));

        public Task RegisterAsync(string username, string email, string password) =>
            Run(Providers.Password, () => _backend.RegisterAsync(username.Trim(), email.Trim(), password));

        public Task SignInPasswordAsync(string login, string password) =>
            Run(Providers.Password, () => _backend.LoginPasswordAsync(login.Trim(), password));

        public async Task SignInGooglePlayAsync()
        {
            var credential = await GooglePlayGamesBridge.SignInAsync(true);
            await Run(Providers.Google, () => _backend.LoginGooglePlayAsync(credential));
        }

        /// <summary>Uses the Steam client ticket when Steam is running, otherwise the browser OpenID flow.</summary>
        public async Task SignInSteamAsync(CancellationToken cancel)
        {
            if (SteamworksBridge.IsAvailable)
            {
                var credential = await SteamworksBridge.GetWebApiTicketAsync();
                await Run(Providers.Steam, () => _backend.LoginSteamTicketAsync(credential));
                return;
            }
            var openId = await SteamOpenIdFlow.RunAsync(_backend, _config.deepLinkScheme, cancel);
            await Run(Providers.Steam, () => _backend.LoginSteamOpenIdAsync(openId.code, openId.verifier));
        }

        public async Task LinkGooglePlayAsync()
        {
            var credential = await GooglePlayGamesBridge.SignInAsync(true);
            Account = await _backend.LinkGooglePlayAsync(credential);
        }

        public async Task LinkSteamAsync(CancellationToken cancel)
        {
            if (SteamworksBridge.IsAvailable)
            {
                Account = await _backend.LinkSteamTicketAsync(await SteamworksBridge.GetWebApiTicketAsync());
                return;
            }
            var openId = await SteamOpenIdFlow.RunAsync(_backend, _config.deepLinkScheme, cancel);
            Account = await _backend.LinkSteamOpenIdAsync(openId.code, openId.verifier);
        }

        public async Task SignOutAsync()
        {
            await _backend.LogoutAsync();
            SessionStore.Clear();
            Account = null;
            Provider = null;
            SignedOut?.Invoke();
        }

        /// <summary>Sets a new password with the emailed code and signs in (old sessions are ended by the server).</summary>
        public Task ResetPasswordAsync(string email, string code, string newPassword) =>
            Run(Providers.Password, () => _backend.ResetPasswordAsync(email.Trim(), code.Trim(), newPassword));

        /// <summary>Deletes the account for good (server data, or the offline save) and returns to the login screen.</summary>
        public async Task DeleteAccountAsync(string password)
        {
            await _backend.DeleteAccountAsync(password);
            SessionStore.Clear();
            Account = null;
            Provider = null;
            SignedOut?.Invoke();
        }

        async Task Run(string provider, Func<Task<AuthResult>> call)
        {
            var result = await call();
            Complete(result, provider);
        }

        void Complete(AuthResult result, string provider)
        {
            Account = result.account;
            Provider = provider;
            SessionStore.Save(new StoredSession
            {
                backendKey = _backend.Key,
                accountId = result.account != null ? result.account.id : null,
                refreshToken = result.refreshToken,
                provider = provider,
            });
            SignedIn?.Invoke(result);
        }

        void OnTokensChanged(AuthResult result)
        {
            // Refresh tokens rotate on every use; persist the newest one immediately.
            var session = SessionStore.Load();
            if (session == null || string.IsNullOrEmpty(result.refreshToken)) return;
            session.refreshToken = result.refreshToken;
            SessionStore.Save(session);
        }
    }
}
