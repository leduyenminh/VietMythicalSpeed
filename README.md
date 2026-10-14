# Viet Mythical Speed — *Tốc Linh Nhạc Mạch*

An Android music racer in the spirit of **Music Racer**: import your own songs and ride a glowing road generated from them, through a fictional feudal Vietnam full of dragons, nine-tailed foxes and Lạc birds. **Navigators** (*Hoa Tiêu*), drawn from a fortune-stick gacha (*xin xăm*), ride with you like Riichi City characters: they talk, build bond, and lend their skills in Skill mode. Menus follow Blue Archive's bright anime style.

- **Engine:** Unity **6.3 LTS** (6000.3.26f1), URP (the Built-in pipeline also works)
- **Targets:** Android (IL2CPP / ARM64, Vulkan + GLES3) and Windows/Steam
- **Backend:** Node.js 24, no runtime dependencies, SQLite

## What's in the box

| Area | Where | Status |
|---|---|---|
| Engine-free game rules: gacha (pity, 50/50, spark, odds), economy, scoring, audio analysis, beatmap/track generation, demo song and SFX synthesis, versions, offline race queue, shop and event rules | `UnityClient/Assets/VietMythicalSpeed/Core` | ✅ 61 EditMode tests pass in Unity 6.3 (1 explicit generator skipped) |
| Race: streamed track, 4 themed environments, procedural vessels, pearls/ghost fires, spectrum towers, spirit dragon, chase camera, 4 control schemes, skills, HUD, navigator barks, bloom, dynamic resolution | `Scripts/Race`, `Scripts/Rendering`, `Resources/Shaders` | ✅ autoplayed demo race: 163/163 Perfect (charts, timing and judging agree) |
| Builds | `Builds/VietMythicalSpeed-dev.apk` | ✅ Android dev APK (43 MB with Google Play Billing, IL2CPP ARM64, target SDK 36) and Windows player built headless |
| UI (code-built, no prefabs): login, lobby, song select + import, navigators, gacha + reveal ceremony, results, shop, garage, mail, events, settings; 8 languages (Tiếng Việt, English, 简体中文, 日本語, 한국어, Español, Deutsch, Suomi) | `Scripts/UI`, `Resources/Localization` | ✅ all UI and game data translated; see [Localization](docs/Localization.md) |
| **World beyond the Five Gates:** five fictional neighbouring realms (cloud-sea peaks, shrine isles, tile-roof highlands, misty castle forests, aurora lakes) with their own roads, procedural architecture, weather and aurora sky, 5 new navigators, realm cosmetics, a festival and a world atlas with a codex | `Resources/Data/world.json`, `Scripts/Race/PropFactory.Realms.cs`, `Scripts/UI/Screens/WorldScreen.cs` | ✅ see [World](docs/World.md) |
| **Low-end mode:** Ultra Low graphics tier, flat UI and power-saving 30 fps menus | `Rendering/QualityProfile.cs`, `UI/UiTheme.cs` | ✅ see [Performance](docs/Performance.md) |
| **Shop & currency:** paid Gold Ingots (*Nén Vàng*) kept apart from free bonus gold. Gold packs, Lantern Pass and starter pack. 50+ cosmetics drawn from data (avatar frames, nameplates, speech bubbles, exhaust trails, outfits, emotions, garage interiors, vessel kits) with a live 3D vessel preview | `Core/Shop`, `Scripts/UI/Screens/ShopScreen.cs`, `GarageScreen.cs`, `Resources/Data/shop.json` | ✅ see [Monetization](docs/Monetization.md) |
| **In-app purchases:** Google Play (Unity IAP 5) and Steam wallet (MicroTxn), verified by the server and granted once. Interrupted purchases are recovered. Google refunds are taken back (as a gold debt if spent) | `Scripts/Payments`, `server/src/payments`, `server/src/liveops.ts` | ✅ store APIs tested with fakes |
| **Live ops:** mail (welcome, broadcasts, personal, admin API), time-limited events (login, races, score), password reset by email, cloud backups of offline progress | `Scripts/UI/Screens/MailScreen.cs`, `EventsScreen.cs`, `Scripts/Backend/CloudSave.cs`, `server/` | ✅ 35 server tests |
| Sign-in: guest, username/email, Google Play Games v2, Steam (Steamworks ticket on PC, browser OpenID + PKCE on Android) | `Scripts/Auth`, `server/` | ✅ server flows tested end-to-end |
| Offline backend (everything works without a server) and online backend (authoritative). Online play continues without a connection: races queue and sync later | `Scripts/Backend` | ✅ |
| In-app account deletion; server: HTTPS (own TLS or proxy), minimum game version with an "Update required" dialog | `Scripts/UI/Screens/SettingsScreen.cs`, `server/` | ✅ |
| Release builds: version bump + Android signing from environment variables | `tools/build-release.ps1` | ✅ see [Release.md](docs/Release.md) |
| Music import: system picker (NativeFilePicker) or in-game browser; mp3/ogg/wav/aiff; ID3 titles; analysis cache | `Scripts/Songs` | ✅ |
| 15 Navigators with bilingual bios, skills and ~180 voice lines; 3 banners | `Resources/Data` | ✅ placeholder art |
| Editor tooling: first-open setup, Android player settings, URP asset, plugin auto-defines, manifest patcher, build menu/CLI | `Assets/VietMythicalSpeed/Editor` | ✅ |
| Docs | `docs/` | [GDD](docs/GDD.md) · [Art direction & briefs](docs/ArtDirection.md) · [Backend & sign-in](docs/Backend.md) · [Monetization](docs/Monetization.md) · [World & lore](docs/World.md) · [Localization](docs/Localization.md) · [Release builds](docs/Release.md) · [Performance](docs/Performance.md) · [Testing](docs/Testing.md) |

## Quick start

1. **Unity Hub** → Add project from disk → `UnityClient`. Open it with **6000.3.26f1**, with Android Build Support installed.
   - On first open, the `VietMythicalSpeed` editor tooling creates `Scenes/Main.unity`, applies the Android settings and assigns the URP asset. Rerun it any time via **VietMythicalSpeed → Setup → Run Full Project Setup**.
   - If Unity asks to enable the new Input System backends, either answer works: the game supports both input systems.
2. Press **Play**. The first launch synthesises the demo song "Lạc Hồng Khúc", then shows the login screen. **Play now (Guest)** works offline.
3. **Build:** **VietMythicalSpeed → Build → Android APK (development)**, or from a terminal:
   ```bash
   Unity.exe -batchmode -quit -projectPath UnityClient -executeMethod VMS.EditorTools.VmsBuild.BuildAndroidCli -vmsOut Builds/Android/vms.apk
   ```
   For store releases use `tools\build-release.ps1`: it raises the version, signs with your upload key from environment variables and writes the `.aab` / `.apk` / Windows `.zip` to `Builds\Release` ([Release.md](docs/Release.md)).

### Optional plugins (detected automatically)

| Plugin | Enables | Install |
|---|---|---|
| Google Play Games plugin for Unity v11+ | Play Games sign-in | `.unitypackage` from github.com/playgameservices/play-games-plugin-for-unity, then Window → Google Play Games → Setup |
| NativeFilePicker (yasirkula) | System file picker on Android/iOS (no storage permission) | Package Manager → Add from git URL `https://github.com/yasirkula/UnityNativeFilePicker.git` |
| Steamworks.NET | Steam sign-in and Steam wallet purchases in PC builds | Add from git URL `https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net` |

### Server

```bash
cd server
cp .env.example .env    # set JWT_SECRET, plus Google/Steam keys when you have them
npm start               # http://localhost:8787
npm test
```

Switch the game to online in `Resources/Config/backend.json` (`"mode": "online"`), or in **Settings → Server** in development builds. Release builds need an `https://` server. See [docs/Backend.md](docs/Backend.md) for HTTPS, the minimum game version, offline play, Play Games and Steam setup, purchases, email, the admin API and the production checklist.

## Try it on a phone

Enable USB debugging, then:

```bash
adb install -r Builds/VietMythicalSpeed-dev.apk
```

`adb` is in Unity's Android SDK: `C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools`. You can also copy the APK to the phone and open it there.

## Notes

- **Keep the Unity project out of OneDrive sync.** `UnityClient/Library` grows to several GB and OneDrive file locking breaks imports. Move the repository (e.g. `D:\Projects\VietMythicalSpeed`) or exclude the folder. Also keep the path short: Windows long paths are disabled on this PC, and Unity's package cache needs ~150 characters below the project folder.
- **Art is placeholder.** Navigator portraits and standing art are generated from each character's colours and emblem. Drop real PNGs into `Resources/Navigators/{id}/` and they're used automatically; see the [art briefs](docs/ArtDirection.md).
- **Before taking real money:**
  - Create the products in the Play Console and on Steam, and configure the server ([Backend.md](docs/Backend.md#shop-currency-and-purchases)).
  - Read the compliance notes in [Monetization.md](docs/Monetization.md#compliance): loot-box odds, Belgium, and Vietnam's Decree 147.
  - Unity IAP (`com.unity.purchasing`) is a package dependency and is used only in Android builds.
- **Offline progress never reaches the online economy.** Cloud save keeps it as a separate backup.
- **Fonts for Chinese, Japanese and Korean** come from the OS. For store builds, bundle Noto Sans SC / JP / KR as `Resources/Fonts/Main_zh`, `Main_ja`, `Main_ko` ([Localization.md](docs/Localization.md#fonts)).
