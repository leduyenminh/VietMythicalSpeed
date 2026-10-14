using System.Text.RegularExpressions;

namespace VMS.Core
{
    /// <summary>Account field rules. server/src/auth/validation.ts enforces the same rules.</summary>
    public static class AccountRules
    {
        static readonly Regex UsernamePattern = new Regex("^[A-Za-z0-9_.]{3,20}$");
        static readonly Regex EmailPattern = new Regex("^[^\\s@]+@[^\\s@]+\\.[^\\s@]{2,}$");

        public const int MinPasswordLength = 8;
        public const int MaxPasswordLength = 128;

        public static bool IsValidUsername(string username) => username != null && UsernamePattern.IsMatch(username);

        public static bool IsValidEmail(string email) => email != null && email.Length <= 254 && EmailPattern.IsMatch(email);

        public static bool IsValidPassword(string password) =>
            password != null && password.Length >= MinPasswordLength && password.Length <= MaxPasswordLength;

        /// <summary>Returns an error code (matching the server's) or null when the registration is acceptable.</summary>
        public static string ValidateRegistration(string username, string email, string password)
        {
            if (!IsValidUsername(username)) return "invalid_username";
            if (!IsValidEmail(email)) return "invalid_email";
            if (!IsValidPassword(password)) return "weak_password";
            return null;
        }
    }
}
