import fs from 'node:fs';
import http from 'node:http';
import https from 'node:https';
import path from 'node:path';
import type { Config } from './config.ts';

type Handler = (req: http.IncomingMessage, res: http.ServerResponse) => void;

export interface Servers {
  /** The API: HTTPS when a certificate is configured, plain HTTP otherwise (local development, or TLS at a proxy). */
  api: http.Server | https.Server;
  /** Optional plain-HTTP listener that redirects to HTTPS and answers ACME http-01 challenges. */
  redirect: http.Server | null;
  /** Re-reads the certificate files (renewals); no-op without TLS. */
  reloadTls: () => void;
  close: () => Promise<void>;
}

function readTls(config: Config) {
  return { cert: fs.readFileSync(config.tlsCertFile), key: fs.readFileSync(config.tlsKeyFile) };
}

export function createServers(config: Config, handler: Handler): Servers {
  let reloadTls = () => {};
  let api: http.Server | https.Server;
  if (config.tlsCertFile) {
    const secure = https.createServer({ ...readTls(config), minVersion: 'TLSv1.2' }, handler);
    reloadTls = () => {
      try {
        secure.setSecureContext({ ...readTls(config), minVersion: 'TLSv1.2' });
        console.log('[tls] certificate reloaded');
      } catch (e) {
        console.error('[tls] reload failed, keeping the previous certificate:', e);
      }
    };
    api = secure;
  } else {
    api = http.createServer(handler);
  }
  api.headersTimeout = 15_000;
  api.requestTimeout = 30_000;
  const redirect = config.httpRedirectPort ? http.createServer(redirectHandler(config)) : null;
  const close = async () => {
    await Promise.all([api, redirect].filter((s) => s !== null).map((s) => new Promise<void>((resolve) => s.close(() => resolve()))));
  };
  return { api, redirect, reloadTls, close };
}

/** Port-80 companion: certbot --webroot challenges are served from ACME_WEBROOT, everything else goes to PUBLIC_URL. */
export function redirectHandler(config: Config): Handler {
  const prefix = '/.well-known/acme-challenge/';
  return (req, res) => {
    const url = new URL(req.url ?? '/', 'http://localhost');
    if (config.acmeWebroot && url.pathname.startsWith(prefix)) {
      const token = url.pathname.slice(prefix.length);
      const file = path.join(config.acmeWebroot, '.well-known', 'acme-challenge', token);
      if (/^[A-Za-z0-9_-]+$/.test(token) && fs.existsSync(file)) {
        res.writeHead(200, { 'content-type': 'text/plain' });
        res.end(fs.readFileSync(file));
      } else {
        res.writeHead(404).end();
      }
      return;
    }
    res.writeHead(301, { location: config.publicUrl + url.pathname + url.search, 'cache-control': 'no-store' });
    res.end();
  };
}
