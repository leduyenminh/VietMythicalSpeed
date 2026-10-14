# Art Direction & Navigator Art Briefs

## Style pillars

1. **Blue Archive-like anime 2D:** clean line art, soft cel shading with one shadow tone and a rim light, bright pastel skies, an expressive but readable silhouette. Each Navigator has a **halo-like emblem** above the head, drawn as a Vietnamese motif rather than a ring:

   | Navigator | Emblem |
   |---|---|
   | Lạc Thanh Vân | Dragon scales |
   | Lạc Hồng Anh | Bronze-drum sun |
   | Kim Quy Lam | Turtle-shell hexes |

2. **Feudal Việt costume, modernised:**
   - Garments: áo giao lĩnh, áo nhật bình, áo tấc, yếm, khăn vấn and nón quai thao, given streetwear-style layering, sneakers or boots.
   - Accessories: a tech accessory (headset, wrist display) tying them to the "Navigator" role.
   - Avoid exact replicas of real royal regalia.
3. **Mythic companion on every Navigator:** her spirit beast appears in the full-body art (small, chibi-ish or wrapping around her).
4. **Race visuals are neon-mythic** (Music Racer energy) while menus are **bright daytime** (Blue Archive energy). The contrast is intentional.

## UI language (already implemented in code)

| Token | Value | Use |
|---|---|---|
| Ink | `#22314F` | Text |
| Primary | `#2E9BFF` / `#1773D6` | Main buttons, headers |
| Accent | `#FFD23F` | Call to action, highlights |
| Lacquer | `#D7263D` | Festive / danger, "NEW" |
| Gold | `#F2C94C` | Shrine, rewards |
| Sky | `#BFE3FF` → `#F7FBFF` | Menu backgrounds |
| ★3 / ★2 / ★1 | `#FFB800` / `#B57BFF` / `#6FA8DC` | Rarity |

- Slanted (22 px skew) buttons with a drop shadow.
- White rounded cards (28 px radius).
- Diagonal hatch on headers and the title tab sticking out of popups.
- Recommended font: **Be Vietnam Pro** (OFL; full Vietnamese diacritics). Drop it at `UnityClient/Assets/VietMythicalSpeed/Resources/Fonts/Main.ttf` and the whole UI switches to it.

## Deliverables per Navigator

Files go in `UnityClient/Assets/VietMythicalSpeed/Resources/Navigators/{id}/` and are picked up automatically:

| File | Size | Notes |
|---|---|---|
| `portrait.png` | 1024×1024 | Bust, transparent or painted background, face in upper-middle third |
| `standing.png` | ~1400×2300 | Full body, transparent, feet at bottom edge (pivot is bottom-centre) |
| `voice/{lineId}.ogg` | — | Optional; line ids are in `navigators.json` (`greet1`, `start`, `cast`, `high`…) |

Texture import: ASTC 6×6 on Android, max size 2048, mipmaps off for UI.

## Art briefs

Colours are each Navigator's `color` / `accentColor` in the data file.

| id | Name | Rarity | Brief |
|---|---|---|---|
| `thanh_van` | Lạc Thanh Vân | ★3 | Dragon princess, regal and calm. Teal-blue áo nhật bình cut short over a pleated skirt, pearl hair ornaments, a slim azure serpentine dragon coiled over her shoulders. Emblem of dragon scales. Pose: one hand raised conducting the sea mist. Secret sweet tooth – a tiny chè cup charm. |
| `lac_hong_anh` | Lạc Hồng Anh | ★3 | Energetic bronze drummer. Orange-bronze outfit with Đông Sơn drum patterns (sun star, flying Lạc birds) printed on the fabric, drumsticks, a small bronze drum on her hip, Lạc birds perched on her head. Big grin, mid-jump. |
| `nguyet_nga` | Hồ Nguyệt Nga | ★3 | Reformed nine-tailed fox of West Lake. Lavender áo giao lĩnh with a dark obi-style sash, nine translucent tails fanning behind, fox ears, crescent-moon hairpin. Sly smile, sitting on a lotus leaf. |
| `quy_lam` | Kim Quy Lam | ★3 | Scholarly archivist of the Turtle Tower. Gold and jade palette, round glasses, a scroll tube, shell-patterned cape; the golden turtle floats beside her carrying books. Calm, slight head tilt. |
| `son_lam` | Sơn Lâm | ★3 limited | Earnest young mountain lord (male). Forest-green armour with stone plates, a long staff topped with a mountain crest, cloud scarf. Stoic, planted stance; small rocky mountain-spirit buddy at his feet. |
| `thuy_ha` | Thủy Hà | ★3 limited | Proud water sovereign. Deep-blue flowing áo dài-like robe turning into water at the hem, a river-serpent (thuồng luồng) arcing overhead, rain droplets suspended. Arms crossed, looking away (tsundere). |
| `lan_nhi` | Lân Nhi | ★2 | Festival qilin dancer. Red-gold lion-dance costume hoodie with a qilin head hood, pom-poms, Mid-Autumn lanterns. Mid-dance. |
| `nghe_bao` | Nghê Bảo | ★2 | Shy stone guardian (Vietnamese nghê). Warm sand palette, stone-textured gauntlets, curly mane-like hair, peeking from behind a gate pillar. |
| `bach_hac` | Bạch Hạc Linh | ★2 | Elegant crane poet. White and pale blue, feather sleeves, a red crown accent, a brush and a poem scroll. Standing on one leg. |
| `giong_thiet` | Gióng Thiết | ★2 | Hot-blooded iron-horse rider (male). Iron armour with flame vents, a bamboo whip, a rice ball in hand; the iron horse breathing small flames behind him. |
| `lua_troi` | Lửa Trơi | ★2 | Spooky-cute will-o'-the-wisp. Mint-green ghostly glow, lantern-shaped skirt, floating feet, a wisp flame for a hair ornament. Giggling behind her sleeve. |
| `ly_ngu` | Lý Ngư | ★2 | Determined carp girl. Orange-white koi patterns, fin-like sleeves, scales on her cheeks, a headband with "100". Mid-leap over a waterfall. |
| `cuoi` | Cuội | ★1 | Moon boy. Simple brown áo, a banyan leaf in his hair, the Jade Rabbit on his shoulder. Mischievous wink. |
| `vang_anh` | Vàng Anh | ★1 | The starter guide. Soft yellow outfit, persimmon (quả thị) charm, an oriole on her finger. Warm, welcoming smile; the "face" of the game. |
| `bom` | Bờm | ★1 | Easygoing boy with a palm-spathe fan (quạt mo), a sticky-rice parcel tied to his belt, a big carefree smile. |

## Placeholders

Until art arrives, `NavigatorArt` draws placeholders from the data: a gradient card in the Navigator's colours, a halo, a silhouette and her Hán-Nôm emblem character (龍 雒 狐 龜 山 水 …). The game is fully playable with placeholders. The vessels and scenery are procedural meshes and are meant to be replaced by modelled assets later, using the same unlit vertex-colour + additive-glow look.
