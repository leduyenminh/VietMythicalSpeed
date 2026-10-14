import assert from 'node:assert/strict';
import path from 'node:path';
import { test } from 'node:test';
import { loadGameData } from '../src/game/data.ts';
import { newProfile } from '../src/game/economy.ts';
import {
  applyProduct,
  buyItem,
  canBuyProduct,
  claimDaily,
  claimEvent,
  equipItem,
  eventOnRace,
  exchangeGold,
  grantRewards,
  revokeProduct,
  spend,
  validateShop,
  visibleEvents,
} from '../src/game/shop.ts';
import type { PlayerProfile, RaceReport } from '../src/game/types.ts';
import { repoRoot } from './helpers.ts';

// Same scenarios as UnityClient/.../Tests/EditMode/ShopTests.cs – the C# and TypeScript rules must agree.

const data = loadGameData(path.join(repoRoot, 'UnityClient', 'Assets', 'VietMythicalSpeed', 'Resources', 'Data'));
const D = (iso: string) => new Date(iso);
const fresh = (): PlayerProfile => newProfile(data.economy, 'acc', 'Tester');
const product = (id: string) => data.shop.products.find((p) => p.id === id)!;
const report = (over: Partial<RaceReport> = {}): RaceReport => ({ songId: 'demo', durationSec: 95, mode: 0, difficulty: 1, score: 72_480, maxCombo: 163, perfect: 163, good: 0, miss: 0, accuracy: 1, navigatorId: 'vang_anh', ...over });

test('shipped shop data is consistent', () => {
  assert.deepEqual(validateShop(data.shop), []);
  for (const category of ['avatarFrame', 'profileFrame', 'bubbleFrame', 'exhaust', 'garage']) {
    assert.equal(data.shop.items.filter((i) => i.category === category && i.basic).length, 1, `${category} needs exactly one basic item`);
  }
  for (const e of data.events.events) for (const m of e.milestones) for (const r of m.rewards) {
    if (r.type === 'item') assert.ok(data.shop.items.some((i) => i.id === r.itemId), `${e.id} rewards unknown item ${r.itemId}`);
  }
});

test('gold spends bonus first; items cost the listed currency; owned items cannot be bought twice', () => {
  const p = fresh();
  p.gold = 500;
  p.goldBonus = 100;
  p.coins = 50_000;
  buyItem(p, data.shop, 'frame_lotus', D('2026-10-12T08:00:00Z')); // 300 gold
  assert.equal(p.goldBonus, 0);
  assert.equal(p.gold, 300);
  assert.ok(p.cosmetics.owned.includes('frame_lotus'));
  assert.throws(() => buyItem(p, data.shop, 'frame_lotus', D('2026-10-12T08:00:00Z')), /Already owned/);
  assert.throws(() => buyItem(p, data.shop, 'frame_phoenix', D('2026-10-12T08:00:00Z')), (e: { code: string }) => e.code === 'insufficient_gold');
  assert.throws(() => buyItem(p, data.shop, 'frame_welcome', D('2026-10-12T08:00:00Z')), (e: { code: string }) => e.code === 'item_unavailable');
  assert.throws(() => buyItem(p, data.shop, 'frame_basic', D('2026-10-12T08:00:00Z')), (e: { code: string }) => e.code === 'item_unavailable');

  const coins = p.coins;
  buyItem(p, data.shop, 'frame_jade_cloud', D('2026-10-12T08:00:00Z'));
  assert.equal(p.coins, coins - 25_000);

  // Consumables can be bought again and pay out their rewards.
  buyItem(p, data.shop, 'coin_pouch_s', D('2026-10-12T08:00:00Z'));
  buyItem(p, data.shop, 'coin_pouch_s', D('2026-10-12T08:00:00Z'));
  assert.equal(p.coins, coins - 25_000 + 12_000);
  assert.equal(p.gold, 180);
});

test('equipping needs ownership and the right target; empty resets to basic', () => {
  const p = fresh();
  assert.throws(() => equipItem(p, data.shop, 'avatarFrame', '', 'frame_lotus'), (e: { code: string }) => e.code === 'not_owned');
  p.cosmetics.owned.push('frame_lotus', 'kit_rong_jade', 'skin_vang_anh_ao_dai');
  equipItem(p, data.shop, 'avatarFrame', '', 'frame_lotus');
  assert.equal(p.cosmetics.avatarFrame, 'frame_lotus');
  equipItem(p, data.shop, 'avatarFrame', '', 'frame_basic');
  assert.equal(p.cosmetics.avatarFrame, '');
  equipItem(p, data.shop, 'vesselKit', 'thuyen_rong', 'kit_rong_jade');
  assert.deepEqual(p.cosmetics.vesselKits, [{ target: 'thuyen_rong', item: 'kit_rong_jade' }]);
  assert.throws(() => equipItem(p, data.shop, 'vesselKit', 'xe_lan', 'kit_rong_jade'), (e: { code: string }) => e.code === 'wrong_target');
  assert.throws(() => equipItem(p, data.shop, 'avatarFrame', '', 'kit_rong_jade'), (e: { code: string }) => e.code === 'item_not_found');
  assert.throws(() => equipItem(p, data.shop, 'emotion', '', 'emo_smile'), (e: { code: string }) => e.code === 'invalid_request');
  equipItem(p, data.shop, 'navigatorSkin', 'vang_anh', 'skin_vang_anh_ao_dai');
  equipItem(p, data.shop, 'navigatorSkin', 'vang_anh', '');
  assert.deepEqual(p.cosmetics.navigatorSkins, []);
});

test('gold exchanges 1:1 into jade', () => {
  const p = fresh();
  p.gold = 10;
  p.goldBonus = 150;
  assert.equal(exchangeGold(p, data.shop, 150), 150);
  assert.equal(p.jade, 3000 + 150);
  assert.equal(p.goldBonus, 0);
  assert.equal(p.gold, 10);
  assert.throws(() => exchangeGold(p, data.shop, 11), (e: { code: string }) => e.code === 'insufficient_gold');
  assert.throws(() => exchangeGold(p, data.shop, 0), /out of range/);
});

test('products: first purchase doubles, limits apply before charging, refunds take everything back as a debt', () => {
  const p = fresh();
  const first = applyProduct(p, data.shop, product('gold_300'), D('2026-10-12T08:00:00Z'));
  assert.deepEqual([first.gold, first.bonus, first.firstPurchase], [300, 300, true]);
  const second = applyProduct(p, data.shop, product('gold_300'), D('2026-10-12T08:00:00Z'));
  assert.deepEqual([second.gold, second.bonus, second.firstPurchase], [300, 30, false]);
  assert.equal(p.gold, 600);
  assert.equal(p.goldBonus, 330);

  assert.ok(canBuyProduct(p, product('starter_pack')));
  applyProduct(p, data.shop, product('starter_pack'), D('2026-10-12T08:00:00Z'));
  assert.ok(!canBuyProduct(p, product('starter_pack')));
  assert.ok(p.cosmetics.owned.includes('frame_starter'));
  assert.equal(p.tickets, 10 + 10);

  // The player spent most of it; a refund of 300 + 300 takes all of it back, leaving a gold debt.
  p.gold = 200;
  p.goldBonus = 50;
  assert.deepEqual(revokeProduct(p, 300, 300), { gold: 550, bonus: 50 });
  assert.deepEqual([p.gold, p.goldBonus], [-350, 0]);
  assert.throws(() => spend(p, 'gold', 1), /Not enough gold/);
  const repay = applyProduct(p, data.shop, product('gold_980'), D('2026-10-13T08:00:00Z'));
  assert.deepEqual([p.gold, p.goldBonus], [-350 + repay.gold, repay.bonus], 'new paid gold repays the debt');
});

test('the Lantern Pass pays jade once a day for 30 days and stacks up to 180 days', () => {
  const p = fresh();
  const grant = applyProduct(p, data.shop, product('lantern_pass'), D('2026-10-12T08:00:00Z'));
  assert.equal(grant.passJade, 90, 'the purchase day pays at once');
  assert.equal(p.passes[0].untilDay, '2026-11-10');
  const jade = p.jade;
  assert.equal(claimDaily(p, data.economy, data.shop, data.events, D('2026-10-12T20:00:00Z')).passJade, 0, 'not twice on the same day');
  assert.equal(claimDaily(p, data.economy, data.shop, data.events, D('2026-10-13T08:00:00Z')).passJade, 90);
  assert.equal(p.jade, jade + 120 + 120 + 90);
  assert.equal(claimDaily(p, data.economy, data.shop, data.events, D('2026-11-11T08:00:00Z')).passJade, 0, 'expired');

  const q = fresh();
  for (let i = 0; i < 8; i++) applyProduct(q, data.shop, product('lantern_pass'), D('2026-10-12T08:00:00Z'));
  assert.equal(q.passes[0].untilDay, '2027-04-09', '180 days including today at most');
});

test('events count logins, races and score; rewards need the target and are claimed once', () => {
  const p = fresh();
  const at = D('2026-10-12T08:00:00Z');
  assert.deepEqual(visibleEvents(data.events, at).map((e) => e.id), ['grand_opening', 'spirit_road', 'score_rush']);

  claimDaily(p, data.economy, data.shop, data.events, at);
  claimDaily(p, data.economy, data.shop, data.events, D('2026-10-12T22:00:00Z'));
  assert.equal(p.events.find((e) => e.id === 'grand_opening')!.value, 1, 'one check-in per day');
  claimDaily(p, data.economy, data.shop, data.events, D('2026-10-13T01:00:00Z'));

  eventOnRace(p, data.events, report(), at, 30);
  eventOnRace(p, data.events, report({ mode: 3 }), at, 30); // Zen doesn't count
  eventOnRace(p, data.events, report({ durationSec: 10 }), at, 30); // too short
  eventOnRace(p, data.events, report(), D('2026-10-05T08:00:00Z'), 30); // before Score Rush started
  assert.equal(p.events.find((e) => e.id === 'spirit_road')!.value, 2);
  assert.equal(p.events.find((e) => e.id === 'score_rush')!.value, 72_480);

  const jade = p.jade;
  assert.deepEqual(claimEvent(p, data.events, data.shop, 'grand_opening', 1, at), [{ type: 'coins', amount: 5000 }]);
  assert.throws(() => claimEvent(p, data.events, data.shop, 'grand_opening', 1, at), (e: { code: string }) => e.code === 'already_claimed');
  assert.throws(() => claimEvent(p, data.events, data.shop, 'grand_opening', 2, at), (e: { code: string }) => e.code === 'not_reached');
  claimEvent(p, data.events, data.shop, 'grand_opening', 0, at);
  assert.equal(p.jade, jade + 120);

  // Rewards stay claimable for 3 days after the end, then the event is gone.
  p.events.find((e) => e.id === 'spirit_road')!.value = 10;
  assert.doesNotThrow(() => claimEvent(p, data.events, data.shop, 'spirit_road', 0, D('2026-11-30T12:00:00Z')));
  assert.doesNotThrow(() => claimEvent(p, data.events, data.shop, 'spirit_road', 1, D('2026-12-03T12:00:00Z')));
  p.events.find((e) => e.id === 'spirit_road')!.value = 25;
  assert.throws(() => claimEvent(p, data.events, data.shop, 'spirit_road', 2, D('2026-12-04T00:00:01Z')), (e: { code: string }) => e.code === 'event_ended');
});

test('reward grants: gold is bonus gold, items once, consumable items pay out', () => {
  const p = fresh();
  grantRewards(p, data.shop, [
    { type: 'gold', amount: 60 },
    { type: 'item', itemId: 'emo_wink' },
    { type: 'item', itemId: 'emo_wink' },
    { type: 'item', itemId: 'incense_10' },
    { type: 'item', itemId: 'frame_basic' },
  ]);
  assert.deepEqual([p.gold, p.goldBonus, p.tickets], [0, 60, 20]);
  assert.deepEqual(p.cosmetics.owned, ['emo_wink']);
});
