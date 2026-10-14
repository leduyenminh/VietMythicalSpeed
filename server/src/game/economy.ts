import type { EconomyConfig, NavigatorDef, PlayerProfile, RaceReport, RaceReward } from './types.ts';

// Port of VMS.Core.EconomyRules – keep both in sync.

export function dayKey(d: Date): string {
  return d.toISOString().slice(0, 10);
}

export function newProfile(cfg: EconomyConfig, accountId: string, displayName: string): PlayerProfile {
  const p: PlayerProfile = {
    accountId,
    displayName,
    jade: cfg.startingJade,
    coins: cfg.startingCoins,
    tickets: cfg.startingTickets,
    selectedNavigator: cfg.starterNavigator,
    navigators: [],
    gacha: { pity: [], spark: [] },
    daily: { day: '', racesRewarded: 0, coinsEarned: 0, lastLoginClaimDay: '', loginStreak: 0 },
    gold: 0,
    goldBonus: 0,
    cosmetics: { owned: [], avatarFrame: '', profileFrame: '', bubbleFrame: '', exhaust: '', garage: '', navigatorSkins: [], vesselKits: [] },
    purchases: [],
    passes: [],
    events: [],
  };
  if (cfg.starterNavigator) p.navigators.push({ id: cfg.starterNavigator, copies: 1, awakening: 0, fragments: 0, bondExp: 0 });
  return p;
}

/** Fills fields missing from older saved profiles so the client always receives a complete shape. */
export function normalizeProfile(p: PlayerProfile): PlayerProfile {
  p.navigators ??= [];
  p.gacha ??= { pity: [], spark: [] };
  p.gacha.pity ??= [];
  p.gacha.spark ??= [];
  p.daily ??= { day: '', racesRewarded: 0, coinsEarned: 0, lastLoginClaimDay: '', loginStreak: 0 };
  p.gold ??= 0;
  p.goldBonus ??= 0;
  p.cosmetics ??= { owned: [], avatarFrame: '', profileFrame: '', bubbleFrame: '', exhaust: '', garage: '', navigatorSkins: [], vesselKits: [] };
  p.cosmetics.owned ??= [];
  p.cosmetics.navigatorSkins ??= [];
  p.cosmetics.vesselKits ??= [];
  for (const key of ['avatarFrame', 'profileFrame', 'bubbleFrame', 'exhaust', 'garage'] as const) p.cosmetics[key] ??= '';
  p.purchases ??= [];
  p.passes ??= [];
  p.events ??= [];
  for (const e of p.events) e.claimed ??= [];
  return p;
}

export function rollDay(p: PlayerProfile, now: Date): void {
  const today = dayKey(now);
  if (p.daily.day === today) return;
  p.daily.day = today;
  p.daily.racesRewarded = 0;
  p.daily.coinsEarned = 0;
}

export interface DailyClaimResult {
  claimed: boolean;
  jade: number;
  tickets: number;
  streak: number;
}

export function claimDailyLogin(p: PlayerProfile, cfg: EconomyConfig, now: Date): DailyClaimResult {
  rollDay(p, now);
  const today = dayKey(now);
  const result: DailyClaimResult = { claimed: false, jade: 0, tickets: 0, streak: p.daily.loginStreak };
  if (p.daily.lastLoginClaimDay === today) return result;
  const yesterday = dayKey(new Date(now.getTime() - 86_400_000));
  p.daily.loginStreak = p.daily.lastLoginClaimDay === yesterday ? p.daily.loginStreak + 1 : 1;
  p.daily.lastLoginClaimDay = today;
  result.claimed = true;
  result.streak = p.daily.loginStreak;
  result.jade = cfg.dailyLoginJade;
  if (cfg.streakBonusEvery > 0 && p.daily.loginStreak % cfg.streakBonusEvery === 0) result.tickets = cfg.streakBonusTickets;
  p.jade += result.jade;
  p.tickets += result.tickets;
  return result;
}

export function bondLevel(cfg: EconomyConfig['bond'], exp: number): number {
  let level = 1;
  for (let i = 1; i < cfg.levelExp.length; i++) if (exp >= cfg.levelExp[i]) level = i + 1;
  return level;
}

// C#'s Math.Round uses banker's rounding; match it so both sides grant identical amounts.
function roundHalfEven(x: number): number {
  const r = Math.round(x);
  return Math.abs(x % 1) === 0.5 && r % 2 !== 0 ? r - 1 : r;
}

export function grantRaceReward(p: PlayerProfile, cfg: EconomyConfig, report: RaceReport, nav: NavigatorDef | undefined, now: Date): RaceReward {
  rollDay(p, now);
  const reward: RaceReward = { coins: 0, jade: 0, bondExp: 0, bondLevelBefore: 0, bondLevelAfter: 0 };
  const owned = p.navigators.find((n) => n.id === report.navigatorId);
  if (owned) reward.bondLevelBefore = reward.bondLevelAfter = bondLevel(cfg.bond, owned.bondExp);
  if (report.durationSec < cfg.race.minDurationSec || report.score < 0) return reward;

  const accuracy = Math.min(1, Math.max(0, report.accuracy));
  let coinBonus = 0;
  let bondBonus = 0;
  if (nav?.passive && owned) {
    const v = nav.passive.value + nav.passive.valuePerAwaken * owned.awakening;
    if (nav.passive.type === 'coins') coinBonus = v;
    if (nav.passive.type === 'bond') bondBonus = v;
  }

  let coins = roundHalfEven(report.score * cfg.race.coinsPerScore * (1 + coinBonus));
  coins = Math.max(cfg.race.minCoins, Math.min(cfg.race.maxCoinsPerRace, coins));
  coins = Math.max(0, Math.min(coins, cfg.race.dailyCoinCap - p.daily.coinsEarned));
  reward.coins = coins;
  p.coins += coins;
  p.daily.coinsEarned += coins;

  if (p.daily.racesRewarded < cfg.race.jadeRacesPerDay) {
    reward.jade = cfg.race.jadePerRace;
    p.jade += reward.jade;
  }
  p.daily.racesRewarded++;

  if (owned) {
    const lengthFactor = report.durationSec >= 90 ? 1 : 0.6;
    const exp = roundHalfEven((cfg.bond.baseExp + cfg.bond.accuracyExp * accuracy) * lengthFactor * (1 + bondBonus));
    const cap = cfg.bond.levelExp[cfg.bond.levelExp.length - 1];
    owned.bondExp = Math.min(cap, owned.bondExp + exp);
    reward.bondExp = exp;
    reward.bondLevelAfter = bondLevel(cfg.bond, owned.bondExp);
  }
  return reward;
}
