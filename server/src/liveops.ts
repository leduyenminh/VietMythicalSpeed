import { randomInt, randomUUID } from 'node:crypto';
import type { IncomingMessage } from 'node:http';
import type { Config } from './config.ts';
import type { MailRow, PurchaseRow, Store } from './db.ts';
import { hashPassword, safeEqual, sha256Hex } from './auth/crypto.ts';
import type { Mailer } from './auth/mailer.ts';
import { languageOf, resetEmail } from './auth/resetMail.ts';
import type { FetchFn } from './auth/providers.ts';
import { isValidEmail, isValidPassword } from './auth/validation.ts';
import type { GameData } from './game/data.ts';
import { normalizeProfile } from './game/economy.ts';
import {
  applyProduct,
  buyItem,
  canBuyProduct,
  catalogItem,
  claimEvent,
  equipItem,
  exchangeGold,
  grantRewards,
  MAX_EXCHANGE,
  revokeProduct,
  visibleEvents,
} from './game/shop.ts';
import type { ProductGrant } from './game/shop.ts';
import { GameError, LANGUAGES, pickText } from './game/types.ts';
import type { LocalizedText, PlayerProfile, ProductDef, Reward } from './game/types.ts';
import { headerValue, int, str } from './http.ts';
import type { RateLimiter, Router } from './http.ts';
import { googlePlayConfigured, listVoidedPurchases, verifyGoogleProduct } from './payments/google.ts';
import { newSteamOrderId, steamFinalizeTxn, steamInitTxn, steamPaymentsConfigured, steamUserCurrency } from './payments/steam.ts';

// Shop, real-money purchases, events, mail, admin API, password reset and cloud saves.

export interface LiveOpsContext {
  router: Router;
  config: Config;
  store: Store;
  data: GameData;
  fetchImpl: FetchFn;
  now: () => Date;
  withProfile: <T>(accountId: string, mutate: (p: PlayerProfile) => T) => { result: T; profile: PlayerProfile };
  profileFor: (accountId: string) => PlayerProfile;
  issueSession: (accountId: string, provider: string) => unknown;
  authLimiter: RateLimiter;
  gameLimiter: RateLimiter;
  mailer: Mailer | null;
}

const DAY = 86_400_000;
const SLOT = /^[a-z0-9_-]{1,32}$/;
const MAX_SAVE = 48 * 1024;
const RESET_TTL = 15 * 60_000;

// ---------------------------------------------------------------- mail helpers (also used by app.ts and the refund poller)

/** Puts a mail.json template into one player's mailbox. */
export function templateMail(store: Store, data: GameData, accountId: string, templateId: string, nowMs: number): number | null {
  const t = data.mail.find((m) => m.id === templateId);
  if (!t) return null;
  return store.insertMail({
    accountId,
    title: JSON.stringify(t.title),
    body: JSON.stringify(t.body),
    attachments: JSON.stringify(t.attachments ?? []),
    audience: 'all',
    createdAt: nowMs,
    expiresAt: nowMs + Math.max(1, t.expiresDays || 30) * DAY,
  });
}

function mailDto(row: MailRow) {
  const attachments = JSON.parse(row.attachments) as Reward[];
  return {
    id: row.id,
    title: JSON.parse(row.title) as LocalizedText,
    body: JSON.parse(row.body) as LocalizedText,
    attachments,
    createdAt: row.created_at,
    expiresAt: row.expires_at,
    read: row.read_at !== null,
    claimed: row.claimed_at !== null,
  };
}

function parseRewards(data: GameData, value: unknown): Reward[] {
  if (value === undefined) return [];
  if (!Array.isArray(value) || value.length > 20) throw new GameError('invalid_request', '"attachments" must be a list of up to 20 rewards.', 400);
  return value.map((raw) => {
    const r = (raw ?? {}) as Record<string, unknown>;
    const type = String(r.type ?? '');
    if (type === 'item') {
      const item = catalogItem(data.shop, String(r.itemId ?? ''));
      if (!item) throw new GameError('invalid_request', `Unknown item ${String(r.itemId)}.`, 400);
      return { type, amount: 1, itemId: item.id };
    }
    if (!['gold', 'jade', 'coins', 'tickets'].includes(type)) throw new GameError('invalid_request', `Unknown reward type ${type}.`, 400);
    const amount = Number(r.amount);
    if (!Number.isInteger(amount) || amount <= 0 || amount > 1_000_000) throw new GameError('invalid_request', 'Reward amounts must be 1–1000000.', 400);
    return { type, amount };
  });
}

function localized(value: unknown, field: string): LocalizedText {
  const v = (value ?? {}) as Record<string, unknown>;
  const text: LocalizedText = {};
  for (const lang of LANGUAGES) {
    const value = v[lang];
    if (typeof value === 'string' && value.trim()) text[lang] = value.slice(0, 4000);
  }
  if (!text.vi && !text.en) throw new GameError('invalid_request', `"${field}" needs vi and/or en text (other languages are optional).`, 400);
  return text;
}

// ---------------------------------------------------------------- refunds

/** Takes back a refunded purchase: its gold (paid first), status revoked, and a mail explaining why. */
export function revokePurchase(store: Store, data: GameData, row: PurchaseRow, nowMs: number): void {
  store.transaction(() => {
    const profile = store.loadProfile(row.account_id);
    if (profile) {
      revokeProduct(normalizeProfile(profile), row.gold, row.bonus);
      store.saveProfile(profile, nowMs);
      templateMail(store, data, row.account_id, 'purchase_revoked', nowMs);
    }
    store.updatePurchase(row.id, 'revoked', row.gold, row.bonus, null, nowMs);
  });
  console.log(`[payments] revoked ${row.id} (${row.product_id}) for ${row.account_id}`);
}

/** Checks Google Play's voided purchases (refunds, chargebacks) since the last poll. Returns how many were revoked. */
export async function pollGoogleRefunds(config: Config, store: Store, data: GameData, fetchImpl: FetchFn, nowMs = Date.now()): Promise<number> {
  if (!googlePlayConfigured(config)) return 0;
  const key = 'google_voided_since';
  // The API only looks back 30 days.
  const since = Math.max(Number(store.getValue(key) ?? 0), nowMs - 30 * DAY);
  const voided = await listVoidedPurchases(config, since, fetchImpl);
  let latest = since;
  let revoked = 0;
  for (const v of voided) {
    latest = Math.max(latest, Number(v.voidedTimeMillis ?? 0));
    const row = store.purchase(`google:${sha256Hex(v.purchaseToken)}`);
    if (!row || row.status !== 'granted') continue;
    revokePurchase(store, data, row, nowMs);
    revoked++;
  }
  store.setValue(key, String(latest));
  return revoked;
}

// ---------------------------------------------------------------- routes

export function registerLiveOps(ctx: LiveOpsContext): void {
  const { router, config, store, data, fetchImpl, now, withProfile, profileFor, issueSession, authLimiter, gameLimiter, mailer } = ctx;
  const auth = { auth: true, limiter: gameLimiter };

  // ------------------------------------------------ shop and cosmetics

  router.add('GET', '/v1/shop', () => ({
    catalog: data.shop,
    stores: { google: googlePlayConfigured(config), steam: steamPaymentsConfigured(config), fake: config.paymentsFake },
    serverTime: now().toISOString(),
  }), { limiter: gameLimiter });

  router.add('POST', '/v1/shop/buy', ({ body, accountId }) => {
    const itemId = str(body, 'itemId', 64);
    const t = now();
    const { result, profile } = store.transaction(() => withProfile(accountId, (p) => buyItem(p, data.shop, itemId, t)));
    store.recordShopBuy(accountId, itemId, result.item.price!.currency, result.item.price!.amount, t.getTime());
    return { profile, itemId, rewards: result.rewards };
  }, auth);

  router.add('POST', '/v1/cosmetics/equip', ({ body, accountId }) => {
    const category = str(body, 'category', 32);
    const target = typeof body.target === 'string' ? body.target : '';
    const itemId = typeof body.itemId === 'string' ? body.itemId : '';
    const { profile } = withProfile(accountId, (p) => equipItem(p, data.shop, category, target, itemId));
    return { profile };
  }, auth);

  router.add('POST', '/v1/shop/exchange', ({ body, accountId }) => {
    const amount = int(body, 'amount', 1, MAX_EXCHANGE);
    const { result, profile } = withProfile(accountId, (p) => exchangeGold(p, data.shop, amount));
    return { profile, jade: result };
  }, auth);

  // ------------------------------------------------ real-money purchases

  function product(id: string): ProductDef {
    const p = data.shop.products.find((x) => x.id === id);
    if (!p) throw new GameError('product_not_found', 'Unknown product.', 404);
    return p;
  }

  function purchaseResponse(profile: PlayerProfile, grant: ProductGrant, productId: string, alreadyGranted: boolean) {
    return { profile, productId, grant, alreadyGranted };
  }

  /** Grants a store transaction exactly once (the transaction id is the primary key). */
  function grantPurchase(accountId: string, storeName: string, txId: string, productId: string, extra: { externalRef?: string | null; amountCents?: number | null; currency?: string | null }) {
    const id = `${storeName}:${txId}`;
    return store.transaction(() => {
      const existing = store.purchase(id);
      if (existing && existing.account_id !== accountId) throw new GameError('purchase_owned_elsewhere', 'This purchase belongs to another account.', 409);
      if (existing?.status === 'granted') {
        const grant: ProductGrant = { gold: existing.gold, bonus: existing.bonus, firstPurchase: false, rewards: [], passJade: 0 };
        return purchaseResponse(profileFor(accountId), grant, existing.product_id, true);
      }
      if (existing?.status === 'revoked') throw new GameError('purchase_refunded', 'This purchase was refunded.', 409);
      const prod = product(productId);
      const { result: grant, profile } = withProfile(accountId, (p) => applyProduct(p, data.shop, prod, now()));
      const t = now().getTime();
      if (existing) {
        store.updatePurchase(id, 'granted', grant.gold, grant.bonus, extra.externalRef ?? null, t);
      } else {
        store.insertPurchase({
          id,
          account_id: accountId,
          store: storeName,
          product_id: prod.id,
          status: 'granted',
          gold: grant.gold,
          bonus: grant.bonus,
          amount_cents: extra.amountCents ?? null,
          currency: extra.currency ?? null,
          external_ref: extra.externalRef ?? null,
          created_at: t,
          updated_at: t,
        });
      }
      console.log(`[payments] ${id} granted ${prod.id} to ${accountId}`);
      return purchaseResponse(profile, grant, prod.id, false);
    });
  }

  /** Before opening a store dialog: is this product still allowed (one-time packs)? */
  router.add('POST', '/v1/purchases/check', ({ body, accountId }) => {
    const prod = product(str(body, 'productId', 64));
    return { allowed: canBuyProduct(profileFor(accountId), prod) };
  }, auth);

  router.add('POST', '/v1/purchases/google', async ({ body, accountId }) => {
    const productId = str(body, 'productId', 64);
    const token = str(body, 'purchaseToken', 4096);
    product(productId);
    const txId = sha256Hex(token);
    const existing = store.purchase(`google:${txId}`);
    if (existing?.status !== 'granted' || existing.account_id !== accountId) {
      const info = await verifyGoogleProduct(config, productId, token, fetchImpl);
      if (info.purchaseState === 2) throw new GameError('purchase_pending', 'The payment is still pending.', 409);
      if (info.purchaseState !== 0) throw new GameError('purchase_invalid', 'This purchase was cancelled.', 400);
      return grantPurchase(accountId, 'google', txId, productId, { externalRef: info.orderId ?? null });
    }
    return grantPurchase(accountId, 'google', txId, productId, {});
  }, auth);

  router.add('POST', '/v1/purchases/steam/init', async ({ body, accountId }) => {
    if (!steamPaymentsConfigured(config)) throw new GameError('store_unavailable', 'Steam purchases are not configured on the server.', 503);
    const prod = product(str(body, 'productId', 64));
    if (!prod.steamItemId) throw new GameError('product_not_found', 'This product is not sold on Steam.', 404);
    const steamId = store.subjectFor(accountId, 'steam');
    if (!steamId) throw new GameError('steam_not_linked', 'Link Steam to this account first.', 409);
    if (!canBuyProduct(profileFor(accountId), prod)) throw new GameError('limit_reached', 'Already bought.', 409);
    const currency = await steamUserCurrency(config, steamId, fetchImpl);
    const price = prod.steamPrices?.find((p) => p.currency === currency);
    if (!price) throw new GameError('currency_unsupported', `No ${currency} price for this product.`, 409);
    const orderId = newSteamOrderId();
    const t = now().getTime();
    store.insertPurchase({
      id: `steam:${orderId}`,
      account_id: accountId,
      store: 'steam',
      product_id: prod.id,
      status: 'pending',
      gold: 0,
      bonus: 0,
      amount_cents: price.cents,
      currency,
      external_ref: null,
      created_at: t,
      updated_at: t,
    });
    const language = typeof body.language === 'string' && /^[a-z]{2}$/.test(body.language) ? body.language : 'en';
    const transId = await steamInitTxn(config, fetchImpl, {
      orderId,
      steamId,
      itemId: prod.steamItemId,
      amountCents: price.cents,
      currency,
      description: pickText(prod.name, language) || prod.id,
      language,
    });
    store.updatePurchase(`steam:${orderId}`, 'pending', 0, 0, transId || null, t);
    return { orderId };
  }, auth);

  router.add('POST', '/v1/purchases/steam/finalize', async ({ body, accountId }) => {
    const orderId = str(body, 'orderId', 24);
    if (!/^\d+$/.test(orderId)) throw new GameError('invalid_request', '"orderId" is invalid.', 400);
    const row = store.purchase(`steam:${orderId}`);
    if (!row || row.account_id !== accountId) throw new GameError('purchase_invalid', 'Unknown order.', 404);
    if (row.status === 'granted') return grantPurchase(accountId, 'steam', orderId, row.product_id, {});
    if (row.status !== 'pending') throw new GameError('purchase_invalid', 'This order is closed.', 409);
    if (body.authorized === false) {
      store.updatePurchase(row.id, 'failed', 0, 0, null, now().getTime());
      throw new GameError('cancelled', 'Purchase cancelled.', 400);
    }
    if (!(await steamFinalizeTxn(config, fetchImpl, orderId))) throw new GameError('purchase_invalid', 'Steam did not approve this order.', 400);
    return grantPurchase(accountId, 'steam', orderId, row.product_id, { amountCents: row.amount_cents, currency: row.currency });
  }, auth);

  /** Development builds and tests: a store that always says yes. Disabled unless PAYMENTS_FAKE=1. */
  router.add('POST', '/v1/purchases/fake', ({ body, accountId }) => {
    if (!config.paymentsFake) throw new GameError('not_found', 'Not found.', 404);
    const prod = product(str(body, 'productId', 64));
    if (!canBuyProduct(profileFor(accountId), prod)) throw new GameError('limit_reached', 'Already bought.', 409);
    return grantPurchase(accountId, 'fake', randomUUID(), prod.id, {});
  }, auth);

  // ------------------------------------------------ events

  router.add('GET', '/v1/events', () => ({
    events: visibleEvents(data.events, now()),
    claimGraceDays: data.events.claimGraceDays,
    serverTime: now().toISOString(),
  }), { limiter: gameLimiter });

  router.add('POST', '/v1/events/claim', ({ body, accountId }) => {
    const eventId = str(body, 'eventId', 64);
    const index = int(body, 'index', 0, 99);
    const { result, profile } = withProfile(accountId, (p) => claimEvent(p, data.events, data.shop, eventId, index, now()));
    return { profile, rewards: result };
  }, auth);

  // ------------------------------------------------ mail

  function accountCreated(accountId: string): number {
    return store.accountById(accountId)?.created_at ?? 0;
  }

  router.add('GET', '/v1/mail', ({ accountId }) => {
    const mail = store.listMail(accountId, accountCreated(accountId), now().getTime()).map(mailDto);
    return { mail, unread: mail.filter((m) => !m.read).length, unclaimed: mail.filter((m) => !m.claimed && m.attachments.length > 0).length };
  }, auth);

  function visibleMail(accountId: string, id: number): MailRow {
    const row = store.mailFor(id, accountId, accountCreated(accountId), now().getTime());
    if (!row) throw new GameError('mail_not_found', 'This mail is gone.', 404);
    return row;
  }

  router.add('POST', '/v1/mail/read', ({ body, accountId }) => {
    const row = visibleMail(accountId, int(body, 'id', 1, 2 ** 31));
    store.markMail(row.id, accountId, 'read_at', now().getTime());
    return { ok: true };
  }, auth);

  router.add('POST', '/v1/mail/claim', ({ body, accountId }) => {
    const t = now().getTime();
    const rows =
      body.all === true
        ? store.listMail(accountId, accountCreated(accountId), t)
        : [visibleMail(accountId, int(body, 'id', 1, 2 ** 31))];
    const claimable = rows.filter((r) => r.claimed_at === null && (JSON.parse(r.attachments) as Reward[]).length > 0);
    if (claimable.length === 0 && body.all !== true) throw new GameError('already_claimed', 'Nothing to claim.', 409);
    const rewards: Reward[] = [];
    const { profile } = store.transaction(() =>
      withProfile(accountId, (p) => {
        for (const row of claimable) {
          const attachments = JSON.parse(row.attachments) as Reward[];
          grantRewards(p, data.shop, attachments);
          rewards.push(...attachments);
          store.markMail(row.id, accountId, 'claimed_at', t);
        }
      }),
    );
    return { profile, rewards, claimed: claimable.map((r) => r.id) };
  }, auth);

  router.add('POST', '/v1/mail/delete', ({ body, accountId }) => {
    const row = visibleMail(accountId, int(body, 'id', 1, 2 ** 31));
    if (row.claimed_at === null && (JSON.parse(row.attachments) as Reward[]).length > 0) throw new GameError('claim_first', 'Claim the gifts before deleting.', 409);
    store.markMail(row.id, accountId, 'deleted_at', now().getTime());
    return { ok: true };
  }, auth);

  // ------------------------------------------------ admin (support tools, broadcasts)

  function requireAdmin(req: IncomingMessage): void {
    if (!config.adminToken) throw new GameError('not_found', 'Not found.', 404);
    if (!safeEqual(sha256Hex(headerValue(req, 'x-admin-token')), sha256Hex(config.adminToken))) throw new GameError('forbidden', 'Admin token required.', 403);
  }

  /** {to: accountId | "all", title: {vi,en}, body: {vi,en}, attachments?, expiresDays?, audience?: "all" | "existing"} */
  router.add('POST', '/v1/admin/mail', ({ req, body }) => {
    requireAdmin(req);
    const to = typeof body.to === 'string' && body.to !== 'all' ? body.to : null;
    if (to && !store.accountExists(to)) throw new GameError('account_not_found', 'Unknown account.', 404);
    const title = localized(body.title, 'title');
    const text = localized(body.body, 'body');
    const attachments = parseRewards(data, body.attachments);
    const expiresDays = body.expiresDays === undefined ? 30 : int(body, 'expiresDays', 1, 365);
    const t = now().getTime();
    const id = store.insertMail({
      accountId: to,
      title: JSON.stringify(title),
      body: JSON.stringify(text),
      attachments: JSON.stringify(attachments),
      audience: body.audience === 'existing' ? 'existing' : 'all',
      createdAt: t,
      expiresAt: t + expiresDays * DAY,
    });
    console.log(`[admin] mail ${id} to ${to ?? 'everyone'}`);
    return { id };
  }, { limiter: authLimiter, gameClient: false });

  /** ?login=<username | email | account id> → account, profile and purchases, for support. */
  router.add('GET', '/v1/admin/account', ({ req, url }) => {
    requireAdmin(req);
    const login = url.searchParams.get('login') ?? '';
    const account = store.accountById(login) ?? store.accountByLogin(login.toLowerCase()) ?? store.accountByLogin(login);
    if (!account) throw new GameError('account_not_found', 'Unknown account.', 404);
    return {
      account: { id: account.id, username: account.username, email: account.email, displayName: account.display_name, createdAt: account.created_at, providers: store.providersFor(account.id) },
      profile: profileFor(account.id),
      purchases: store.purchasesFor(account.id),
    };
  }, { limiter: authLimiter, gameClient: false });

  router.add('POST', '/v1/admin/refunds/poll', async ({ req }) => {
    requireAdmin(req);
    return { revoked: await pollGoogleRefunds(config, store, data, fetchImpl, now().getTime()) };
  }, { limiter: authLimiter, gameClient: false });

  // ------------------------------------------------ password reset by email

  router.add('POST', '/v1/auth/password/forgot', async ({ body }) => {
    if (!mailer) throw new GameError('email_unavailable', 'Password reset by email is not set up on this server.', 503);
    const email = str(body, 'email', 254).trim().toLowerCase();
    if (!isValidEmail(email)) throw new GameError('invalid_email', 'Invalid email.', 400);
    const lang = languageOf(body.lang);
    const account = store.accountByEmail(email);
    if (account?.password_hash) {
      const t = now().getTime();
      const prev = store.passwordReset(account.id);
      const sameWindow = !!prev && t - prev.window_start < 3_600_000;
      // At most 3 codes per hour per account; extra requests get the same answer but no mail.
      if (!sameWindow || prev!.sent_count < 3) {
        const code = String(randomInt(0, 1_000_000)).padStart(6, '0');
        store.savePasswordReset(account.id, sha256Hex(`${code}:${account.id}`), t + RESET_TTL, sameWindow ? prev!.sent_count + 1 : 1, sameWindow ? prev!.window_start : t);
        const name = account.username ?? account.display_name;
        const message = resetEmail(lang, name, code);
        try {
          await mailer.send(email, message.subject, message.text);
        } catch (e) {
          console.error('[mail] reset email failed:', e);
        }
      }
    }
    // Same answer whether or not the address has an account: no account enumeration.
    return { ok: true };
  }, { limiter: authLimiter });

  router.add('POST', '/v1/auth/password/reset', async ({ body }) => {
    const email = str(body, 'email', 254).trim().toLowerCase();
    const code = str(body, 'code', 12).trim();
    if (!isValidPassword(body.newPassword)) throw new GameError('weak_password', 'Passwords need at least 8 characters.', 400);
    const t = now().getTime();
    const account = store.accountByEmail(email);
    const row = account ? store.passwordReset(account.id) : undefined;
    if (!account || !row || row.expires_at < t || row.attempts >= 5) throw new GameError('code_invalid', 'The code is wrong or expired.', 400);
    if (!safeEqual(sha256Hex(`${code}:${account.id}`), row.code_hash)) {
      store.countResetAttempt(account.id);
      throw new GameError('code_invalid', 'The code is wrong or expired.', 400);
    }
    const hash = await hashPassword(body.newPassword as string);
    store.transaction(() => {
      store.setPasswordHash(account.id, hash);
      store.clearPasswordReset(account.id);
      // Anyone holding an old session (e.g. on a stolen phone) is signed out.
      store.revokeAllRefreshTokens(account.id);
    });
    return issueSession(account.id, 'password');
  }, { limiter: authLimiter });

  // ------------------------------------------------ cloud saves (offline-mode backups; never merged into the online profile)

  function slotOf(value: unknown): string {
    const slot = typeof value === 'string' && value ? value : 'offline';
    if (!SLOT.test(slot)) throw new GameError('invalid_request', '"slot" is invalid.', 400);
    return slot;
  }

  router.add('GET', '/v1/cloudsave', ({ url, accountId }) => {
    const slot = slotOf(url.searchParams.get('slot'));
    const row = store.cloudSave(accountId, slot);
    if (!row) return { exists: false, slot, revision: 0 };
    return { exists: true, slot, revision: row.revision, updatedAt: row.updated_at, device: row.device, summary: row.summary, data: row.data };
  }, auth);

  router.add('POST', '/v1/cloudsave', ({ body, accountId }) => {
    const slot = slotOf(body.slot);
    const save = str(body, 'data', MAX_SAVE);
    const baseRevision = int(body, 'baseRevision', 0, 1_000_000_000);
    const summary = typeof body.summary === 'string' ? body.summary.slice(0, 512) : '';
    const device = typeof body.device === 'string' ? body.device.slice(0, 64) : '';
    const t = now().getTime();
    return store.transaction(() => {
      const existing = store.cloudSave(accountId, slot);
      if (!existing && store.cloudSaveCount(accountId) >= 3) throw new GameError('too_many_slots', 'Up to 3 cloud saves per account.', 409);
      if (existing && body.force !== true && existing.revision !== baseRevision) {
        throw new GameError('save_conflict', 'The cloud save was changed on another device.', 409, {
          revision: existing.revision,
          updatedAt: existing.updated_at,
          device: existing.device,
          summary: existing.summary,
        });
      }
      const revision = (existing?.revision ?? 0) + 1;
      store.putCloudSave({ account_id: accountId, slot, data: save, summary, device, revision, updated_at: t });
      return { slot, revision, updatedAt: t };
    });
  }, auth);
}
