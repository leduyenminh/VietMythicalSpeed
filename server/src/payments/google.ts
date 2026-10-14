import { sign } from 'node:crypto';
import fs from 'node:fs';
import type { FetchFn } from '../auth/providers.ts';
import type { Config } from '../config.ts';
import { GameError } from '../game/types.ts';

// Google Play Developer API (androidpublisher v3) with a service account – no SDK needed.
// Play Console → Users and permissions: invite the service account with "View financial data" and "Manage orders".

interface ServiceAccount {
  client_email: string;
  private_key: string;
  token_uri?: string;
}

export interface GoogleProductPurchase {
  orderId?: string;
  /** 0 purchased, 1 cancelled, 2 pending */
  purchaseState?: number;
  consumptionState?: number;
  acknowledgementState?: number;
  /** 0 = test purchase (licence tester), 1 = promo, 2 = rewarded; absent for real purchases. */
  purchaseType?: number;
  purchaseTimeMillis?: string;
}

export interface VoidedPurchase {
  purchaseToken: string;
  orderId?: string;
  voidedTimeMillis?: string;
}

const API = 'https://androidpublisher.googleapis.com/androidpublisher/v3/applications';
let cached: { token: string; expiresAt: number; key: string } | null = null;

const b64url = (data: Buffer | string) => Buffer.from(data).toString('base64url');

function serviceAccount(config: Config): ServiceAccount {
  if (!config.googleServiceAccountFile) throw new GameError('store_unavailable', 'Google Play purchases are not configured on the server.', 503);
  return JSON.parse(fs.readFileSync(config.googleServiceAccountFile, 'utf8')) as ServiceAccount;
}

export function googlePlayConfigured(config: Config): boolean {
  return !!config.googleServiceAccountFile;
}

/** OAuth2 JWT-bearer flow (RFC 7523) signed with the service account key; tokens are cached ~55 minutes. */
export async function googleAccessToken(config: Config, fetchImpl: FetchFn, nowMs = Date.now()): Promise<string> {
  const sa = serviceAccount(config);
  if (cached && cached.key === sa.client_email && cached.expiresAt > nowMs + 60_000) return cached.token;
  const iat = Math.floor(nowMs / 1000);
  const tokenUri = sa.token_uri || 'https://oauth2.googleapis.com/token';
  const unsigned = `${b64url(JSON.stringify({ alg: 'RS256', typ: 'JWT' }))}.${b64url(
    JSON.stringify({ iss: sa.client_email, scope: 'https://www.googleapis.com/auth/androidpublisher', aud: tokenUri, iat, exp: iat + 3600 }),
  )}`;
  const assertion = `${unsigned}.${b64url(sign('RSA-SHA256', Buffer.from(unsigned), sa.private_key))}`;
  const res = await fetchImpl(tokenUri, {
    method: 'POST',
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({ grant_type: 'urn:ietf:params:oauth:grant-type:jwt-bearer', assertion }).toString(),
  });
  const json = (await res.json().catch(() => ({}))) as { access_token?: string; expires_in?: number };
  if (!res.ok || !json.access_token) throw new GameError('store_unavailable', 'Could not authenticate with Google Play.', 503);
  cached = { token: json.access_token, expiresAt: nowMs + Math.min(3300, json.expires_in ?? 3300) * 1000, key: sa.client_email };
  return json.access_token;
}

async function call(config: Config, fetchImpl: FetchFn, url: string): Promise<Response> {
  const token = await googleAccessToken(config, fetchImpl);
  try {
    return await fetchImpl(url, { headers: { authorization: `Bearer ${token}` } });
  } catch {
    throw new GameError('store_unavailable', 'Google Play is unreachable.', 503);
  }
}

/** purchases.products.get – the authoritative state of a one-time product purchase. */
export async function verifyGoogleProduct(config: Config, productId: string, purchaseToken: string, fetchImpl: FetchFn): Promise<GoogleProductPurchase> {
  const url = `${API}/${encodeURIComponent(config.googlePlayPackage)}/purchases/products/${encodeURIComponent(productId)}/tokens/${encodeURIComponent(purchaseToken)}`;
  const res = await call(config, fetchImpl, url);
  if (res.status >= 500) throw new GameError('store_unavailable', 'Google Play is having trouble. Try again later.', 503);
  if (!res.ok) throw new GameError('purchase_invalid', 'Google Play does not know this purchase.', 400);
  return (await res.json()) as GoogleProductPurchase;
}

/** purchases.voidedpurchases.list – refunds, chargebacks and cancellations since startTime (ms). */
export async function listVoidedPurchases(config: Config, startTimeMs: number, fetchImpl: FetchFn): Promise<VoidedPurchase[]> {
  const out: VoidedPurchase[] = [];
  let pageToken = '';
  for (let page = 0; page < 20; page++) {
    const query = new URLSearchParams({ startTime: String(startTimeMs), type: '0', maxResults: '1000' });
    if (pageToken) query.set('token', pageToken);
    const res = await call(config, fetchImpl, `${API}/${encodeURIComponent(config.googlePlayPackage)}/purchases/voidedpurchases?${query}`);
    if (!res.ok) throw new GameError('store_unavailable', `Voided purchases request failed (${res.status}).`, 503);
    const json = (await res.json()) as { voidedPurchases?: VoidedPurchase[]; tokenPagination?: { nextPageToken?: string } };
    out.push(...(json.voidedPurchases ?? []));
    pageToken = json.tokenPagination?.nextPageToken ?? '';
    if (!pageToken) break;
  }
  return out;
}

/** For tests: forget the cached access token. */
export function resetGoogleTokenCache(): void {
  cached = null;
}
