import fs from 'node:fs';
import path from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import type { PlayerProfile, PullResult } from './game/types.ts';

export interface AccountRow {
  id: string;
  username: string | null;
  email: string | null;
  password_hash: string | null;
  display_name: string;
  created_at: number;
}

export interface PurchaseRow {
  /** `${store}:${transaction}` – one row per store transaction makes grants idempotent. */
  id: string;
  account_id: string;
  store: string;
  product_id: string;
  /** pending (Steam order started) | granted | revoked | failed */
  status: string;
  gold: number;
  bonus: number;
  amount_cents: number | null;
  currency: string | null;
  external_ref: string | null;
  created_at: number;
  updated_at: number;
}

export interface MailRow {
  id: number;
  account_id: string | null;
  title: string;
  body: string;
  attachments: string;
  audience: string;
  created_at: number;
  expires_at: number;
  read_at: number | null;
  claimed_at: number | null;
}

export interface CloudSaveRow {
  account_id: string;
  slot: string;
  data: string;
  summary: string;
  device: string;
  revision: number;
  updated_at: number;
}

const SCHEMA = `
PRAGMA journal_mode = WAL;
PRAGMA foreign_keys = ON;
CREATE TABLE IF NOT EXISTS accounts (
  id TEXT PRIMARY KEY,
  username TEXT UNIQUE COLLATE NOCASE,
  email TEXT UNIQUE COLLATE NOCASE,
  password_hash TEXT,
  display_name TEXT NOT NULL,
  created_at INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS identities (
  provider TEXT NOT NULL,
  subject TEXT NOT NULL,
  account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  secret_hash TEXT,
  created_at INTEGER NOT NULL,
  PRIMARY KEY (provider, subject)
);
CREATE INDEX IF NOT EXISTS identities_account ON identities(account_id);
CREATE TABLE IF NOT EXISTS refresh_tokens (
  token_hash TEXT PRIMARY KEY,
  account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  provider TEXT,
  expires_at INTEGER NOT NULL,
  revoked INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS refresh_account ON refresh_tokens(account_id);
CREATE TABLE IF NOT EXISTS profiles (
  account_id TEXT PRIMARY KEY REFERENCES accounts(id) ON DELETE CASCADE,
  data TEXT NOT NULL,
  updated_at INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS gacha_history (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  account_id TEXT NOT NULL,
  banner_id TEXT NOT NULL,
  navigator_id TEXT NOT NULL,
  rarity INTEGER NOT NULL,
  featured INTEGER NOT NULL,
  seed INTEGER,
  created_at INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS gacha_history_account ON gacha_history(account_id, created_at);
CREATE TABLE IF NOT EXISTS race_days (
  account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  day TEXT NOT NULL,
  coins INTEGER NOT NULL,
  races INTEGER NOT NULL,
  PRIMARY KEY (account_id, day)
);
CREATE TABLE IF NOT EXISTS race_submissions (
  account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  id TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  PRIMARY KEY (account_id, id)
);
CREATE TABLE IF NOT EXISTS purchases (
  id TEXT PRIMARY KEY,
  account_id TEXT NOT NULL,
  store TEXT NOT NULL,
  product_id TEXT NOT NULL,
  status TEXT NOT NULL,
  gold INTEGER NOT NULL DEFAULT 0,
  bonus INTEGER NOT NULL DEFAULT 0,
  amount_cents INTEGER,
  currency TEXT,
  external_ref TEXT,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS purchases_account ON purchases(account_id, created_at);
CREATE TABLE IF NOT EXISTS shop_history (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  item_id TEXT NOT NULL,
  currency TEXT NOT NULL,
  amount INTEGER NOT NULL,
  created_at INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS shop_history_account ON shop_history(account_id, created_at);
CREATE TABLE IF NOT EXISTS mail (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  account_id TEXT REFERENCES accounts(id) ON DELETE CASCADE,
  title TEXT NOT NULL,
  body TEXT NOT NULL,
  attachments TEXT NOT NULL,
  audience TEXT NOT NULL DEFAULT 'all',
  created_at INTEGER NOT NULL,
  expires_at INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS mail_account ON mail(account_id, created_at);
CREATE TABLE IF NOT EXISTS mail_state (
  mail_id INTEGER NOT NULL REFERENCES mail(id) ON DELETE CASCADE,
  account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  read_at INTEGER,
  claimed_at INTEGER,
  deleted_at INTEGER,
  PRIMARY KEY (mail_id, account_id)
);
CREATE TABLE IF NOT EXISTS password_resets (
  account_id TEXT PRIMARY KEY REFERENCES accounts(id) ON DELETE CASCADE,
  code_hash TEXT NOT NULL,
  expires_at INTEGER NOT NULL,
  attempts INTEGER NOT NULL DEFAULT 0,
  sent_count INTEGER NOT NULL DEFAULT 1,
  window_start INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS cloud_saves (
  account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  slot TEXT NOT NULL,
  data TEXT NOT NULL,
  summary TEXT NOT NULL,
  device TEXT NOT NULL,
  revision INTEGER NOT NULL,
  updated_at INTEGER NOT NULL,
  PRIMARY KEY (account_id, slot)
);
CREATE TABLE IF NOT EXISTS kv (
  key TEXT PRIMARY KEY,
  value TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS openid_states (
  sid TEXT PRIMARY KEY,
  redirect_uri TEXT NOT NULL,
  client_state TEXT NOT NULL,
  challenge TEXT NOT NULL,
  expires_at INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS openid_codes (
  code_hash TEXT PRIMARY KEY,
  steam_id TEXT NOT NULL,
  challenge TEXT NOT NULL,
  expires_at INTEGER NOT NULL,
  used INTEGER NOT NULL DEFAULT 0
);
`;

/** Thin repository over node:sqlite. All calls are synchronous, so a request handler that does not await
 * between reading and writing a profile is atomic with respect to other requests. */
export class Store {
  readonly db: DatabaseSync;

  constructor(file: string) {
    if (file !== ':memory:') fs.mkdirSync(path.dirname(path.resolve(file)), { recursive: true });
    this.db = new DatabaseSync(file);
    this.db.exec(SCHEMA);
  }

  transaction<T>(fn: () => T): T {
    this.db.exec('BEGIN IMMEDIATE');
    try {
      const result = fn();
      this.db.exec('COMMIT');
      return result;
    } catch (e) {
      this.db.exec('ROLLBACK');
      throw e;
    }
  }

  // ---------------------------------------------------------------- accounts

  createAccount(row: AccountRow): void {
    this.db
      .prepare('INSERT INTO accounts (id, username, email, password_hash, display_name, created_at) VALUES (?, ?, ?, ?, ?, ?)')
      .run(row.id, row.username, row.email, row.password_hash, row.display_name, row.created_at);
  }

  accountById(id: string): AccountRow | undefined {
    return this.db.prepare('SELECT * FROM accounts WHERE id = ?').get(id) as AccountRow | undefined;
  }

  accountByLogin(login: string): AccountRow | undefined {
    return this.db.prepare('SELECT * FROM accounts WHERE username = ? OR email = ?').get(login, login) as AccountRow | undefined;
  }

  usernameTaken(username: string): boolean {
    return !!this.db.prepare('SELECT 1 FROM accounts WHERE username = ?').get(username);
  }

  emailTaken(email: string): boolean {
    return !!this.db.prepare('SELECT 1 FROM accounts WHERE email = ?').get(email);
  }

  accountExists(id: string): boolean {
    return !!this.db.prepare('SELECT 1 FROM accounts WHERE id = ?').get(id);
  }

  /** Erases the account and everything tied to it (identities, sessions, profile, history). */
  deleteAccount(id: string): boolean {
    return this.transaction(() => {
      this.db.prepare('DELETE FROM gacha_history WHERE account_id = ?').run(id);
      // identities, refresh_tokens, profiles, race_days and race_submissions cascade.
      return Number(this.db.prepare('DELETE FROM accounts WHERE id = ?').run(id).changes) > 0;
    });
  }

  // ---------------------------------------------------------------- identities

  addIdentity(provider: string, subject: string, accountId: string, secretHash: string | null, now: number): void {
    this.db
      .prepare('INSERT INTO identities (provider, subject, account_id, secret_hash, created_at) VALUES (?, ?, ?, ?, ?)')
      .run(provider, subject, accountId, secretHash, now);
  }

  identity(provider: string, subject: string): { account_id: string; secret_hash: string | null } | undefined {
    return this.db.prepare('SELECT account_id, secret_hash FROM identities WHERE provider = ? AND subject = ?').get(provider, subject) as
      | { account_id: string; secret_hash: string | null }
      | undefined;
  }

  providersFor(accountId: string): string[] {
    const rows = this.db.prepare('SELECT DISTINCT provider FROM identities WHERE account_id = ? ORDER BY provider').all(accountId) as { provider: string }[];
    return rows.map((r) => r.provider);
  }

  // ---------------------------------------------------------------- refresh tokens

  storeRefreshToken(hash: string, accountId: string, provider: string, expiresAt: number): void {
    this.db.prepare('INSERT INTO refresh_tokens (token_hash, account_id, provider, expires_at) VALUES (?, ?, ?, ?)').run(hash, accountId, provider, expiresAt);
  }

  refreshToken(hash: string): { account_id: string; provider: string; expires_at: number; revoked: number } | undefined {
    return this.db.prepare('SELECT account_id, provider, expires_at, revoked FROM refresh_tokens WHERE token_hash = ?').get(hash) as
      | { account_id: string; provider: string; expires_at: number; revoked: number }
      | undefined;
  }

  revokeRefreshToken(hash: string): void {
    this.db.prepare('UPDATE refresh_tokens SET revoked = 1 WHERE token_hash = ?').run(hash);
  }

  revokeAllRefreshTokens(accountId: string): void {
    this.db.prepare('UPDATE refresh_tokens SET revoked = 1 WHERE account_id = ?').run(accountId);
  }

  // ---------------------------------------------------------------- profiles

  loadProfile(accountId: string): PlayerProfile | undefined {
    const row = this.db.prepare('SELECT data FROM profiles WHERE account_id = ?').get(accountId) as { data: string } | undefined;
    return row ? (JSON.parse(row.data) as PlayerProfile) : undefined;
  }

  saveProfile(profile: PlayerProfile, now: number): void {
    this.db
      .prepare('INSERT INTO profiles (account_id, data, updated_at) VALUES (?, ?, ?) ON CONFLICT(account_id) DO UPDATE SET data = excluded.data, updated_at = excluded.updated_at')
      .run(profile.accountId, JSON.stringify(profile), now);
  }

  recordPulls(accountId: string, bannerId: string, results: PullResult[], seed: number | null, now: number): void {
    const stmt = this.db.prepare(
      'INSERT INTO gacha_history (account_id, banner_id, navigator_id, rarity, featured, seed, created_at) VALUES (?, ?, ?, ?, ?, ?, ?)',
    );
    for (const r of results) stmt.run(accountId, bannerId, r.navigatorId, r.rarity, r.featured ? 1 : 0, seed, now);
  }

  // ---------------------------------------------------------------- race ledger

  /** Reward counters of one UTC day, so races synced late still respect that day's caps. */
  raceDay(accountId: string, day: string): { coins: number; races: number } | undefined {
    return this.db.prepare('SELECT coins, races FROM race_days WHERE account_id = ? AND day = ?').get(accountId, day) as
      | { coins: number; races: number }
      | undefined;
  }

  saveRaceDay(accountId: string, day: string, coins: number, races: number): void {
    this.db
      .prepare('INSERT INTO race_days (account_id, day, coins, races) VALUES (?, ?, ?, ?) ON CONFLICT(account_id, day) DO UPDATE SET coins = excluded.coins, races = excluded.races')
      .run(accountId, day, coins, races);
  }

  /** Records a client race id; false when it was already seen (a retried or re-synced race). */
  claimSubmission(accountId: string, id: string, now: number): boolean {
    const result = this.db.prepare('INSERT OR IGNORE INTO race_submissions (account_id, id, created_at) VALUES (?, ?, ?)').run(accountId, id, now);
    return Number(result.changes) > 0;
  }

  /** Ledger rows older than the sync window can no longer change. */
  pruneRaceLedger(accountId: string, oldestDay: string, oldestSubmission: number): void {
    this.db.prepare('DELETE FROM race_days WHERE account_id = ? AND day < ?').run(accountId, oldestDay);
    this.db.prepare('DELETE FROM race_submissions WHERE account_id = ? AND created_at < ?').run(accountId, oldestSubmission);
  }

  // ---------------------------------------------------------------- purchases (kept after account deletion as financial records)

  purchase(id: string): PurchaseRow | undefined {
    return this.db.prepare('SELECT * FROM purchases WHERE id = ?').get(id) as PurchaseRow | undefined;
  }

  insertPurchase(row: PurchaseRow): void {
    this.db
      .prepare(
        'INSERT INTO purchases (id, account_id, store, product_id, status, gold, bonus, amount_cents, currency, external_ref, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)',
      )
      .run(row.id, row.account_id, row.store, row.product_id, row.status, row.gold, row.bonus, row.amount_cents, row.currency, row.external_ref, row.created_at, row.updated_at);
  }

  updatePurchase(id: string, status: string, gold: number, bonus: number, externalRef: string | null, now: number): void {
    this.db
      .prepare('UPDATE purchases SET status = ?, gold = ?, bonus = ?, external_ref = COALESCE(?, external_ref), updated_at = ? WHERE id = ?')
      .run(status, gold, bonus, externalRef, now, id);
  }

  purchasesFor(accountId: string, limit = 50): PurchaseRow[] {
    return this.db.prepare('SELECT * FROM purchases WHERE account_id = ? ORDER BY created_at DESC LIMIT ?').all(accountId, limit) as unknown as PurchaseRow[];
  }

  recordShopBuy(accountId: string, itemId: string, currency: string, amount: number, now: number): void {
    this.db.prepare('INSERT INTO shop_history (account_id, item_id, currency, amount, created_at) VALUES (?, ?, ?, ?, ?)').run(accountId, itemId, currency, amount, now);
  }

  // ---------------------------------------------------------------- mail

  insertMail(m: { accountId: string | null; title: string; body: string; attachments: string; audience: string; createdAt: number; expiresAt: number }): number {
    const result = this.db
      .prepare('INSERT INTO mail (account_id, title, body, attachments, audience, created_at, expires_at) VALUES (?, ?, ?, ?, ?, ?, ?)')
      .run(m.accountId, m.title, m.body, m.attachments, m.audience, m.createdAt, m.expiresAt);
    return Number(result.lastInsertRowid);
  }

  /** Personal mail plus broadcasts this account may see ('existing' broadcasts skip accounts created later). */
  listMail(accountId: string, accountCreatedAt: number, now: number, limit = 100): MailRow[] {
    return this.db
      .prepare(
        `SELECT m.*, s.read_at, s.claimed_at FROM mail m
         LEFT JOIN mail_state s ON s.mail_id = m.id AND s.account_id = ?
         WHERE (m.account_id = ? OR (m.account_id IS NULL AND (m.audience = 'all' OR m.created_at >= ?)))
           AND m.expires_at > ? AND s.deleted_at IS NULL
         ORDER BY m.created_at DESC, m.id DESC LIMIT ?`,
      )
      .all(accountId, accountId, accountCreatedAt, now, limit) as unknown as MailRow[];
  }

  mailFor(mailId: number, accountId: string, accountCreatedAt: number, now: number): MailRow | undefined {
    return this.listMail(accountId, accountCreatedAt, now, 1000).find((m) => m.id === mailId);
  }

  markMail(mailId: number, accountId: string, field: 'read_at' | 'claimed_at' | 'deleted_at', now: number): void {
    this.db.prepare('INSERT OR IGNORE INTO mail_state (mail_id, account_id) VALUES (?, ?)').run(mailId, accountId);
    this.db.prepare(`UPDATE mail_state SET ${field} = COALESCE(${field}, ?) WHERE mail_id = ? AND account_id = ?`).run(now, mailId, accountId);
    if (field === 'claimed_at') this.db.prepare('UPDATE mail_state SET read_at = COALESCE(read_at, ?) WHERE mail_id = ? AND account_id = ?').run(now, mailId, accountId);
  }

  // ---------------------------------------------------------------- password reset

  accountByEmail(email: string): AccountRow | undefined {
    return this.db.prepare('SELECT * FROM accounts WHERE email = ?').get(email) as AccountRow | undefined;
  }

  passwordReset(accountId: string): { code_hash: string; expires_at: number; attempts: number; sent_count: number; window_start: number } | undefined {
    return this.db.prepare('SELECT code_hash, expires_at, attempts, sent_count, window_start FROM password_resets WHERE account_id = ?').get(accountId) as
      | { code_hash: string; expires_at: number; attempts: number; sent_count: number; window_start: number }
      | undefined;
  }

  savePasswordReset(accountId: string, codeHash: string, expiresAt: number, sentCount: number, windowStart: number): void {
    this.db
      .prepare(
        `INSERT INTO password_resets (account_id, code_hash, expires_at, attempts, sent_count, window_start) VALUES (?, ?, ?, 0, ?, ?)
         ON CONFLICT(account_id) DO UPDATE SET code_hash = excluded.code_hash, expires_at = excluded.expires_at, attempts = 0,
           sent_count = excluded.sent_count, window_start = excluded.window_start`,
      )
      .run(accountId, codeHash, expiresAt, sentCount, windowStart);
  }

  countResetAttempt(accountId: string): void {
    this.db.prepare('UPDATE password_resets SET attempts = attempts + 1 WHERE account_id = ?').run(accountId);
  }

  clearPasswordReset(accountId: string): void {
    this.db.prepare('DELETE FROM password_resets WHERE account_id = ?').run(accountId);
  }

  setPasswordHash(accountId: string, hash: string): void {
    this.db.prepare('UPDATE accounts SET password_hash = ? WHERE id = ?').run(hash, accountId);
  }

  // ---------------------------------------------------------------- cloud saves

  cloudSave(accountId: string, slot: string): CloudSaveRow | undefined {
    return this.db.prepare('SELECT * FROM cloud_saves WHERE account_id = ? AND slot = ?').get(accountId, slot) as CloudSaveRow | undefined;
  }

  cloudSaveCount(accountId: string): number {
    return (this.db.prepare('SELECT COUNT(*) AS n FROM cloud_saves WHERE account_id = ?').get(accountId) as { n: number }).n;
  }

  putCloudSave(row: CloudSaveRow): void {
    this.db
      .prepare(
        `INSERT INTO cloud_saves (account_id, slot, data, summary, device, revision, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?)
         ON CONFLICT(account_id, slot) DO UPDATE SET data = excluded.data, summary = excluded.summary, device = excluded.device,
           revision = excluded.revision, updated_at = excluded.updated_at`,
      )
      .run(row.account_id, row.slot, row.data, row.summary, row.device, row.revision, row.updated_at);
  }

  // ---------------------------------------------------------------- misc

  subjectFor(accountId: string, provider: string): string | undefined {
    const row = this.db.prepare('SELECT subject FROM identities WHERE account_id = ? AND provider = ?').get(accountId, provider) as { subject: string } | undefined;
    return row?.subject;
  }

  getValue(key: string): string | undefined {
    return (this.db.prepare('SELECT value FROM kv WHERE key = ?').get(key) as { value: string } | undefined)?.value;
  }

  setValue(key: string, value: string): void {
    this.db.prepare('INSERT INTO kv (key, value) VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value').run(key, value);
  }

  // ---------------------------------------------------------------- Steam OpenID

  saveOpenIdState(sid: string, redirectUri: string, clientState: string, challenge: string, expiresAt: number): void {
    this.db.prepare('DELETE FROM openid_states WHERE expires_at < ?').run(Date.now());
    this.db
      .prepare('INSERT INTO openid_states (sid, redirect_uri, client_state, challenge, expires_at) VALUES (?, ?, ?, ?, ?)')
      .run(sid, redirectUri, clientState, challenge, expiresAt);
  }

  takeOpenIdState(sid: string): { redirect_uri: string; client_state: string; challenge: string; expires_at: number } | undefined {
    const row = this.db.prepare('SELECT redirect_uri, client_state, challenge, expires_at FROM openid_states WHERE sid = ?').get(sid) as
      | { redirect_uri: string; client_state: string; challenge: string; expires_at: number }
      | undefined;
    if (row) this.db.prepare('DELETE FROM openid_states WHERE sid = ?').run(sid);
    return row;
  }

  saveOpenIdCode(codeHash: string, steamId: string, challenge: string, expiresAt: number): void {
    this.db.prepare('DELETE FROM openid_codes WHERE expires_at < ?').run(Date.now());
    this.db.prepare('INSERT INTO openid_codes (code_hash, steam_id, challenge, expires_at) VALUES (?, ?, ?, ?)').run(codeHash, steamId, challenge, expiresAt);
  }

  /** Marks the code used and returns it, or undefined when unknown/used/expired. */
  consumeOpenIdCode(codeHash: string, now: number): { steam_id: string; challenge: string } | undefined {
    const row = this.db.prepare('SELECT steam_id, challenge, expires_at, used FROM openid_codes WHERE code_hash = ?').get(codeHash) as
      | { steam_id: string; challenge: string; expires_at: number; used: number }
      | undefined;
    if (!row || row.used || row.expires_at < now) return undefined;
    this.db.prepare('UPDATE openid_codes SET used = 1 WHERE code_hash = ?').run(codeHash);
    return { steam_id: row.steam_id, challenge: row.challenge };
  }
}
