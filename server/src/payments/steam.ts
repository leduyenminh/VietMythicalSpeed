import { randomBytes } from 'node:crypto';
import type { FetchFn } from '../auth/providers.ts';
import type { Config } from '../config.ts';
import { GameError } from '../game/types.ts';

// Steam in-game purchases (ISteamMicroTxn). Flow: InitTxn → the Steam overlay asks the player →
// the client gets MicroTxnAuthorizationResponse_t → FinalizeTxn charges the wallet. Steam requires its
// wallet for in-game purchases in Steam builds. STEAM_MICROTXN_SANDBOX=1 uses ISteamMicroTxnSandbox.

const HOST = 'https://partner.steam-api.com';

function iface(config: Config): string {
  return config.steamMicroTxnSandbox ? 'ISteamMicroTxnSandbox' : 'ISteamMicroTxn';
}

export function steamPaymentsConfigured(config: Config): boolean {
  return !!config.steamAppId && !!config.steamPublisherKey;
}

/** Random positive 63-bit order id (Steam wants a uint64 unique per app). */
export function newSteamOrderId(): string {
  return (BigInt(`0x${randomBytes(8).toString('hex')}`) & 0x7fff_ffff_ffff_ffffn).toString();
}

interface SteamEnvelope {
  response?: { result?: string; params?: Record<string, string>; error?: { errorcode?: number; errordesc?: string } };
}

async function read(res: Response): Promise<SteamEnvelope> {
  if (res.status >= 500) throw new GameError('store_unavailable', 'Steam is having trouble. Try again later.', 503);
  return (await res.json().catch(() => ({}))) as SteamEnvelope;
}

async function post(config: Config, fetchImpl: FetchFn, method: string, form: Record<string, string>): Promise<SteamEnvelope> {
  try {
    return await read(
      await fetchImpl(`${HOST}/${iface(config)}/${method}/`, {
        method: 'POST',
        headers: { 'content-type': 'application/x-www-form-urlencoded' },
        body: new URLSearchParams({ key: config.steamPublisherKey, appid: config.steamAppId, ...form }).toString(),
      }),
    );
  } catch (e) {
    if (e instanceof GameError) throw e;
    throw new GameError('store_unavailable', 'Steam is unreachable.', 503);
  }
}

/** The player's wallet currency (prices must be given in it). */
export async function steamUserCurrency(config: Config, steamId: string, fetchImpl: FetchFn): Promise<string> {
  const query = new URLSearchParams({ key: config.steamPublisherKey, steamid: steamId });
  let env: SteamEnvelope;
  try {
    env = await read(await fetchImpl(`${HOST}/${iface(config)}/GetUserInfo/v2/?${query}`));
  } catch (e) {
    if (e instanceof GameError) throw e;
    throw new GameError('store_unavailable', 'Steam is unreachable.', 503);
  }
  const currency = env.response?.params?.currency;
  if (env.response?.result !== 'OK' || !currency) throw new GameError('steam_failed', env.response?.error?.errordesc ?? 'Could not read the Steam wallet.', 502);
  return currency;
}

export async function steamInitTxn(
  config: Config,
  fetchImpl: FetchFn,
  order: { orderId: string; steamId: string; itemId: string; amountCents: number; currency: string; description: string; language: string },
): Promise<string> {
  const env = await post(config, fetchImpl, 'InitTxn/v3', {
    orderid: order.orderId,
    steamid: order.steamId,
    itemcount: '1',
    language: order.language,
    currency: order.currency,
    usersession: 'client',
    'itemid[0]': order.itemId,
    'qty[0]': '1',
    'amount[0]': String(order.amountCents),
    'description[0]': order.description.slice(0, 128),
  });
  if (env.response?.result !== 'OK') throw new GameError('steam_failed', env.response?.error?.errordesc ?? 'Steam refused the order.', 502);
  return env.response.params?.transid ?? '';
}

/** Charges an order the player approved in the overlay. Returns false when Steam says it wasn't approved. */
export async function steamFinalizeTxn(config: Config, fetchImpl: FetchFn, orderId: string): Promise<boolean> {
  const env = await post(config, fetchImpl, 'FinalizeTxn/v2', { orderid: orderId });
  if (env.response?.result === 'OK') return true;
  // Already finalized (e.g. a retried request): trust QueryTxn's status instead.
  return (await steamOrderStatus(config, fetchImpl, orderId)) === 'Succeeded';
}

export async function steamOrderStatus(config: Config, fetchImpl: FetchFn, orderId: string): Promise<string> {
  const query = new URLSearchParams({ key: config.steamPublisherKey, appid: config.steamAppId, orderid: orderId });
  try {
    const env = await read(await fetchImpl(`${HOST}/${iface(config)}/QueryTxn/v3/?${query}`));
    return env.response?.result === 'OK' ? (env.response.params?.status ?? '') : '';
  } catch (e) {
    if (e instanceof GameError) throw e;
    throw new GameError('store_unavailable', 'Steam is unreachable.', 503);
  }
}
