import { randomUUID } from 'node:crypto';
import type { IncomingMessage, ServerResponse } from 'node:http';
import type { Config } from './config.ts';
import type { Store } from './db.ts';
import { createMailer } from './auth/mailer.ts';
import type { Mailer } from './auth/mailer.ts';
import { hashPassword, randomToken, safeEqual, sha256Base64Url, sha256Hex, signJwt, verifyDummy, verifyJwt, verifyPassword } from './auth/crypto.ts';
import type { FetchFn, VerifiedIdentity } from './auth/providers.ts';
import { steamOpenIdRedirect, steamPersona, verifyGooglePlayGames, verifySteamOpenId, verifySteamTicket } from './auth/providers.ts';
import { isAllowedRedirect, isValidEmail, validateRegistration } from './auth/validation.ts';
import type { GameData } from './game/data.ts';
import { dayKey, grantRaceReward, newProfile, normalizeProfile } from './game/economy.ts';
import { claimDaily, eventOnRace } from './game/shop.ts';
import { awaken, isBannerActive, pullForProfile, applyPulls } from './game/gacha.ts';
import { Xoshiro128 } from './game/random.ts';
import { GameError } from './game/types.ts';
import type { PlayerProfile, PullResult, RaceReport, RaceReward } from './game/types.ts';
import { headerValue, int, num, RateLimiter, redirect, Router, str } from './http.ts';
import { registerLiveOps, templateMail } from './liveops.ts';
import { meetsMinimum } from './version.ts';

export interface AppDeps {
  config: Config;
  store: Store;
  data: GameData;
  fetch?: FetchFn;
  now?: () => Date;
  /** Password-reset email sender; defaults to EMAIL_PROVIDER. Tests pass a capturing mailer. */
  mailer?: Mailer | null;
}

const PROVIDER_PASSWORD = 'password';
const PROVIDER_DEVICE = 'device';
const PROVIDER_GOOGLE = 'google';
const PROVIDER_STEAM = 'steam';

export function createApp(deps: AppDeps): (req: IncomingMessage, res: ServerResponse) => void {
  const { config, store, data } = deps;
  const fetchImpl = deps.fetch ?? fetch;
  const now = deps.now ?? (() => new Date());
  const authLimiter = new RateLimiter(20, 60_000);
  const gameLimiter = new RateLimiter(240, 60_000);

  const router = new Router({
    // Tokens of deleted accounts stop working immediately, not when they expire.
    authenticate: (token) => {
      const sub = verifyJwt(token, config.jwtSecret, Math.floor(now().getTime() / 1000))?.sub;
      return sub && store.accountExists(sub) ? sub : null;
    },
    trustProxy: config.trustProxy,
    requireHttps: config.requireHttps,
    checkClient,
  });

  /** Old builds may not match the current rules or data; they are asked to update before using the API. */
  function checkClient(req: IncomingMessage): void {
    if (!config.minClientVersion || meetsMinimum(headerValue(req, 'x-client-version'), config.minClientVersion)) return;
    const platform = headerValue(req, 'x-client-platform').toLowerCase();
    throw new GameError('client_outdated', 'This version of the game is too old. Please update it.', 426, {
      minVersion: config.minClientVersion,
      updateUrl: config.updateUrls[platform] ?? '',
    });
  }

  // ---------------------------------------------------------------- sessions

  function accountInfo(accountId: string) {
    const a = store.accountById(accountId);
    if (!a) throw new GameError('unauthorized', 'Account not found.', 401);
    return { id: a.id, username: a.username ?? '', email: a.email ?? '', displayName: a.display_name, providers: store.providersFor(a.id) };
  }

  function profileFor(accountId: string): PlayerProfile {
    const existing = store.loadProfile(accountId);
    if (existing) return normalizeProfile(existing);
    const a = store.accountById(accountId);
    const created = newProfile(data.economy, accountId, a?.display_name ?? 'Envoy');
    store.saveProfile(created, Date.now());
    return created;
  }

  function issueSession(accountId: string, provider: string) {
    const issuedAt = Math.floor(now().getTime() / 1000);
    const accessToken = signJwt({ sub: accountId, iat: issuedAt, exp: issuedAt + config.accessTokenTtlSeconds }, config.jwtSecret);
    const refreshToken = randomToken(32);
    store.storeRefreshToken(sha256Hex(refreshToken), accountId, provider, now().getTime() + config.refreshTokenTtlDays * 86_400_000);
    return { accessToken, refreshToken, expiresIn: config.accessTokenTtlSeconds, account: accountInfo(accountId), profile: profileFor(accountId) };
  }

  function cleanName(name: string | undefined, fallback: string): string {
    const trimmed = (name ?? '').replace(/[\u0000-\u001f]/g, '').trim().slice(0, 24);
    return trimmed || fallback;
  }

  /** Finds the account behind an external identity or creates one (account + identity + profile atomically). */
  function accountForIdentity(provider: string, identity: VerifiedIdentity, secretHash: string | null): string {
    const existing = store.identity(provider, identity.subject);
    if (existing) return existing.account_id;
    const id = randomUUID();
    const t = now().getTime();
    store.transaction(() => {
      store.createAccount({ id, username: null, email: null, password_hash: null, display_name: cleanName(identity.displayName, 'Envoy'), created_at: t });
      store.addIdentity(provider, identity.subject, id, secretHash, t);
      store.saveProfile(newProfile(data.economy, id, cleanName(identity.displayName, 'Envoy')), t);
      templateMail(store, data, id, 'welcome', t);
    });
    return id;
  }

  function linkIdentity(accountId: string, provider: string, subject: string) {
    const owner = store.identity(provider, subject);
    if (owner && owner.account_id !== accountId) throw new GameError('identity_in_use', 'Already linked to another account.', 409);
    if (!owner) store.addIdentity(provider, subject, accountId, null, Date.now());
    return { account: accountInfo(accountId) };
  }

  // ---------------------------------------------------------------- auth routes

  router.add('POST', '/v1/auth/register', async ({ body }) => {
    const username = typeof body.username === 'string' ? body.username.trim() : body.username;
    const email = typeof body.email === 'string' ? body.email.trim().toLowerCase() : body.email;
    const problem = validateRegistration(username, email, body.password);
    if (problem) throw new GameError(problem, 'Invalid registration.', 400);
    const u = username as string;
    const e = email as string;
    if (store.usernameTaken(u)) throw new GameError('username_taken', 'Username already exists.', 409);
    if (store.emailTaken(e)) throw new GameError('email_taken', 'Email already registered.', 409);
    const hash = await hashPassword(body.password as string);
    const id = randomUUID();
    const t = now().getTime();
    try {
      store.transaction(() => {
        store.createAccount({ id, username: u, email: e, password_hash: hash, display_name: u, created_at: t });
        store.addIdentity(PROVIDER_PASSWORD, id, id, null, t);
        store.saveProfile(newProfile(data.economy, id, u), t);
        templateMail(store, data, id, 'welcome', t);
      });
    } catch (err) {
      // A concurrent registration won the race for the same username/email.
      if (String(err).includes('UNIQUE')) throw new GameError('username_taken', 'Username or email already exists.', 409);
      throw err;
    }
    return issueSession(id, PROVIDER_PASSWORD);
  }, { limiter: authLimiter });

  router.add('POST', '/v1/auth/login', async ({ body }) => {
    const login = str(body, 'login', 254).trim();
    const password = str(body, 'password', 256);
    const account = store.accountByLogin(isValidEmail(login) ? login.toLowerCase() : login);
    if (!account || !account.password_hash) {
      await verifyDummy(password);
      throw new GameError('invalid_credentials', 'Wrong username/email or password.', 401);
    }
    if (!(await verifyPassword(password, account.password_hash))) throw new GameError('invalid_credentials', 'Wrong username/email or password.', 401);
    return issueSession(account.id, PROVIDER_PASSWORD);
  }, { limiter: authLimiter });

  router.add('POST', '/v1/auth/guest', ({ body }) => {
    const deviceId = str(body, 'deviceId', 64);
    const secret = str(body, 'deviceSecret', 128);
    if (secret.length < 32) throw new GameError('invalid_request', 'Device secret too short.', 400);
    const secretHash = sha256Hex(secret);
    const existing = store.identity(PROVIDER_DEVICE, deviceId);
    if (existing) {
      if (!existing.secret_hash || !safeEqual(existing.secret_hash, secretHash)) throw new GameError('invalid_credentials', 'Device credentials do not match.', 401);
      return issueSession(existing.account_id, PROVIDER_DEVICE);
    }
    const accountId = accountForIdentity(PROVIDER_DEVICE, { subject: deviceId, displayName: 'Envoy' }, secretHash);
    return issueSession(accountId, PROVIDER_DEVICE);
  }, { limiter: authLimiter });

  router.add('POST', '/v1/auth/google', async ({ body }) => {
    const identity = await verifyGooglePlayGames(config, str(body, 'serverAuthCode', 2048), fetchImpl);
    return issueSession(accountForIdentity(PROVIDER_GOOGLE, identity, null), PROVIDER_GOOGLE);
  }, { limiter: authLimiter });

  router.add('POST', '/v1/auth/steam/ticket', async ({ body }) => {
    const identity = await verifySteamTicket(config, str(body, 'ticket', 8192), fetchImpl);
    return issueSession(accountForIdentity(PROVIDER_STEAM, identity, null), PROVIDER_STEAM);
  }, { limiter: authLimiter });

  router.add('GET', '/v1/auth/steam/openid/start', ({ url, res }) => {
    const redirectUri = url.searchParams.get('redirect_uri') ?? '';
    const challenge = url.searchParams.get('code_challenge') ?? '';
    const clientState = url.searchParams.get('state') ?? '';
    if (!isAllowedRedirect(redirectUri, config.appScheme)) throw new GameError('invalid_request', 'redirect_uri not allowed.', 400);
    if (!/^[A-Za-z0-9_-]{43}$/.test(challenge)) throw new GameError('invalid_request', 'code_challenge must be a base64url SHA-256.', 400);
    if (!/^[A-Za-z0-9_-]{8,64}$/.test(clientState)) throw new GameError('invalid_request', 'state is required.', 400);
    const sid = randomToken(18);
    store.saveOpenIdState(sid, redirectUri, clientState, challenge, Date.now() + 10 * 60_000);
    redirect(res, steamOpenIdRedirect(config, sid));
  }, { limiter: authLimiter, gameClient: false });

  router.add('GET', '/v1/auth/steam/openid/callback', async ({ url, res }) => {
    const pending = store.takeOpenIdState(url.searchParams.get('sid') ?? '');
    if (!pending || pending.expires_at < Date.now()) throw new GameError('invalid_request', 'Sign-in session expired. Start again from the game.', 400);
    const back = new URL(pending.redirect_uri);
    back.searchParams.set('state', pending.client_state);
    try {
      const steamId = await verifySteamOpenId(config, url.searchParams, fetchImpl);
      const code = randomToken(32);
      store.saveOpenIdCode(sha256Hex(code), steamId, pending.challenge, Date.now() + 2 * 60_000);
      back.searchParams.set('code', code);
    } catch (e) {
      back.searchParams.set('error', e instanceof GameError ? e.code : 'openid_failed');
    }
    redirect(res, back.toString());
  }, { limiter: authLimiter, gameClient: false });

  async function exchangeOpenIdCode(body: Record<string, unknown>): Promise<VerifiedIdentity> {
    const code = str(body, 'code', 128);
    const verifier = str(body, 'codeVerifier', 128);
    const row = store.consumeOpenIdCode(sha256Hex(code), Date.now());
    if (!row || !safeEqual(sha256Base64Url(verifier), row.challenge)) throw new GameError('code_invalid', 'Sign-in code invalid or expired.', 401);
    return { subject: row.steam_id, displayName: await steamPersona(config, row.steam_id, fetchImpl) };
  }

  router.add('POST', '/v1/auth/steam/openid/exchange', async ({ body }) => {
    const identity = await exchangeOpenIdCode(body);
    return issueSession(accountForIdentity(PROVIDER_STEAM, identity, null), PROVIDER_STEAM);
  }, { limiter: authLimiter });

  router.add('POST', '/v1/auth/refresh', ({ body }) => {
    const token = str(body, 'refreshToken', 128);
    const hash = sha256Hex(token);
    const row = store.refreshToken(hash);
    if (!row) throw new GameError('unauthorized', 'Unknown session.', 401);
    if (row.revoked) {
      // A rotated token was replayed: assume it leaked and end every session of the account.
      store.revokeAllRefreshTokens(row.account_id);
      throw new GameError('unauthorized', 'Session revoked.', 401);
    }
    if (row.expires_at < now().getTime()) throw new GameError('unauthorized', 'Session expired.', 401);
    store.revokeRefreshToken(hash);
    return issueSession(row.account_id, row.provider);
  }, { limiter: authLimiter });

  router.add('POST', '/v1/auth/logout', ({ body }) => {
    if (typeof body.refreshToken === 'string') store.revokeRefreshToken(sha256Hex(body.refreshToken));
    return { ok: true };
  }, { limiter: authLimiter });

  router.add('POST', '/v1/account/link/google', async ({ body, accountId }) => {
    const identity = await verifyGooglePlayGames(config, str(body, 'serverAuthCode', 2048), fetchImpl);
    return linkIdentity(accountId, PROVIDER_GOOGLE, identity.subject);
  }, { auth: true, limiter: authLimiter });

  router.add('POST', '/v1/account/link/steam', async ({ body, accountId }) => {
    const identity = typeof body.ticket === 'string' ? await verifySteamTicket(config, body.ticket, fetchImpl) : await exchangeOpenIdCode(body);
    return linkIdentity(accountId, PROVIDER_STEAM, identity.subject);
  }, { auth: true, limiter: authLimiter });

  // Store policy (Google Play): players who can create an account must be able to delete it from the app.
  router.add('POST', '/v1/account/delete', async ({ body, accountId }) => {
    if (body.confirm !== true) throw new GameError('invalid_request', '"confirm": true is required.', 400);
    const account = store.accountById(accountId);
    if (!account) throw new GameError('unauthorized', 'Account not found.', 401);
    if (account.password_hash) {
      const password = typeof body.password === 'string' ? body.password : '';
      if (!password || !(await verifyPassword(password, account.password_hash))) throw new GameError('invalid_password', 'Wrong password.', 403);
    }
    store.deleteAccount(accountId);
    console.log(`[account] deleted ${accountId}`);
    return { ok: true };
  }, { auth: true, limiter: authLimiter });

  // ---------------------------------------------------------------- game routes

  /** Load → mutate → save without awaiting in between, so concurrent requests can't interleave. */
  function withProfile<T>(accountId: string, mutate: (p: PlayerProfile) => T): { result: T; profile: PlayerProfile } {
    const profile = profileFor(accountId);
    const result = mutate(profile);
    store.saveProfile(profile, Date.now());
    return { result, profile };
  }

  router.add('GET', '/v1/profile', ({ accountId }) => ({ profile: profileFor(accountId) }), { auth: true, limiter: gameLimiter });

  router.add('POST', '/v1/profile/navigator', ({ body, accountId }) => {
    const navigatorId = str(body, 'navigatorId', 64);
    const { profile } = withProfile(accountId, (p) => {
      if (!p.navigators.some((n) => n.id === navigatorId)) throw new GameError('not_owned', 'Navigator not owned.');
      p.selectedNavigator = navigatorId;
    });
    return { profile };
  }, { auth: true, limiter: gameLimiter });

  router.add('POST', '/v1/navigators/awaken', ({ body, accountId }) => {
    const navigatorId = str(body, 'navigatorId', 64);
    const { profile } = withProfile(accountId, (p) => awaken(p, navigatorId, data.gacha));
    return { profile };
  }, { auth: true, limiter: gameLimiter });

  router.add('GET', '/v1/gacha/banners', () => {
    const t = now();
    const banners = data.banners.filter((b) => isBannerActive(b, t)).sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0));
    return { banners };
  }, { limiter: gameLimiter });

  router.add('POST', '/v1/gacha/pull', ({ body, accountId }) => {
    const bannerId = str(body, 'bannerId', 64);
    const count = int(body, 'count', 1, 10);
    const { rng, seed } = Xoshiro128.randomlySeeded();
    const { result, profile } = withProfile(accountId, (p) => pullForProfile(data.engine, p, data.banner(bannerId), count, rng, now()));
    store.recordPulls(accountId, bannerId, result, seed, Date.now());
    return { results: result, profile };
  }, { auth: true, limiter: gameLimiter });

  router.add('POST', '/v1/gacha/spark', ({ body, accountId }) => {
    const bannerId = str(body, 'bannerId', 64);
    const navigatorId = str(body, 'navigatorId', 64);
    const banner = data.banner(bannerId);
    if (!banner) throw new GameError('banner_not_found', 'Unknown banner.', 404);
    if (!isBannerActive(banner, now())) throw new GameError('banner_inactive', 'This banner is not running.');
    const { result, profile } = withProfile(accountId, (p) => {
      const r: PullResult = data.engine.spark(banner, p.gacha, navigatorId);
      applyPulls(p, [r], data.gacha);
      return r;
    });
    store.recordPulls(accountId, bannerId, [result], null, Date.now());
    return { results: [result], profile };
  }, { auth: true, limiter: gameLimiter });

  function parseReport(r: unknown): RaceReport {
    if (!r || typeof r !== 'object') throw new GameError('invalid_request', '"report" is required.', 400);
    const o = r as Record<string, unknown>;
    const report: RaceReport = {
      songId: str(o, 'songId', 128),
      durationSec: num(o, 'durationSec', 0, 3600),
      mode: int(o, 'mode', 0, 3),
      difficulty: int(o, 'difficulty', 0, 2),
      score: int(o, 'score', 0, 2_000_000_000),
      maxCombo: int(o, 'maxCombo', 0, 100_000),
      perfect: int(o, 'perfect', 0, 100_000),
      good: int(o, 'good', 0, 100_000),
      miss: int(o, 'miss', 0, 100_000),
      accuracy: num(o, 'accuracy', 0, 1),
      navigatorId: typeof o.navigatorId === 'string' ? o.navigatorId : '',
    };
    if (report.mode === 3) throw new GameError('invalid_request', 'Zen races are not rewarded.', 400);
    return report;
  }

  /** Optional client-generated race id; makes retries and offline re-syncs idempotent. */
  function raceId(value: unknown): string | null {
    if (value === undefined || value === null || value === '') return null;
    if (typeof value !== 'string' || !/^[A-Za-z0-9_-]{8,64}$/.test(value)) throw new GameError('invalid_request', '"id" is invalid.', 400);
    return value;
  }

  const noReward = (): RaceReward => ({ coins: 0, jade: 0, bondExp: 0, bondLevelBefore: 0, bondLevelAfter: 0 });

  /**
   * Grants a race reward against the caps of the UTC day it was played. Today's counters live in the profile;
   * every day's counters are also kept in race_days so a race synced later can't reuse a day's cap.
   */
  function grantForDay(accountId: string, p: PlayerProfile, report: RaceReport, playedAt: Date): RaceReward {
    eventOnRace(p, data.events, report, playedAt, data.economy.race.minDurationSec);
    const nav = data.navigator(report.navigatorId);
    const t = now();
    const day = dayKey(playedAt);
    if (day === dayKey(t)) {
      const reward = grantRaceReward(p, data.economy, report, nav, t);
      store.saveRaceDay(accountId, day, p.daily.coinsEarned, p.daily.racesRewarded);
      return reward;
    }
    const today = p.daily;
    const ledger = store.raceDay(accountId, day) ?? { coins: 0, races: 0 };
    p.daily = { ...today, day, coinsEarned: ledger.coins, racesRewarded: ledger.races };
    try {
      const reward = grantRaceReward(p, data.economy, report, nav, playedAt);
      store.saveRaceDay(accountId, day, p.daily.coinsEarned, p.daily.racesRewarded);
      return reward;
    } finally {
      p.daily = today;
    }
  }

  router.add('POST', '/v1/race/complete', ({ body, accountId }) => {
    const report = parseReport(body.report);
    const id = raceId(body.id);
    const { result, profile } = store.transaction(() =>
      withProfile(accountId, (p) => {
        if (id && !store.claimSubmission(accountId, id, now().getTime())) return null;
        return grantForDay(accountId, p, report, now());
      }),
    );
    return result ? { reward: result, profile } : { reward: noReward(), profile, duplicate: true };
  }, { auth: true, limiter: gameLimiter });

  /** Races finished while the game had no connection, uploaded when it reconnects. */
  router.add('POST', '/v1/race/sync', ({ body, accountId }) => {
    const races = body.races;
    if (!Array.isArray(races) || races.length === 0 || races.length > 50) throw new GameError('invalid_request', '"races" must hold 1–50 entries.', 400);
    const t = now().getTime();
    const window = config.offlineSyncMaxAgeHours * 3_600_000;
    const created = store.accountById(accountId)?.created_at ?? 0;
    const items = races.map((raw: unknown) => {
      const o = (raw && typeof raw === 'object' ? raw : {}) as Record<string, unknown>;
      const id = typeof o.id === 'string' ? o.id : '';
      try {
        if (!raceId(id)) throw new GameError('invalid_request', '"id" is required.', 400);
        const playedAt = typeof o.finishedAt === 'string' ? new Date(o.finishedAt) : new Date(NaN);
        if (Number.isNaN(playedAt.getTime()) || playedAt.getTime() > t + 5 * 60_000) throw new GameError('invalid_request', '"finishedAt" is invalid.', 400);
        return { id, playedAt: new Date(Math.min(playedAt.getTime(), t)), report: parseReport(o.report), error: '' };
      } catch (e) {
        return { id, playedAt: new Date(0), report: null, error: e instanceof GameError ? e.code : 'invalid_request' };
      }
    });
    items.sort((a, b) => a.playedAt.getTime() - b.playedAt.getTime());

    const total = { applied: 0, coins: 0, jade: 0, bondExp: 0 };
    const { result: results, profile } = store.transaction(() =>
      withProfile(accountId, (p) => {
        const out: { id: string; status: string; reward?: RaceReward }[] = [];
        for (const item of items) {
          if (!item.report) {
            out.push({ id: item.id, status: item.error });
          } else if (item.playedAt.getTime() < t - window || item.playedAt.getTime() < created) {
            out.push({ id: item.id, status: 'expired' });
          } else if (!store.claimSubmission(accountId, item.id, t)) {
            out.push({ id: item.id, status: 'duplicate' });
          } else {
            const reward = grantForDay(accountId, p, item.report, item.playedAt);
            total.applied++;
            total.coins += reward.coins;
            total.jade += reward.jade;
            total.bondExp += reward.bondExp;
            out.push({ id: item.id, status: 'applied', reward });
          }
        }
        store.pruneRaceLedger(accountId, dayKey(new Date(t - window - 86_400_000)), t - 2 * window);
        return out;
      }),
    );
    return { results, total, profile };
  }, { auth: true, limiter: gameLimiter });

  router.add('POST', '/v1/daily/claim', ({ accountId }) => {
    const { result, profile } = withProfile(accountId, (p) => claimDaily(p, data.economy, data.shop, data.events, now()));
    return { result, profile };
  }, { auth: true, limiter: gameLimiter });

  registerLiveOps({
    router,
    config,
    store,
    data,
    fetchImpl,
    now,
    withProfile,
    profileFor,
    issueSession,
    authLimiter,
    gameLimiter,
    mailer: deps.mailer === undefined ? createMailer(config, fetchImpl) : deps.mailer,
  });

  router.add('GET', '/health', () => ({ ok: true, time: now().toISOString(), minClientVersion: config.minClientVersion }), { limiter: gameLimiter, gameClient: false });

  return (req, res) => {
    void router.handle(req, res);
  };
}
