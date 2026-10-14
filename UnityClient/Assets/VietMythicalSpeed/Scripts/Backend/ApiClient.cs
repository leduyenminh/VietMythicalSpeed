using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace VMS.Backend
{
    public static class UnityWebRequestAsync
    {
        /// <summary>Awaitable SendWebRequest; continuations run on Unity's main thread.</summary>
        public static Task<UnityWebRequest> SendAsync(this UnityWebRequest request)
        {
            var tcs = new TaskCompletionSource<UnityWebRequest>();
            var op = request.SendWebRequest();
            if (op.isDone) tcs.TrySetResult(request);
            else op.completed += _ => tcs.TrySetResult(request);
            return tcs.Task;
        }
    }

    [Serializable]
    class ErrorEnvelope
    {
        public ErrorBody error;
    }

    [Serializable]
    class ErrorBody
    {
        public string code;
        public string message;
        public string minVersion;
        public string updateUrl;
    }

    [Serializable]
    class RefreshRequest
    {
        public string refreshToken;
    }

    /// <summary>JSON-over-HTTPS client with bearer auth and one transparent refresh on 401.</summary>
    public sealed class ApiClient
    {
        public readonly string BaseUrl;
        public string AccessToken;
        public string RefreshToken;
        public int TimeoutSeconds = 20;

        /// <summary>Raised whenever the token pair changes so the session can be persisted.</summary>
        public event Action<AuthResult> TokensChanged;
        /// <summary>Raised when the server refuses this game version (before the call throws).</summary>
        public event Action<ClientOutdatedException> ClientOutdated;

        Task<AuthResult> _refreshInFlight;

        public ApiClient(string baseUrl)
        {
            BaseUrl = baseUrl.TrimEnd('/');
        }

        public Task<T> Get<T>(string path, bool auth = true) => Send<T>("GET", path, null, auth, true);

        public Task<T> Post<T>(string path, object body, bool auth = true) => Send<T>("POST", path, body, auth, true);

        async Task<T> Send<T>(string method, string path, object body, bool auth, bool allowRefresh)
        {
            using (var request = Build(method, path, body, auth))
            {
                await request.SendAsync();

                if (request.responseCode == 401 && auth && allowRefresh && !string.IsNullOrEmpty(RefreshToken))
                {
                    await RefreshAsync();
                    return await Send<T>(method, path, body, auth, false);
                }
                if (request.result == UnityWebRequest.Result.ConnectionError)
                    throw new BackendException("network", request.error);
                if (request.responseCode >= 400 || request.result == UnityWebRequest.Result.ProtocolError)
                {
                    var error = ParseError(request);
                    if (error is ClientOutdatedException outdated) ClientOutdated?.Invoke(outdated);
                    throw error;
                }

                string text = request.downloadHandler.text;
                if (string.IsNullOrEmpty(text)) return default(T);
                try
                {
                    return JsonUtility.FromJson<T>(text);
                }
                catch (Exception e)
                {
                    throw new BackendException("bad_response", e.Message, (int)request.responseCode);
                }
            }
        }

        public Task<AuthResult> RefreshAsync()
        {
            // Concurrent 401s share a single refresh so the rotating refresh token is only spent once.
            if (_refreshInFlight != null) return _refreshInFlight;
            _refreshInFlight = DoRefresh();
            return _refreshInFlight;
        }

        async Task<AuthResult> DoRefresh()
        {
            try
            {
                var result = await Send<AuthResult>("POST", "/v1/auth/refresh", new RefreshRequest { refreshToken = RefreshToken }, false, false);
                Accept(result);
                return result;
            }
            finally
            {
                _refreshInFlight = null;
            }
        }

        public void Accept(AuthResult result)
        {
            if (result == null) return;
            if (!string.IsNullOrEmpty(result.accessToken)) AccessToken = result.accessToken;
            if (!string.IsNullOrEmpty(result.refreshToken)) RefreshToken = result.refreshToken;
            TokensChanged?.Invoke(result);
        }

        public void Clear()
        {
            AccessToken = null;
            RefreshToken = null;
        }

        /// <summary>Release builds only talk HTTPS (Unity also blocks plain HTTP there); dev builds may use a local server.</summary>
        public static bool IsAllowedUrl(string url) =>
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && (Application.isEditor || Debug.isDebugBuild));

        static string Platform
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.Android: return "android";
                    case RuntimePlatform.IPhonePlayer: return "ios";
                    case RuntimePlatform.WindowsPlayer: return "windows";
                    case RuntimePlatform.OSXPlayer: return "macos";
                    case RuntimePlatform.LinuxPlayer: return "linux";
                    default: return Application.isEditor ? "editor" : "other";
                }
            }
        }

        UnityWebRequest Build(string method, string path, object body, bool auth)
        {
            if (!IsAllowedUrl(BaseUrl)) throw new BackendException("insecure_url", "The server URL must use https://.");
            var request = new UnityWebRequest(BaseUrl + path, method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = TimeoutSeconds,
            };
            if (body != null)
            {
                var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));
                request.uploadHandler = new UploadHandlerRaw(bytes) { contentType = "application/json" };
                request.SetRequestHeader("Content-Type", "application/json");
            }
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("X-Client-Version", Application.version);
            request.SetRequestHeader("X-Client-Platform", Platform);
            if (auth && !string.IsNullOrEmpty(AccessToken)) request.SetRequestHeader("Authorization", "Bearer " + AccessToken);
            return request;
        }

        static BackendException ParseError(UnityWebRequest request)
        {
            int status = (int)request.responseCode;
            string text = request.downloadHandler != null ? request.downloadHandler.text : null;
            if (!string.IsNullOrEmpty(text))
            {
                try
                {
                    var env = JsonUtility.FromJson<ErrorEnvelope>(text);
                    if (env != null && env.error != null && env.error.code == "client_outdated")
                        return new ClientOutdatedException(env.error.message, env.error.minVersion, env.error.updateUrl);
                    if (env != null && env.error != null && !string.IsNullOrEmpty(env.error.code))
                        return new BackendException(env.error.code, env.error.message, status);
                }
                catch (ArgumentException)
                {
                    // Not JSON (proxy error page etc.) – fall through.
                }
            }
            string code = status == 401 ? "unauthorized" : status >= 500 ? "server" : "request_failed";
            return new BackendException(code, request.error, status);
        }
    }
}
