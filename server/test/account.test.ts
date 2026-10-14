import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { startApp } from './helpers.ts';
import type { TestApp } from './helpers.ts';

let app: TestApp;

before(async () => {
  app = await startApp();
});

after(() => app.close());

const report = { songId: 'demo', durationSec: 95, mode: 0, difficulty: 1, score: 50_000, maxCombo: 80, perfect: 100, good: 10, miss: 3, accuracy: 0.93, navigatorId: 'vang_anh' };

test('account deletion needs confirmation and the password, erases everything and ends every session', async () => {
  app.setNow('2026-10-12T08:00:00Z');
  const reg = await app.call('POST', '/v1/auth/register', { username: 'leaver', email: 'leaver@example.com', password: 'longenough' });
  const token = reg.json.accessToken;
  const id = reg.json.account.id;
  assert.equal((await app.call('POST', '/v1/gacha/pull', { bannerId: 'standard', count: 10 }, token)).status, 200);
  assert.equal((await app.call('POST', '/v1/race/complete', { report, id: 'race-leaver-0001' }, token)).status, 200);

  assert.equal((await app.call('POST', '/v1/account/delete', { password: 'longenough' }, token)).json.error.code, 'invalid_request');
  const wrong = await app.call('POST', '/v1/account/delete', { confirm: true, password: 'not-the-password' }, token);
  assert.equal(wrong.status, 403);
  assert.equal(wrong.json.error.code, 'invalid_password');
  assert.equal((await app.call('POST', '/v1/account/delete', { confirm: true }, token)).json.error.code, 'invalid_password');
  assert.equal((await app.call('POST', '/v1/account/delete', { confirm: true, password: 'longenough' })).status, 401);

  const ok = await app.call('POST', '/v1/account/delete', { confirm: true, password: 'longenough' }, token);
  assert.equal(ok.status, 200, JSON.stringify(ok.json));

  // The unexpired access token and the refresh token stop working at once, and nothing is left behind.
  assert.equal((await app.call('GET', '/v1/profile', undefined, token)).status, 401);
  assert.equal((await app.call('POST', '/v1/auth/refresh', { refreshToken: reg.json.refreshToken })).status, 401);
  assert.equal((await app.call('POST', '/v1/auth/login', { login: 'leaver', password: 'longenough' })).json.error.code, 'invalid_credentials');
  for (const table of ['accounts', 'identities', 'refresh_tokens', 'profiles', 'gacha_history', 'race_days', 'race_submissions']) {
    const column = table === 'accounts' ? 'id' : 'account_id';
    const row = app.store.db.prepare(`SELECT COUNT(*) AS n FROM ${table} WHERE ${column} = ?`).get(id) as { n: number };
    assert.equal(row.n, 0, `${table} still holds rows of the deleted account`);
  }
  // The username and email can be registered again.
  assert.equal((await app.call('POST', '/v1/auth/register', { username: 'leaver', email: 'leaver@example.com', password: 'longenough' })).status, 200);
});

test('guest accounts delete without a password; the same device then starts a fresh account', async () => {
  const creds = { deviceId: 'device-del', deviceSecret: 'z'.repeat(43) };
  const first = await app.call('POST', '/v1/auth/guest', creds);
  assert.equal((await app.call('POST', '/v1/account/delete', { confirm: true }, first.json.accessToken)).status, 200);
  const again = await app.call('POST', '/v1/auth/guest', creds);
  assert.equal(again.status, 200);
  assert.notEqual(again.json.account.id, first.json.account.id);
  assert.equal(again.json.profile.jade, 3000);
});

test('races played offline sync once each, against the caps of the day they were played', async () => {
  app.setNow('2026-10-10T08:00:00Z');
  assert.equal((await app.call('POST', '/v1/auth/register', { username: 'roadie', email: 'roadie@example.com', password: 'longenough' })).status, 200);
  app.setNow('2026-10-12T08:00:00Z');
  const token = (await app.call('POST', '/v1/auth/login', { login: 'roadie', password: 'longenough' })).json.accessToken;

  const online = await app.call('POST', '/v1/race/complete', { report, id: 'online-today-1' }, token);
  assert.equal(online.json.reward.jade, 30);

  const batch = [
    { id: 'off-today-01', finishedAt: '2026-10-12T07:30:00Z', report },
    { id: 'off-yday-001', finishedAt: '2026-10-11T20:00:00Z', report },
    { id: 'off-today-01', finishedAt: '2026-10-12T07:30:00Z', report },
    { id: 'off-before-1', finishedAt: '2026-10-09T12:00:00Z', report }, // before the account existed
    { id: 'off-ancient1', finishedAt: '2026-10-01T12:00:00Z', report }, // outside the 72 h window
    { id: 'off-future-1', finishedAt: '2026-10-12T09:00:00Z', report },
    { id: 'off-zen-0001', finishedAt: '2026-10-12T07:00:00Z', report: { ...report, mode: 3 } },
    { id: 'online-today-1', finishedAt: '2026-10-12T07:59:00Z', report },
  ];
  const sync = await app.call('POST', '/v1/race/sync', { races: batch }, token);
  assert.equal(sync.status, 200, JSON.stringify(sync.json));
  const status = Object.fromEntries(sync.json.results.map((r: { id: string; status: string }) => [r.id + (r.status === 'duplicate' ? '#dup' : ''), r.status]));
  assert.deepEqual(status, {
    'off-yday-001': 'applied',
    'off-today-01': 'applied',
    'off-today-01#dup': 'duplicate',
    'off-before-1': 'expired',
    'off-ancient1': 'expired',
    'off-future-1': 'invalid_request',
    'off-zen-0001': 'invalid_request',
    'online-today-1#dup': 'duplicate',
  });
  assert.deepEqual(sync.json.total, { applied: 2, coins: 2000, jade: 60, bondExp: sync.json.total.bondExp });
  assert.equal(sync.json.profile.coins, 5000 + 1000 + 2000);
  assert.equal(sync.json.profile.daily.racesRewarded, 2, "yesterday's race doesn't count against today");

  // Re-sending the same batch (e.g. the response was lost) grants nothing twice.
  const again = await app.call('POST', '/v1/race/sync', { races: batch }, token);
  assert.equal(again.json.total.applied, 0);
  assert.equal(again.json.profile.coins, sync.json.profile.coins);
  const retried = await app.call('POST', '/v1/race/complete', { report, id: 'off-today-01' }, token);
  assert.equal(retried.json.duplicate, true);
  assert.equal(retried.json.reward.coins, 0);

  // Yesterday already had one jade race; only 4 of 6 more get jade (5 per day), today is untouched.
  const yesterday = Array.from({ length: 6 }, (_, i) => ({ id: `off-yday-10${i}`, finishedAt: `2026-10-11T1${i}:00:00Z`, report }));
  const capped = await app.call('POST', '/v1/race/sync', { races: yesterday }, token);
  assert.equal(capped.json.total.applied, 6);
  assert.equal(capped.json.total.jade, 4 * 30);
  assert.equal(capped.json.profile.daily.racesRewarded, 2);
  const today = await app.call('POST', '/v1/race/complete', { report }, token);
  assert.equal(today.json.reward.jade, 30, 'today still has jade races left');

  assert.equal((await app.call('POST', '/v1/race/sync', { races: [] }, token)).status, 400);
  assert.equal((await app.call('POST', '/v1/race/sync', { races: Array(51).fill(batch[0]) }, token)).status, 400);
  assert.equal((await app.call('POST', '/v1/race/complete', { report, id: 'bad id!' }, token)).status, 400);
});

test('the server turns away game builds older than MIN_CLIENT_VERSION', async () => {
  const gated = await startApp({ MIN_CLIENT_VERSION: '1.2.0', UPDATE_URL_ANDROID: 'https://play.google.com/store/apps/details?id=com.vietmythical.speed' });
  try {
    const guest = { deviceId: 'device-ver', deviceSecret: 'q'.repeat(43) };
    const none = await gated.call('POST', '/v1/auth/guest', guest);
    assert.equal(none.status, 426);
    assert.equal(none.json.error.code, 'client_outdated');
    assert.equal(none.json.error.minVersion, '1.2.0');

    const old = await gated.call('POST', '/v1/auth/guest', guest, undefined, { 'x-client-version': '1.1.9', 'x-client-platform': 'android' });
    assert.equal(old.status, 426);
    assert.match(old.json.error.updateUrl, /play\.google\.com/);
    assert.equal((await gated.call('POST', '/v1/auth/guest', guest, undefined, { 'x-client-version': '1.1.9', 'x-client-platform': 'windows' })).json.error.updateUrl, '');

    for (const version of ['1.2.0', '1.10.0', '2.0', 'v1.2.1-beta']) {
      assert.equal((await gated.call('POST', '/v1/auth/guest', guest, undefined, { 'x-client-version': version })).status, 200, version);
    }
    // Health checks and the browser half of Steam sign-in carry no game version.
    assert.equal((await gated.call('GET', '/health')).status, 200);
    assert.equal((await gated.call('GET', '/v1/auth/steam/openid/start?redirect_uri=x')).status, 400);
  } finally {
    await gated.close();
  }
});
