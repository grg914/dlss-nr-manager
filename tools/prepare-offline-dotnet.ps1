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

$downloadArgs = @{
    Repository = $Repository
    ReleaseTag = $ReleaseTag
    Assets = @("nuget-offline.zip")
    Destination = $seed
}
& (Join-Path $PSScriptRoot "download-runtime-seed.ps1") @downloadArgs

Expand-Archive -LiteralPath (Join-Path $seed "nuget-offline.zip") -DestinationPath $feed -Force
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