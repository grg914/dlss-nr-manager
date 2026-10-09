param(
    [string]$Repository = "grg914/dlss-nr-manager",
    [string]$ReleaseTag = "runtime-seed-v1",
    [string]$WorkingDirectory = "build-local/offline-dotnet"
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$work = if ([IO.Path]::IsPathRooted($WorkingDirectory)) { $WorkingDirectory } else { Join-Path $Root $WorkingDirectory }
$seed = Join-Path $work "seed"
$feed = Join-Path $work "feed"
$tempBase = if (-not [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$packages = Join-Path $tempBase ("DlssNrManager-offline-packages-" + [Guid]::NewGuid().ToString("N"))
$config = Join-Path $work "NuGet.Config"

Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $seed, $feed, $packages | Out-Null

# The Git-tracked manifest is the atomic promotion pointer. Downloading a
# newly staged release asset is forbidden until its exact digest is reviewed.
$pinPath = Join-Path $Root "manifests/runtime-seed-assets.json"
if (-not (Test-Path -LiteralPath $pinPath)) {
    throw "Missing Git-tracked runtime seed asset pin manifest."
}
$pins = Get-Content -LiteralPath $pinPath -Raw | ConvertFrom-Json
if ([int]$pins.schema -ne 1 -or [string]$pins.release_tag -cne $ReleaseTag) {
    throw "Unsupported runtime seed pin manifest or release tag mismatch."
}
$pinName = [string]$pins.nuget_offline.asset_name
$pinSha = [string]$pins.nuget_offline.sha256
$pinSize = [long]$pins.nuget_offline.size
if ($pinName -cnotmatch '^nuget-offline(?:\.sha256-[0-9a-f]{64})?\.zip$' -or
    $pinSha -cnotmatch '^[0-9a-f]{64}$' -or $pinSize -le 0) {
    throw "Runtime seed NuGet pin is invalid or unsafe."
}
if ($pinName -match '\.sha256-([0-9a-f]{64})\.zip$' -and $Matches[1] -cne $pinSha) {
    throw "Content-addressed NuGet asset name does not match the pinned digest."
}

$downloadArgs = @{
    Repository = $Repository
    ReleaseTag = $ReleaseTag
    Assets = @($pinName)
    Destination = $seed
}
& (Join-Path $PSScriptRoot "download-runtime-seed.ps1") @downloadArgs

$pinnedArchive = Join-Path $seed $pinName
if (-not (Test-Path -LiteralPath $pinnedArchive)) {
    throw "Pinned offline NuGet seed archive is missing."
}
if ([long](Get-Item -LiteralPath $pinnedArchive).Length -ne $pinSize -or
    (Get-FileHash -LiteralPath $pinnedArchive -Algorithm SHA256).Hash.ToLowerInvariant() -cne $pinSha) {
    throw "Pinned offline NuGet seed size/SHA-256 mismatch."
}
Expand-Archive -LiteralPath $pinnedArchive -DestinationPath $feed -Force

$manifestPath = Join-Path $feed "manifest.json"
if (!(Test-Path -LiteralPath $manifestPath)) { throw "Offline NuGet seed has no manifest.json." }

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach ($package in @($manifest.packages)) {
    $path = Join-Path $feed ([string]$package.name)
    if (!(Test-Path -LiteralPath $path)) { throw "Offline NuGet package is missing: $($package.name)" }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne ([string]$package.sha256).ToLowerInvariant()) { throw "Offline NuGet package hash mismatch: $($package.name)" }
}

$feedXml = [Security.SecurityElement]::Escape($feed)
$packagesXml = [Security.SecurityElement]::Escape($packages)
$configText = "<?xml version=`"1.0`" encoding=`"utf-8`"?>`n<configuration>`n  <packageSources>`n    <clear />`n    <add key=`"manager-owned-offline`" value=`"$feedXml`" />`n  </packageSources>`n  <config>`n    <add key=`"globalPackagesFolder`" value=`"$packagesXml`" />`n  </config>`n</configuration>`n"
[IO.File]::WriteAllText($config, $configText, [Text.UTF8Encoding]::new($false))

Write-Host "Offline NuGet feed ready: $feed"
Write-Host "NuGet config: $config"
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    "config=$config" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "packages=$packages" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "feed=$feed" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}