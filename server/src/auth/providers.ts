import type { Config } from '../config.ts';
import { GameError } from '../game/types.ts';

export type FetchFn = typeof fetch;

export interface VerifiedIdentity {
  subject: string;
  displayName: string;
}

/**
 * Google Play Games Services v2: exchange the one-time server auth code (from
 * PlayGamesPlatform.RequestServerSideAccess) for an access token, then ask the Games API who the player is.
 */
export async function verifyGooglePlayGames(cfg: Config, serverAuthCode: string, fetchImpl: FetchFn): Promise<VerifiedIdentity> {
  if (!cfg.googleClientId || !cfg.googleClientSecret) throw new GameError('gpgs_unavailable', 'Google Play Games is not configured on the server.', 503);
  const tokenResponse = await fetchImpl('https://oauth2.googleapis.com/token', {
    method: 'POST',
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      code: serverAuthCode,
      client_id: cfg.googleClientId,
      client_secret: cfg.googleClientSecret,
      grant_type: 'authorization_code',
      redirect_uri: '',
    }),
  });
  if (!tokenResponse.ok) throw new GameError('gpgs_failed', `Token exchange failed (${tokenResponse.status}).`, 401);
  const token = (await tokenResponse.json()) as { access_token?: string };
  if (!token.access_token) throw new GameError('gpgs_failed', 'No access token returned.', 401);

  const playerResponse = await fetchImpl('https://games.googleapis.com/games/v1/players/me', {
    headers: { authorization: `Bearer ${token.access_token}` },
  });
  if (!playerResponse.ok) throw new GameError('gpgs_failed', `Player lookup failed (${playerResponse.status}).`, 401);
  const player = (await playerResponse.json()) as { playerId?: string; displayName?: string };
  if (!player.playerId) throw new GameError('gpgs_failed', 'No Play Games player id.', 401);
  return { subject: player.playerId, displayName: player.displayName || 'Play Games' };
}

/** PC builds: verify a Steamworks GetAuthTicketForWebApi ticket with the partner Web API. */
export async function verifySteamTicket(cfg: Config, ticketHex: string, fetchImpl: FetchFn): Promise<VerifiedIdentity> {
  if (!cfg.steamAppId || !cfg.steamPublisherKey) throw new GameError('steam_unavailable', 'Steam is not configured on the server.', 503);
  if (!/^[0-9a-fA-F]{16,8192}$/.test(ticketHex)) throw new GameError('steam_failed', 'Malformed ticket.', 400);
  const url = new URL('https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1/');
  url.searchParams.set('key', cfg.steamPublisherKey);
  url.searchParams.set('appid', cfg.steamAppId);
  url.searchParams.set('ticket', ticketHex);
  url.searchParams.set('identity', 'vms-backend');
  const response = await fetchImpl(url);
  if (!response.ok) throw new GameError('steam_failed', `Steam returned ${response.status}.`, 401);
  const body = (await response.json()) as { response?: { params?: { result?: string; steamid?: string; vacbanned?: boolean; publisherbanned?: boolean }; error?: unknown } };
  const params = body.response?.params;
  if (!params || params.result !== 'OK' || !params.steamid) throw new GameError('steam_failed', 'Steam rejected the ticket.', 401);
  if (params.publisherbanned) throw new GameError('steam_failed', 'This Steam account is banned.', 403);
  return { subject: params.steamid, displayName: await steamPersona(cfg, params.steamid, fetchImpl) };
}

export const STEAM_OPENID = 'https://steamcommunity.com/openid/login';
const CLAIMED_ID = /^https:\/\/steamcommunity\.com\/openid\/id\/(\d{17})$/;

export function steamOpenIdRedirect(cfg: Config, sid: string): string {
  const returnTo = `${cfg.publicUrl}/v1/auth/steam/openid/callback?sid=${encodeURIComponent(sid)}`;
  const params = new URLSearchParams({
    'openid.ns': 'http://specs.openid.net/auth/2.0',
    'openid.mode': 'checkid_setup',
    'openid.return_to': returnTo,
    'openid.realm': cfg.publicUrl,
    'openid.identity': 'http://specs.openid.net/auth/2.0/identifier_select',
    'openid.claimed_id': 'http://specs.openid.net/auth/2.0/identifier_select',
  });
  return `${STEAM_OPENID}?${params.toString()}`;
}

/** Verifies a positive OpenID 2.0 assertion by asking Steam directly (check_authentication). Returns the SteamID64. */
export async function verifySteamOpenId(cfg: Config, query: URLSearchParams, fetchImpl: FetchFn): Promise<string> {
  if (query.get('openid.mode') !== 'id_res') throw new GameError('openid_failed', 'Sign-in was cancelled or failed.', 401);
  if (query.get('openid.op_endpoint') !== STEAM_OPENID) throw new GameError('openid_failed', 'Unexpected OpenID provider.', 401);
  const returnTo = query.get('openid.return_to') ?? '';
  if (!returnTo.startsWith(`${cfg.publicUrl}/v1/auth/steam/openid/callback`)) throw new GameError('openid_failed', 'Unexpected return URL.', 401);
  const claimed = query.get('openid.claimed_id') ?? '';
  const match = CLAIMED_ID.exec(claimed);
  if (!match || query.get('openid.identity') !== claimed) throw new GameError('openid_failed', 'Invalid Steam identity.', 401);

  const verify = new URLSearchParams();
  for (const [k, v] of query) if (k.startsWith('openid.')) verify.set(k, v);
  verify.set('openid.mode', 'check_authentication');
  const response = await fetchImpl(STEAM_OPENID, {
    method: 'POST',
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    body: verify,
  });
  const text = await response.text();
  if (!response.ok || !/is_valid\s*:\s*true/.test(text)) throw new GameError('openid_failed', 'Steam did not confirm the sign-in.', 401);
  return match[1];
}

/** Persona name when a Web API key is configured, otherwise a neutral default. */
export async function steamPersona(cfg: Config, steamId: string, fetchImpl: FetchFn): Promise<string> {
  const key = cfg.steamWebApiKey || cfg.steamPublisherKey;
  const fallback = `Steam ${steamId.slice(-4)}`;
  if (!key) return fallback;
  try {
    const url = new URL('https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/');
    url.searchParams.set('key', key);
    url.searchParams.set('steamids', steamId);
    const response = await fetchImpl(url);
    if (!response.ok) return fallback;
    const body = (await response.json()) as { response?: { players?: { personaname?: string }[] } };
    return body.response?.players?.[0]?.personaname || fallback;
  } catch {
    return fallback;
  }
}
