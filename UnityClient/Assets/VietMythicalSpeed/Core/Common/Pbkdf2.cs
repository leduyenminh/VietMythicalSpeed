using System;
using System.Security.Cryptography;
using System.Text;

namespace VMS.Core
{
    /// <summary>
    /// PBKDF2-HMAC-SHA256 (RFC 8018). Implemented directly because the HashAlgorithmName overload of
    /// Rfc2898DeriveBytes is not available on every Unity API compatibility level.
    /// Used for offline-mode accounts only; online passwords are hashed by the server.
    /// </summary>
    public static class Pbkdf2
    {
        public const int DefaultIterations = 60000;

        public static byte[] DeriveSha256(byte[] password, byte[] salt, int iterations, int length)
        {
            if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations));
            var output = new byte[length];
            using (var hmac = new HMACSHA256(password))
            {
                int blocks = (length + 31) / 32;
                var saltBlock = new byte[salt.Length + 4];
                Buffer.BlockCopy(salt, 0, saltBlock, 0, salt.Length);
                for (int block = 1; block <= blocks; block++)
                {
                    saltBlock[salt.Length] = (byte)(block >> 24);
                    saltBlock[salt.Length + 1] = (byte)(block >> 16);
                    saltBlock[salt.Length + 2] = (byte)(block >> 8);
                    saltBlock[salt.Length + 3] = (byte)block;
                    byte[] u = hmac.ComputeHash(saltBlock);
                    var t = (byte[])u.Clone();
                    for (int i = 1; i < iterations; i++)
                    {
                        u = hmac.ComputeHash(u);
                        for (int k = 0; k < t.Length; k++) t[k] ^= u[k];
                    }
                    int offset = (block - 1) * 32;
                    Buffer.BlockCopy(t, 0, output, offset, Math.Min(32, length - offset));
                }
            }
            return output;
        }

        public static string Hash(string password, out string saltBase64, int iterations = DefaultIterations)
        {
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            saltBase64 = Convert.ToBase64String(salt);
            return Convert.ToBase64String(DeriveSha256(Encoding.UTF8.GetBytes(password), salt, iterations, 32));
        }

        public static bool Verify(string password, string saltBase64, string hashBase64, int iterations)
        {
            byte[] expected, salt;
            try
            {
                expected = Convert.FromBase64String(hashBase64);
                salt = Convert.FromBase64String(saltBase64);
            }
            catch (FormatException)
            {
                return false;
            }
            var actual = DeriveSha256(Encoding.UTF8.GetBytes(password), salt, iterations, expected.Length);
            int diff = 0;
            for (int i = 0; i < expected.Length; i++) diff |= expected[i] ^ actual[i];
            return diff == 0;
        }
    }
}
