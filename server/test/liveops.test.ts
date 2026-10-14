import assert from 'node:assert/strict';
import { generateKeyPairSync, verify } from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { after, before, test } from 'node:test';
import { resetGoogleTokenCache } from '../src/payments/google.ts';
import { startApp } from './helpers.ts';
import type { TestApp } from './helpers.ts';

const STEAM_ID = '76561198000000077';
const ADMIN = 'admin-token-admin-token-123456';

// ---------------------------------------------------------------- fake Google Play + Steam

const { publicKey, privateKey } = generateKeyPairSync('rsa', { modulusLength: 2048 });
const saFile = path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'vms-sa-')), 'service-account.json');
fs.writeFileSync(saFile, JSON.stringify({ client_email: 'vms@test.iam.gserviceaccount.com', private_key: privateKey.export({ type: 'pkcs8', format: 'pem' }), token_uri: 'https://oauth2.googleapis.com/token' }));

let voided: { purchaseToken: string; voidedTimeMillis: string }[] = [];
const steamOrders = new Map<string, string>();

const fakeFetch = (async (input: string | URL | Request, init?: RequestInit) => {
  const url = new URL(typeof input === 'string' ? input : input instanceof URL ? input : input.url);
  const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
  const form = new URLSearchParams(String(init?.body ?? ''));
  if (url.href === 'https://oauth2.googleapis.com/token') {
    // Check the service-account JWT really is signed with the key from the file.
    const [h, c, s] = (form.get('assertion') ?? '').split('.');
    const ok = verify('RSA-SHA256', Buffer.from(`${h}.${c}`), publicKey, Buffer.from(s, 'base64url'));
    const claims = JSON.parse(Buffer.from(c, 'base64url').toString());
    return ok && claims.scope === 'https://www.googleapis.com/auth/androidpublisher' ? json({ access_token: 'g-token', expires_in: 3600 }) : json({ error: 'invalid_grant' }, 400);
  }
  if (url.hostname === 'androidpublisher.googleapis.com') {
    if ((init?.headers as Record<string, string>)?.authorization !== 'Bearer g-token') return json({}, 401);
    if (url.pathname.endsWith('/purchases/voidedpurchases')) return json({ voidedPurchases: voided });
    const token = decodeURIComponent(url.pathname.split('/tokens/')[1] ?? '');
    if (token.startsWith('tok-good')) return json({ purchaseState: 0, orderId: `GPA.${token}`, consumptionState: 0 });
    if (token.startsWith('tok-pending')) return json({ purchaseState: 2 });
    return json({ error: { code: 400 } }, 400);
  }
  if (url.pathname === '/ISteamUserAuth/AuthenticateUserTicket/v1/') {
    return json({ response: { params: { result: 'OK', steamid: STEAM_ID, ownersteamid: STEAM_ID, vacbanned: false, publisherbanned: false } } });
  }
  if (url.pathname === '/ISteamUser/GetPlayerSummaries/v2/') return json({ response: { players: [{ personaname: 'Buyer' }] } });
  if (url.pathname === '/ISteamMicroTxnSandbox/GetUserInfo/v2/') return json({ response: { result: 'OK', params: { currency: 'USD', country: 'US' } } });
  if (url.pathname === '/ISteamMicroTxnSandbox/InitTxn/v3/') {
    if (form.get('amount[0]') !== '499' || form.get('itemid[0]') !== '102' || form.get('currency') !== 'USD') return json({ response: { result: 'Failure', error: { errordesc: 'bad order' } } });
    steamOrders.set(form.get('orderid')!, 'Init');
    return json({ response: { result: 'OK', params: { orderid: form.get('orderid'), transid: '999' } } });
  }
  if (url.pathname === '/ISteamMicroTxnSandbox/FinalizeTxn/v2/') {
    const id = form.get('orderid')!;
    if (steamOrders.get(id) !== 'Init') return json({ response: { result: 'Failure', error: { errordesc: 'not approved' } } });
    steamOrders.set(id, 'Succeeded');
    return json({ response: { result: 'OK', params: { orderid: id } } });
  }
  if (url.pathname === '/ISteamMicroTxnSandbox/QueryTxn/v3/') return json({ response: { result: 'OK', params: { status: steamOrders.get(url.searchParams.get('orderid') ?? '') ?? '' } } });
  return new Response('not mocked', { status: 500 });
}) as typeof fetch;

const sent: { to: string; subject: string; text: string }[] = [];
const mailer = { send: async (to: string, subject: string, text: string) => void sent.push({ to, subject, text }) };

let app: TestApp;

before(async () => {
  resetGoogleTokenCache();
  app = await startApp(
    {
      PAYMENTS_FAKE: '1',
      ADMIN_TOKEN: ADMIN,
      GOOGLE_SERVICE_ACCOUNT_FILE: saFile,
      STEAM_APP_ID: '480',
      STEAM_PUBLISHER_KEY: 'publisher',
      STEAM_MICROTXN_SANDBOX: '1',
    },
    '2026-10-12T08:00:00Z',
    { fetch: fakeFetch, mailer },
  );
});

after(() => app.close());

async function register(name: string) {
  const reg = await app.call('POST', '/v1/auth/register', { username: name, email: `${name}@example.com`, password: 'longenough' });
  assert.equal(reg.status, 200, JSON.stringify(reg.json));
  return reg.json as { accessToken: string; refreshToken: string; account: { id: string } };
}

test('fake store (dev only), shop, equip and exchange over the API', async () => {
  const { accessToken: t } = await register('shopper');
  const buy = await app.call('POST', '/v1/purchases/fake', { productId: 'gold_980' }, t);
  assert.equal(buy.status, 200, JSON.stringify(buy.json));
  assert.deepEqual([buy.json.grant.gold, buy.json.grant.bonus, buy.json.grant.firstPurchase], [980, 980, true]);
  assert.deepEqual([buy.json.profile.gold, buy.json.profile.goldBonus], [980, 980]);

  assert.equal((await app.call('POST', '/v1/purchases/fake', { productId: 'starter_pack' }, t)).status, 200);
  assert.equal((await app.call('POST', '/v1/purchases/check', { productId: 'starter_pack' }, t)).json.allowed, false);
  assert.equal((await app.call('POST', '/v1/purchases/fake', { productId: 'starter_pack' }, t)).json.error.code, 'limit_reached');

  const frame = await app.call('POST', '/v1/shop/buy', { itemId: 'frame_phoenix' }, t);
  assert.equal(frame.status, 200, JSON.stringify(frame.json));
  assert.deepEqual([frame.json.profile.gold, frame.json.profile.goldBonus], [980 + 120, 980 - 680], 'bonus gold is spent first');
  assert.equal((await app.call('POST', '/v1/shop/buy', { itemId: 'frame_phoenix' }, t)).json.error.code, 'already_owned');
  assert.equal((await app.call('POST', '/v1/shop/buy', { itemId: 'nope' }, t)).json.error.code, 'item_not_found');

  const equip = await app.call('POST', '/v1/cosmetics/equip', { category: 'avatarFrame', itemId: 'frame_phoenix' }, t);
  assert.equal(equip.json.profile.cosmetics.avatarFrame, 'frame_phoenix');
  assert.equal((await app.call('POST', '/v1/cosmetics/equip', { category: 'exhaust', itemId: 'exhaust_phoenix' }, t)).json.error.code, 'not_owned');

  const ex = await app.call('POST', '/v1/shop/exchange', { amount: 150 }, t);
  assert.equal(ex.json.jade, 150);
  assert.equal(ex.json.profile.jade, 3000 + 150);

  const shop = await app.call('GET', '/v1/shop');
  assert.deepEqual(shop.json.stores, { google: true, steam: true, fake: true });
  assert.ok(shop.json.catalog.items.length > 40);
  assert.ok(app.store.db.prepare("SELECT COUNT(*) AS n FROM shop_history WHERE item_id = 'frame_phoenix'").get());
});

test('Google Play purchases are verified with the Developer API, granted once and taken back on refund', async () => {
  const buyer = await register('gbuyer');
  const other = await register('gother');
  const good = { productId: 'gold_300', purchaseToken: 'tok-good-1' };

  const first = await app.call('POST', '/v1/purchases/google', good, buyer.accessToken);
  assert.equal(first.status, 200, JSON.stringify(first.json));
  assert.equal(first.json.alreadyGranted, false);
  assert.deepEqual([first.json.profile.gold, first.json.profile.goldBonus], [300, 300]);
  const retry = await app.call('POST', '/v1/purchases/google', good, buyer.accessToken);
  assert.equal(retry.json.alreadyGranted, true);
  assert.equal(retry.json.profile.gold, 300, 'a retried confirmation grants nothing twice');
  assert.equal((await app.call('POST', '/v1/purchases/google', good, other.accessToken)).json.error.code, 'purchase_owned_elsewhere');
  assert.equal((await app.call('POST', '/v1/purchases/google', { productId: 'gold_300', purchaseToken: 'tok-pending-1' }, buyer.accessToken)).json.error.code, 'purchase_pending');
  assert.equal((await app.call('POST', '/v1/purchases/google', { productId: 'gold_300', purchaseToken: 'forged' }, buyer.accessToken)).json.error.code, 'purchase_invalid');
  assert.equal((await app.call('POST', '/v1/purchases/google', { productId: 'not_a_product', purchaseToken: 'tok-good-2' }, buyer.accessToken)).json.error.code, 'product_not_found');

  // The buyer spends the gold, then gets a refund: the poller takes it all back (a debt) and leaves a mail.
  const spent = await app.call('POST', '/v1/shop/buy', { itemId: 'frame_dragon_scale' }, buyer.accessToken);
  assert.deepEqual([spent.json.profile.gold, spent.json.profile.goldBonus], [120, 0]);
  voided = [{ purchaseToken: 'tok-good-1', voidedTimeMillis: String(Date.parse('2026-10-12T09:00:00Z')) }];
  assert.equal((await app.call('POST', '/v1/admin/refunds/poll', {}, undefined, { 'x-admin-token': ADMIN })).json.revoked, 1);
  const profile = await app.call('GET', '/v1/profile', undefined, buyer.accessToken);
  assert.deepEqual([profile.json.profile.gold, profile.json.profile.goldBonus], [120 - 600, 0]);
  assert.ok(profile.json.profile.cosmetics.owned.includes('frame_dragon_scale'), 'items stay; the debt blocks gold spending instead');
  assert.equal((await app.call('POST', '/v1/shop/buy', { itemId: 'frame_lotus' }, buyer.accessToken)).json.error.code, 'insufficient_gold');
  const mail = await app.call('GET', '/v1/mail', undefined, buyer.accessToken);
  assert.ok(mail.json.mail.some((m: { title: { en: string } }) => m.title.en === 'A purchase was refunded'));
  assert.equal((await app.call('POST', '/v1/purchases/google', good, buyer.accessToken)).json.error.code, 'purchase_refunded');
  assert.equal((await app.call('POST', '/v1/admin/refunds/poll', {}, undefined, { 'x-admin-token': ADMIN })).json.revoked, 0, 'only once');
});

test('Steam purchases: InitTxn in the wallet currency, FinalizeTxn after approval, idempotent', async () => {
  const reg = await register('steamer');
  assert.equal((await app.call('POST', '/v1/purchases/steam/init', { productId: 'gold_300' }, reg.accessToken)).json.error.code, 'steam_not_linked');
  assert.equal((await app.call('POST', '/v1/account/link/steam', { ticket: 'ABCDEF0123456789' }, reg.accessToken)).status, 200);

  const init = await app.call('POST', '/v1/purchases/steam/init', { productId: 'gold_300', language: 'vi' }, reg.accessToken);
  assert.equal(init.status, 200, JSON.stringify(init.json));
  const orderId = init.json.orderId as string;
  assert.match(orderId, /^\d+$/);
  const done = await app.call('POST', '/v1/purchases/steam/finalize', { orderId, authorized: true }, reg.accessToken);
  assert.equal(done.status, 200, JSON.stringify(done.json));
  assert.equal(done.json.profile.gold, 300);
  assert.equal((await app.call('POST', '/v1/purchases/steam/finalize', { orderId, authorized: true }, reg.accessToken)).json.alreadyGranted, true);

  const declined = await app.call('POST', '/v1/purchases/steam/init', { productId: 'gold_300' }, reg.accessToken);
  assert.equal((await app.call('POST', '/v1/purchases/steam/finalize', { orderId: declined.json.orderId, authorized: false }, reg.accessToken)).json.error.code, 'cancelled');
  assert.equal(app.store.purchase(`steam:${declined.json.orderId}`)?.status, 'failed');
  const someoneElse = await register('nosy');
  assert.equal((await app.call('POST', '/v1/purchases/steam/finalize', { orderId, authorized: true }, someoneElse.accessToken)).status, 404);
});

test('mail: welcome gifts, admin broadcasts and personal mail, claim once, delete after claiming', async () => {
  const reg = await register('mailer');
  const list = await app.call('GET', '/v1/mail', undefined, reg.accessToken);
  assert.equal(list.json.unread, 1);
  const welcome = list.json.mail[0];
  assert.equal(welcome.title.en, 'Welcome, Envoy!');
  assert.equal((await app.call('POST', '/v1/mail/delete', { id: welcome.id }, reg.accessToken)).json.error.code, 'claim_first');

  assert.equal((await app.call('POST', '/v1/admin/mail', { to: 'all', title: { en: 'x' }, body: { en: 'y' } })).status, 403);
  const broadcast = await app.call('POST', '/v1/admin/mail', {
    to: 'all', audience: 'existing', title: { vi: 'Bảo trì', en: 'Maintenance' }, body: { en: 'Sorry for the downtime.' },
    attachments: [{ type: 'gold', amount: 50 }, { type: 'item', itemId: 'bubble_lotus' }],
  }, undefined, { 'x-admin-token': ADMIN });
  assert.equal(broadcast.status, 200, JSON.stringify(broadcast.json));
  const personal = await app.call('POST', '/v1/admin/mail', { to: reg.account.id, title: { en: 'Just for you' }, body: { en: '!' }, attachments: [{ type: 'coins', amount: 1234 }] }, undefined, { 'x-admin-token': ADMIN });
  assert.equal(personal.status, 200);
  assert.equal((await app.call('POST', '/v1/admin/mail', { to: 'all', title: { en: 'x' }, body: { en: 'y' }, attachments: [{ type: 'item', itemId: 'nope' }] }, undefined, { 'x-admin-token': ADMIN })).status, 400);

  const claimed = await app.call('POST', '/v1/mail/claim', { all: true }, reg.accessToken);
  assert.equal(claimed.json.claimed.length, 3);
  assert.equal(claimed.json.profile.jade, 3000 + 300);
  assert.equal(claimed.json.profile.goldBonus, 50);
  assert.equal(claimed.json.profile.coins, 5000 + 1234);
  assert.ok(claimed.json.profile.cosmetics.owned.includes('frame_welcome'));
  assert.ok(claimed.json.profile.cosmetics.owned.includes('bubble_lotus'));
  assert.equal((await app.call('POST', '/v1/mail/claim', { id: welcome.id }, reg.accessToken)).json.error.code, 'already_claimed');
  assert.equal((await app.call('POST', '/v1/mail/delete', { id: welcome.id }, reg.accessToken)).status, 200);
  assert.equal((await app.call('GET', '/v1/mail', undefined, reg.accessToken)).json.mail.length, 2);

  // "existing" broadcasts skip players who join later; personal mail stays personal.
  app.setNow('2026-10-12T09:00:00Z');
  const late = await register('latecomer');
  const lateMail = (await app.call('GET', '/v1/mail', undefined, late.accessToken)).json.mail as { title: { en: string } }[];
  assert.deepEqual(lateMail.map((m) => m.title.en), ['Welcome, Envoy!']);
  app.setNow('2026-10-12T08:00:00Z');

  const lookup = await app.call('GET', '/v1/admin/account?login=mailer', undefined, undefined, { 'x-admin-token': ADMIN });
  assert.equal(lookup.json.account.id, reg.account.id);
});

test('events over the API: daily login checks in, races count, milestones pay once', async () => {
  const reg = await register('eventer');
  const events = await app.call('GET', '/v1/events');
  assert.deepEqual(events.json.events.map((e: { id: string }) => e.id), ['grand_opening', 'spirit_road', 'score_rush']);
  assert.equal((await app.call('POST', '/v1/daily/claim', {}, reg.accessToken)).json.profile.events[0].value, 1);
  const report = { songId: 'demo', durationSec: 95, mode: 0, difficulty: 1, score: 72_480, maxCombo: 163, perfect: 163, good: 0, miss: 0, accuracy: 1, navigatorId: 'vang_anh' };
  for (let i = 0; i < 3; i++) await app.call('POST', '/v1/race/complete', { report }, reg.accessToken);
  const claim = await app.call('POST', '/v1/events/claim', { eventId: 'spirit_road', index: 0 }, reg.accessToken);
  assert.equal(claim.status, 200, JSON.stringify(claim.json));
  assert.deepEqual(claim.json.rewards, [{ type: 'coins', amount: 3000 }]);
  assert.equal((await app.call('POST', '/v1/events/claim', { eventId: 'spirit_road', index: 1 }, reg.accessToken)).json.error.code, 'not_reached');
  assert.equal((await app.call('POST', '/v1/events/claim', { eventId: 'tet_2027', index: 0 }, reg.accessToken)).json.error.code, 'event_ended');
});

test('password reset by email: 6-digit code, 5 tries, old sessions end, no account enumeration', async () => {
  const reg = await register('forgetful');
  sent.length = 0;
  assert.deepEqual((await app.call('POST', '/v1/auth/password/forgot', { email: 'nobody@example.com' })).json, { ok: true });
  assert.equal(sent.length, 0);
  assert.deepEqual((await app.call('POST', '/v1/auth/password/forgot', { email: 'Forgetful@Example.com', lang: 'en' })).json, { ok: true });
  assert.equal(sent.length, 1);
  assert.equal(sent[0].to, 'forgetful@example.com');
  const code = /code is: (\d{6})/.exec(sent[0].text)![1];

  const wrong = await app.call('POST', '/v1/auth/password/reset', { email: 'forgetful@example.com', code: code === '000000' ? '111111' : '000000', newPassword: 'brand-new-pass' });
  assert.equal(wrong.json.error.code, 'code_invalid');
  assert.equal((await app.call('POST', '/v1/auth/password/reset', { email: 'forgetful@example.com', code, newPassword: 'short' })).json.error.code, 'weak_password');
  const ok = await app.call('POST', '/v1/auth/password/reset', { email: 'forgetful@example.com', code, newPassword: 'brand-new-pass' });
  assert.equal(ok.status, 200, JSON.stringify(ok.json));
  assert.ok(ok.json.accessToken);
  assert.equal((await app.call('POST', '/v1/auth/refresh', { refreshToken: reg.refreshToken })).status, 401, 'old sessions are revoked');
  assert.equal((await app.call('POST', '/v1/auth/login', { login: 'forgetful', password: 'brand-new-pass' })).status, 200);
  assert.equal((await app.call('POST', '/v1/auth/password/reset', { email: 'forgetful@example.com', code, newPassword: 'another-pass-1' })).json.error.code, 'code_invalid', 'codes work once');

  // Brute force: the 6th guess fails even with the right code; mails are limited to 3 per hour.
  sent.length = 0;
  await app.call('POST', '/v1/auth/password/forgot', { email: 'forgetful@example.com' });
  const code2 = /là: (\d{6})/.exec(sent[0].text)![1];
  for (let i = 0; i < 5; i++) await app.call('POST', '/v1/auth/password/reset', { email: 'forgetful@example.com', code: code2 === '999999' ? '888888' : '999999', newPassword: 'brand-new-pass-2' });
  assert.equal((await app.call('POST', '/v1/auth/password/reset', { email: 'forgetful@example.com', code: code2, newPassword: 'brand-new-pass-2' })).json.error.code, 'code_invalid');
  await app.call('POST', '/v1/auth/password/forgot', { email: 'forgetful@example.com' });
  await app.call('POST', '/v1/auth/password/forgot', { email: 'forgetful@example.com' });
  await app.call('POST', '/v1/auth/password/forgot', { email: 'forgetful@example.com' });
  assert.equal(sent.length, 3);

  const noEmail = await startApp({}, '2026-10-12T08:00:00Z', { mailer: null });
  try {
    assert.equal((await noEmail.call('POST', '/v1/auth/password/forgot', { email: 'a@b.cd' })).json.error.code, 'email_unavailable');
  } finally {
    await noEmail.close();
  }
});

test('cloud saves: revisions detect conflicts between devices; force overwrites; 3 slots', async () => {
  const reg = await register('saver');
  const t = reg.accessToken;
  assert.deepEqual((await app.call('GET', '/v1/cloudsave', undefined, t)).json, { exists: false, slot: 'offline', revision: 0 });
  const first = await app.call('POST', '/v1/cloudsave', { slot: 'offline', data: '{"v":1}', baseRevision: 0, device: 'Phone', summary: 'lvl 1' }, t);
  assert.equal(first.json.revision, 1);
  const stale = await app.call('POST', '/v1/cloudsave', { slot: 'offline', data: '{"v":"pc"}', baseRevision: 0, device: 'PC' }, t);
  assert.equal(stale.status, 409);
  assert.equal(stale.json.error.code, 'save_conflict');
  assert.equal(stale.json.error.device, 'Phone');
  assert.equal((await app.call('POST', '/v1/cloudsave', { slot: 'offline', data: '{"v":"pc"}', baseRevision: 0, device: 'PC', force: true }, t)).json.revision, 2);
  const got = await app.call('GET', '/v1/cloudsave?slot=offline', undefined, t);
  assert.deepEqual([got.json.data, got.json.device, got.json.revision], ['{"v":"pc"}', 'PC', 2]);
  for (const slot of ['a', 'b']) assert.equal((await app.call('POST', '/v1/cloudsave', { slot, data: 'x', baseRevision: 0 }, t)).status, 200);
  assert.equal((await app.call('POST', '/v1/cloudsave', { slot: 'c', data: 'x', baseRevision: 0 }, t)).json.error.code, 'too_many_slots');
  assert.equal((await app.call('POST', '/v1/cloudsave', { slot: 'Bad Slot', data: 'x', baseRevision: 0 }, t)).status, 400);
  assert.equal((await app.call('POST', '/v1/cloudsave', { slot: 'offline', data: 'x'.repeat(49 * 1024), baseRevision: 2 }, t)).status, 400);
});

test('fake purchases are off unless PAYMENTS_FAKE=1, and never allowed in production', async () => {
  const plain = await startApp();
  try {
    const reg = await plain.call('POST', '/v1/auth/register', { username: 'plainbuyer', email: 'p@example.com', password: 'longenough' });
    assert.equal((await plain.call('POST', '/v1/purchases/fake', { productId: 'gold_60' }, reg.json.accessToken)).status, 404);
    assert.deepEqual((await plain.call('GET', '/v1/shop')).json.stores, { google: false, steam: false, fake: false });
  } finally {
    await plain.close();
  }
  const { loadConfig } = await import('../src/config.ts');
  assert.throws(() => loadConfig({ NODE_ENV: 'production', JWT_SECRET: 'x'.repeat(40), PUBLIC_URL: 'https://a.test', PAYMENTS_FAKE: '1' }), /PAYMENTS_FAKE/);
});

test('languages: mail keeps all eight translations, reset emails follow the player language, shop data is translated', async () => {
  const reg = await register('polyglot');
  const title = { vi: 'Quà', en: 'Gift', zh: '礼物', ja: '贈り物', ko: '선물', es: 'Regalo', de: 'Geschenk', fi: 'Lahja', xx: 'ignored' };
  const sentMail = await app.call('POST', '/v1/admin/mail', { to: reg.account.id, title, body: { en: 'Hi', ja: 'やあ' } }, undefined, { 'x-admin-token': ADMIN });
  assert.equal(sentMail.status, 200, JSON.stringify(sentMail.json));
  const mail = (await app.call('GET', '/v1/mail', undefined, reg.accessToken)).json.mail.find((m: { id: number }) => m.id === sentMail.json.id);
  assert.deepEqual(mail.title, { vi: 'Quà', en: 'Gift', zh: '礼物', ja: '贈り物', ko: '선물', es: 'Regalo', de: 'Geschenk', fi: 'Lahja' });
  assert.equal(mail.body.ja, 'やあ');

  sent.length = 0;
  await app.call('POST', '/v1/auth/password/forgot', { email: 'polyglot@example.com', lang: 'ja' });
  assert.equal(sent.length, 1);
  assert.match(sent[0].subject, /パスワード/);
  assert.match(sent[0].text, /\d{6}/);

  const shop = (await app.call('GET', '/v1/shop')).json.catalog;
  const frame = shop.items.find((i: { id: string }) => i.id === 'frame_aurora');
  for (const lang of ['vi', 'en', 'zh', 'ja', 'ko', 'es', 'de', 'fi']) assert.ok(frame.name[lang], `frame_aurora has no ${lang} name`);
});
