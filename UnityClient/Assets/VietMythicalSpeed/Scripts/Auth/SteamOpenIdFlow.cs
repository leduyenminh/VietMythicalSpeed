using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VMS.Backend;

namespace VMS.Auth
{
    /// <summary>
    /// "Sign in through Steam" in the system browser (Steam OpenID 2.0, verified by the server).
    /// Used on Android, where the Steamworks SDK does not exist, and on PC when Steam is not running.
    ///
    /// 1. The app creates a PKCE verifier and opens {server}/v1/auth/steam/openid/start in the browser.
    /// 2. The server bounces to steamcommunity.com, verifies the assertion and redirects back with a one-time code
    ///    – to vietmythicalspeed://auth/steam on mobile, or to a loopback port on desktop/editor.
    /// 3. The app exchanges code + verifier for a session.
    /// </summary>
    public static class SteamOpenIdFlow
    {
        public struct Result
        {
            public string code;
            public string verifier;
        }

        static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

        public static async Task<Result> RunAsync(IBackend backend, string scheme, CancellationToken cancel)
        {
            if (!backend.SupportsSteamOpenId)
                throw new BackendException("needs_server", "Steam sign-in through the browser needs the online server.");

            string verifier = Pkce.RandomToken(32);
            string challenge = Pkce.Challenge(verifier);
            string state = Pkce.RandomToken(12);
            string callbackUrl;

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            string redirect = scheme + "://auth/steam";
            var waiter = new DeepLinkWaiter(redirect);
            Application.OpenURL(backend.SteamOpenIdStartUrl(redirect, challenge, state));
            callbackUrl = await waiter.WaitAsync(Timeout, cancel);
#else
            using (var receiver = new LoopbackReceiver())
            {
                Application.OpenURL(backend.SteamOpenIdStartUrl(receiver.RedirectUri, challenge, state));
                callbackUrl = await receiver.WaitAsync(Timeout, cancel);
            }
#endif
            string error = QueryString.Get(callbackUrl, "error");
            if (!string.IsNullOrEmpty(error)) throw new BackendException(error, "Steam sign-in failed: " + error);
            if (QueryString.Get(callbackUrl, "state") != state) throw new BackendException("state_mismatch", "Sign-in response did not match the request.");
            string code = QueryString.Get(callbackUrl, "code");
            if (string.IsNullOrEmpty(code)) throw new BackendException("steam_failed", "No sign-in code returned.");
            return new Result { code = code, verifier = verifier };
        }

        static async Task<string> WithTimeout(Task<string> task, TimeSpan timeout, CancellationToken cancel)
        {
            var delay = Task.Delay(timeout, cancel);
            var done = await Task.WhenAny(task, delay);
            if (done != task)
                throw new BackendException(cancel.IsCancellationRequested ? "cancelled" : "timeout", "Sign-in was not completed.");
            return await task;
        }

        /// <summary>Waits for the custom-scheme deep link registered in the Android manifest.</summary>
        sealed class DeepLinkWaiter
        {
            readonly string _prefix;
            readonly TaskCompletionSource<string> _tcs = new TaskCompletionSource<string>();

            public DeepLinkWaiter(string prefix)
            {
                _prefix = prefix;
                Application.deepLinkActivated += OnDeepLink;
            }

            void OnDeepLink(string url)
            {
                if (!url.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase)) return;
                Application.deepLinkActivated -= OnDeepLink;
                _tcs.TrySetResult(url);
            }

            public async Task<string> WaitAsync(TimeSpan timeout, CancellationToken cancel)
            {
                try
                {
                    return await WithTimeout(_tcs.Task, timeout, cancel);
                }
                finally
                {
                    Application.deepLinkActivated -= OnDeepLink;
                }
            }
        }

        /// <summary>
        /// One-shot HTTP listener on 127.0.0.1 (RFC 8252 loopback redirect) for the editor and PC builds.
        /// Implemented on TcpListener because HttpListener support differs between Mono and IL2CPP.
        /// </summary>
        sealed class LoopbackReceiver : IDisposable
        {
            const string Path = "/steam-callback";
            readonly TcpListener _listener;
            public readonly string RedirectUri;

            public LoopbackReceiver()
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                RedirectUri = "http://127.0.0.1:" + port + Path;
            }

            public Task<string> WaitAsync(TimeSpan timeout, CancellationToken cancel) =>
                WithTimeout(Task.Run(AcceptCallback), timeout, cancel);

            async Task<string> AcceptCallback()
            {
                while (true)
                {
                    using (var client = await _listener.AcceptTcpClientAsync())
                    using (var stream = client.GetStream())
                    {
                        var reader = new StreamReader(stream, Encoding.ASCII);
                        string requestLine = await reader.ReadLineAsync();
                        string header;
                        while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync())) { }

                        string[] parts = requestLine != null ? requestLine.Split(' ') : new string[0];
                        string target = parts.Length >= 2 ? parts[1] : "/";
                        bool isCallback = target.StartsWith(Path, StringComparison.Ordinal);
                        byte[] body = Encoding.UTF8.GetBytes(isCallback ? Page : "Not found");
                        string head = "HTTP/1.1 " + (isCallback ? "200 OK" : "404 Not Found") +
                                      "\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length +
                                      "\r\nConnection: close\r\n\r\n";
                        byte[] headBytes = Encoding.ASCII.GetBytes(head);
                        await stream.WriteAsync(headBytes, 0, headBytes.Length);
                        await stream.WriteAsync(body, 0, body.Length);
                        if (isCallback) return "http://127.0.0.1" + target;
                    }
                }
            }

            const string Page =
                "<!doctype html><meta charset=utf-8><title>Viet Mythical Speed</title>" +
                "<body style=\"font-family:sans-serif;text-align:center;padding:4em;background:#eef6ff;color:#23324d\">" +
                "<h2>Đăng nhập thành công · Signed in</h2><p>Bạn có thể quay lại trò chơi. You can return to the game.</p>";

            public void Dispose()
            {
                _listener.Stop();
            }
        }
    }
}
