import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseVersion } from './version.ts';

export interface Config {
  port: number;
  publicUrl: string;
  trustProxy: boolean;
  /** PEM certificate chain + private key; when both are set the server speaks HTTPS itself. */
  tlsCertFile: string;
  tlsKeyFile: string;
  /** Plain-HTTP port that redirects to PUBLIC_URL and serves ACME challenges (0 = off). */
  httpRedirectPort: number;
  /** Directory certbot --webroot writes /.well-known/acme-challenge files into. */
  acmeWebroot: string;
  /** Reject API calls that did not arrive over HTTPS (directly, or via a trusted proxy's X-Forwarded-Proto). */
  requireHttps: boolean;
  databasePath: string;
  gameDataDir: string;
  jwtSecret: string;
  accessTokenTtlSeconds: number;
  refreshTokenTtlDays: number;
  /** Oldest game version allowed to call the API ("" = no check). */
  minClientVersion: string;
  /** Store pages shown by the "update required" dialog, by X-Client-Platform. */
  updateUrls: Record<string, string>;
  /** How far back races played offline may be dated when they are synced. */
  offlineSyncMaxAgeHours: number;
  googleClientId: string;
  googleClientSecret: string;
  steamAppId: string;
  steamPublisherKey: string;
  steamWebApiKey: string;
  /** URL schemes / loopback hosts the Steam browser sign-in may redirect back to. */
  appScheme: string;
  /** Secret for /v1/admin/* (mail, account lookup); empty disables the admin API. */
  adminToken: string;
  /** Development only: /v1/purchases/fake grants products without a store. Refused in production. */
  paymentsFake: boolean;
  /** Android package name as registered in Play Console. */
  googlePlayPackage: string;
  /** Service account JSON key with access to the Play Developer API (purchase verification, refunds). */
  googleServiceAccountFile: string;
  /** How often refunded Google Play purchases are checked and taken back (minutes, 0 = never). */
  googleVoidedPollMinutes: number;
  steamMicroTxnSandbox: boolean;
  /** Password-reset email. provider: console | resend | postmark | sendgrid | "" (off). */
  email: { provider: string; from: string; apiKey: string };
}

const serverRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

export function loadConfig(env: NodeJS.ProcessEnv = process.env): Config {
  const production = env.NODE_ENV === 'production';
  const tls = !!(env.TLS_CERT_FILE && env.TLS_KEY_FILE);
  const port = Number(env.PORT ?? 8787);
  const config: Config = {
    port,
    publicUrl: (env.PUBLIC_URL ?? `${tls ? 'https' : 'http'}://localhost:${port}`).replace(/\/$/, ''),
    trustProxy: env.TRUST_PROXY === '1',
    tlsCertFile: env.TLS_CERT_FILE ?? '',
    tlsKeyFile: env.TLS_KEY_FILE ?? '',
    httpRedirectPort: Number(env.HTTP_REDIRECT_PORT ?? 0),
    acmeWebroot: env.ACME_WEBROOT ?? '',
    requireHttps: env.REQUIRE_HTTPS ? env.REQUIRE_HTTPS === '1' : production,
    databasePath: env.DATABASE_PATH || path.join(serverRoot, 'data', 'vms.sqlite'),
    gameDataDir: env.GAME_DATA_DIR || path.resolve(serverRoot, '..', 'UnityClient', 'Assets', 'VietMythicalSpeed', 'Resources', 'Data'),
    jwtSecret: env.JWT_SECRET ?? '',
    accessTokenTtlSeconds: Number(env.ACCESS_TOKEN_TTL_SECONDS ?? 3600),
    refreshTokenTtlDays: Number(env.REFRESH_TOKEN_TTL_DAYS ?? 60),
    minClientVersion: (env.MIN_CLIENT_VERSION ?? '').trim(),
    updateUrls: {
      android: env.UPDATE_URL_ANDROID ?? '',
      windows: env.UPDATE_URL_WINDOWS ?? '',
    },
    offlineSyncMaxAgeHours: Number(env.OFFLINE_SYNC_MAX_AGE_HOURS ?? 72),
    googleClientId: env.GOOGLE_CLIENT_ID ?? '',
    googleClientSecret: env.GOOGLE_CLIENT_SECRET ?? '',
    steamAppId: env.STEAM_APP_ID ?? '',
    steamPublisherKey: env.STEAM_PUBLISHER_KEY ?? '',
    steamWebApiKey: env.STEAM_WEB_API_KEY ?? '',
    appScheme: 'vietmythicalspeed',
    adminToken: env.ADMIN_TOKEN ?? '',
    paymentsFake: env.PAYMENTS_FAKE === '1',
    googlePlayPackage: env.GOOGLE_PLAY_PACKAGE || 'com.vietmythical.speed',
    googleServiceAccountFile: env.GOOGLE_SERVICE_ACCOUNT_FILE ?? '',
    googleVoidedPollMinutes: Number(env.GOOGLE_VOIDED_POLL_MINUTES ?? 360),
    steamMicroTxnSandbox: env.STEAM_MICROTXN_SANDBOX === '1',
    email: {
      provider: (env.EMAIL_PROVIDER || (production ? '' : 'console')).toLowerCase(),
      from: env.EMAIL_FROM ?? '',
      apiKey: env.EMAIL_API_KEY ?? '',
    },
  };
  if (config.jwtSecret.length < 32) {
    if (production) throw new Error('JWT_SECRET must be at least 32 characters in production.');
    config.jwtSecret = config.jwtSecret || 'dev-only-insecure-secret-change-me-0123456789';
    console.warn('[config] Using a development JWT secret. Set JWT_SECRET before deploying.');
  }
  if (!!env.TLS_CERT_FILE !== !!env.TLS_KEY_FILE) throw new Error('Set both TLS_CERT_FILE and TLS_KEY_FILE, or neither.');
  if (config.minClientVersion && !parseVersion(config.minClientVersion)) throw new Error(`MIN_CLIENT_VERSION "${config.minClientVersion}" is not a version like 1.2.3.`);
  if (production && config.paymentsFake) throw new Error('PAYMENTS_FAKE must not be enabled in production.');
  if (production && config.email.provider === 'console') throw new Error('EMAIL_PROVIDER=console would print reset codes to the log; use a real provider in production.');
  if (config.adminToken && config.adminToken.length < 24) throw new Error('ADMIN_TOKEN must be at least 24 characters.');
  if (['resend', 'postmark', 'sendgrid'].includes(config.email.provider) && (!config.email.apiKey || !config.email.from)) {
    throw new Error(`EMAIL_PROVIDER=${config.email.provider} needs EMAIL_API_KEY and EMAIL_FROM.`);
  }
  if (production && !config.publicUrl.startsWith('https://')) throw new Error('PUBLIC_URL must be an https:// URL in production.');
  if (production && !tls && !config.trustProxy) {
    console.warn('[config] Production without TLS_CERT_FILE or TRUST_PROXY=1: plain-HTTP API calls will be rejected (REQUIRE_HTTPS).');
  }
  return config;
}
