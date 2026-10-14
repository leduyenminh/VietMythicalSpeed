# Release builds

`tools/build-release.ps1` makes store builds in one step:
- raises the version number;
- signs Android with your upload key, read from environment variables, so passwords never land in files, git or command lines;
- refuses builds a store would reject (no signing key, a plain-`http://` server URL, a version that doesn't go up);
- writes the files and their SHA-256 checksums to `Builds\Release`.

If a build fails, the version is left as it was.

## One-time: create the upload key

```powershell
.\tools\new-upload-key.ps1 -Path "$HOME\keys\vms-upload.jks"
```

keytool (from Unity's bundled JDK) asks for the passwords and your name/organisation. **Back up the `.jks` file and its passwords outside the repository** (a password manager works). With **Play App Signing** (the default for new apps), Google keeps the app signing key. If you lose this upload key, Play Console support can reset it, but that takes days.

## Each release

Close the project in the Unity editor first. Then, in PowerShell:

```powershell
$env:VMS_KEYSTORE_PATH = "$HOME\keys\vms-upload.jks"
$env:VMS_KEY_ALIAS     = "upload"
$env:VMS_KEYSTORE_PASS = Read-Host -Prompt "Keystore password"
$env:VMS_KEY_PASS      = Read-Host -Prompt "Key password"

.\tools\build-release.ps1                          # Google Play bundle, 0.1.0 -> 0.1.1
.\tools\build-release.ps1 -Target All -Bump minor  # bundle + Windows zip, 0.1.1 -> 0.2.0
.\tools\build-release.ps1 -Target AndroidApk       # signed APK (GitHub Releases / sideloading)
.\tools\build-release.ps1 -Target Windows -Bump none   # Windows only, same version
.\tools\build-release.ps1 -Version 1.0.0           # exact version
```

The variables last until you close that PowerShell window. The Windows target doesn't need them.

| Output | Use |
|---|---|
| `VietMythicalSpeed-<version>-<code>.aab` | Google Play (Play Console → a testing track or Production) |
| `VietMythicalSpeed-<version>-<code>.apk` | GitHub Releases, direct installs |
| `VietMythicalSpeed-<version>-windows.zip` | Steam depot (upload the unzipped folder with SteamPipe), GitHub Releases |
| `SHA256SUMS.txt` | Paste next to downloads so players can check them |
| `unity-release-*.log` | Full Unity log of the build |
| `*_BurstDebugInformation_DoNotShip` | Debug symbols for crash reports. Keep them for that version, never upload them (the Windows `.zip` already leaves them out) |

Afterwards, **commit `UnityClient/ProjectSettings/ProjectSettings.asset`**: it holds the new version, so the next release continues from it.

## Version numbers

- **Version** (`PlayerSettings.bundleVersion`, e.g. `0.2.0`): what players see. The server's `MIN_CLIENT_VERSION` compares it. `-Bump patch|minor|major|none` or `-Version x.y.z`.
- **Version code** (`AndroidBundleVersionCode`): an integer Google Play requires to grow with every upload. It goes up by 1 on every release except `-Bump none`.

## Online releases

The release build reads `UnityClient/Assets/VietMythicalSpeed/Resources/Config/backend.json`:
- `"mode": "offline"`: the game plays fully on the device (a warning is printed, the build continues).
- `"mode": "online"`: `baseUrl` must be `https://…`, or the build stops. Release builds can't talk plain HTTP anyway, and the developer **Settings → Server** field is hidden in them.

When a new release changes rules or data that old builds can't handle, publish it, wait for the store rollout, then set `MIN_CLIENT_VERSION` on the server ([Backend.md](Backend.md#minimum-game-version)).

## Where to upload

- **Google Play:** a new personal developer account must run a closed test with at least 12 testers for 14 days before Production unlocks. Upload the `.aab` to the closed testing track first.
- **Steam:** upload the Windows folder as a depot build with SteamPipe (`steamcmd +login … +run_app_build app_build.vdf`).
- **GitHub Releases:** attach the `.apk`, the Windows `.zip` and `SHA256SUMS.txt`. Never commit the keystore: `.gitignore` already excludes `*.jks`, `*.keystore`, `*.apk` and `*.aab`.

## Troubleshooting

- **"The project is open in the Unity editor":** batch builds need the project to themselves.
- **The build was interrupted (PC shut down, Unity killed):** the version may already be raised in `ProjectSettings.asset`. Check it, and rebuild with `-Bump none` if that number was never uploaded. The signing key is only set during the Android build itself, so at most the keystore path can stay behind; `git diff` shows it.
- **"Note: Unity exited with code … while shutting down":** Unity sometimes crashes on exit when it can't reach its licensing/cloud services. The script judges success from the build log, so the files listed are good.
- **"Release blocked: …":** the message says what's missing (signing variables, keystore file, https server URL, version).
- **Path errors in Unity's package cache:** long paths are disabled on this PC. Keep the project in a short path outside OneDrive, e.g. `D:\Projects\VietMythicalSpeed`.
