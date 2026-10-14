# Testing

## Unity EditMode tests (game rules)

`UnityClient/Assets/VietMythicalSpeed/Tests/EditMode` tests everything in `VMS.Core`, which is engine-free C#:

- **Gacha:** base rates over 200k draws, hard/soft pity, featured 50/50 guarantee, limited Navigators kept off the standard banner, 10-draw guarantee, payments, duplicates → shards → awakening, spark, and the published rates summing to 100%.
- **Golden vectors:** the C# engine must reproduce `shared/gacha-golden.json`, which the TypeScript server also checks. Any rule drift between offline and online play fails a test.
- **Music analysis:** FFT correctness and kick detection on the synthesised demo song (recall >85%; measured 128/128 at ≈2 ms mean error). Also tempo, curve normalisation, playable beatmaps (minimum gaps, no impossible jumps), dodge charts always leave a reachable lane, and the beat-grid fallback for ambient tracks.
- **Economy and scoring:** daily streaks, race reward caps, bond levels, shields and combo guards, ranks, and the RFC PBKDF2 test vector.
- **Localization and world** (`LocalizationTests`):
  - all eight UI files have the same keys and `{0}` placeholders as English
  - `LocalizedText` falls back to English, then Vietnamese
  - every navigator text, voice line, codex entry and item name exists in all eight languages
  - navigators come from realms on the world map, and every realm has a navigator and a codex
- **Release and sync:** version parsing/ordering (same cases as the server's minimum-version check), release version bumps, and the offline race queue (duplicates, size limit, settling only answered races).
- **Shop and live ops** (`ShopTests`, same cases as the server's `shop.test.ts`):
  - the shipped catalog is consistent
  - bonus gold is spent first, and owned items can't be bought twice
  - equipping needs ownership and the right navigator or vessel
  - gold → jade exchange
  - first-purchase doubling and one-time limits
  - the Lantern Pass (daily jade, stacking cap)
  - event progress and claims
  - reward grants
  - old saves without the new fields load safely

Ways to run:
- **In the editor:** Window → General → Test Runner → EditMode → Run All.
- **Headless:**
  ```bash
  Unity.exe -batchmode -projectPath UnityClient -runTests -testPlatform EditMode -testResults results.xml
  ```

## Server tests

```bash
cd server && npm test && npm install && npm run typecheck
```

`test/account.test.ts` (with `test/helpers.ts`) covers:
- account deletion: confirmation and password checks, every table emptied, live tokens rejected at once, the username free again; guest deletion
- offline race sync: applied / duplicate / expired / invalid per race, each day's caps (yesterday's races don't use today's), re-sending a batch grants nothing twice, idempotent `race/complete` ids
- the minimum game version: 426 `client_outdated` with the store URL per platform, numeric comparison (`1.10.0` > `1.2.0`), browser and health routes not gated

`test/shop.test.ts` runs the shop rules on the shipped `shop.json` / `events.json`, the same cases as the C# `ShopTests`. Refunds leave a gold debt that blocks spending until new gold repays it.

`test/liveops.test.ts` runs the HTTP server with fake Google, Steam and email endpoints. It covers:
- the fake store (only with `PAYMENTS_FAKE=1`, never in production), buying, equipping and exchange
- Google Play: a service-account JWT whose RS256 signature the fake verifies, purchase verification, one grant per token, `purchase_owned_elsewhere`, pending and forged tokens, and the refund poller (debt, mail, only once)
- Steam: `InitTxn` in the wallet currency, finalize after approval, retries
- mail: welcome gifts, broadcasts for everyone or existing players, personal mail, claim once, delete after claiming
- events: check-in with the daily reward, races counting, milestones paid once
- password reset: the code from the mail, 5 attempts, other sessions revoked, the same answer for unknown emails
- cloud saves: revision conflicts between devices, `force`, the 3-slot limit

`liveops.test.ts` also checks languages: admin mail keeps all eight translations (unknown languages are dropped), the reset email follows the player's language, and the shop catalog is translated.

`node tools/i18n/check.js` checks the same things without Unity, plus every literal `Loc.T` key used in code.

`test/https.test.ts` creates a throwaway certificate with `openssl` (skipped if it isn't installed; Git for Windows includes it) and checks: the server speaking HTTPS itself with HSTS, the port-80 redirect, ACME challenge files (including a path-traversal attempt), hot certificate reload, `REQUIRE_HTTPS` behind a proxy, and the production config refusing `http://`.

`test/api.test.ts` runs the real HTTP server against an in-memory database, with Google and Steam replaced by fakes. It covers:
- registration and validation errors
- login by username or email
- refresh-token rotation and replay revocation
- guest device secrets
- Google Play Games and Steam ticket sign-in
- account linking conflicts
- the complete Steam OpenID + PKCE round trip, including a forged assertion and a stolen-code attempt
- server-side gacha payments, bad requests and capped race rewards

## Regenerating the golden vectors

Only do this when you change the gacha rules **on purpose**:

1. Change both `GachaEngine.cs` and `server/src/game/gacha.ts`.
2. In Unity's Test Runner, run the explicit test `GachaTests.RegenerateGoldenVectors` (it is skipped by Run All). It rewrites the file from the C# engine via `GoldenGenerator` in `Tests/EditMode/TestData.cs`.
3. Run `npm test` in `server/`. The TypeScript engine must match the new file exactly.

## Headless builds and screenshot automation

```bash
# setup + tests + Android build without opening the editor
Unity.exe -batchmode -quit -projectPath UnityClient -buildTarget Android -executeMethod VMS.EditorTools.VmsProjectSetup.RunAllBatch
Unity.exe -batchmode -projectPath UnityClient -runTests -testPlatform EditMode -testResults results.xml
Unity.exe -batchmode -projectPath UnityClient -buildTarget Android -executeMethod VMS.EditorTools.VmsBuild.BuildAndroidCli -vmsOut Builds/Android/vms.apk
Unity.exe -batchmode -projectPath UnityClient -buildTarget Win64 -executeMethod VMS.EditorTools.VmsBuild.BuildWindowsCli -vmsOut Builds/Windows/vms.exe

# Development builds only: walk every screen and three themed races, saving screenshots (muted)
Builds/Windows/vms.exe -screen-fullscreen 0 -vmsAutoShots C:/temp/shots -vmsAutoplay
```

The same tour also covers the shop, garage, mail, events, emotions, the profile card and outfits in offline mode. Add `-vmsFresh` to delete the local account first and start as a new guest (welcome mail, first-purchase bonuses, day-1 event).

More development switches:
- `-vmsLang <code>` starts in another language.
- `-vmsGraphics UltraLow|Low|Medium|High` sets the tier.
- `-vmsFlat` turns on the flat UI.

The default tour now also covers the world atlas (two realms and a codex page), the road picker, and races on all five realm roads.

`-vmsAutoplay` steers with the autopilot logic. An autoplayed race should end at 100% Perfect. If it doesn't, chart generation, song timing and judging have drifted apart.

### Online scenarios (development builds)

`-vmsServer <url> -vmsScenario <name>` runs one scenario against a real server. Where the server has to change mid-run, the game writes a request file (`need_offline`, `need_online`) into the screenshot folder and waits for the driver's answer file (`offline_ok`, `online_ok`):

| Scenario | Server | Checks |
|---|---|---|
| `offline-race` | up, then stopped by the driver | guest sign-in, an online race with rewards, then a race with no server: queued, "rewards when you reconnect", lobby badge |
| `resume-sync-delete` | down at launch, then restarted | the game resumes from the cache straight into the lobby, gacha/settings show the offline state, the queued race syncs once the server is back, then the account is deleted and the game returns to the login screen |
| `outdated` | `MIN_CLIENT_VERSION=99.0.0` | signing in ends at the blocking **Update required** dialog |
| `languages` | none (offline) | switches through all eight languages at runtime: lobby, navigator detail, world atlas, shop, events, settings and song select per language (48 screenshots) |
| `liveops` | `PAYMENTS_FAKE=1`, `ADMIN_TOKEN` set, a broadcast mail sent first | the password-reset screen, guest sign-in, gold and Lantern Pass bought through the server's fake store, a full look bought and equipped (frame, nameplate, bubble, exhaust, garage, emote, outfit, kit), shop and garage previews, mail claimed, an event reward claimed |

Release script checks: without the four signing variables it stops before starting Unity; with a version that doesn't go up, Unity refuses and the version stays unchanged; a real run produces a signed `.aab` (`jarsigner -verify`), a Windows `.zip` and `SHA256SUMS.txt`, and leaves no keystore path in `ProjectSettings.asset`.

**Issues these runs caught (now fixed):**
- A missing engine module (`ScreenCapture`).
- URP/`UnityEngine` type ambiguity in the editor tooling.
- A laptop accelerometer (HP DriveGuard) that Windows exposes as a joystick and that "pressed" UI buttons. UI navigation events are now off.
- Layout and readability tweaks: pearl size, selection outlines, placeholder art.

## Manual checklist on a phone

- [ ] First launch composes the demo song and shows a login screen; Guest sign-in reaches the lobby.
- [ ] Import an mp3 from the phone (with the NativeFilePicker plugin: system picker; without it: in-game browser asks for the music permission).
- [ ] Race each mode and difficulty; check Tap, Swipe, Drag and Tilt; pause and resume; leave the app mid-race (auto-pause).
- [ ] Draw ×1 / ×10 and check the rates popup; awaken with duplicates; set a partner and check that her lines show in races.
- [ ] Graphics Low/Medium/High; 60 vs 90/120 fps on a high-refresh phone; a 10-minute session to check thermal behaviour.
- [ ] Online mode against the server: register/login, Google Play Games (needs Play Console setup), Steam browser sign-in.
- [ ] Airplane mode mid-session: race, see the pending note and lobby badge; restart the app offline; turn the network back on and wait for the sync toast.
- [ ] Settings → Delete account with a password account (wrong password, then the right one).
- [ ] Shop with a Play Console license tester: buy `gold_60`, then the starter pack (refused the second time). Kill the app during a purchase and restart: the gold arrives. Refund a test order in the Play Console and wait for the poll (or `POST /v1/admin/refunds/poll`): the gold goes, and a mail explains why.
- [ ] Buy and equip one item of every category; check them in the lobby (frame, nameplate, bubble, emote), garage (interior, kit, exhaust), races (exhaust, bubble) and the profile card.
- [ ] Mail from the admin API arrives, claims once, and can be deleted. An event milestone claims once.
- [ ] Forgot password with a real email provider: the code arrives; a wrong code 5 times locks it.
- [ ] Switch through the eight languages: no overflowing buttons (German, Finnish), CJK text wraps, and the language names render in the picker. On Android, check the CJK fonts (or bundle them).
- [ ] Ultra Low + Flat UI on the oldest phone you have: steady frame rate, readable notes, cooler and longer battery.
- [ ] Offline mode with `cloudSaveUrl`: back up on one device, restore on another, and see the conflict prompt after backing up on both.
