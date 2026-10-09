param(
    [string]$Repository = "grg914/dlss-nr-manager",
    [string]$ReleaseTag = "runtime-seed-v1"
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$BuildRoot = Join-Path $Root "build-local\temurin25"
Remove-Item -LiteralPath $BuildRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $BuildRoot | Out-Null

$headers = @{ Accept = "application/vnd.github+json"; "User-Agent" = "DlssNrManager-TemurinRefresh/1.0" }
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) { $headers.Authorization = "Bearer $env:GITHUB_TOKEN" }

$release = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/adoptium/temurin25-binaries/releases/latest"
if (-not $release -or $release.draft -or $release.prerelease) {
    throw "Adoptium latest release is missing, a draft, or a prerelease."
}
$upstreamReleaseTag = [string]$release.tag_name
if ($upstreamReleaseTag -notmatch "^jdk-25(?:\.|\+|$)") {
    throw "Adoptium latest stable release '$upstreamReleaseTag' is not a Temurin 25 release."
}

$asset = @($release.assets) | Where-Object { [string]$_.name -match "^OpenJDK25U-jre_x64_windows_hotspot_.*\.zip$" } | Select-Object -First 1
if (-not $asset) { throw "Stable Temurin 25 release has no Windows x64 HotSpot JRE ZIP." }

$digest = [string]$asset.digest
if ([string]::IsNullOrWhiteSpace($digest) -or -not $digest.StartsWith("sha256:", [StringComparison]::OrdinalIgnoreCase)) {
    throw "Temurin release asset has no GitHub SHA-256 digest."
}
$expected = $digest.Substring(7).ToLowerInvariant()
$sourceZip = Join-Path $BuildRoot ([string]$asset.name)
$managerZip = Join-Path $BuildRoot "temurin-25-jre-win-x64.zip"
Invoke-WebRequest -Headers $headers -Uri ([string]$asset.browser_download_url) -OutFile $sourceZip
$actual = (Get-FileHash -LiteralPath $sourceZip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) { throw "Temurin SHA-256 mismatch. Expected $expected, got $actual." }
Copy-Item -LiteralPath $sourceZip -Destination $managerZip -Force

$extract = Join-Path $BuildRoot "verify"
Expand-Archive -LiteralPath $managerZip -DestinationPath $extract -Force
$java = Get-ChildItem -LiteralPath $extract -Filter "java.exe" -File -Recurse | Where-Object { $_.FullName -match "[\\/]bin[\\/]java\.exe$" } | Select-Object -First 1
if (-not $java) { throw "Temurin archive contains no bin\java.exe." }
$javaStdout = Join-Path $BuildRoot "java-version.stdout.txt"
$javaStderr = Join-Path $BuildRoot "java-version.stderr.txt"
$javaProcess = Start-Process -FilePath $java.FullName -ArgumentList "-version" -NoNewWindow -Wait -PassThru -RedirectStandardOutput $javaStdout -RedirectStandardError $javaStderr
$versionOutput = @(
    if (Test-Path -LiteralPath $javaStdout) { Get-Content -LiteralPath $javaStdout -Raw }
    if (Test-Path -LiteralPath $javaStderr) { Get-Content -LiteralPath $javaStderr -Raw }
) -join [Environment]::NewLine
if ($javaProcess.ExitCode -ne 0 -or $versionOutput -notmatch '(?i)version\s+"25(?:\.|")') {
    throw "Downloaded Temurin runtime did not verify as Java 25: $versionOutput"
}

$managerHeaders = @{ Accept = "application/vnd.github+json"; "User-Agent" = "DlssNrManager-TemurinRefresh/1.0" }
if (-not [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) { $managerHeaders.Authorization = "Bearer $env:GH_TOKEN" }
elseif (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) { $managerHeaders.Authorization = "Bearer $env:GITHUB_TOKEN" }

$changed = $true
try {
    $seed = Invoke-RestMethod -Headers $managerHeaders -Uri "https://api.github.com/repos/$Repository/releases/tags/$ReleaseTag"
    $existing = @($seed.assets) | Where-Object { [string]$_.name -eq "temurin-25-jre-win-x64.zip" } | Select-Object -First 1
    if ($existing -and ([string]$existing.digest).StartsWith("sha256:", [StringComparison]::OrdinalIgnoreCase)) {
        $changed = ([string]$existing.digest).Substring(7).ToLowerInvariant() -ne $actual
    }
} catch {
    Write-Host "Runtime seed metadata not yet available; Temurin asset will be published."
}

# If a fixed-name JRE already exists, the new digest is staged under
# a content-addressed name. Never point metadata for a new build at the old ZIP.
$managerAssetName = "temurin-25-jre-win-x64.zip"
if ($existing -and $changed) {
    $managerAssetName = "temurin-25-jre-win-x64.sha256-$actual.zip"
}
$metadata = [ordered]@{
    schema = 1
    version = [string]$release.tag_name
    upstream_repository = "adoptium/temurin25-binaries"
    upstream_release = [string]$release.tag_name
    upstream_asset = [string]$asset.name
    manager_asset = $managerAssetName
    sha256 = $actual
    validated_java_version = ($versionOutput.Trim())
}
$metadataPath = Join-Path $BuildRoot "temurin-25-jre.json"
$metadata | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $metadataPath -Encoding UTF8

if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN) -and [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
    throw "GH_TOKEN or GITHUB_TOKEN is required to publish the manager-owned Temurin runtime."
}
$env:GH_TOKEN = if (-not [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) { $env:GH_TOKEN } else { $env:GITHUB_TOKEN }
# Both files are uploaded non-destructively. Existing canonical assets remain
# usable by builds referencing a previous release manifest.
& (Join-Path $PSScriptRoot "publish-append-only-runtime-seed.ps1") `
    -Path $managerZip -Repository $Repository -ReleaseTag $ReleaseTag -OnChanged StageImmutable
& (Join-Path $PSScriptRoot "publish-append-only-runtime-seed.ps1") `
    -Path $metadataPath -Repository $Repository -ReleaseTag $ReleaseTag -OnChanged StageImmutable

Write-Host "Temurin 25 manager runtime ready: $($release.tag_name) • SHA-256 $actual • changed=$changed"
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    "changed=$($changed.ToString().ToLowerInvariant())" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "version=$([string]$release.tag_name)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "sha256=$actual" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}