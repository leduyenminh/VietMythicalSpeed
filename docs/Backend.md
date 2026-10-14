# Backend, Sign-in & Deployment

The game runs in two modes, chosen in `UnityClient/Assets/VietMythicalSpeed/Resources/Config/backend.json` (or the developer field in **Settings → Server**):

| Mode | Where data lives | Good for |
|---|---|---|
| `offline` (default) | `persistentDataPath/offline` on the device, same rules (VMS.Core); optional [cloud backups](#cloud-save-offline-mode) | Development, playtests, no server |
| `online` | `server/` – authoritative accounts, gacha and rewards; the device keeps a cache so play continues without a connection (see [Playing without a connection](#playing-without-a-connection)) | Production |

## Running the server

Requirements: **Node.js 24+**. There are no runtime dependencies; storage is the built-in `node:sqlite`.

```bash
cd server
cp .env.example .env      # set JWT_SECRET at least
npm start                 # http://localhost:8787
npm test                  # 35 tests: auth, gacha parity with C#, rewards, deletion, offline sync, version gate, HTTPS,
                          #   shop, purchases (Google/Steam fakes), refunds, events, mail, password reset, cloud save
npm install && npm run typecheck   # optional strict TypeScript check
```

The server reads the game data straight from `UnityClient/Assets/VietMythicalSpeed/Resources/Data`, so a banner or rate change ships to the client and the server together. To point a **development build** at it (release builds only accept `https://`):
- **Android emulator:** `http://10.0.2.2:8787`
- **Phone on the same Wi-Fi:** `http://<PC-IP>:8787`

Enter either in Settings → Server (shown in development builds only), then restart the game.

## HTTPS

Release builds refuse plain-HTTP servers, and with `NODE_ENV=production` the server requires an `https://` `PUBLIC_URL` and rejects API calls that didn't arrive over HTTPS (`REQUIRE_HTTPS`, default on in production; `/health` stays reachable for load balancers). Pick one setup:

**A. A reverse proxy terminates TLS (simplest).** Caddy gets and renews Let's Encrypt certificates by itself:
```
# Caddyfile
api.your-domain.com {
    reverse_proxy 127.0.0.1:8787
}
```
Server `.env`: `PUBLIC_URL=https://api.your-domain.com`, `TRUST_PROXY=1` (trusts `X-Forwarded-For` / `X-Forwarded-Proto`; only set it when the proxy is the only way in). nginx or a cloud load balancer works the same way.

**B. The server terminates TLS itself.** No proxy needed:
```
PORT=443
TLS_CERT_FILE=/etc/letsencrypt/live/api.your-domain.com/fullchain.pem
TLS_KEY_FILE=/etc/letsencrypt/live/api.your-domain.com/privkey.pem
HTTP_REDIRECT_PORT=80              # redirects http:// to PUBLIC_URL
ACME_WEBROOT=/var/www/acme         # serves certbot's http-01 challenges on port 80
```
First certificate: start the server once without the two `TLS_*` lines (port 80 then answers ACME challenges), run `certbot certonly --webroot -w /var/www/acme -d api.your-domain.com`, add the `TLS_*` lines and restart. certbot remembers the webroot, so automatic renewals keep working while the server runs. Renewals replace the files; the server reloads them within a minute (or on `SIGHUP`) without dropping connections. Responses over HTTPS carry `Strict-Transport-Security`.

### Production checklist
- HTTPS as above, `PUBLIC_URL=https://…`, `NODE_ENV=production`.
- A long random `JWT_SECRET` (with `NODE_ENV=production`, startup fails without one).
- `MIN_CLIENT_VERSION` and `UPDATE_URL_*` once you have released builds (see below).
- Back up `DATABASE_PATH` (SQLite WAL). For many players, move to Postgres. The `Store` class is the only DB-specific code.
- The limits are per-instance: 20 auth requests/min/IP and 240 game requests/min/IP. Use a shared store if you scale out.
- Optionally store client tokens in Android Keystore-backed storage instead of PlayerPrefs (`SessionStore`).
- Purchases: `GOOGLE_SERVICE_ACCOUNT_FILE` (and Steam keys) set, `PAYMENTS_FAKE` unset; check the startup line `[vms] purchases: …`.
- `EMAIL_PROVIDER` + `EMAIL_FROM` + `EMAIL_API_KEY` for password resets, and a long random `ADMIN_TOKEN` if you use the admin API.

## Minimum game version

Every game request carries `X-Client-Version` (PlayerSettings "Version", e.g. `0.2.0`) and `X-Client-Platform` (`android`, `windows`, …). When `MIN_CLIENT_VERSION` is set, older (or unlabelled) clients get HTTP **426** `client_outdated` with `minVersion` and the store page from `UPDATE_URL_ANDROID` / `UPDATE_URL_WINDOWS`. The game then shows a blocking **Update required** dialog: *Update* opens that page (on Android it falls back to the Play Store listing), *Quit* closes the game. Browser routes (Steam OpenID) and `/health` aren't gated.

Typical flow when a release changes rules or data the old builds can't handle:
1. Publish the new build (`tools/build-release.ps1`, see [Release.md](Release.md)) and wait until the store has rolled it out.
2. Raise `MIN_CLIENT_VERSION` to the new version and restart the server.

Races an outdated client played offline stay queued on the device and sync after the player updates.

## Playing without a connection

In online mode the game caches the account and profile per account (`persistentDataPath/online/<server>/account_<id>.json`). When the server can't be reached (no network, timeout, or HTTP 502/503/504 from a proxy):
- **Starting the game** resumes the last session from the cache instead of showing the login screen. The first sign-in on a device still needs a connection.
- **Races** work normally. The result is queued with a random race id and the time it ended; the results screen says rewards come when the game reconnects. Choosing a navigator also works and is replayed later. Once the game knows it is offline, both happen instantly instead of waiting for a request to time out.
- **Draws, awakening, daily reward, linking, account deletion, the shop, purchases, mail and event rewards** need the server and show the connection error. Races played offline still count toward events (by the time they were played) once they sync. The fortune-stick screen still shows the banners (from the game's own data) with an "Offline: drawing needs a connection" note.
- The lobby shows an "Offline · N races waiting" badge. The game retries every 20 seconds (and when it regains focus), then uploads the queue to `POST /v1/race/sync` and shows how many races and rewards were added.

The server stays authoritative: each race id is rewarded once (retries and re-syncs are safe), races are rewarded against the caps of the UTC day they were played (`race_days` ledger, shared with online races), and races older than `OFFLINE_SYNC_MAX_AGE_HOURS` (default 72) or dated before the account existed are refused. The device clock decides the race date, so the window bounds how much a manipulated clock can gain: at most the normal caps of those few days.

## Account deletion

Players delete their account in **Settings → Account → Delete account**: they read what is lost, type a confirmation word (`DELETE` / `XOA`), and password accounts re-enter their password. The game calls `POST /v1/account/delete`; the server checks the password again, then removes the account, its sign-in identities, sessions, profile, race ledger and draw history in one transaction. Access tokens stop working immediately (every authenticated request checks the account still exists). Mail, cloud saves, shop history and pending password resets go too. Real-money purchase records (`purchases`: store, order reference, product, amount) are kept without a link to any remaining account data, because stores, tax and refunds need them. In offline mode the same button deletes the local account, its save and its mailbox. Imported songs stay on the device.

Google Play also asks for a **web link** where people can request deletion without the app; list a support email or form in the Play Console Data safety section if you don't build a web page.

## API (JSON)

All errors use the shape `{"error":{"code","message"}}` (plus `minVersion`/`updateUrl` for `client_outdated`). Error codes map to localised messages in the client (`err.*`). Game requests send `X-Client-Version` and `X-Client-Platform`.

| Method & path | Body | Notes |
|---|---|---|
| POST `/v1/auth/register` | `username, email, password` | Rules shared with `VMS.Core.AccountRules` |
| POST `/v1/auth/login` | `login, password` | `login` is a username or an email |
| POST `/v1/auth/guest` | `deviceId, deviceSecret` | Per-install random secret (hashed server-side) |
| POST `/v1/auth/google` | `serverAuthCode` | Google Play Games v2 |
| POST `/v1/auth/steam/ticket` | `ticket` (hex) | PC builds with Steamworks |
| GET `/v1/auth/steam/openid/start` | `redirect_uri, code_challenge, state` | Browser sign-in (Android) |
| GET `/v1/auth/steam/openid/callback` | (from Steam) | Verifies with Steam, then redirects back with a one-time code |
| POST `/v1/auth/steam/openid/exchange` | `code, codeVerifier` | PKCE-protected |
| POST `/v1/auth/refresh` | `refreshToken` | Rotating; replaying an old token revokes every session |
| POST `/v1/auth/logout` | `refreshToken` | |
| POST `/v1/account/link/google` | `serverAuthCode` | Bearer auth |
| POST `/v1/account/link/steam` | `ticket` or `code, codeVerifier` | Bearer auth |
| POST `/v1/account/delete` | `confirm: true`, `password` (password accounts) | Bearer auth; erases everything, `403 invalid_password` on a wrong password |
| GET `/v1/profile` | — | Bearer auth |
| POST `/v1/profile/navigator` | `navigatorId` | |
| POST `/v1/navigators/awaken` | `navigatorId` | |
| GET `/v1/gacha/banners` | — | Active banners by date |
| POST `/v1/gacha/pull` | `bannerId, count (1\|10)` | Seed stored in `gacha_history` for audits |
| POST `/v1/gacha/spark` | `bannerId, navigatorId` | |
| POST `/v1/race/complete` | `report{…}`, optional `id` | Rewards are capped per race and per day; a repeated `id` returns `duplicate: true` and no reward |
| POST `/v1/race/sync` | `races[{id, finishedAt, report}]` (1–50) | Offline races; per-race `status`: `applied`, `duplicate`, `expired` or an error code, plus `total` and `profile` |
| POST `/v1/daily/claim` | — | |
| GET `/health` | — | Not version-gated or HTTPS-gated; reports `minClientVersion` |
| GET `/v1/shop` | — | Catalog (`shop.json`), which stores are configured, server time |
| POST `/v1/shop/buy` | `itemId` | Pays gold, jade or coins; `insufficient_*`, `already_owned`, `item_unavailable` |
| POST `/v1/cosmetics/equip` | `category, itemId, target?` | `target` = navigator (outfits) or vessel (kits); empty `itemId` resets to basic |
| POST `/v1/shop/exchange` | `amount` | Gold → jade (bonus gold first) |
| POST `/v1/purchases/check` | `productId` | `{allowed}`: one-time packs already bought are refused before the store opens |
| POST `/v1/purchases/google` | `productId, purchaseToken` | Verified with the Play Developer API; one grant per token |
| POST `/v1/purchases/steam/init` | `productId, language?` | `ISteamMicroTxn/InitTxn` in the player's wallet currency → `{orderId}` |
| POST `/v1/purchases/steam/finalize` | `orderId, authorized` | After the overlay; `FinalizeTxn` then grant |
| POST `/v1/purchases/fake` | `productId` | Only with `PAYMENTS_FAKE=1` (never in production) |
| GET `/v1/events` | — | Running events, plus finished ones still in the claim grace period |
| POST `/v1/events/claim` | `eventId, index` | `not_reached`, `already_claimed`, `event_ended` |
| GET `/v1/mail` | — | `{mail[], unread, unclaimed}`; broadcasts and personal mail, newest first |
| POST `/v1/mail/read` · `/v1/mail/claim` · `/v1/mail/delete` | `id` (claim: or `all: true`) | Delete refuses unclaimed gifts (`claim_first`) |
| POST `/v1/auth/password/forgot` | `email, lang` | Always `{ok:true}` (no account enumeration) |
| POST `/v1/auth/password/reset` | `email, code, newPassword` | Signs in; all other sessions are revoked |
| GET · POST `/v1/cloudsave` | `slot, data, baseRevision, summary, device, force?` | Offline-mode backups; `409 save_conflict` when another device saved first |
| POST `/v1/admin/mail` · GET `/v1/admin/account` · POST `/v1/admin/refunds/poll` | see [Admin API](#admin-api) | `X-Admin-Token` header |

Successful sign-ins return `{accessToken, refreshToken, expiresIn, account{id, username, email, displayName, providers[]}, profile}`.

## Google Play Games Services v2

1. Play Console → **Play Games Services → Setup**: create the game project and an **Android credential** (package `com.vietmythical.speed`, SHA-1 of your upload/app-signing key).
2. Create a **Web application** OAuth client in the same Google Cloud project for server-side access. Put its ID and secret in `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET`.
3. In Unity, import the **Google Play Games plugin for Unity v11+** (`.unitypackage` from github.com/playgameservices/play-games-plugin-for-unity). Then open **Window → Google Play Games → Setup → Android setup**, paste the resources XML, and enter the **web client ID**.
4. The editor tooling detects the plugin and defines `VMS_GPGS` (menu **VietMythicalSpeed → Setup → Refresh Plugin Defines**). The client calls `RequestServerSideAccess`, and the server exchanges the code and reads `players/me`.

## Steam

The client supports two ways in:
- **PC (Steam build):** install Steamworks.NET (`https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net` via Package Manager → Add from git URL). `VMS_STEAMWORKS` is defined automatically for Standalone. The client sends `GetAuthTicketForWebApi("vms-backend")`, and the server verifies it with `ISteamUserAuth/AuthenticateUserTicket` using `STEAM_APP_ID` + `STEAM_PUBLISHER_KEY`.
- **Android, or PC without Steam:** "Sign in through Steam" in the browser (OpenID 2.0).
  1. The app opens `/v1/auth/steam/openid/start` with a PKCE challenge.
  2. Steam asserts the identity; the server double-checks it with `check_authentication`.
  3. The server redirects to `vietmythicalspeed://auth/steam?code=…&state=…`. On desktop it uses a loopback `http://127.0.0.1:<port>/steam-callback` instead.
  4. The app exchanges `code + verifier`.

The Android deep-link intent filter is injected at build time by `VmsAndroidManifestPatcher`. This needs the online server and a public `PUBLIC_URL`.

## Username / email accounts
- Passwords: scrypt (N=16384, r=8, p=1) on the server, plus constant-time comparison and a dummy hash for unknown users (no username enumeration via timing).
- Offline mode stores local accounts with PBKDF2-SHA256 (60k iterations). These are local-only and never trusted by the server.
- Forgotten passwords: reset by email, see [Password reset](#password-reset-by-email).

## Shop, currency and purchases

The design (currencies, price table, catalog, monetization ideas, compliance) is in [Monetization.md](Monetization.md). This section covers setup.

The server is authoritative for everything bought with real money. The game asks the store for a purchase, sends the proof to the server, and the server verifies it with the store, grants the gold **once** per store transaction (`purchases` table), and returns the new profile. Only then does the game confirm or consume the purchase.

### Google Play (Android)

1. **Unity:** the project uses **Unity IAP 5** (`com.unity.purchasing`, in `Packages/manifest.json`). `VMS_UNITY_IAP` is defined automatically for Android. No Unity Gaming Services project is needed.
2. **Products:** upload a build with billing to a testing track first (Play requires it). Then go to **Play Console → Monetize with Play → Products → One-time products** and create one product per `shop.json` product: `gold_60` … `gold_6480`, `lantern_pass`, `starter_pack`. Ids must match exactly. Set prices per country there (see Monetization.md for Vietnamese đồng suggestions). All of them are consumables in the game, including the pass, which extends by 30 days per purchase.
3. **Service account** for server verification:
   1. In Google Cloud, in any project, enable the **Google Play Android Developer API**.
   2. Create a service account and download its JSON key.
   3. In **Play Console → Users and permissions**, invite the service account's email with *View financial data* and *Manage orders and subscriptions* for this app. It can take up to a day before the API accepts it.
4. **Server `.env`:**
   - `GOOGLE_PLAY_PACKAGE=com.vietmythical.speed`
   - `GOOGLE_SERVICE_ACCOUNT_FILE=/path/to/key.json` (keep it out of the repo)
5. **Refunds:** every `GOOGLE_VOIDED_POLL_MINUTES` (default 360; `0` = off) the server reads *voided purchases*. It takes refunded gold back (as a debt if it was spent), marks the purchase `revoked`, and mails the player.
6. **Testing:** add tester accounts under **Play Console → Settings → License testing**. They buy with test cards and are never charged.

### Steam (Windows)

Purchases use **ISteamMicroTxn**: the server starts the order, the Steam overlay asks the player, and the server finalizes it. Players need Steam linked to their account (Settings → Account; accounts created through Steam already are). Otherwise the game says so (`steam_not_linked`).
1. Enable in-game purchases for the app in Steamworks. Use the same `STEAM_APP_ID` and `STEAM_PUBLISHER_KEY` (publisher Web API key) as Steam sign-in.
2. `steamItemId` (any number unique to the product) and `steamPrices` (cents per currency) are in `shop.json`. Steam charges the wallet currency; add every currency you want to sell in.
3. While testing, set `STEAM_MICROTXN_SANDBOX=1` (uses `ISteamMicroTxnSandbox`: no money moves).
4. Steam refunds aren't polled yet. Check Steamworks financial reports, and use `GET /v1/admin/account` to find the player.

### Development

With `PAYMENTS_FAKE=1` (refused when `NODE_ENV=production`) the server accepts `POST /v1/purchases/fake`. Development builds then show a dev store that always succeeds, which is useful for screenshot runs and UI work. In offline mode, the editor and development builds use the same dev store locally. Release builds have no dev store.

## Events and mail

- **Events** are in `events.json`, read by the game and the server:
  - `login` counts days with a daily-reward claim; `races` and `score` count rewarded races.
  - Rewards stay claimable for `claimGraceDays` after `endUtc`.
  - Progress of events removed from the file is pruned from profiles.
- **Mail templates** are in `mail.json`. `welcome` is sent to every new account; `purchase_revoked` is sent after a refund.
- **Broadcast or personal mail** goes through the admin API. A broadcast with `"audience": "existing"` only reaches accounts that existed when it was sent; `"all"` also reaches later sign-ups. Mail expires after `expiresDays` (default 30, max 365).
- Offline mode has a local mailbox (`mail_<account>.json`) with the welcome mail, and the same events from the bundled data.

## Admin API

Disabled unless `ADMIN_TOKEN` (24+ characters) is set. Each call needs the header `X-Admin-Token: <token>`. Call it from your machine or a support tool, never from the game.

```bash
# Gift everyone 300 jade and an item (title/body in Vietnamese and/or English)
curl -X POST https://api.your-domain.com/v1/admin/mail -H "X-Admin-Token: $ADMIN_TOKEN" -H "content-type: application/json" \
  -d '{"to":"all","audience":"all","title":{"vi":"Quà bảo trì","en":"Maintenance gift"},"body":{"vi":"Cảm ơn bạn đã chờ!","en":"Thanks for waiting!"},"attachments":[{"type":"jade","amount":300},{"type":"item","itemId":"frame_lotus"}],"expiresDays":14}'

# Support: look up a player (username, email or account id) with profile and purchases
curl "https://api.your-domain.com/v1/admin/account?login=someone@example.com" -H "X-Admin-Token: $ADMIN_TOKEN"

# Check Google Play refunds now
curl -X POST https://api.your-domain.com/v1/admin/refunds/poll -H "X-Admin-Token: $ADMIN_TOKEN"
```

Attachments are `gold` (gifted gold counts as bonus gold), `jade`, `coins`, `tickets` (with `amount`) or `item` (with `itemId`, including hidden items).

## Password reset by email

**Login → Forgot password?** asks for the account email. The server mails a 6-digit code, and the player enters it with a new password.
- The code expires after 15 minutes. It is stored hashed and allows 5 attempts.
- At most 3 emails per account per hour.
- The answer is the same whether or not the email exists.
- A successful reset signs the player in and revokes every other session.

Configure a provider in `.env`:
- `EMAIL_PROVIDER=resend`, `postmark` or `sendgrid`
- `EMAIL_FROM="Viet Mythical Speed <no-reply@your-domain.com>"` (a sender verified with the provider)
- `EMAIL_API_KEY=…`

Without a provider the endpoint answers `503 email_unavailable`. In development the default `console` provider prints codes to the server log; production refuses it.

Offline mode has no email: local accounts can't be recovered, which is what the cloud backup is for.

## Cloud save (offline mode)

Offline progress lives only on the device. To keep it safe or move it to another device:
1. Set `cloudSaveUrl` in `backend.json` to a running server. An empty value hides the feature.
2. In **Settings → Cloud save**, the player signs in to (or creates) an account on that server.
3. They can then use:
   - **Back up now**
   - **Restore from cloud**
   - **Back up automatically after races** (at most every 3 minutes, and when the app goes to the background)

Backups (`/v1/cloudsave`, slot `offline`, up to 48 KB, 3 slots per account) are opaque. They are **never merged into the online profile**, because offline saves can be edited on the device. Each upload carries the revision it was based on. If another device backed up in between, the game asks whether to overwrite or restore instead.
