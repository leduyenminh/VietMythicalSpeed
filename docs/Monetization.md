# Monetization

How the game makes money: the premium currency, what the shop sells, how real-money purchases are verified, ideas for more revenue, and the rules each store and country imposes.

Everything below is data-driven. Prices, packs, items and events live in `UnityClient/Assets/VietMythicalSpeed/Resources/Data/` (`shop.json`, `events.json`, `mail.json`). The server reads the same files. In online mode the game takes the catalog and events from the server (`GET /v1/shop`, `GET /v1/events`), so new items, price changes and events go live with a server restart and need no game update.

## Currencies

| Currency | Name (vi) | How players get it | What it buys |
|---|---|---|---|
| **Gold Ingots** ▰ (`gold`) | Nén Vàng | **Real money only** (packs, pass, starter pack), plus free *bonus* gold from pack bonuses, mail and events | Cosmetics, supplies; exchanged 1:1 for jade |
| **Jade** ◆ (`jade`) | Ngọc Bích | Daily login (120), the first 5 races each day (30 each), events, mail, the Lantern Pass, gold exchange | Fortune-stick draws (150 per draw); one outfit (`skin_bach_hac_snow`, 1600) |
| **Coins** ● (`coins`) | Xu | Races (score × 0.02, up to 3,000 per race and 30,000 per day) | Awakening, a few starter cosmetics |
| **Incense** ✦ (`tickets`) | Nhang | Daily streak (+3 every 7 days), events, the starter pack, the Supplies tab | One draw each |

**Paid and free gold are kept separate.** Real-money gold goes to `gold` and bonus or gifted gold to `goldBonus`; the bar shows the total. Spending uses bonus gold first. A refund takes back everything the purchase gave. If that gold was already spent, paid gold goes below zero: a debt that blocks gold spending until new gold covers it. The cosmetics stay, but buying, spending and refunding gains nothing. The server records each store transaction (`purchases` table) with the paid and bonus amounts. These records outlive account deletion so tax and refund history survives. Some countries treat unspent paid currency as a prepaid balance (Japan, for example), and this split is what that requires.

**Why players can't buy jade directly:** jade buys random results. Exchanging gold for jade is one deliberate step, which makes it easy to turn off paid draws in a country that bans paid loot boxes (see [Compliance](#compliance)) while cosmetics stay on sale.

## Real-money products

Store product ids must match `shop.json` exactly, in the Google Play Console and in the Steam item ids.

| Product | USD | Gold | Bonus | First-purchase bonus | Notes |
|---|---|---|---|---|---|
| `gold_60` | 0.99 | 60 | — | +60 | |
| `gold_300` | 4.99 | 300 | +30 | +300 | |
| `gold_980` | 14.99 | 980 | +110 | +980 | |
| `gold_1980` | 29.99 | 1,980 | +260 | +1,980 | |
| `gold_3280` | 49.99 | 3,280 | +600 | +3,280 | |
| `gold_6480` | 99.99 | 6,480 | +1,600 | +6,480 | |
| `lantern_pass` | 4.99 | 300 | — | — | **Lantern Pass**: +90 jade every day for 30 days (claimed with the daily login). Stacks up to 180 days. |
| `starter_pack` | 1.99 | 120 | — | — | 10 incense + the *Wayfarer* frame; once per account |

- **First-purchase bonus:** the first purchase of each gold pack doubles its gold. The bonus is free gold.
- **Value per dollar:**
  - Gold packs give about 60–81 gold per dollar.
  - The Lantern Pass gives about 600 per dollar (300 gold plus 2,700 jade).
  - The starter pack gives about 810 per dollar.

  The pass and the starter pack are deliberately the best deals. They turn non-payers into payers, and the pass brings players back every day.
- **Steam prices:** set per currency in `steamPrices` (cents). Steam charges the player's wallet currency. A product without a price in that currency is refused with `currency_unsupported`, so add a row for every currency you sell in.
- **Google Play prices:** set in the Play Console; the game displays the store's localized price string. `priceUsd` is only the fallback label in development.

**A free player's draw budget:** daily login (120) plus five races (150) is 270 jade a day, about 8,100 a month or 54 draws. Hard pity is 100 draws (15,000 jade) and spark is 200 draws. A Lantern Pass holder gets about 11,000 jade a month, or 72 draws. Keep these ratios in mind when you tune prices.

## What the shop sells

The ★ numbers are rarity and only change the card colour. Every cosmetic is drawn from its `style` block in `shop.json`: colours plus a named shape or pattern. **A new item is a JSON entry, with no art files and no game update.** Real artwork can replace any of them later: navigator skins look in `Resources/Navigators/{id}/skins/{skinId}/`.

| Category (tab) | Shown where | Items (price) |
|---|---|---|
| Avatar frames | Home avatar, profile card, results | Lotus 300▰, Jade Cloud 25,000●, Full Moon 400▰, Dragon Scale 480▰, Phoenix 680▰ (+ event-only Welcome / Wayfarer) |
| Nameplates | Home name banner, profile card | Silk 300▰, Bamboo 30,000●, Lacquer 480▰, Đông Sơn Drum 680▰, Imperial 980▰ |
| Speech bubbles | Navigator lines in the lobby, races and results | Lotus 200▰, Scroll 15,000●, Lantern 300▰, Jade 300▰, Night 380▰ |
| Exhaust trails | Behind the vessel in races and the garage | Firefly 20,000●, Lotus 480▰, Thunder 580▰, Phoenix 680▰, Moonbeam 780▰, Lantern 880▰ |
| Outfits (navigator skins) | Lobby, navigator screens, gacha | Vàng Anh *Áo Dài* 1,280▰, Cuội *Moon* 1,280▰, Lân Nhi *Lion Dance* 1,480▰, Bạch Hạc *Snow* 1,600◆, Thanh Vân *Storm* 1,980▰, Nguyệt Nga *Night* 1,980▰ |
| Emotions | Lobby emote button; navigator face | Laugh 8,000●, Wink 120▰, Sleepy 150▰, Pout 180▰, Surprised 180▰, Heart 300▰ (Smile and Wave are free) |
| Garage interiors | Garage backdrop, profile card | Lotus Pond 680▰, Hội An Street 880▰, Starry Night 980▰, Imperial Palace 1,280▰ |
| Vessel kits | Paint + ornaments (lanterns, banners, horns) for one vessel | 2 per vessel: one for coins (20–25k●), the other 580–980▰ |
| Supplies | — | Coin pouch 60▰ (6,000●), coin chest 300▰ (33,000●), incense ×1 150▰, incense ×10 1,500▰ |

Frames, nameplates, bubbles, exhaust, emotions and garages each have free basic items. Outfits and kits fall back to the navigator's and vessel's own look. The coin-priced items give free players a goal and show off the system. The other changeable categories (frames, plates, bubbles, garage) have no coin items. In the shop, **Detail** shows a live 3D preview for kits and exhaust and a large preview for everything else.

**Adding things:**
- **An item:** add it to `items` with a `category`, `price {currency, amount}`, `rarity`, `name {vi, en}`, `desc` and `style`. Add `"hidden": true` for event or mail-only items.
- **A bundle:** use a `special` item with `"consumable": true` and a `rewards` list (items and currencies). Buying it pays out the rewards, and items already owned are skipped.
- **A real-money pack:** add a product with `gold`, `bonus`, optional `rewards`, `limit` and `passDays`. Create the same id in the Play Console, and set `steamItemId` and `steamPrices`.
- **An event:** add it to `events.json`:
  - `type` is `login`, `races` or `score`.
  - Set `startUtc` / `endUtc`, `milestones[{target, rewards}]`, `color`, `title` and `desc`.
  - Players can claim rewards for `claimGraceDays` days after the end. Races played offline count by the time they were played.
- **A mail to everyone, or to one player:** `POST /v1/admin/mail` (see [Backend.md](Backend.md#admin-api)).

`npm test` and the Unity EditMode tests validate the catalog: unique ids, known categories, prices present, and vessel kits that name a real vessel.

## How a purchase works

| Store | Flow |
|---|---|
| **Google Play** (Android, Unity IAP 5) | Game asks `POST /v1/purchases/check` (one-time packs) → Play billing sheet → `POST /v1/purchases/google {productId, purchaseToken}`. The server verifies the token with the Play Developer API (service account) and grants once per token. **Only then** does the game confirm (consume) the purchase. Purchases interrupted by a crash or lost connection are found again on the next start and delivered. |
| **Steam** (Windows + Steamworks) | `POST /v1/purchases/steam/init` (server calls `ISteamMicroTxn/InitTxn`) → Steam overlay asks the player → `MicroTxnAuthorizationResponse_t` → `POST /v1/purchases/steam/finalize` (server calls `FinalizeTxn`) → granted. Needs Steam linked to the account. |
| **Development** | `DevStore`: offline mode in the editor and in development builds, or online against a server with `PAYMENTS_FAKE=1` (refused when `NODE_ENV=production`). Release builds never offer it. |

Refunds:
- **Google Play:** the server polls Google's *voided purchases* every `GOOGLE_VOIDED_POLL_MINUTES` (default 6 h; also `POST /v1/admin/refunds/poll`). For each refunded purchase it takes the gold back (leaving a debt if it was spent), marks the purchase `revoked` and sends the player a mail explaining why.
- **Steam:** refunds are not polled yet. Check Steamworks reports, and handle them with the admin account lookup.

Offline mode has no real-money purchases. Real money always goes through the server.

## More ways to monetize

Ordered by expected return for the effort, given what the code already has.

**Use what's already built (data only)**
1. **Themed bundles:** a `special` consumable with item rewards. For example, a *Tết Bundle* with an outfit, emote, bubble and exhaust at about 25% off the separate prices. Run them for a limited time alongside an event.
2. **More outfits per navigator.** Outfits are the best-selling cosmetic in character games. Ship one or two each month, timed to Vietnamese holidays: Tết, Hùng Kings, Mid-Autumn, Hội An lantern nights.
3. **Event-only cosmetics:** hidden items given as event milestone rewards. They drive daily activity, which drives pass and pack sales.
4. **Limited banners** for new navigators. The gacha, pity, spark and odds screen already exist; a new banner is a `banners.json` entry.
5. **Prices in Vietnamese đồng:** set real local prices in the Play Console instead of auto-conversion, for example 25,000₫ / 129,000₫ / 399,000₫. Consider a smaller pack for players who prefer to spend little. Google Play in Vietnam already offers carrier billing and e-wallets, so you get those payment methods without extra work.

**Small code changes**

6. **Season pass (Hành Trình Mùa):** about 8 weeks of tiers earned by racing, with a free track and a premium track (about $9.99) holding outfits, kits, jade and incense. This is the biggest proven earner for rhythm and racing games. It can reuse the `events` progress plus a `passes` entry the way the Lantern Pass does.
7. **Weekly and monthly offers:** cheap repeatable packs, such as a weekly incense pack for $0.99, with a per-week `limit`. Needs a reset period on `limit`.
8. **Make the Lantern Pass a subscription:** Google Play subscriptions with auto-renewal. Verify with `purchases.subscriptionsv2` instead of one-off products.
9. **Titles and badges** on the nameplate, earned for achievements or sold. A new cosmetic category is about 50 lines: a category, `CosmeticArt` drawing and a shop tab.

**New content**

10. **Note and lane skins, hit sounds:** cosmetics players see in every race.
11. **Navigator voice packs:** new voiced lines for lobby, race and results. Real voice actors make this a premium item.
12. **Song packs:** curated tracks with hand-tuned charts, for example licensed Vietnamese indie or traditional music, sold as one-off products with revenue share to artists. Imported music stays free; this adds a "featured" side.
13. **Steam extras:** a *Supporter Pack* DLC (soundtrack, art book, an exclusive outfit), and a fair base price on Steam ($4.99–9.99) with cosmetics on top, since PC players dislike free-to-play.

**Optional, with care**

14. **Rewarded ads** on Android only, never during a race. For example, watch an ad for +50% race coins, at most 3 a day. Use AdMob or Unity Ads mediation. This earns from players who never pay, but needs ad consent screens (GDPR and Vietnamese rules), and the Data safety form changes.
15. **Gifting** cosmetics to friends, once a friends list exists. Gifts drive spending at Tết.
16. **Creator and merch:** physical goods (stickers, acrylic standees of the navigators) and Patreon/Ko-fi supporter tiers with an in-game supporter frame. Keep supporter perks cosmetic.

What to avoid: selling power (score multipliers, accuracy aids), energy timers that block racing, and pay-to-skip pity. These hurt a rhythm game's fairness and reviews more than they earn.

## Compliance

Check these before launch. They are not legal advice; confirm the country-specific items with a lawyer.

- **Store billing rules.**
  - Google Play requires Google Play Billing for digital goods in Android apps, apart from the alternative-billing programs offered in some regions.
  - Steam requires Steam Wallet (MicroTxn) for purchases in Steam builds.
  - The code follows both, and the dev store is never offered in release builds.
- **Random items: odds disclosure.** Google Play requires published odds for randomized purchases. The fortune-stick screen's **Rates** button shows base rates, soft and hard pity, the featured share and spark. Keep it accurate whenever `gacha.json` changes.
- **Age ratings (IARC):**
  - Declare *In-game purchases* and *Includes random items*.
  - If you target children, the Families policy applies, with limits on ads and purchase prompts.
  - Keep purchase prompts calm: no countdown pressure aimed at minors.
- **Show real prices:** EU consumer authorities (CPC network, 2025 principles on in-game currencies) expect the real-money price of virtual-currency items to be clear, and no artificial urgency. Each gold pack shows its local price. Consider showing an approximate real price on items costing 1,000▰ or more.
- **Belgium (and check the Netherlands):** paid loot boxes count as gambling in Belgium. For Belgian players, disable the gold→jade exchange and the incense sold for gold (Supplies tab), so draws use only free jade and incense. Gold still buys cosmetics. The Netherlands relaxed after a 2022 court ruling; check the current position.
- **Vietnam (Decree 147/2024/ND-CP on internet services and online games):** the online mode is a game with payments for Vietnamese players. The decree covers:
  - game licensing and classification
  - player identification, using Vietnamese phone numbers or national electronic ID
  - playtime limits for under-18 players
  - local payment rules

  Foreign publishers usually work through a licensed Vietnamese partner. Plan for this before taking payments from Vietnam, and keep the server's account data able to store a verified phone number.
- **Refunds and chargebacks:** handled for Google Play (see above). Answer support requests through `GET /v1/admin/account`, which returns profile plus purchases. Don't punish players for refunds beyond taking the currency back.
- **Privacy:** purchases store an order reference and the store transaction hash, not payment details. Update the Play *Data safety* form to cover *Purchase history* and *App interactions*. If you add ads, also cover advertising ID and device identifiers.
