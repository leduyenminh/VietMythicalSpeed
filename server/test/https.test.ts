import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import http from 'node:http';
import https from 'node:https';
import type { AddressInfo } from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import type { TLSSocket } from 'node:tls';
import { createApp } from '../src/app.ts';
import { loadConfig } from '../src/config.ts';
import { Store } from '../src/db.ts';
import { loadGameData } from '../src/game/data.ts';
import { createServers } from '../src/server.ts';
import { repoRoot, startApp } from './helpers.ts';

/** Test certificates are generated on the fly, so no private key is ever committed. */
function findOpenSsl(): string | null {
  for (const candidate of ['openssl', 'C:\\Program Files\\Git\\usr\\bin\\openssl.exe', 'C:\\Program Files\\Git\\mingw64\\bin\\openssl.exe']) {
    try {
      execFileSync(candidate, ['version'], { stdio: 'ignore' });
      return candidate;
    } catch {
      // try the next location
    }
  }
  return null;
}

const openssl = findOpenSsl();

function makeCert(openssl: string, dir: string): { cert: string; key: string } {
  const cert = path.join(dir, 'cert.pem');
  const key = path.join(dir, 'key.pem');
  execFileSync(openssl, ['req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-keyout', key, '-out', cert, '-days', '2', '-subj', '/CN=localhost', '-addext', 'subjectAltName=IP:127.0.0.1,DNS:localhost'], { stdio: 'ignore' });
  return { cert, key };
}

interface Reply {
  status: number;
  headers: http.IncomingHttpHeaders;
  body: string;
  fingerprint: string;
}

function collect(res: http.IncomingMessage, resolve: (r: Reply) => void): void {
  const fingerprint = (res.socket as TLSSocket).getPeerCertificate?.()?.fingerprint256 ?? '';
  const chunks: Buffer[] = [];
  res.on('data', (c: Buffer) => chunks.push(c));
  res.on('end', () => resolve({ status: res.statusCode ?? 0, headers: res.headers, body: Buffer.concat(chunks).toString(), fingerprint }));
}

const getHttps = (url: string, ca: Buffer) => new Promise<Reply>((resolve, reject) => https.get(url, { ca, agent: false }, (res) => collect(res, resolve)).on('error', reject));
const getHttp = (url: string) => new Promise<Reply>((resolve, reject) => http.get(url, { agent: false }, (res) => collect(res, resolve)).on('error', reject));
const listen = (server: http.Server, port = 0) => new Promise<number>((resolve) => server.listen(port, '127.0.0.1', () => resolve((server.address() as AddressInfo).port)));

test('serves HTTPS itself, redirects plain HTTP, answers ACME challenges and reloads renewed certificates', { skip: openssl ? false : 'openssl not found' }, async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'vms-tls-'));
  const first = makeCert(openssl!, dir);
  const config = loadConfig({
    PORT: '0',
    PUBLIC_URL: 'https://play.example.test',
    JWT_SECRET: 'test-secret-test-secret-test-secret-123',
    TLS_CERT_FILE: first.cert,
    TLS_KEY_FILE: first.key,
    HTTP_REDIRECT_PORT: '1', // enables the listener; the test binds it to a free port
    ACME_WEBROOT: dir,
    GAME_DATA_DIR: path.join(repoRoot, 'UnityClient', 'Assets', 'VietMythicalSpeed', 'Resources', 'Data'),
  });
  const servers = createServers(config, createApp({ config, store: new Store(':memory:'), data: loadGameData(config.gameDataDir) }));
  try {
    const apiPort = await listen(servers.api);
    const redirectPort = await listen(servers.redirect!);

    const health = await getHttps(`https://127.0.0.1:${apiPort}/health`, fs.readFileSync(first.cert));
    assert.equal(health.status, 200);
    assert.equal(JSON.parse(health.body).ok, true);
    assert.equal(health.headers['strict-transport-security'], 'max-age=31536000');

    const moved = await getHttp(`http://127.0.0.1:${redirectPort}/v1/gacha/banners?lang=vi`);
    assert.equal(moved.status, 301);
    assert.equal(moved.headers.location, 'https://play.example.test/v1/gacha/banners?lang=vi');

    fs.mkdirSync(path.join(dir, '.well-known', 'acme-challenge'), { recursive: true });
    fs.writeFileSync(path.join(dir, '.well-known', 'acme-challenge', 'tok_123'), 'tok_123.thumbprint');
    const challenge = await getHttp(`http://127.0.0.1:${redirectPort}/.well-known/acme-challenge/tok_123`);
    assert.equal(challenge.status, 200);
    assert.equal(challenge.body, 'tok_123.thumbprint');
    assert.equal((await getHttp(`http://127.0.0.1:${redirectPort}/.well-known/acme-challenge/..%2Fcert.pem`)).status, 404);

    // A renewal writes new files in place; reloadTls (file watcher / SIGHUP) swaps them in without a restart.
    const renewedDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vms-tls2-'));
    const renewed = makeCert(openssl!, renewedDir);
    fs.copyFileSync(renewed.cert, first.cert);
    fs.copyFileSync(renewed.key, first.key);
    servers.reloadTls();
    const after = await getHttps(`https://127.0.0.1:${apiPort}/health`, fs.readFileSync(renewed.cert));
    assert.equal(after.status, 200);
    assert.notEqual(after.fingerprint, health.fingerprint);
  } finally {
    await servers.close();
  }
});

test('behind a TLS proxy, REQUIRE_HTTPS rejects calls the proxy received over plain HTTP', async () => {
  const app = await startApp({ TRUST_PROXY: '1', REQUIRE_HTTPS: '1' });
  try {
    const plain = await app.call('GET', '/v1/gacha/banners');
    assert.equal(plain.status, 403);
    assert.equal(plain.json.error.code, 'https_required');
    const viaProxy = await app.call('GET', '/v1/gacha/banners', undefined, undefined, { 'x-forwarded-proto': 'https' });
    assert.equal(viaProxy.status, 200);
    assert.equal(viaProxy.headers.get('strict-transport-security'), 'max-age=31536000');
    assert.equal((await app.call('GET', '/health')).status, 200, 'load balancer health checks stay on plain HTTP');
  } finally {
    await app.close();
  }
});

test('production configuration insists on HTTPS', () => {
  const env = { NODE_ENV: 'production', JWT_SECRET: 'x'.repeat(40) };
  assert.throws(() => loadConfig({ ...env, PUBLIC_URL: 'http://play.example.test' }), /https/);
  assert.equal(loadConfig({ ...env, PUBLIC_URL: 'https://play.example.test' }).requireHttps, true);
  assert.equal(loadConfig({ PUBLIC_URL: 'http://localhost:8787' }).requireHttps, false);
  assert.throws(() => loadConfig({ TLS_CERT_FILE: 'cert.pem' }), /TLS_KEY_FILE/);
  assert.throws(() => loadConfig({ MIN_CLIENT_VERSION: 'soon' }), /MIN_CLIENT_VERSION/);
});
