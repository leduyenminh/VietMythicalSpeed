<#
.SYNOPSIS
  Creates the Android upload keystore used to sign Google Play releases.

.DESCRIPTION
  Runs keytool from Unity's bundled OpenJDK. keytool asks for the passwords itself, so they are never
  stored by this script. Keep the keystore and its passwords safe and backed up OUTSIDE the repository:
  with Play App Signing, Google holds the app signing key and you can reset a lost upload key through
  Play Console support, but it takes time.

.EXAMPLE
  .\tools\new-upload-key.ps1 -Path "$HOME\keys\vms-upload.jks"
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)] [string] $Path,
  [string] $Alias = 'upload',
  [string] $Unity = ''
)

$ErrorActionPreference = 'Stop'
if (Test-Path $Path) { throw "'$Path' already exists; refusing to overwrite a keystore." }
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Unity) { $Unity = $env:UNITY_EXE }
if (-not $Unity) {
  $line = Get-Content (Join-Path $repo 'UnityClient\ProjectSettings\ProjectVersion.txt') | Where-Object { $_ -like 'm_EditorVersion:*' } | Select-Object -First 1
  $Unity = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$(($line -split ':', 2)[1].Trim())\Editor\Unity.exe"
}
$keytool = Join-Path (Split-Path -Parent $Unity) 'Data\PlaybackEngines\AndroidPlayer\OpenJDK\bin\keytool.exe'
if (-not (Test-Path $keytool)) { throw "keytool not found at '$keytool' (install Android Build Support in Unity Hub)." }

New-Item -ItemType Directory -Force (Split-Path -Parent ([IO.Path]::GetFullPath($Path))) | Out-Null
& $keytool -genkeypair -v -keystore $Path -alias $Alias -keyalg RSA -keysize 4096 -validity 10000
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ''
Write-Host 'Upload key created. Before each release, set (for example in this PowerShell window):' -ForegroundColor Green
Write-Host "  `$env:VMS_KEYSTORE_PATH = '$([IO.Path]::GetFullPath($Path))'"
Write-Host "  `$env:VMS_KEY_ALIAS = '$Alias'"
Write-Host '  $env:VMS_KEYSTORE_PASS = Read-Host -Prompt "Keystore password"'
Write-Host '  $env:VMS_KEY_PASS = Read-Host -Prompt "Key password"'
