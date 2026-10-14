# The World: Vạn Lạc and the Realms Beyond the Five Gates

Everything here is **fiction**, in the spirit of open-world gacha games: each realm borrows architecture, artifacts, words and myths from a real culture, but has its own invented name, places and people. No real country, city, dynasty or person is named in the game. The inspirations are noted here for the team only.

## The premise

**Vạn Lạc** is the home realm: bronze drums, cloud dragons, lantern rivers. Under every river run the **music-veins** (nhạc mạch), and **Tốc Sứ** (Speed Envoys) ride spirit vessels along them, guided by **Hoa Tiêu** (Navigators).

On the edges of Vạn Lạc stood **Ngũ Quan**, the Five Gates: stone gates sealed for a thousand years. When the music-veins swelled (the Grand Opening), all five opened at once. Now the veins reach five neighbouring realms; visitors come through, and Envoys can race their roads.

In the game this means:
- a **World** button on the lobby, opening the atlas
- five new **roads**, one per realm
- five new **navigators**
- realm **cosmetics**
- the **Five Gates Festival** event
- a limited banner

## The realms

| Realm (in-game name) | Inspired by (internal) | Road | Navigator |
|---|---|---|---|
| **Vạn Lạc** (雒) | Vietnam | Dragon Bay, Lantern River, Bamboo Dawn, Imperial Citadel | the original 15 |
| **Vân Khuyết / Yunque** (雲) | continental East Asian (cloud-sea mountains, scholars) | Cloud-Sea Peaks | **Mặc Vân Thư / Mo Yunshu** ★3: scholar with the White Sage-Beast |
| **Hanayume** (花) | Japanese island culture | Thousand Gates | **Kurobane Tsuzumi** ★2: crow-spirit drummer |
| **Dalmaru** (달) | Korean peninsula folklore | Moonlit Village | **Seol Bitna** ★2: dokkaebi with the golden club |
| **Nebelreich** (♜) | Central European forests and towns | Misty Castle Road | **Liesel Silberklang** ★2: river-rock singer |
| **Lumikaari** (❄) | Nordic lake-land and its epic songs | Aurora Lakes | **Aava Revontuli** ★3 (limited): fire fox of the aurora |

Each realm has a codex in the atlas (`world.json` → `lore`) of 4–5 entries: a place, architecture, an artifact, a myth, a custom.

### Vân Khuyết (Yunque): Realm of the Cloud Sea
- **Look:** stone pillars rising from a pink-gold cloud sea at dawn, pavilions with flying eaves, round **moon gates** spanning the road, red lantern posts, a distant pagoda. Low clouds drift past below the road.
- **Codex:**
  - Moon Gate (architecture)
  - Hundred-Scroll Atlas (artifact)
  - the White Sage-Beast (myth; an all-knowing beast)
  - Monsoon Kites (custom)
- **Tone:** scholarly, serene, ink and zither music.

### Hanayume: Isles of the Flower Dream
- **Look:** a tunnel of vermilion gates in time with the music (one every two beats), stone lanterns, blossom trees, a five-storey pagoda, falling petals.
- **Codex:**
  - the Thousand Vermilion Gates
  - the Crow-Spirit Taiko
  - the Hundred-Year Objects (old things that wake up)
  - Floating Lantern Night
- **Tone:** festive, mischievous spirits, taiko drums.

### Dalmaru: Highlands of the Moon Ridge
- **Look:** curved tile-roof houses with glowing paper doors, pairs of carved guardian posts, pine ridges, blue goblin fires, coloured lotus-lantern strings, a red spiked gate with a guardian jewel. Blue sparks rise around the road.
- **Codex:**
  - Hanok Tile Houses
  - the Dokkaebi Club
  - the Sun and Moon Siblings
  - Village Guardian Posts
- **Tone:** cheeky goblins, four-drum harvest music, moonlight.

### Nebelreich: Kingdom of the Mists
- **Look:** fir forests, timber-framed houses with warm windows, clock towers, castles on crags, pointed stone arches with rose windows, will-o'-wisps, drifting fog motes.
- **Codex:**
  - Timber-Frame Houses
  - the Figure Clock
  - the Singer on the Rock
  - the Winter Market
- **Tone:** fairy-tale forest, clockwork, choirs and organs.

### Lumikaari: Land of the Snow Arc
- **Look:** birch groves, snow-capped pines, frozen lakes, lakeside saunas with smoking chimneys, half-ring ice arches, falling snow. The **aurora** is painted by the sky shader.
- **Codex:**
  - the Fire Fox (aurora myth)
  - the Kantele
  - the Lakeside Sauna
  - the Wish-Mill
- **Tone:** quiet, starry, long sung stories. The Fire Fox and Vạn Lạc's nine-tailed fox are "distant cousins": a bridge between the realms.

## Cosmetics from the realms

| Category | Items |
|---|---|
| Avatar frames | Moon Gate, Hanayume Blossom (sakura shape), Dokkaebi Fire (coins), Clockwork (gear shape), Aurora Snowflake; event-only *The Five Gates* |
| Nameplates | Ink-Wash Peaks, Hanayume Waves, Hanji Paper (coins), Stained Glass, Birch Bark |
| Speech bubbles | Blossom Letter, Celadon (coins), Old Parchment, Snowdrift |
| Exhaust | Drifting Clouds, Blossom Storm, Goblin Fire (coins), Mistwood Wisps, Foxfire Trail |
| Garage interiors | Cloud-Sea Pavilion, Shrine Garden, Hanok Courtyard, Clockmaker's Workshop, Aurora Lodge |
| Outfits (crossover) | Nguyệt Nga · Hanayume Shrine Maiden, Hồng Anh · Dalmaru Festival Dress, Thanh Vân · Lumikaari Winter Coat |
| Vessel kits | Aurora Kite, Brass Clockwork Cart |

They are all drawn from `shop.json` styles. The new shapes and patterns are in `UiSprites` (sakura, gear, snow; ink, arcs, glass, birch), and the new garage props are in `CosmeticArt.Garage`.

## Adding a realm

1. **`world.json`:** add the realm with name, title, description, colour, glyph, `sortOrder` and lore entries.
2. **`RaceTheme.All`:** add a road with `realm = "<id>"`, a palette, `ambient` (petals / snow / motes / sparks / clouds) and optionally `aurora`.
3. **`PropFactory.Realms.cs`:** add the scenery (`PropStyle` value, a populate method, and a section gate in `SectionGate`). Respect `Density` and `Minimal`.
4. **Navigators:** add them with `"realm": "<id>"` in `navigators.json`, and optionally a banner.
5. **Translations:** translate everything (see [Localization.md](Localization.md)).
6. **Tests:** run `LocalizationTests`, which checks realms, navigator realms and translations.
