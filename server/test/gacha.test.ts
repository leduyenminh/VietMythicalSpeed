import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { GachaEngine, pullForProfile } from '../src/game/gacha.ts';
import { loadGameData } from '../src/game/data.ts';
import { newProfile } from '../src/game/economy.ts';
import { Xoshiro128 } from '../src/game/random.ts';
import type { BannerDef, GachaConfig, GachaState, NavigatorDef, PullResult } from '../src/game/types.ts';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const golden = JSON.parse(fs.readFileSync(path.join(root, 'shared', 'gacha-golden.json'), 'utf8')) as {
  xoshiro128Seed12345: number[];
  config: GachaConfig;
  navigators: NavigatorDef[];
  banners: BannerDef[];
  cases: { bannerId: string; seed: number; pulls: number[]; expected: Pick<PullResult, 'navigatorId' | 'rarity' | 'featured'>[]; finalState: GachaState }[];
};
const data = loadGameData(path.join(root, 'UnityClient', 'Assets', 'VietMythicalSpeed', 'Resources', 'Data'));

test('xoshiro128** matches the C# generator', () => {
  const rng = new Xoshiro128(12345);
  const values = golden.xoshiro128Seed12345.map(() => rng.nextUInt());
  assert.deepEqual(values, golden.xoshiro128Seed12345);
});

test('gacha engine reproduces the C# golden vectors exactly', () => {
  const engine = new GachaEngine(golden.config, golden.navigators);
  for (const c of golden.cases) {
    const banner = golden.banners.find((b) => b.id === c.bannerId)!;
    const state: GachaState = { pity: [], spark: [] };
    const rng = new Xoshiro128(c.seed);
    const actual: PullResult[] = [];
    for (const count of c.pulls) actual.push(...engine.pull(banner, state, count, rng));
    assert.equal(actual.length, c.expected.length, `seed ${c.seed}`);
    actual.forEach((r, i) => {
      assert.equal(r.navigatorId, c.expected[i].navigatorId, `seed ${c.seed} pull ${i}`);
      assert.equal(r.rarity, c.expected[i].rarity, `seed ${c.seed} pull ${i}`);
      assert.equal(r.featured, c.expected[i].featured, `seed ${c.seed} pull ${i}`);
    });
    assert.deepEqual(state.pity, c.finalState.pity);
    assert.deepEqual(state.spark, c.finalState.spark);
  }
});

test('shipped rates: ~3% Thien over many pulls without pity', () => {
  const cfg = { ...data.gacha, hardPity: 0, softPityStart: 0 };
  const engine = new GachaEngine(cfg, data.navigators);
  const banner = data.banner('standard')!;
  const state: GachaState = { pity: [], spark: [] };
  const rng = new Xoshiro128(7);
  let thien = 0;
  const n = 200_000;
  for (let i = 0; i < n; i++) if (engine.pull(banner, state, 1, rng)[0].rarity === 3) thien++;
  assert.ok(Math.abs(thien / n - 0.03) < 0.002, `rate ${thien / n}`);
});

test('pullForProfile pays with tickets first, then jade', () => {
  const profile = newProfile(data.economy, 'acc', 'Tester');
  profile.tickets = 3;
  profile.jade = 10_000;
  const results = pullForProfile(data.engine, profile, data.banner('standard'), 10, new Xoshiro128(1), new Date('2026-10-12T00:00:00Z'));
  assert.equal(results.length, 10);
  assert.equal(profile.tickets, 0);
  assert.equal(profile.jade, 10_000 - 7 * data.gacha.costPerPull);
});

test('limited banners outside their dates are rejected', () => {
  const profile = newProfile(data.economy, 'acc', 'Tester');
  assert.throws(
    () => pullForProfile(data.engine, profile, data.banner('limited_son_lam'), 1, new Xoshiro128(1), new Date('2020-01-01T00:00:00Z')),
    (e: Error & { code?: string }) => e.code === 'banner_inactive',
  );
});
