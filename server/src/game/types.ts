// Wire/data shapes shared with the Unity client (field names match the C# [Serializable] classes).

/** Languages of the game (vi and en are the source languages; the others fall back to en). */
export const LANGUAGES = ['vi', 'en', 'zh', 'ja', 'ko', 'es', 'de', 'fi'] as const;
export type Language = (typeof LANGUAGES)[number];

export type LocalizedText = Partial<Record<Language, string>>;

/** The text in one language, falling back to English, then Vietnamese. */
export function pickText(text: LocalizedText | undefined, lang: string): string {
  if (!text) return '';
  return text[lang as Language] || text.en || text.vi || '';
}

export interface NavigatorDef {
  id: string;
  rarity: number;
  limited?: boolean;
  name?: LocalizedText;
  passive?: { type: string; value: number; valuePerAwaken: number };
}

export interface BannerDef {
  id: string;
  type: string;
  title?: LocalizedText;
  subtitle?: LocalizedText;
  featured?: string[];
  pityGroup?: string;
  startUtc?: string;
  endUtc?: string;
  color?: string;
  sortOrder?: number;
}

export interface GachaConfig {
  rates: { thien: number; dia: number };
  softPityStart: number;
  softPityStep: number;
  hardPity: number;
  tenPullMinRarity: number;
  featuredShare: number;
  guaranteeFeaturedAfterLoss: boolean;
  featuredDiaShare: number;
  sparkCost: number;
  costPerPull: number;
  dupeFragments: { thien: number; dia: number; nhan: number };
  awakenFragmentCosts: number[];
  awakenCoinCosts: number[];
  maxAwaken: number;
}

export interface EconomyConfig {
  startingJade: number;
  startingCoins: number;
  startingTickets: number;
  starterNavigator: string;
  dailyLoginJade: number;
  streakBonusEvery: number;
  streakBonusTickets: number;
  race: {
    coinsPerScore: number;
    minCoins: number;
    maxCoinsPerRace: number;
    dailyCoinCap: number;
    jadePerRace: number;
    jadeRacesPerDay: number;
    minDurationSec: number;
  };
  bond: { levelExp: number[]; baseExp: number; accuracyExp: number };
}

export interface PityCounter {
  group: string;
  pullsSinceThien: number;
  featuredGuaranteed: boolean;
}

export interface SparkCounter {
  bannerId: string;
  points: number;
}

export interface GachaState {
  pity: PityCounter[];
  spark: SparkCounter[];
}

export interface OwnedNavigator {
  id: string;
  copies: number;
  awakening: number;
  fragments: number;
  bondExp: number;
}

export interface DailyState {
  day: string;
  racesRewarded: number;
  coinsEarned: number;
  lastLoginClaimDay: string;
  loginStreak: number;
}

export interface TargetedChoice {
  target: string;
  item: string;
}

export interface CosmeticLoadout {
  owned: string[];
  avatarFrame: string;
  profileFrame: string;
  bubbleFrame: string;
  exhaust: string;
  garage: string;
  navigatorSkins: TargetedChoice[];
  vesselKits: TargetedChoice[];
}

export interface ProductCount {
  id: string;
  count: number;
}

export interface PassState {
  id: string;
  untilDay: string;
  lastClaimDay: string;
}

export interface EventProgress {
  id: string;
  value: number;
  lastDay: string;
  claimed: number[];
}

export interface PlayerProfile {
  accountId: string;
  displayName: string;
  jade: number;
  coins: number;
  tickets: number;
  /** Nén Vàng bought with real money; refunds take these back. */
  gold: number;
  /** Free Nén Vàng (pack bonuses, events, mail); spent first. */
  goldBonus: number;
  selectedNavigator: string;
  navigators: OwnedNavigator[];
  gacha: GachaState;
  daily: DailyState;
  cosmetics: CosmeticLoadout;
  purchases: ProductCount[];
  passes: PassState[];
  events: EventProgress[];
}

// ---------------------------------------------------------------- shop, events, mail data (shop.json, events.json, mail.json)

export interface Reward {
  type: string;
  amount?: number;
  itemId?: string;
}

export interface Price {
  currency: string;
  amount: number;
}

export interface ShopItem {
  id: string;
  category: string;
  target?: string;
  basic?: boolean;
  hidden?: boolean;
  consumable?: boolean;
  rarity?: number;
  price?: Price;
  name?: LocalizedText;
  desc?: LocalizedText;
  style?: Record<string, unknown>;
  rewards?: Reward[];
  startUtc?: string;
  endUtc?: string;
  sortOrder?: number;
}

export interface ProductDef {
  id: string;
  kind: string;
  gold?: number;
  bonus?: number;
  firstBonus?: number;
  limit?: number;
  passDays?: number;
  passDailyJade?: number;
  priceUsd?: number;
  steamItemId?: string;
  steamPrices?: { currency: string; cents: number }[];
  rewards?: Reward[];
  name?: LocalizedText;
  desc?: LocalizedText;
  sortOrder?: number;
}

export interface ShopCatalog {
  goldToJadeRate: number;
  maxPassDays: number;
  vessels: string[];
  products: ProductDef[];
  items: ShopItem[];
}

export interface EventMilestone {
  target: number;
  rewards: Reward[];
}

export interface EventDef {
  id: string;
  type: string;
  startUtc?: string;
  endUtc?: string;
  color?: string;
  sortOrder?: number;
  title?: LocalizedText;
  desc?: LocalizedText;
  milestones: EventMilestone[];
}

export interface EventCatalog {
  claimGraceDays: number;
  events: EventDef[];
}

export interface MailTemplate {
  id: string;
  expiresDays: number;
  title: LocalizedText;
  body: LocalizedText;
  attachments: Reward[];
}

export interface PullResult {
  navigatorId: string;
  rarity: number;
  featured: boolean;
  isNew: boolean;
  fragments: number;
}

export interface RaceReport {
  songId: string;
  durationSec: number;
  mode: number;
  difficulty: number;
  score: number;
  maxCombo: number;
  perfect: number;
  good: number;
  miss: number;
  accuracy: number;
  navigatorId: string;
}

export interface RaceReward {
  coins: number;
  jade: number;
  bondExp: number;
  bondLevelBefore: number;
  bondLevelAfter: number;
}

export class GameError extends Error {
  readonly code: string;
  readonly status: number;
  /** Extra fields sent to the client next to code/message (e.g. minVersion for client_outdated). */
  readonly details: Record<string, unknown> | undefined;

  constructor(code: string, message: string, status = 400, details?: Record<string, unknown>) {
    super(message);
    this.code = code;
    this.status = status;
    this.details = details;
  }
}
