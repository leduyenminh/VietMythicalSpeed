import { claimDailyLogin, dayKey, normalizeProfile as normalizeLiveOps } from './economy.ts';
import type { DailyClaimResult } from './economy.ts';
import { GameError } from './types.ts';
import type { EconomyConfig, EventCatalog, EventDef, EventProgress, PlayerProfile, ProductDef, RaceReport, Reward, ShopCatalog, ShopItem } from './types.ts';

// Port of VMS.Core.ShopRules / Wallet / EventRules / DailyRules (Core/Shop/ShopRules.cs) – keep both in sync.

export const CATEGORIES = ['avatarFrame', 'profileFrame', 'bubbleFrame', 'exhaust', 'navigatorSkin', 'emotion', 'garage', 'vesselKit'];
const EQUIPPABLE = ['avatarFrame', 'profileFrame', 'bubbleFrame', 'exhaust', 'garage', 'navigatorSkin', 'vesselKit'];
const TARGETED = ['navigatorSkin', 'vesselKit'];
export const MAX_EXCHANGE = 100_000;
const DAY = 86_400_000;

export function inWindow(startUtc: string | undefined, endUtc: string | undefined, now: Date, extraDays = 0): boolean {
  if (startUtc && now < new Date(startUtc)) return false;
  if (endUtc && now.getTime() >= new Date(endUtc).getTime() + extraDays * DAY) return false;
  return true;
}

const dayStart = (day: string) => new Date(`${day}T00:00:00Z`);

export function catalogItem(catalog: ShopCatalog, id: string | undefined): ShopItem | undefined {
  return id ? catalog.items.find((i) => i.id === id) : undefined;
}

export function validateShop(catalog: ShopCatalog): string[] {
  const problems: string[] = [];
  const ids = new Set<string>();
  for (const i of catalog.items) {
    if (ids.has(i.id)) problems.push(`Duplicate item id ${i.id}`);
    ids.add(i.id);
    if (!CATEGORIES.includes(i.category) && i.category !== 'special') problems.push(`${i.id}: unknown category ${i.category}`);
    if (TARGETED.includes(i.category) && !i.target) problems.push(`${i.id}: needs a target`);
    if (i.category === 'vesselKit' && !catalog.vessels.includes(i.target ?? '')) problems.push(`${i.id}: unknown vessel ${i.target}`);
    if (i.consumable && !i.rewards?.length) problems.push(`${i.id}: consumable without rewards`);
    if (i.price && i.price.amount > 0 && !['gold', 'jade', 'coins'].includes(i.price.currency)) problems.push(`${i.id}: price currency must be gold, jade or coins`);
  }
  for (const p of catalog.products) {
    if (ids.has(p.id)) problems.push(`Duplicate product id ${p.id}`);
    ids.add(p.id);
    if (p.kind === 'pass' && (!p.passDays || !p.passDailyJade)) problems.push(`${p.id}: pass needs passDays and passDailyJade`);
    for (const r of p.rewards ?? []) if (r.type === 'item' && !catalogItem(catalog, r.itemId)) problems.push(`${p.id}: rewards unknown item ${r.itemId}`);
  }
  return problems;
}

// ---------------------------------------------------------------- wallet

export function balance(p: PlayerProfile, currency: string): number {
  switch (currency) {
    case 'gold': return p.gold + p.goldBonus;
    case 'jade': return p.jade;
    case 'coins': return p.coins;
    case 'tickets': return p.tickets;
    default: return 0;
  }
}

/** Gold spends its free (bonus) part first; the total must cover the cost (paid gold can be negative after a refund). */
export function spend(p: PlayerProfile, currency: string, amount: number): void {
  if (amount < 0) throw new GameError('invalid_request', 'Negative amount.');
  if (balance(p, currency) < amount) throw new GameError(`insufficient_${currency}`, `Not enough ${currency}.`);
  switch (currency) {
    case 'gold': {
      const fromBonus = Math.min(p.goldBonus, amount);
      p.goldBonus -= fromBonus;
      p.gold -= amount - fromBonus;
      break;
    }
    case 'jade': p.jade -= amount; break;
    case 'coins': p.coins -= amount; break;
    case 'tickets': p.tickets -= amount; break;
    default: throw new GameError('invalid_request', `Unknown currency ${currency}.`);
  }
}

/** Gold given as a reward is bonus gold. Already-owned items are skipped. */
export function grantRewards(p: PlayerProfile, catalog: ShopCatalog, rewards: Reward[] | undefined): void {
  for (const r of rewards ?? []) {
    const amount = Math.max(0, Math.floor(r.amount ?? 0));
    switch (r.type) {
      case 'gold': p.goldBonus += amount; break;
      case 'jade': p.jade += amount; break;
      case 'coins': p.coins += amount; break;
      case 'tickets': p.tickets += amount; break;
      case 'item': {
        const item = catalogItem(catalog, r.itemId);
        if (!item || item.basic) break;
        if (item.consumable) grantRewards(p, catalog, item.rewards);
        else if (!p.cosmetics.owned.includes(item.id)) p.cosmetics.owned.push(item.id);
        break;
      }
    }
  }
}

// ---------------------------------------------------------------- shop

export function owns(p: PlayerProfile, item: ShopItem | undefined): boolean {
  return !!item && (!!item.basic || p.cosmetics.owned.includes(item.id));
}

export function isOnSale(item: ShopItem | undefined, now: Date): boolean {
  return !!item && !item.hidden && !!item.price && item.price.amount > 0 && !!item.price.currency && inWindow(item.startUtc, item.endUtc, now);
}

export function buyItem(p: PlayerProfile, catalog: ShopCatalog, itemId: string, now: Date): { item: ShopItem; rewards: Reward[] } {
  normalizeLiveOps(p);
  const item = catalogItem(catalog, itemId);
  if (!item) throw new GameError('item_not_found', 'Unknown item.', 404);
  if (!isOnSale(item, now)) throw new GameError('item_unavailable', "This item isn't for sale.");
  if (!item.consumable && owns(p, item)) throw new GameError('already_owned', 'Already owned.', 409);
  spend(p, item.price!.currency, item.price!.amount);
  if (item.consumable) {
    grantRewards(p, catalog, item.rewards);
    return { item, rewards: item.rewards ?? [] };
  }
  p.cosmetics.owned.push(item.id);
  return { item, rewards: [{ type: 'item', amount: 1, itemId: item.id }] };
}

function setChoice(p: PlayerProfile, category: string, target: string, itemId: string): void {
  const c = p.cosmetics;
  switch (category) {
    case 'avatarFrame': c.avatarFrame = itemId; break;
    case 'profileFrame': c.profileFrame = itemId; break;
    case 'bubbleFrame': c.bubbleFrame = itemId; break;
    case 'exhaust': c.exhaust = itemId; break;
    case 'garage': c.garage = itemId; break;
    case 'navigatorSkin':
    case 'vesselKit': {
      const list = category === 'navigatorSkin' ? c.navigatorSkins : c.vesselKits;
      const kept = list.filter((x) => x.target !== target);
      if (itemId) kept.push({ target, item: itemId });
      if (category === 'navigatorSkin') c.navigatorSkins = kept;
      else c.vesselKits = kept;
      break;
    }
  }
}

/** Equips an owned item; an empty id goes back to the basic look. */
export function equipItem(p: PlayerProfile, catalog: ShopCatalog, category: string, target: string, itemId: string): void {
  normalizeLiveOps(p);
  if (!EQUIPPABLE.includes(category)) throw new GameError('invalid_request', `Nothing to equip in ${category}.`);
  const targeted = TARGETED.includes(category);
  if (targeted && !target) throw new GameError('invalid_request', 'A target is required.');
  if (!itemId) {
    setChoice(p, category, targeted ? target : '', '');
    return;
  }
  const item = catalogItem(catalog, itemId);
  if (!item || item.category !== category) throw new GameError('item_not_found', 'Unknown item.', 404);
  if (targeted && item.target !== target) throw new GameError('wrong_target', 'That item is for something else.');
  if (!owns(p, item)) throw new GameError('not_owned', 'Not owned.');
  setChoice(p, category, targeted ? target : '', item.basic ? '' : item.id);
}

export function exchangeGold(p: PlayerProfile, catalog: ShopCatalog, amount: number): number {
  if (!Number.isInteger(amount) || amount <= 0 || amount > MAX_EXCHANGE) throw new GameError('invalid_request', 'Amount out of range.');
  spend(p, 'gold', amount);
  const jade = amount * Math.max(1, catalog.goldToJadeRate);
  p.jade += jade;
  return jade;
}

// ---------------------------------------------------------------- real-money products

export function purchaseCount(p: PlayerProfile, productId: string): number {
  return p.purchases.find((c) => c.id === productId)?.count ?? 0;
}

export function canBuyProduct(p: PlayerProfile, product: ProductDef): boolean {
  return !product.limit || purchaseCount(normalizeLiveOps(p), product.id) < product.limit;
}

export interface ProductGrant {
  gold: number;
  bonus: number;
  firstPurchase: boolean;
  rewards: Reward[];
  passJade: number;
}

/** Grants a verified purchase. Never refuses: the player already paid; limits are checked before charging. */
export function applyProduct(p: PlayerProfile, catalog: ShopCatalog, product: ProductDef, now: Date): ProductGrant {
  normalizeLiveOps(p);
  const count = purchaseCount(p, product.id);
  const firstPurchase = count === 0;
  const grant: ProductGrant = {
    gold: Math.max(0, product.gold ?? 0),
    bonus: firstPurchase && (product.firstBonus ?? 0) > 0 ? product.firstBonus! : Math.max(0, product.bonus ?? 0),
    firstPurchase,
    rewards: product.rewards ?? [],
    passJade: 0,
  };
  p.gold += grant.gold;
  p.goldBonus += grant.bonus;
  grantRewards(p, catalog, product.rewards);
  if (product.kind === 'pass' && (product.passDays ?? 0) > 0) {
    extendPass(p, catalog, product, now);
    grant.passJade = claimPassJade(p, catalog, now);
  }
  const entry = p.purchases.find((c) => c.id === product.id);
  if (entry) entry.count++;
  else p.purchases.push({ id: product.id, count: 1 });
  return grant;
}

/**
 * Takes back everything a refunded purchase granted. The purchase's bonus comes out of bonus gold while it lasts;
 * the rest comes out of paid gold, which may go below zero: a debt that blocks gold spending until new gold repays it,
 * so buying, spending and refunding gains nothing.
 */
export function revokeProduct(p: PlayerProfile, gold: number, bonus: number): { gold: number; bonus: number } {
  normalizeLiveOps(p);
  const fromBonus = Math.min(Math.max(0, p.goldBonus), bonus);
  p.goldBonus -= fromBonus;
  p.gold -= gold + bonus - fromBonus;
  return { gold: gold + bonus - fromBonus, bonus: fromBonus };
}

function extendPass(p: PlayerProfile, catalog: ShopCatalog, product: ProductDef, now: Date): void {
  const today = dayStart(dayKey(now));
  let pass = p.passes.find((x) => x.id === product.id);
  if (!pass) {
    pass = { id: product.id, untilDay: '', lastClaimDay: '' };
    p.passes.push(pass);
  }
  let start = today;
  if (pass.untilDay) {
    const until = dayStart(pass.untilDay);
    if (until >= today) start = new Date(until.getTime() + DAY);
  }
  const days = product.passDays ?? 0;
  let end = new Date(start.getTime() + (days - 1) * DAY);
  const cap = new Date(today.getTime() + (Math.max(days, catalog.maxPassDays) - 1) * DAY);
  if (end > cap) end = cap;
  pass.untilDay = dayKey(end);
}

/** Pays today's jade for every active pass, once per UTC day. */
export function claimPassJade(p: PlayerProfile, catalog: ShopCatalog, now: Date): number {
  normalizeLiveOps(p);
  const today = dayKey(now);
  let total = 0;
  for (const pass of p.passes) {
    const product = catalog.products.find((x) => x.id === pass.id);
    if (!product || !pass.untilDay) continue;
    if (pass.untilDay < today || pass.lastClaimDay === today) continue;
    pass.lastClaimDay = today;
    total += product.passDailyJade ?? 0;
  }
  p.jade += total;
  return total;
}

// ---------------------------------------------------------------- events

export function isEventRunning(e: EventDef, now: Date): boolean {
  return inWindow(e.startUtc, e.endUtc, now);
}

export function isEventVisible(e: EventDef, catalog: EventCatalog, now: Date): boolean {
  return inWindow(e.startUtc, e.endUtc, now, catalog.claimGraceDays);
}

export function visibleEvents(catalog: EventCatalog, now: Date): EventDef[] {
  return catalog.events.filter((e) => isEventVisible(e, catalog, now)).sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0));
}

function progress(p: PlayerProfile, eventId: string): EventProgress {
  let entry = p.events.find((x) => x.id === eventId);
  if (!entry) {
    entry = { id: eventId, value: 0, lastDay: '', claimed: [] };
    p.events.push(entry);
  }
  return entry;
}

export function eventCheckIn(p: PlayerProfile, catalog: EventCatalog, now: Date): void {
  normalizeLiveOps(p);
  const today = dayKey(now);
  for (const e of catalog.events) {
    if (e.type !== 'login' || !isEventRunning(e, now)) continue;
    const entry = progress(p, e.id);
    if (entry.lastDay === today) continue;
    entry.lastDay = today;
    entry.value++;
  }
}

export function eventOnRace(p: PlayerProfile, catalog: EventCatalog, report: RaceReport, playedAt: Date, minDurationSec: number): void {
  normalizeLiveOps(p);
  if (report.mode === 3 || report.durationSec < minDurationSec) return;
  for (const e of catalog.events) {
    if (!isEventRunning(e, playedAt)) continue;
    if (e.type === 'races') progress(p, e.id).value += 1;
    else if (e.type === 'score') progress(p, e.id).value += Math.max(0, report.score);
  }
}

export function claimEvent(p: PlayerProfile, catalog: EventCatalog, shop: ShopCatalog, eventId: string, index: number, now: Date): Reward[] {
  normalizeLiveOps(p);
  const e = catalog.events.find((x) => x.id === eventId);
  if (!e) throw new GameError('event_not_found', 'Unknown event.', 404);
  if (!isEventVisible(e, catalog, now)) throw new GameError('event_ended', 'This event has ended.');
  if (!Number.isInteger(index) || index < 0 || index >= e.milestones.length) throw new GameError('invalid_request', 'No such reward.');
  const entry = progress(p, eventId);
  if (entry.claimed.includes(index)) throw new GameError('already_claimed', 'Already claimed.', 409);
  if (entry.value < e.milestones[index].target) throw new GameError('not_reached', 'Not reached yet.');
  entry.claimed.push(index);
  grantRewards(p, shop, e.milestones[index].rewards);
  return e.milestones[index].rewards;
}

export function pruneEvents(p: PlayerProfile, catalog: EventCatalog): void {
  normalizeLiveOps(p);
  p.events = p.events.filter((x) => catalog.events.some((e) => e.id === x.id));
}

// ---------------------------------------------------------------- daily login

export interface FullDailyClaim extends DailyClaimResult {
  passJade: number;
}

/** Everything the once-a-day login gives: the base reward, pass jade and event check-ins. */
export function claimDaily(p: PlayerProfile, economy: EconomyConfig, shop: ShopCatalog, events: EventCatalog, now: Date): FullDailyClaim {
  normalizeLiveOps(p);
  const base = claimDailyLogin(p, economy, now);
  const passJade = claimPassJade(p, shop, now);
  eventCheckIn(p, events, now);
  return { ...base, passJade };
}
