<#
.SYNOPSIS
  Builds store releases of Viet Mythical Speed: raises the version, signs Android with your upload key,
  and leaves ready-to-upload files (plus SHA-256 checksums) in Builds\Release.

.DESCRIPTION
  Runs Unity in batch mode (VMS.EditorTools.VmsRelease.BuildCli). Close the project in the Unity editor first.

  Android signing comes from environment variables, so passwords never appear on a command line,
  in the project files or in git:
    VMS_KEYSTORE_PATH   upload keystore file (create one with tools\new-upload-key.ps1)
    VMS_KEYSTORE_PASS   keystore password
    VMS_KEY_ALIAS       key alias inside the keystore
    VMS_KEY_PASS        key password

  The new version is written to UnityClient\ProjectSettings\ProjectSettings.asset; commit it with the release.
  If a build fails, the version is left unchanged.

.EXAMPLE
  .\tools\build-release.ps1
  Google Play bundle (.aab), patch bump: 0.1.0 -> 0.1.1, version code +1.

.EXAMPLE
  .\tools\build-release.ps1 -Target All -Bump minor
  Google Play bundle and a zipped Windows build, 0.1.1 -> 0.2.0.

.EXAMPLE
  .\tools\build-release.ps1 -Target Windows -Bump none
  Rebuild Windows at the current version (e.g. for GitHub Releases after an Android-only release).
#>
[CmdletBinding()]
param(
  [ValidateSet('Android', 'AndroidApk', 'Windows', 'All')] [string] $Target = 'Android',
  [ValidateSet('patch', 'minor', 'major', 'none')] [string] $Bump = 'patch',
  # Exact version instead of a bump, e.g. 1.0.0. Must be newer than the current one.
  [string] $Version = '',
  [string] $Project = '',
  [string] $OutDir = '',
  # Unity.exe; defaults to $env:UNITY_EXE, then the Hub install matching ProjectVersion.txt.
  [string] $Unity = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Project) { $Project = Join-Path $repo 'UnityClient' }
$Project = (Resolve-Path $Project).Path
if (-not $OutDir) { $OutDir = Join-Path $repo 'Builds\Release' }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

if (-not $Unity) { $Unity = $env:UNITY_EXE }
if (-not $Unity) {
  $line = Get-Content (Join-Path $Project 'ProjectSettings\ProjectVersion.txt') | Where-Object { $_ -like 'm_EditorVersion:*' } | Select-Object -First 1
  $editorVersion = ($line -split ':', 2)[1].Trim()
  $Unity = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$editorVersion\Editor\Unity.exe"
}
if (-not (Test-Path $Unity)) { throw "Unity not found at '$Unity'. Install that version with Unity Hub, or pass -Unity / set UNITY_EXE." }

# Batch mode needs the project to itself; the editor holds Temp\UnityLockfile open while it runs.
$lock = Join-Path $Project 'Temp\UnityLockfile'
if (Test-Path $lock) {
  try { [IO.File]::Open($lock, 'Open', 'ReadWrite', 'None').Close() }
  catch { throw 'The project is open in the Unity editor. Close it, then run this again.' }
}

$targets = switch ($Target) {
  'Android' { 'android' }
  'AndroidApk' { 'android-apk' }
  'Windows' { 'windows' }
  'All' { 'android,windows' }
}

if ($targets -like '*android*') {
  $missing = @('VMS_KEYSTORE_PATH', 'VMS_KEYSTORE_PASS', 'VMS_KEY_ALIAS', 'VMS_KEY_PASS') |
    Where-Object { -not [Environment]::GetEnvironmentVariable($_) }
  if ($missing) { throw "Set the Android upload key environment variables first: $($missing -join ', '). See docs\Release.md." }
  if (-not (Test-Path $env:VMS_KEYSTORE_PATH)) { throw "VMS_KEYSTORE_PATH points to a missing file: $env:VMS_KEYSTORE_PATH" }
  # Unity resolves relative paths against the project folder; hand it an absolute one.
  $env:VMS_KEYSTORE_PATH = (Resolve-Path $env:VMS_KEYSTORE_PATH).Path
}

function Quote([string] $s) {
  if ($s -match '[\s"]') { return '"' + ($s -replace '"', '\"') + '"' }
  return $s
}

$buildTarget = 'Win64'
if ($targets -like 'android*') { $buildTarget = 'Android' }
$log = Join-Path $OutDir ('unity-release-{0:yyyyMMdd-HHmmss}.log' -f (Get-Date))
$unityArgs = @('-batchmode', '-quit', '-projectPath', $Project, '-buildTarget', $buildTarget,
  '-executeMethod', 'VMS.EditorTools.VmsRelease.BuildCli', '-vmsTarget', $targets, '-vmsBump', $Bump,
  '-vmsOutDir', $OutDir, '-logFile', $log)
if ($Version) { $unityArgs += @('-vmsVersion', $Version) }

Write-Host "Building '$targets' (bump: $(if ($Version) { $Version } else { $Bump })) - this takes several minutes..."
$started = Get-Date
$process = Start-Process -FilePath $Unity -ArgumentList (($unityArgs | ForEach-Object { Quote $_ }) -join ' ') -Wait -PassThru -NoNewWindow
$minutes = [math]::Round(((Get-Date) - $started).TotalMinutes, 1)

$vmsLines = @()
if (Test-Path $log) { $vmsLines = @(Select-String -Path $log -Pattern '\[VMS\]' | ForEach-Object { $_.Line.Trim() }) }
$vmsLines | ForEach-Object { Write-Host $_ }

# The log decides: Unity sometimes crashes while shutting down (e.g. when its licensing/cloud services are
# unreachable) after the build already finished and the version was saved.
$builtArtifacts = @($vmsLines | Where-Object { $_ -match '\[VMS\] Release artifact: ' })
$reportedFailure = @($vmsLines | Where-Object { $_ -match '\[VMS\] Release (failed|blocked)' })
$succeeded = ($process.ExitCode -eq 0) -or ($builtArtifacts.Count -gt 0 -and $reportedFailure.Count -eq 0)
if ($succeeded -and $process.ExitCode -ne 0) {
  Write-Host "Note: Unity exited with code $($process.ExitCode) while shutting down, after the build had finished." -ForegroundColor Yellow
}

if (-not $succeeded) {
  Write-Host ''
  Write-Host "Release FAILED after $minutes min (Unity exit code $($process.ExitCode)). Errors from the log:" -ForegroundColor Red
  if (Test-Path $log) {
    Select-String -Path $log -Pattern 'error|Exception|blocked|failed' |
      Where-Object { $_.Line -notmatch 'Curl error|Licensing|LogAssemblyErrors|UnityEngine\.Debug|^\s*at ' } |
      Select-Object -Last 25 | ForEach-Object { Write-Host "  $($_.Line.Trim())" }
  }
  Write-Host "Full log: $log"
  exit 1
}

# Package and checksum what Unity produced.
$artifacts = @()
foreach ($line in $vmsLines) {
  if ($line -match '\[VMS\] Release artifact: (.+)$') {
    $path = $Matches[1].Trim()
    if ($path -like '*.exe') {
      # Zip the whole player folder for Steam/GitHub, minus Unity's "don't ship" debug folders.
      $folder = Split-Path -Parent $path
      $zip = "$folder.zip"
      if (Test-Path $zip) { Remove-Item $zip -Force }
      $items = Get-ChildItem $folder | Where-Object { $_.Name -notmatch 'DoNotShip|DontShip' }
      Compress-Archive -Path ($items | ForEach-Object { $_.FullName }) -DestinationPath $zip
      $artifacts += $zip
    } else {
      $artifacts += $path
    }
  }
}

$sums = Join-Path $OutDir 'SHA256SUMS.txt'
foreach ($a in $artifacts) {
  $hash = (Get-FileHash -Algorithm SHA256 $a).Hash.ToLowerInvariant()
  Add-Content -Path $sums -Value "$hash  $(Split-Path -Leaf $a)" -Encoding ascii
}

Write-Host ''
Write-Host "Release built in $minutes min:" -ForegroundColor Green
foreach ($a in $artifacts) {
  $mb = [math]::Round((Get-Item $a).Length / 1MB, 1)
  Write-Host "  $a ($mb MB)"
}
Write-Host "Checksums: $sums"
Write-Host 'Commit UnityClient\ProjectSettings\ProjectSettings.asset so the next release continues from this version.'
