import http from 'node:http';
import type { AddressInfo } from 'node:net';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createApp } from '../src/app.ts';
import { loadConfig } from '../src/config.ts';
import type { Config } from '../src/config.ts';
import { Store } from '../src/db.ts';
import { loadGameData } from '../src/game/data.ts';
import type { Mailer } from '../src/auth/mailer.ts';
import type { FetchFn } from '../src/auth/providers.ts';

export const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');

export interface TestApp {
  base: string;
  config: Config;
  store: Store;
  /** Moves the server clock. */
  setNow: (iso: string) => void;
  call: (method: string, p: string, body?: unknown, token?: string, headers?: Record<string, string>) => Promise<{ status: number; headers: Headers; json: any }>;
  close: () => Promise<void>;
}

export async function startApp(
  env: Record<string, string> = {},
  nowIso = '2026-10-12T08:00:00Z',
  extra: { fetch?: FetchFn; mailer?: Mailer | null } = {},
): Promise<TestApp> {
  const config = loadConfig({
    PORT: '0',
    PUBLIC_URL: 'https://api.example.test',
    JWT_SECRET: 'test-secret-test-secret-test-secret-123',
    GAME_DATA_DIR: path.join(repoRoot, 'UnityClient', 'Assets', 'VietMythicalSpeed', 'Resources', 'Data'),
    ...env,
  });
  let now = new Date(nowIso);
  const store = new Store(':memory:');
  const server = http.createServer(createApp({ config, store, data: loadGameData(config.gameDataDir), now: () => now, fetch: extra.fetch, mailer: extra.mailer }));
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
  return {
    base,
    config,
    store,
    setNow: (iso) => {
      now = new Date(iso);
    },
    call: async (method, p, body, token, headers) => {
      const res = await fetch(base + p, {
        method,
        redirect: 'manual',
        headers: { 'content-type': 'application/json', ...(token ? { authorization: `Bearer ${token}` } : {}), ...headers },
        body: body === undefined ? undefined : JSON.stringify(body),
      });
      const text = await res.text();
      return { status: res.status, headers: res.headers, json: text ? JSON.parse(text) : null };
    },
    close: () => new Promise<void>((resolve) => server.close(() => resolve())),
  };
}
