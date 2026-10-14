using System.Threading.Tasks;
using VMS.Backend;
#if VMS_GPGS && UNITY_ANDROID && !UNITY_EDITOR
using GooglePlayGames;
using GooglePlayGames.BasicApi;
#endif

namespace VMS.Auth
{
    /// <summary>
    /// Google Play Games Services v2 sign-in (play-games-plugin-for-unity v11+).
    /// VMS_GPGS is defined automatically by the editor tooling once the plugin is imported.
    /// The server exchanges the one-time server auth code for the verified Play Games player id.
    /// </summary>
    public static class GooglePlayGamesBridge
    {
#if VMS_GPGS && UNITY_ANDROID && !UNITY_EDITOR
        static bool _activated;

        public static bool IsAvailable => true;

        public static Task<GooglePlayCredential> SignInAsync(bool interactive)
        {
            if (!_activated)
            {
                PlayGamesPlatform.Activate();
                _activated = true;
            }
            var tcs = new TaskCompletionSource<GooglePlayCredential>();
            System.Action<SignInStatus> onSignIn = status =>
            {
                if (status != SignInStatus.Success)
                {
                    tcs.TrySetException(new BackendException(status == SignInStatus.Canceled ? "cancelled" : "gpgs_failed", status.ToString()));
                    return;
                }
                PlayGamesPlatform.Instance.RequestServerSideAccess(false, code =>
                {
                    if (string.IsNullOrEmpty(code))
                    {
                        tcs.TrySetException(new BackendException("gpgs_failed", "No server auth code. Check the web client id in the Play Games setup."));
                        return;
                    }
                    tcs.TrySetResult(new GooglePlayCredential
                    {
                        serverAuthCode = code,
                        playerId = PlayGamesPlatform.Instance.GetUserId(),
                        displayName = PlayGamesPlatform.Instance.GetUserDisplayName(),
                    });
                });
            };
            if (interactive) PlayGamesPlatform.Instance.ManuallyAuthenticate(onSignIn);
            else PlayGamesPlatform.Instance.Authenticate(onSignIn);
            return tcs.Task;
        }
#else
        public static bool IsAvailable => false;

        public static Task<GooglePlayCredential> SignInAsync(bool interactive)
        {
            var tcs = new TaskCompletionSource<GooglePlayCredential>();
            tcs.SetException(new BackendException("gpgs_unavailable", "Google Play Games is only available in Android builds with the plugin installed."));
            return tcs.Task;
        }
#endif
    }
}
