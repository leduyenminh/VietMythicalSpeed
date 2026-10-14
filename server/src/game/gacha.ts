import type { RandomSource } from './random.ts';
import { GameError } from './types.ts';
import type { BannerDef, GachaConfig, GachaState, NavigatorDef, PityCounter, PlayerProfile, PullResult, SparkCounter } from './types.ts';

// Port of VMS.Core.GachaEngine / GachaService / CollectionRules. The RNG draw order is a contract with the
// C# implementation; shared/gacha-golden.json is checked by both test suites.

const THIEN = 3;
const DIA = 2;
const NHAN = 1;

export interface BannerPools {
  featuredThien: string[];
  offThien: string[];
  allThien: string[];
  featuredDia: string[];
  offDia: string[];
  allDia: string[];
  allNhan: string[];
}

export function pityGroup(banner: BannerDef): string {
  return banner.pityGroup ? banner.pityGroup : banner.id;
}

export function hasFeatured(banner: BannerDef): boolean {
  return !!banner.featured && banner.featured.length > 0;
}

export function isBannerActive(banner: BannerDef, now: Date): boolean {
  if (banner.startUtc && now < new Date(banner.startUtc)) return false;
  if (banner.endUtc && now >= new Date(banner.endUtc)) return false;
  return true;
}

export function thienChanceAt(cfg: GachaConfig, n: number): number {
  let p = cfg.rates.thien;
  if (cfg.softPityStart > 0 && n >= cfg.softPityStart) p += cfg.softPityStep * (n - cfg.softPityStart + 1);
  if (cfg.hardPity > 0 && n >= cfg.hardPity) p = 1;
  return p > 1 ? 1 : p;
}

export function getPity(state: GachaState, group: string): PityCounter {
  let p = state.pity.find((x) => x.group === group);
  if (!p) {
    p = { group, pullsSinceThien: 0, featuredGuaranteed: false };
    state.pity.push(p);
  }
  return p;
}

export function getSpark(state: GachaState, bannerId: string): SparkCounter {
  let s = state.spark.find((x) => x.bannerId === bannerId);
  if (!s) {
    s = { bannerId, points: 0 };
    state.spark.push(s);
  }
  return s;
}

export class GachaEngine {
  readonly config: GachaConfig;
  private readonly navigators: NavigatorDef[];

  constructor(config: GachaConfig, navigators: NavigatorDef[]) {
    this.config = config;
    this.navigators = navigators;
  }

  buildPools(banner: BannerDef): BannerPools {
    const featured = new Set(banner.featured ?? []);
    const pools: BannerPools = { featuredThien: [], offThien: [], allThien: [], featuredDia: [], offDia: [], allDia: [], allNhan: [] };
    for (const nav of this.navigators) {
      const isFeatured = featured.has(nav.id);
      if (nav.limited && !isFeatured) continue;
      if (nav.rarity === THIEN) {
        pools.allThien.push(nav.id);
        (isFeatured ? pools.featuredThien : pools.offThien).push(nav.id);
      } else if (nav.rarity === DIA) {
        pools.allDia.push(nav.id);
        (isFeatured ? pools.featuredDia : pools.offDia).push(nav.id);
      } else {
        pools.allNhan.push(nav.id);
      }
    }
    if (!pools.allThien.length || !pools.allDia.length || !pools.allNhan.length) {
      throw new Error(`Banner ${banner.id} has an empty rarity pool.`);
    }
    return pools;
  }

  pull(banner: BannerDef, state: GachaState, count: number, rng: RandomSource): PullResult[] {
    if (count < 1 || count > 10) throw new RangeError('count');
    const cfg = this.config;
    const pools = this.buildPools(banner);
    const pity = getPity(state, pityGroup(banner));
    const sparkEnabled = cfg.sparkCost > 0 && hasFeatured(banner);
    const results: PullResult[] = [];
    let bestRarity = 0;

    for (let i = 0; i < count; i++) {
      const n = pity.pullsSinceThien + 1;
      const pThien = thienChanceAt(cfg, n);
      const pDia = Math.min(cfg.rates.dia, 1 - pThien);

      const roll = rng.nextDouble();
      let rarity = roll < pThien ? THIEN : roll < pThien + pDia ? DIA : NHAN;
      const guaranteeSlot = count >= 10 && i === count - 1 && bestRarity < cfg.tenPullMinRarity;
      if (guaranteeSlot && rarity < cfg.tenPullMinRarity) rarity = Math.min(3, Math.max(1, cfg.tenPullMinRarity));

      const result: PullResult = { navigatorId: '', rarity, featured: false, isNew: false, fragments: 0 };
      if (rarity === THIEN) {
        if (pools.featuredThien.length > 0) {
          let win = pity.featuredGuaranteed || rng.nextDouble() < cfg.featuredShare;
          if (pools.offThien.length === 0) win = true;
          if (win) {
            result.navigatorId = pools.featuredThien[rng.nextInt(pools.featuredThien.length)];
            result.featured = true;
            pity.featuredGuaranteed = false;
          } else {
            result.navigatorId = pools.offThien[rng.nextInt(pools.offThien.length)];
            if (cfg.guaranteeFeaturedAfterLoss) pity.featuredGuaranteed = true;
          }
        } else {
          result.navigatorId = pools.allThien[rng.nextInt(pools.allThien.length)];
        }
        pity.pullsSinceThien = 0;
      } else {
        pity.pullsSinceThien++;
        if (rarity === DIA && pools.featuredDia.length > 0) {
          let win = rng.nextDouble() < cfg.featuredDiaShare;
          if (pools.offDia.length === 0) win = true;
          const pool = win ? pools.featuredDia : pools.offDia;
          result.navigatorId = pool[rng.nextInt(pool.length)];
          result.featured = win;
        } else {
          const pool = rarity === DIA ? pools.allDia : pools.allNhan;
          result.navigatorId = pool[rng.nextInt(pool.length)];
        }
      }

      if (sparkEnabled) getSpark(state, banner.id).points++;
      if (rarity > bestRarity) bestRarity = rarity;
      results.push(result);
    }
    return results;
  }

  spark(banner: BannerDef, state: GachaState, navigatorId: string): PullResult {
    const cfg = this.config;
    if (cfg.sparkCost <= 0 || !hasFeatured(banner)) throw new GameError('spark_unavailable', 'Spark is not available on this banner.');
    if (!banner.featured!.includes(navigatorId)) throw new GameError('spark_unavailable', 'Only featured navigators can be chosen with spark.');
    const spark = getSpark(state, banner.id);
    if (spark.points < cfg.sparkCost) throw new GameError('spark_unavailable', 'Not enough spark points.');
    const nav = this.navigators.find((n) => n.id === navigatorId);
    if (!nav) throw new GameError('spark_unavailable', 'Unknown navigator.');
    spark.points -= cfg.sparkCost;
    return { navigatorId, rarity: nav.rarity, featured: true, isNew: false, fragments: 0 };
  }
}

function dupeFragments(cfg: GachaConfig, rarity: number): number {
  return rarity === THIEN ? cfg.dupeFragments.thien : rarity === DIA ? cfg.dupeFragments.dia : cfg.dupeFragments.nhan;
}

export function applyPulls(profile: PlayerProfile, results: PullResult[], cfg: GachaConfig): void {
  for (const r of results) {
    const owned = profile.navigators.find((n) => n.id === r.navigatorId);
    if (!owned) {
      profile.navigators.push({ id: r.navigatorId, copies: 1, awakening: 0, fragments: 0, bondExp: 0 });
      r.isNew = true;
      r.fragments = 0;
    } else {
      owned.copies++;
      r.isNew = false;
      r.fragments = dupeFragments(cfg, r.rarity);
      owned.fragments += r.fragments;
    }
  }
}

export function awaken(profile: PlayerProfile, navigatorId: string, cfg: GachaConfig): void {
  const owned = profile.navigators.find((n) => n.id === navigatorId);
  if (!owned) throw new GameError('not_owned', 'Navigator not owned.');
  const level = owned.awakening;
  if (level >= cfg.maxAwaken || level >= cfg.awakenFragmentCosts.length || level >= cfg.awakenCoinCosts.length) {
    throw new GameError('awaken_max', 'Already fully awakened.');
  }
  const fragments = cfg.awakenFragmentCosts[level];
  const coins = cfg.awakenCoinCosts[level];
  if (owned.fragments < fragments || profile.coins < coins) throw new GameError('awaken_cost', 'Not enough fragments or coins.');
  owned.fragments -= fragments;
  profile.coins -= coins;
  owned.awakening++;
}

/** Payment + roll + collection, mirroring VMS.Core.GachaService.Pull. */
export function pullForProfile(
  engine: GachaEngine,
  profile: PlayerProfile,
  banner: BannerDef | undefined,
  count: number,
  rng: RandomSource,
  now: Date,
): PullResult[] {
  if (count !== 1 && count !== 10) throw new GameError('bad_count', 'Pull 1 or 10 at a time.');
  if (!banner) throw new GameError('banner_not_found', 'Unknown banner.', 404);
  if (!isBannerActive(banner, now)) throw new GameError('banner_inactive', 'This banner is not running.');
  const ticketsUsed = Math.min(profile.tickets, count);
  const jadeCost = (count - ticketsUsed) * engine.config.costPerPull;
  if (profile.jade < jadeCost) throw new GameError('insufficient_jade', 'Not enough jade.');
  const results = engine.pull(banner, profile.gacha, count, rng);
  profile.tickets -= ticketsUsed;
  profile.jade -= jadeCost;
  applyPulls(profile, results, engine.config);
  return results;
}

export interface RateLine {
  navigatorId: string;
  rarity: number;
  featured: boolean;
  probability: number;
}
