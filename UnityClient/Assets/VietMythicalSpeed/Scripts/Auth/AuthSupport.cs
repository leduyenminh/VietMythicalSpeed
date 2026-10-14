using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using VMS.Backend;

namespace VMS.Auth
{
    /// <summary>
    /// Persists the signed-in session. Only the refresh token is stored (access tokens are short-lived).
    /// PlayerPrefs is plain storage; see docs/Backend.md for moving this to Android Keystore-backed storage.
    /// </summary>
    public static class SessionStore
    {
        const string Key = "vms.session";

        public static StoredSession Load()
        {
            string json = PlayerPrefs.GetString(Key, string.Empty);
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

        public static void Save(StoredSession session)
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(session));
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }

    /// <summary>Random per-install credentials used for guest accounts.</summary>
    public static class DeviceIdentity
    {
        const string IdKey = "vms.device.id";
        const string SecretKey = "vms.device.secret";

        public static string Id
        {
            get
            {
                Ensure();
                return PlayerPrefs.GetString(IdKey);
            }
        }

        public static string Secret
        {
            get
            {
                Ensure();
                return PlayerPrefs.GetString(SecretKey);
            }
        }

        static void Ensure()
        {
            if (PlayerPrefs.HasKey(IdKey) && PlayerPrefs.HasKey(SecretKey)) return;
            PlayerPrefs.SetString(IdKey, Guid.NewGuid().ToString("N"));
            PlayerPrefs.SetString(SecretKey, Pkce.RandomToken(32));
            PlayerPrefs.Save();
        }
    }

    /// <summary>RFC 7636 helpers; protects the Steam browser sign-in from intercepted redirects.</summary>
    public static class Pkce
    {
        public static string RandomToken(int bytes)
        {
            var data = new byte[bytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(data);
            return Base64Url(data);
        }

        public static string Challenge(string verifier)
        {
            using (var sha = SHA256.Create())
                return Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        }

        public static string Base64Url(byte[] data) =>
            Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static class QueryString
    {
        /// <summary>Reads one parameter from a URL's query string (null when absent).</summary>
        public static string Get(string url, string name)
        {
            int q = url.IndexOf('?');
            if (q < 0) return null;
            string query = url.Substring(q + 1);
            int hash = query.IndexOf('#');
            if (hash >= 0) query = query.Substring(0, hash);
            foreach (var part in query.Split('&'))
            {
                int eq = part.IndexOf('=');
                string key = eq >= 0 ? part.Substring(0, eq) : part;
                if (Uri.UnescapeDataString(key) != name) continue;
                return eq >= 0 ? Uri.UnescapeDataString(part.Substring(eq + 1).Replace('+', ' ')) : string.Empty;
            }
            return null;
        }
    }
}
