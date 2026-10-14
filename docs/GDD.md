# Viet Mythical Speed — Game Design

*Tốc Linh Nhạc Mạch* · Android-first music racer · Unity 6.3 LTS

## Pitch

Ride a mythic vessel along a glowing road that is generated from **your own music**. Every beat becomes a spirit pearl to catch or a ghost fire to dodge. A **Navigator** (*Hoa Tiêu*) rides with you: she cheers, teases, and in Skill mode lends you her power. Collect Navigators at the shrine by drawing fortune sticks (*xin xăm*).

Inspirations: **Music Racer** (music-generated track, chill neon driving), **Riichi City / Mahjong Soul** (gacha companions with voices, bond and an optional skill ruleset), **Blue Archive** (anime 2D art and clean, bright UI).

## World: Vân Lạc Quốc

A fictional realm modelled on feudal Vietnam where music flows through the land as **spirit veins** (*linh mạch*). Couriers called **Speed Envoys** (*Tốc Sứ*) carry messages along these veins on spirit vessels, guided by Navigators who can hear the music.

| Region | Inspiration | Race theme |
|---|---|---|
| Vịnh Rồng Đáp (Descending-Dragon Bay) | Hạ Long Bay | Night bay, limestone karsts, fishing-boat lights |
| Hội Đèn Sông Hương | Perfume River lantern festival | Floating lanterns, lotus candles, seven-tier pagoda |
| Rừng Trúc Sương Mai | Bamboo forests at dawn | Bamboo groves, fireflies, golden sky |
| Hoàng Thành Thăng Vân | Imperial citadel | Crenellated walls, gates every four bars, lantern posts |

Mythic beings: Rồng (serpentine dragon), Chim Lạc (bird of the Đông Sơn bronze drums), Hồ ly chín đuôi (nine-tailed fox of West Lake), Kim Quy (Golden Turtle), Sơn Tinh / Thủy Tinh (mountain and water spirits), Kỳ Lân, Nghê, Hạc, Ma trơi, Cá chép hóa rồng, Thỏ Ngọc, and folk heroes such as Gióng, Cuội and Bờm.

### Beyond the Five Gates

The Five Gates (*Ngũ Quan*) at the edges of Vạn Lạc have opened, and the music-veins now reach five fictional neighbouring realms. Each has its own road, weather and architecture, a navigator and a codex in the in-game **World Atlas**:
- **Vân Khuyết / Yunque**: cloud-sea peaks, moon gates, scholars and spirit-beast scrolls.
- **Hanayume**: a tunnel of vermilion gates, blossom storms, crow-spirit taiko drums.
- **Dalmaru**: tile-roof villages, dokkaebi goblins, guardian posts and blue goblin fire.
- **Nebelreich**: fir forests, timber-framed towns, clock towers, a singer on the river rock.
- **Lumikaari**: birch lakes, saunas, snow and an aurora painted by the Fire Fox.

Details, inspirations and how to add a realm: [World.md](World.md).

**Languages:** Vietnamese, English, Simplified Chinese, Japanese, Korean, Spanish, German and Finnish. Signature Vietnamese terms (Vạn Lạc, Hoa Tiêu, Tốc Sứ, Xin Xăm, Nén Vàng, áo dài, Tết) stay untranslated ([Localization.md](Localization.md)).

## Core loop

1. **Import a song** (mp3/ogg/wav). It is analysed once, on-device: band-wise spectral-flux onsets, loudness, brightness and tempo.
2. **Pick a mode, difficulty, scenery and vessel.** The chart and track are generated deterministically, so the same song and settings always give the same road.
3. **Race.** Steer between 3 lanes; pearls land on the music's onsets. The road's curves, hills and speed follow the music's energy.
4. **Rewards.** Coins (daily-capped), jade for the first 5 races of the day, and bond EXP with your Navigator.
5. **Draw fortune sticks** with jade or incense tickets to meet new Navigators, then awaken duplicates and raise bond.

## Modes

| Mode | Notes | Skills |
|---|---|---|
| **Cổ Điển (Classic)** | Collect pearls; scores comparable between players | Off (like Riichi City's no-skill rules) |
| **Linh Kỹ (Skill)** | Collect pearls; spirit gauge fills, tap to cast | Active and passive |
| **Né Tránh (Dodge)** | Dodge ghost fires; pearls in the safe lane are optional bonuses | Active and passive |
| **Thiền (Zen)** | No notes, no score, just the ride | — |

Difficulty changes note density (minimum gap 0.42 s / 0.25 s / 0.15 s), base speed (30 / 37 / 44 m/s), curve strength and dodge reaction time. Charts never ask for an impossible two-lane jump inside the "fast gap", and dodge rows always leave a reachable safe lane. Both rules are unit-tested.

## Judging and scoring

- Judging is by lateral distance from the note's lane when the vessel passes it: **Perfect** ≤ 0.3 lane, **Good** ≤ 0.62 lane, otherwise **Miss**. Ghost fire hits within 0.55 lane.
- Points: Perfect 300, Good 120, big pearl ×2, dodge 50. The combo multiplier rises by +0.1 every 20 combo, up to ×2.0.
- Ranks by accuracy: S+ (≥98.5% with no misses), S ≥95%, A ≥88%, B ≥78%, C ≥65%, D otherwise.
- The spirit gauge (0–100) fills on hits. A skill costs 70–100 spirit depending on the Navigator.

## Navigators

15 at launch: 6 ★3 *Thiên* (2 limited), 6 ★2 *Địa*, 3 ★1 *Nhân*. Full bios and voice lines are in `UnityClient/Assets/VietMythicalSpeed/Resources/Data/navigators.json`.

| Skill type | Effect | Example |
|---|---|---|
| magnet | Pearls in neighbouring lanes count as Perfect | Lạc Thanh Vân |
| widePerfect | Perfect window +X% | Lạc Hồng Anh, Lửa Trơi, Vàng Anh |
| comboGuard | Next N misses don't break combo | Hồ Nguyệt Nga, Lý Ngư |
| shield | Absorb N hits or misses | Kim Quy Lam, Nghê Bảo, Bờm |
| scoreMult | Score ×X | Thủy Hà, Gióng Thiết |
| spiritBoost | Spirit gain ×X | Lân Nhi, Cuội |
| autopilot | Steers to the pearls | Bạch Hạc Linh |
| transmute | Ghost fires become pearls, pearls become big pearls | Sơn Lâm |

Passives give score, spirit, perfect-window, coin or bond bonuses. Passives that affect the race only apply in Skill and Dodge modes.

**Companion features (Riichi City style):**
- Lobby presence: tap her for greetings, and bond-gated "idle" lines.
- Race barks for start, combo milestones, miss streaks, skill ready/cast and the finish.
- A skill cut-in banner.
- An "obtained" line on her first draw.
- A voice gallery in the details screen.
- Real voice clips play automatically when placed at `Resources/Navigators/{id}/voice/{lineId}.ogg`.

**Bond:** 10 levels; each race gives 15–40 EXP. **Awakening:** duplicates become 100 / 30 / 10 soul shards (★3/★2/★1). Each awakening level costs shards plus coins and raises skill values.

## Gacha — Xin Xăm (fortune sticks)

All values live in `Resources/Data/gacha.json` and are shared with the server.

- Base rates: ★3 3% · ★2 18.5% · ★1 78.5%. **Hard pity** at pull 100. Soft pity is available but off by default.
- **Featured 50/50** on limited banners. Losing it guarantees the next ★3 is featured. The pity counter is shared across limited banners.
- **10-draw guarantee:** at least one ★2 or better.
- **Spark (Hương Hỏa):** 1 point per draw on a featured banner; 200 points buys the featured Navigator of your choice.
- Cost: 150 jade per draw, or one incense ticket (tickets are spent first).
- **Odds disclosure:** the Rates popup lists every Navigator's exact probability, which Google Play requires for loot boxes. Paid randomised items are restricted in some countries (e.g. Belgium), so check before enabling purchases there.

## Economy

Starting kit: 3000 jade, 5000 coins, 10 tickets and Vàng Anh (★1) as the starter Navigator.

| Source | Amount | Cap |
|---|---|---|
| Daily login | 120 jade | +3 tickets every 7-day streak |
| Race | score × 0.02 coins (50–3000 per race) | 30 000 coins/day |
| First 5 races per day | +30 jade each | — |
| Lantern Pass (paid) | +90 jade per daily login | 30 days per purchase, up to 180 |
| Events, mail | jade, coins, incense, gold, cosmetics | per event / per mail |

Rewards are capped server-side, so a forged race report can't mint currency.

**Premium currency and shop.** Gold Ingots (*Nén Vàng*, ▰) are bought with real money. They buy cosmetics and exchange 1:1 into jade. Free gold from bonuses, events and mail is tracked separately and spent first.

The shop sells cosmetics, plus a few supplies (coin pouches, incense for draws). The cosmetics are:
- avatar frames, nameplates and speech bubbles
- exhaust trails and vessel kits
- navigator outfits and emotions
- garage interiors

Every cosmetic is drawn from a small style description in `shop.json`, so new ones need no art pipeline and, in online mode, no game update.

**Live ops.**
- **Mail:** welcome gifts, broadcasts and support compensation.
- **Events:** login calendars, race counts and score totals, with milestone rewards. They run on fixed dates from `events.json`.

Prices, the pass, compliance and ideas for more revenue are in [Monetization.md](Monetization.md).

## Controls

- **Tap** left/right half of the screen (default), **Swipe**, **Drag** (analogue steering like Music Racer's mouse mode) or **Tilt**.
- On PC and in the editor: A/D or the arrow keys.
- An audio offset (−200…+200 ms) compensates Bluetooth latency.
