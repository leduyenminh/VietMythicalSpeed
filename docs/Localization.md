# Localization

The game ships in eight languages:

| Code | Language | Picker name |
|---|---|---|
| `vi` | Vietnamese (source) | Tiếng Việt |
| `en` | English (source, fallback) | English |
| `zh` | Simplified Chinese | 简体中文 |
| `ja` | Japanese | 日本語 |
| `ko` | Korean | 한국어 |
| `es` | Spanish | Español |
| `de` | German | Deutsch |
| `fi` | Finnish | Suomi |

On first launch the game picks the device language (any Chinese variant becomes `zh`) and falls back to English. Players change it in **Settings → Language**, and the menus rebuild immediately. Development builds accept `-vmsLang ja` and so on.

## Where the text lives

| Text | File | Fallback |
|---|---|---|
| Interface (474 keys) | `Resources/Localization/{lang}.txt`: `key = value`, `#` comments, `\n` for line breaks, `{0}` parameters | English, then the key |
| Game data (639 texts): navigators, voice lines, skills, shop items, events, banners, mail templates, world atlas | Inline in `Resources/Data/*.json`: every text is `{ "vi", "en", "zh", "ja", "ko", "es", "de", "fi" }` | English, then Vietnamese |
| Password-reset email | `server/src/auth/resetMail.ts` | Vietnamese |
| Date formats | `format.date` / `format.dateTime` keys (`dd/MM/yyyy`, `yyyy/MM/dd`, `dd.MM.yyyy`…) | — |

Data texts are inline so the server sends translated live-ops content (a new shop item, an event, an admin mail) without a game update. `POST /v1/admin/mail` accepts any of the eight languages in `title` and `body` and needs at least `vi` or `en`.

## Glossary: Vietnamese terms we keep

Translations keep these Vietnamese terms in every language. They are the game's identity, the way other games keep their invented words:

| Term | Meaning | Note |
|---|---|---|
| **Vạn Lạc** | the home realm | proper noun |
| **Hoa Tiêu** | Navigator, the companion characters | never translated; uppercase on the big lobby button |
| **Tốc Sứ** | Speed Envoy, the player | written 速使 in Chinese and Japanese (the same word in Hán script); English uses "Envoy" |
| **Xin Xăm** | the fortune-stick draw (gacha) | feature name; the verb "draw" is translated (抽签 / おみくじ / 뽑기 / tirada / Ziehung / nosto) |
| **Nén Vàng** | Gold Ingots, the premium currency | currency name in every language |
| áo dài, Tết, chè, dó paper, Hà Đông silk, Đông Sơn / Ngọc Lũ drums | cultural items | kept, with diacritics |
| Navigator and place names of Vạn Lạc | e.g. Lạc Thanh Vân, Vịnh Rồng Đáp | kept with diacritics (English keeps the existing unaccented spelling) |

Everything else is translated naturally: jade (碧玉 / 翡翠 / 비취), coins, incense, spirit pearls, bond, judgement words.

**The neighbouring realms** use the name native to their own culture where that script exists, and Latin elsewhere:

| Realm | vi | en / es / de / fi | zh | ja | ko |
|---|---|---|---|---|---|
| Cloud-sea realm | Vân Khuyết | Yunque | 云阙 | 雲闕 | 운궐 |
| Flower-dream isles | Hanayume | Hanayume | 花梦 | 花夢 | 하나유메 |
| Moon-ridge highlands | Dalmaru | Dalmaru | 月岭 | ダルマル | 달마루 |
| Mist kingdom | Nebelreich | Nebelreich | 雾境 | ネーベルライヒ | 네벨라이히 |
| Snow-arc land | Lumikaari | Lumikaari | 卢米卡里 | ルミカーリ | 루미카리 |

Their navigators follow the same rule, for example Kurobane Tsuzumi becomes 黒羽ツヅミ in Japanese and 黑羽鼓 in Chinese.

## Fonts

The UI uses OS fonts unless one is bundled. The Latin UI font comes first (Roboto / Noto Sans / Segoe UI), followed by the CJK font of the current language, so Japanese, Chinese and Korean each get their own shapes for shared characters:

| Language | Windows | Android / others |
|---|---|---|
| Chinese | Microsoft YaHei UI | Noto Sans CJK SC |
| Japanese | Yu Gothic UI, Meiryo | Noto Sans CJK JP |
| Korean | Malgun Gothic | Noto Sans CJK KR |

The other CJK fonts follow as fallbacks, so the language picker can always show 简体中文 / 日本語 / 한국어.

**For store releases, bundle the fonts.** Some Android builds ship only part of the CJK fonts. Put OFL-licensed fonts in `Resources/Fonts/`:
- `Main` (Latin and Vietnamese, e.g. Be Vietnam Pro)
- `Main_zh` (Noto Sans SC)
- `Main_ja` (Noto Sans JP)
- `Main_ko` (Noto Sans KR)

Each CJK font adds roughly 5–10 MB. Subset them to the characters actually used if size matters.

## Workflow

```bash
node tools/i18n/check.js                                   # UI keys, placeholders, keys used in code, data completeness
node tools/i18n/data-strings.js status                     # data texts per language
node tools/i18n/data-strings.js export ja ja-data.txt      # flat "key = text" file for a translator (TODO marks gaps)
node tools/i18n/data-strings.js import ja ja-data.txt      # write the translations back into Resources/Data/*.json
node tools/i18n/jsonfmt.js UnityClient/Assets/VietMythicalSpeed/Resources/Data/*.json   # normalise formatting
```

**Adding a UI string:**
1. Add the key to `en.txt` and `vi.txt`.
2. Translate it in the other six files.
3. Run `check.js`, which fails on missing keys or mismatched `{0}` placeholders.

**Adding data (a navigator, an item, an event):**
1. Write the `vi` and `en` text in the JSON.
2. Export the language files, translate them, and import them.

The EditMode test `LocalizationTests` and the server test `languages:` fail when a language is incomplete.

**Adding a language:**
1. Add it to `Loc.Languages`, `Loc.NativeName`, `Loc.FromSystemLanguage`, `LocalizedText` (a field and `Raw`), `LANGUAGES` in `server/src/game/types.ts`, `resetMail.ts`, and the `LANGS` lists in `tools/i18n/*.js` and `LocalizationTests`.
2. Create the UI file and import the data translations.

**Style notes:**
- German and Finnish run long. Titles use `FitLabel`, which shrinks to fit, but check new long buttons in the `languages` screenshot run (see [Testing.md](Testing.md)).
- CJK text has no spaces. Unity's text wraps it at character boundaries.
- Keep voice lines in character: each navigator has a voice (regal, teasing, shy, boastful) that the translations follow.
