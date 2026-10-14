import fs from 'node:fs';
import path from 'node:path';
import { GachaEngine } from './gacha.ts';
import { validateShop } from './shop.ts';
import type { BannerDef, EconomyConfig, EventCatalog, GachaConfig, MailTemplate, NavigatorDef, ShopCatalog } from './types.ts';

export interface GameData {
  navigators: NavigatorDef[];
  banners: BannerDef[];
  gacha: GachaConfig;
  economy: EconomyConfig;
  shop: ShopCatalog;
  events: EventCatalog;
  mail: MailTemplate[];
  engine: GachaEngine;
  navigator(id: string): NavigatorDef | undefined;
  banner(id: string): BannerDef | undefined;
}

function readJson<T>(dir: string, name: string): T {
  const file = path.join(dir, name);
  return JSON.parse(fs.readFileSync(file, 'utf8')) as T;
}

/** Loads the same JSON the Unity client ships in Resources/Data and validates the pools. */
export function loadGameData(dir: string): GameData {
  const navigators = readJson<{ navigators: NavigatorDef[] }>(dir, 'navigators.json').navigators;
  const banners = readJson<{ banners: BannerDef[] }>(dir, 'banners.json').banners;
  const gacha = readJson<GachaConfig>(dir, 'gacha.json');
  const economy = readJson<EconomyConfig>(dir, 'economy.json');
  const shop = readJson<ShopCatalog>(dir, 'shop.json');
  shop.items ??= [];
  shop.products ??= [];
  shop.vessels ??= [];
  shop.goldToJadeRate ??= 1;
  shop.maxPassDays ??= 180;
  const shopProblems = validateShop(shop);
  if (shopProblems.length) throw new Error(`shop.json: ${shopProblems.join('; ')}`);
  const events = readJson<EventCatalog>(dir, 'events.json');
  events.events ??= [];
  events.claimGraceDays ??= 3;
  const mail = readJson<{ templates: MailTemplate[] }>(dir, 'mail.json').templates ?? [];
  const byId = new Map(navigators.map((n) => [n.id, n]));
  for (const rarity of [1, 2, 3]) {
    if (!navigators.some((n) => n.rarity === rarity && !n.limited)) throw new Error(`No standard navigator of rarity ${rarity}`);
  }
  for (const b of banners) {
    for (const id of b.featured ?? []) if (!byId.has(id)) throw new Error(`Banner ${b.id} features unknown navigator ${id}`);
  }
  const engine = new GachaEngine(gacha, navigators);
  return {
    navigators,
    banners,
    gacha,
    economy,
    shop,
    events,
    mail,
    engine,
    navigator: (id) => byId.get(id),
    banner: (id) => banners.find((b) => b.id === id),
  };
}
