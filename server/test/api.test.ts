import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import http from 'node:http';
import type { AddressInfo } from 'node:net';
import path from 'node:path';
import { after, before, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { createApp } from '../src/app.ts';
import { loadConfig } from '../src/config.ts';
import { Store } from '../src/db.ts';
import { loadGameData } from '../src/game/data.ts';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
let server: http.Server;
let base = '';

const STEAM_ID = '76561198000000042';

/** Fake Google / Steam endpoints so the auth flows run end-to-end offline. */
const fakeFetch = (async (input: string | URL | Request, init?: RequestInit) => {
  const url = new URL(typeof input === 'string' ? input : input instanceof URL ? input : input.url);
  const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
  if (url.href === 'https://oauth2.googleapis.com/token') {
    const form = new URLSearchParams(String(init?.body));
    return form.get('code') === 'good-code' ? json({ access_token: 'gpgs-token' }) : json({ error: 'invalid_grant' }, 400);
  }
  if (url.href === 'https://games.googleapis.com/games/v1/players/me') return json({ playerId: 'g1234', displayName: 'Play Tester' });
  if (url.pathname === '/ISteamUserAuth/AuthenticateUserTicket/v1/') {
    return url.searchParams.get('ticket') === 'ABCDEF0123456789'
      ? json({ response: { params: { result: 'OK', steamid: STEAM_ID, ownersteamid: STEAM_ID, vacbanned: false, publisherbanned: false } } })
      : json({ response: { error: { errorcode: 101, errordesc: 'Invalid ticket' } } });
  }
  if (url.pathname === '/ISteamUser/GetPlayerSummaries/v2/') return json({ response: { players: [{ personaname: 'SteamTester' }] } });
  if (url.href === 'https://steamcommunity.com/openid/login') {
    const form = new URLSearchParams(String(init?.body));
    const ok = form.get('openid.mode') === 'check_authentication' && form.get('openid.sig') === 'valid-signature';
    return new Response(`ns:http://specs.openid.net/auth/2.0\nis_valid:${ok}\n`);
  }
  return new Response('not mocked', { status: 500 });
}) as typeof fetch;

before(async () => {
  const config = loadConfig({
    PORT: '0',
    PUBLIC_URL: 'https://api.example.test',
    JWT_SECRET: 'test-secret-test-secret-test-secret-123',
    GOOGLE_CLIENT_ID: 'client',
    GOOGLE_CLIENT_SECRET: 'secret',
    STEAM_APP_ID: '480',
    STEAM_PUBLISHER_KEY: 'publisher',
    GAME_DATA_DIR: path.join(root, 'UnityClient', 'Assets', 'VietMythicalSpeed', 'Resources', 'Data'),
  });
  const app = createApp({
    config,
    store: new Store(':memory:'),
    data: loadGameData(config.gameDataDir),
    fetch: fakeFetch,
    now: () => new Date('2026-10-12T08:00:00Z'),
  });
  server = http.createServer(app);
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
});

after(() => server.close());

async function call(method: string, p: string, body?: unknown, token?: string) {
  const res = await fetch(base + p, {
    method,
    redirect: 'manual',
    headers: { 'content-type': 'application/json', ...(token ? { authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  return { status: res.status, headers: res.headers, json: text ? JSON.parse(text) : null };
}

test('register, login, profile and validation errors', async () => {
  const reg = await call('POST', '/v1/auth/register', { username: 'tocsu_01', email: 'Toc@Example.com', password: 'longenough' });
  assert.equal(reg.status, 200, JSON.stringify(reg.json));
  assert.equal(reg.json.account.email, 'toc@example.com');
  assert.deepEqual(reg.json.account.providers, ['password']);
  assert.equal(reg.json.profile.jade, 3000);
  assert.equal(reg.json.profile.selectedNavigator, 'vang_anh');

  assert.equal((await call('POST', '/v1/auth/register', { username: 'TOCSU_01', email: 'x@example.com', password: 'longenough' })).json.error.code, 'username_taken');
  assert.equal((await call('POST', '/v1/auth/register', { username: 'other', email: 'toc@example.com', password: 'longenough' })).json.error.code, 'email_taken');
  assert.equal((await call('POST', '/v1/auth/register', { username: 'ab', email: 'a@b.cd', password: 'longenough' })).json.error.code, 'invalid_username');
  assert.equal((await call('POST', '/v1/auth/register', { username: 'abc', email: 'a@b.cd', password: 'short' })).json.error.code, 'weak_password');

  const byEmail = await call('POST', '/v1/auth/login', { login: 'TOC@example.com', password: 'longenough' });
  assert.equal(byEmail.status, 200);
  const wrong = await call('POST', '/v1/auth/login', { login: 'tocsu_01', password: 'nope-nope' });
  assert.equal(wrong.status, 401);
  assert.equal(wrong.json.error.code, 'invalid_credentials');

  const profile = await call('GET', '/v1/profile', undefined, byEmail.json.accessToken);
  assert.equal(profile.status, 200);
  assert.equal((await call('GET', '/v1/profile')).status, 401);
  assert.equal((await call('GET', '/v1/profile', undefined, 'garbage.token.value')).status, 401);
});

test('refresh tokens rotate and replay revokes the account sessions', async () => {
  const reg = await call('POST', '/v1/auth/register', { username: 'rotator', email: 'rot@example.com', password: 'longenough' });
  const first = reg.json.refreshToken;
  const second = await call('POST', '/v1/auth/refresh', { refreshToken: first });
  assert.equal(second.status, 200);
  assert.notEqual(second.json.refreshToken, first);
  const replay = await call('POST', '/v1/auth/refresh', { refreshToken: first });
  assert.equal(replay.status, 401);
  // The replay revoked everything, including the newest token.
  assert.equal((await call('POST', '/v1/auth/refresh', { refreshToken: second.json.refreshToken })).status, 401);
});

test('guest device login is stable and secret-checked', async () => {
  const creds = { deviceId: 'device-1', deviceSecret: 'x'.repeat(43) };
  const a = await call('POST', '/v1/auth/guest', creds);
  const b = await call('POST', '/v1/auth/guest', creds);
  assert.equal(a.status, 200);
  assert.equal(a.json.account.id, b.json.account.id);
  const stolen = await call('POST', '/v1/auth/guest', { deviceId: 'device-1', deviceSecret: 'y'.repeat(43) });
  assert.equal(stolen.status, 401);
});

test('Google Play Games and Steam ticket sign-in, then linking', async () => {
  const google = await call('POST', '/v1/auth/google', { serverAuthCode: 'good-code' });
  assert.equal(google.status, 200, JSON.stringify(google.json));
  assert.equal(google.json.account.displayName, 'Play Tester');
  assert.equal((await call('POST', '/v1/auth/google', { serverAuthCode: 'bad-code' })).json.error.code, 'gpgs_failed');

  const steam = await call('POST', '/v1/auth/steam/ticket', { ticket: 'ABCDEF0123456789' });
  assert.equal(steam.status, 200, JSON.stringify(steam.json));
  assert.equal(steam.json.account.displayName, 'SteamTester');
  assert.equal((await call('POST', '/v1/auth/steam/ticket', { ticket: 'FFFFFFFFFFFFFFFF' })).json.error.code, 'steam_failed');

  // A password account can link Google; the Steam identity already belongs to another account.
  const reg = await call('POST', '/v1/auth/register', { username: 'linker', email: 'link@example.com', password: 'longenough' });
  const linkGoogle = await call('POST', '/v1/account/link/google', { serverAuthCode: 'good-code' }, reg.json.accessToken);
  assert.equal(linkGoogle.status, 409, 'google id already used by the account created above');
  const linkSteam = await call('POST', '/v1/account/link/steam', { ticket: 'ABCDEF0123456789' }, reg.json.accessToken);
  assert.equal(linkSteam.json.error.code, 'identity_in_use');
});

test('Steam browser (OpenID + PKCE) round trip', async () => {
  const verifier = 'v'.repeat(43);
  const challenge = createHash('sha256').update(verifier).digest('base64url');
  const redirectUri = 'vietmythicalspeed://auth/steam';

  const bad = await call('GET', `/v1/auth/steam/openid/start?redirect_uri=${encodeURIComponent('https://evil.example/cb')}&code_challenge=${challenge}&state=abcdefgh`);
  assert.equal(bad.status, 400);

  const start = await call('GET', `/v1/auth/steam/openid/start?redirect_uri=${encodeURIComponent(redirectUri)}&code_challenge=${challenge}&state=state123`);
  assert.equal(start.status, 302);
  const steamUrl = new URL(start.headers.get('location')!);
  assert.equal(steamUrl.origin + steamUrl.pathname, 'https://steamcommunity.com/openid/login');
  const returnTo = new URL(steamUrl.searchParams.get('openid.return_to')!);
  assert.equal(returnTo.origin, 'https://api.example.test');

  // Simulate Steam redirecting the browser back with a signed assertion.
  const claimed = `https://steamcommunity.com/openid/id/${STEAM_ID}`;
  const assertion = new URLSearchParams({
    sid: returnTo.searchParams.get('sid')!,
    'openid.ns': 'http://specs.openid.net/auth/2.0',
    'openid.mode': 'id_res',
    'openid.op_endpoint': 'https://steamcommunity.com/openid/login',
    'openid.claimed_id': claimed,
    'openid.identity': claimed,
    'openid.return_to': returnTo.toString(),
    'openid.response_nonce': '2026-10-12T08:00:00Zabc',
    'openid.assoc_handle': '1234567890',
    'openid.signed': 'signed,op_endpoint,claimed_id,identity,return_to,response_nonce,assoc_handle',
    'openid.sig': 'valid-signature',
  });
  const callback = await call('GET', `/v1/auth/steam/openid/callback?${assertion.toString()}`);
  assert.equal(callback.status, 302);
  const back = new URL(callback.headers.get('location')!);
  assert.equal(back.protocol, 'vietmythicalspeed:');
  assert.equal(back.searchParams.get('state'), 'state123');
  const code = back.searchParams.get('code')!;
  assert.ok(code);

  assert.equal((await call('POST', '/v1/auth/steam/openid/exchange', { code, codeVerifier: 'w'.repeat(43) })).json.error.code, 'code_invalid');
  // The failed attempt consumed the code: interception + guessing can't be retried.
  assert.equal((await call('POST', '/v1/auth/steam/openid/exchange', { code, codeVerifier: verifier })).status, 401);

  // Second, clean round trip succeeds and lands on the same Steam account as the ticket login.
  const start2 = await call('GET', `/v1/auth/steam/openid/start?redirect_uri=${encodeURIComponent('http://127.0.0.1:53123/steam-callback')}&code_challenge=${challenge}&state=state456`);
  const returnTo2 = new URL(new URL(start2.headers.get('location')!).searchParams.get('openid.return_to')!);
  assertion.set('sid', returnTo2.searchParams.get('sid')!);
  assertion.set('openid.return_to', returnTo2.toString());
  const back2 = new URL((await call('GET', `/v1/auth/steam/openid/callback?${assertion.toString()}`)).headers.get('location')!);
  const session = await call('POST', '/v1/auth/steam/openid/exchange', { code: back2.searchParams.get('code'), codeVerifier: verifier });
  assert.equal(session.status, 200, JSON.stringify(session.json));
  assert.deepEqual(session.json.account.providers, ['steam']);

  // Forged assertion (Steam says is_valid:false) is rejected with an error redirect.
  const start3 = await call('GET', `/v1/auth/steam/openid/start?redirect_uri=${encodeURIComponent(redirectUri)}&code_challenge=${challenge}&state=state789`);
  const returnTo3 = new URL(new URL(start3.headers.get('location')!).searchParams.get('openid.return_to')!);
  assertion.set('sid', returnTo3.searchParams.get('sid')!);
  assertion.set('openid.return_to', returnTo3.toString());
  assertion.set('openid.sig', 'forged');
  const back3 = new URL((await call('GET', `/v1/auth/steam/openid/callback?${assertion.toString()}`)).headers.get('location')!);
  assert.equal(back3.searchParams.get('error'), 'openid_failed');
  assert.equal(back3.searchParams.get('code'), null);
});

test('gacha, awakening, race rewards and daily login are server-authoritative', async () => {
  const reg = await call('POST', '/v1/auth/register', { username: 'gambler', email: 'g@example.com', password: 'longenough' });
  const token = reg.json.accessToken;

  const banners = await call('GET', '/v1/gacha/banners');
  assert.deepEqual(banners.json.banners.map((b: { id: string }) => b.id), ['limited_son_lam', 'limited_thuy_ha', 'standard']);

  const pull = await call('POST', '/v1/gacha/pull', { bannerId: 'limited_son_lam', count: 10 }, token);
  assert.equal(pull.status, 200, JSON.stringify(pull.json));
  assert.equal(pull.json.results.length, 10);
  assert.ok(pull.json.results.some((r: { rarity: number }) => r.rarity >= 2), 'ten-pull guarantee');
  assert.equal(pull.json.profile.tickets, 0);
  assert.equal(pull.json.profile.jade, 3000);
  assert.equal(pull.json.profile.gacha.spark[0].points, 10);

  assert.equal((await call('POST', '/v1/gacha/pull', { bannerId: 'nope', count: 1 }, token)).json.error.code, 'banner_not_found');
  assert.equal((await call('POST', '/v1/gacha/pull', { bannerId: 'standard', count: 3 }, token)).json.error.code, 'bad_count');
  let last = pull;
  for (let i = 0; i < 2; i++) last = await call('POST', '/v1/gacha/pull', { bannerId: 'standard', count: 10 }, token);
  assert.equal((await call('POST', '/v1/gacha/pull', { bannerId: 'standard', count: 10 }, token)).json.error.code, 'insufficient_jade');

  // Awakening spends duplicate shards + coins when affordable, and refuses otherwise.
  type Owned = { id: string; fragments: number; awakening: number };
  const starter = (last.json.profile.navigators as Owned[]).find((n) => n.id === 'vang_anh')!;
  const awakened = await call('POST', '/v1/navigators/awaken', { navigatorId: 'vang_anh' }, token);
  if (starter.fragments >= 50) {
    assert.equal(awakened.status, 200, JSON.stringify(awakened.json));
    const after = (awakened.json.profile.navigators as Owned[]).find((n) => n.id === 'vang_anh')!;
    assert.equal(after.awakening, 1);
    assert.equal(after.fragments, starter.fragments - 50);
  } else {
    assert.equal(awakened.json.error.code, 'awaken_cost');
  }
  assert.equal((await call('POST', '/v1/navigators/awaken', { navigatorId: 'not_a_navigator' }, token)).json.error.code, 'not_owned');
  assert.equal((await call('POST', '/v1/profile/navigator', { navigatorId: 'son_lam_not_real' }, token)).json.error.code, 'not_owned');

  const report = { songId: 'demo', durationSec: 95, mode: 0, difficulty: 1, score: 50_000, maxCombo: 80, perfect: 100, good: 10, miss: 3, accuracy: 0.93, navigatorId: 'vang_anh' };
  const race = await call('POST', '/v1/race/complete', { report }, token);
  assert.equal(race.status, 200, JSON.stringify(race.json));
  assert.equal(race.json.reward.coins, 1000);
  assert.equal(race.json.reward.jade, 30);
  assert.ok(race.json.reward.bondExp > 0);
  assert.equal((await call('POST', '/v1/race/complete', { report: { ...report, accuracy: 7 } }, token)).status, 400);

  const daily = await call('POST', '/v1/daily/claim', {}, token);
  assert.equal(daily.json.result.claimed, true);
  assert.equal((await call('POST', '/v1/daily/claim', {}, token)).json.result.claimed, false);
});
