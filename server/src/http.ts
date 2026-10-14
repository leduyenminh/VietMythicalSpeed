import type { IncomingMessage, ServerResponse } from 'node:http';
import type { TLSSocket } from 'node:tls';
import { GameError } from './game/types.ts';

export interface Ctx {
  req: IncomingMessage;
  res: ServerResponse;
  url: URL;
  ip: string;
  body: Record<string, unknown>;
  accountId: string;
}

export type Handler = (ctx: Ctx) => unknown;

interface Route {
  method: string;
  path: string;
  handler: Handler;
  auth: boolean;
  limiter: RateLimiter;
  gameClient: boolean;
}

export interface RouterOptions {
  /** Returns the account id for a valid access token, or null. */
  authenticate: (token: string) => string | null;
  trustProxy: boolean;
  requireHttps: boolean;
  /** Throws (e.g. client_outdated) when the calling game build may not use the API. */
  checkClient?: (req: IncomingMessage) => void;
}

export interface RouteOptions {
  auth?: boolean;
  limiter: RateLimiter;
  /** false for routes a browser opens (Steam OpenID) or monitoring calls: no game-version check. */
  gameClient?: boolean;
}

const MAX_BODY = 64 * 1024;

/** Fixed-window per-key limiter; enough to blunt credential stuffing on a single instance. */
export class RateLimiter {
  private readonly hits = new Map<string, { count: number; start: number }>();
  private readonly limit: number;
  private readonly windowMs: number;

  constructor(limit: number, windowMs: number) {
    this.limit = limit;
    this.windowMs = windowMs;
  }

  allow(key: string, now = Date.now()): boolean {
    const entry = this.hits.get(key);
    if (!entry || now - entry.start >= this.windowMs) {
      this.hits.set(key, { count: 1, start: now });
      if (this.hits.size > 50_000) this.sweep(now);
      return true;
    }
    entry.count++;
    return entry.count <= this.limit;
  }

  private sweep(now: number): void {
    for (const [k, v] of this.hits) if (now - v.start >= this.windowMs) this.hits.delete(k);
  }
}

export class Router {
  private readonly routes: Route[] = [];
  private readonly options: RouterOptions;

  constructor(options: RouterOptions) {
    this.options = options;
  }

  add(method: string, path: string, handler: Handler, options: RouteOptions): void {
    this.routes.push({ method, path, handler, auth: options.auth ?? false, limiter: options.limiter, gameClient: options.gameClient ?? true });
  }

  async handle(req: IncomingMessage, res: ServerResponse): Promise<void> {
    const url = new URL(req.url ?? '/', 'http://localhost');
    const route = this.routes.find((r) => r.method === req.method && r.path === url.pathname);
    const secure = isSecure(req, this.options.trustProxy);
    if (secure) res.setHeader('strict-transport-security', 'max-age=31536000');
    try {
      if (!route) throw new GameError('not_found', 'Not found.', 404);
      // Load balancer health checks usually arrive over plain HTTP on the private network.
      if (this.options.requireHttps && !secure && route.path !== '/health') throw new GameError('https_required', 'Use HTTPS.', 403);
      const ip = this.clientIp(req);
      if (!route.limiter.allow(`${ip}|${route.path}`)) throw new GameError('rate_limited', 'Too many requests.', 429);
      if (route.gameClient) this.options.checkClient?.(req);
      const ctx: Ctx = { req, res, url, ip, body: {}, accountId: '' };
      if (route.auth) {
        const header = req.headers.authorization ?? '';
        const accountId = header.startsWith('Bearer ') ? this.options.authenticate(header.slice(7)) : null;
        if (!accountId) throw new GameError('unauthorized', 'Sign in again.', 401);
        ctx.accountId = accountId;
      }
      if (req.method === 'POST') ctx.body = await readJson(req);
      const result = await route.handler(ctx);
      if (!res.headersSent) sendJson(res, 200, result ?? { ok: true });
    } catch (e) {
      if (res.headersSent) return;
      if (e instanceof GameError) {
        sendJson(res, e.status, { error: { ...e.details, code: e.code, message: e.message } });
      } else {
        console.error('[http]', req.method, url.pathname, e);
        sendJson(res, 500, { error: { code: 'server', message: 'Internal error.' } });
      }
    }
  }

  private clientIp(req: IncomingMessage): string {
    if (this.options.trustProxy) {
      const forwarded = req.headers['x-forwarded-for'];
      const first = (Array.isArray(forwarded) ? forwarded[0] : forwarded)?.split(',')[0]?.trim();
      if (first) return first;
    }
    return req.socket.remoteAddress ?? 'unknown';
  }
}

/** True for TLS connections to this process, or (behind a trusted proxy) when the proxy received HTTPS. */
export function isSecure(req: IncomingMessage, trustProxy: boolean): boolean {
  if ((req.socket as TLSSocket).encrypted) return true;
  if (!trustProxy) return false;
  return headerValue(req, 'x-forwarded-proto').split(',')[0].trim().toLowerCase() === 'https';
}

export function headerValue(req: IncomingMessage, name: string): string {
  const value = req.headers[name];
  return (Array.isArray(value) ? value[0] : value) ?? '';
}

async function readJson(req: IncomingMessage): Promise<Record<string, unknown>> {
  const chunks: Buffer[] = [];
  let size = 0;
  for await (const chunk of req) {
    size += (chunk as Buffer).length;
    if (size > MAX_BODY) throw new GameError('invalid_request', 'Body too large.', 413);
    chunks.push(chunk as Buffer);
  }
  if (size === 0) return {};
  try {
    const parsed = JSON.parse(Buffer.concat(chunks).toString('utf8'));
    if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('not an object');
    return parsed as Record<string, unknown>;
  } catch {
    throw new GameError('invalid_request', 'Body must be a JSON object.', 400);
  }
}

export function sendJson(res: ServerResponse, status: number, body: unknown): void {
  const data = JSON.stringify(body);
  res.writeHead(status, {
    'content-type': 'application/json; charset=utf-8',
    'content-length': Buffer.byteLength(data),
    'cache-control': 'no-store',
    'x-content-type-options': 'nosniff',
  });
  res.end(data);
}

export function redirect(res: ServerResponse, location: string): void {
  res.writeHead(302, { location, 'cache-control': 'no-store' });
  res.end();
}

// ---------------------------------------------------------------- input helpers

export function str(body: Record<string, unknown>, key: string, max = 512): string {
  const v = body[key];
  if (typeof v !== 'string' || v.length === 0 || v.length > max) throw new GameError('invalid_request', `"${key}" is required.`, 400);
  return v;
}

export function int(body: Record<string, unknown>, key: string, min: number, max: number): number {
  const v = body[key];
  if (typeof v !== 'number' || !Number.isFinite(v) || v < min || v > max) throw new GameError('invalid_request', `"${key}" is out of range.`, 400);
  return Math.floor(v);
}

export function num(body: Record<string, unknown>, key: string, min: number, max: number): number {
  const v = body[key];
  if (typeof v !== 'number' || !Number.isFinite(v) || v < min || v > max) throw new GameError('invalid_request', `"${key}" is out of range.`, 400);
  return v;
}
