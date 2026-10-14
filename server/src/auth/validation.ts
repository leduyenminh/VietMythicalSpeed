// Same rules as VMS.Core.AccountRules (client) – keep in sync.

const USERNAME = /^[A-Za-z0-9_.]{3,20}$/;
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

export const MIN_PASSWORD = 8;
export const MAX_PASSWORD = 128;

export function isValidUsername(v: unknown): v is string {
  return typeof v === 'string' && USERNAME.test(v);
}

export function isValidEmail(v: unknown): v is string {
  return typeof v === 'string' && v.length <= 254 && EMAIL.test(v);
}

export function isValidPassword(v: unknown): v is string {
  return typeof v === 'string' && v.length >= MIN_PASSWORD && v.length <= MAX_PASSWORD;
}

/** Error code or null, matching AccountRules.ValidateRegistration. */
export function validateRegistration(username: unknown, email: unknown, password: unknown): string | null {
  if (!isValidUsername(username)) return 'invalid_username';
  if (!isValidEmail(email)) return 'invalid_email';
  if (!isValidPassword(password)) return 'weak_password';
  return null;
}

/** Native-app redirect targets allowed for the Steam browser sign-in (custom scheme or loopback, RFC 8252). */
export function isAllowedRedirect(uri: string, scheme: string): boolean {
  if (uri === `${scheme}://auth/steam`) return true;
  try {
    const u = new URL(uri);
    return u.protocol === 'http:' && (u.hostname === '127.0.0.1' || u.hostname === 'localhost') && u.port !== '' && u.pathname === '/steam-callback';
  } catch {
    return false;
  }
}
