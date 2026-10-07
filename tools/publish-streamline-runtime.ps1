param(
    [string]$Repository = "grg914/dlss-nr-manager",
    [string]$ReleaseTag
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$lockPath = Join-Path $Root "third_party\DEPENDENCIES.lock.json"

if (!(Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) is required to bootstrap the manager-owned Streamline runtime."
}

& gh auth status --hostname github.com 1>$null 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "GitHub CLI is not authenticated. Run gh auth login first."
}

$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
$entry = @($lock.sources) | Where-Object { $_.id -eq "streamline" } | Select-Object -First 1
if (-not $entry) { throw "Streamline lock entry is missing." }

$sourceRef = [string]$entry.ref
$sourceTag = [string]$entry.release_tag
$sourceAsset = [string]$entry.release_asset
$expectedSha256 = ([string]$entry.release_sha256).ToLowerInvariant()

if ($sourceRef -notmatch "^[0-9a-fA-F]{40}$") { throw "Streamline source ref is not immutable." }
if ([string]::IsNullOrWhiteSpace($sourceTag) -or [string]::IsNullOrWhiteSpace($sourceAsset)) {
    throw "Streamline release metadata is missing from the lock file."
}
if ($expectedSha256 -notmatch "^[0-9a-f]{64}$") { throw "Streamline release SHA-256 is invalid." }

if ([string]::IsNullOrWhiteSpace($ReleaseTag)) {
    $ReleaseTag = (& gh release view --repo $Repository --json tagName --jq ".tagName").Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($ReleaseTag)) {
        throw "Unable to resolve the latest DLSS NR Manager release tag."
    }
}

$officialJson = & gh api "repos/NVIDIA-RTX/Streamline/releases/tags/$sourceTag"
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($officialJson)) {
    throw "Unable to resolve official NVIDIA Streamline release $sourceTag."
}
$official = $officialJson | ConvertFrom-Json
$asset = @($official.assets) | Where-Object { $_.name -eq $sourceAsset } | Select-Object -First 1
if (-not $asset) { throw "Official Streamline asset $sourceAsset was not found in $sourceTag." }

if (-not $asset.digest -or -not ([string]$asset.digest).StartsWith("sha256:")) {
    throw "Official Streamline asset has no GitHub SHA-256 digest."
}
$officialDigest = ([string]$asset.digest).Substring(7).ToLowerInvariant()
if ($officialDigest -ne $expectedSha256) {
    throw "Official Streamline digest changed. Lock=$expectedSha256 GitHub=$officialDigest"
}

$work = Join-Path $Root "build-local\streamline-bootstrap"
$download = Join-Path $work $sourceAsset
$extract = Join-Path $work "extract"
$package = Join-Path $work ("streamline-runtime-" + $sourceTag + "-win-x64")
$zip = "$package.zip"

if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work | Out-Null

Write-Host "Downloading official NVIDIA Streamline $sourceTag..."
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $download

$actualSha256 = (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualSha256 -ne $expectedSha256) {
    throw "Downloaded Streamline SHA-256 mismatch. Expected=$expectedSha256 Actual=$actualSha256"
}

Expand-Archive -LiteralPath $download -DestinationPath $extract -Force
New-Item -ItemType Directory -Force -Path $package | Out-Null

$required = @(
    "sl.interposer.dll",
    "sl.common.dll",
    "sl.dlss.dll",
    "nvngx_dlss.dll"
)

$optional = @(
    "sl.dlss_d.dll",
    "nvngx_dlssd.dll",
    "sl.dlss_g.dll",
    "nvngx_dlssg.dll",
    "sl.reflex.dll",
    "NvLowLatencyVk.dll",
    "sl.pcl.dll",
    "sl.nis.dll"
)

function Find-PreferredRuntimeFile {
    param([string]$Name)

    $files = @(Get-ChildItem -LiteralPath $extract -Filter $Name -File -Recurse -ErrorAction SilentlyContinue)
    if ($files.Count -eq 0) { return $null }

    return $files | Sort-Object @{ Expression = {
        $p = $_.FullName.Replace("\", "/").ToLowerInvariant()
        if ($p.Contains("/development/")) { return 3 }
        if ($p.Contains("/debug/")) { return 4 }
        if ($p.Contains("/production/")) { return 0 }
        if ($p.Contains("/bin/x64/")) { return 1 }
        return 2
    } }, FullName | Select-Object -First 1
}

foreach ($name in $required) {
    $found = Find-PreferredRuntimeFile $name
    if (-not $found) { throw "Required Streamline runtime file is missing from official package: $name" }
    Copy-Item -LiteralPath $found.FullName -Destination (Join-Path $package $name) -Force
}

foreach ($name in $optional) {
    $found = Find-PreferredRuntimeFile $name
    if ($found) {
        Copy-Item -LiteralPath $found.FullName -Destination (Join-Path $package $name) -Force
    }
}

$vendoredStreamline = Join-Path $Root "third_party\NVIDIA-Streamline"
foreach ($licenseName in @("license.txt", "3rd-party-licenses.md", "SOURCE.json")) {
    $source = Join-Path $vendoredStreamline $licenseName
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $package $licenseName) -Force
    }
}

$copiedDlls = @(Get-ChildItem -LiteralPath $package -Filter "*.dll" -File)
foreach ($dll in $copiedDlls) {
    $hash = (Get-FileHash -LiteralPath $dll.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $sig = Get-AuthenticodeSignature -LiteralPath $dll.FullName
    $signer = if ($sig.SignerCertificate) { [string]$sig.SignerCertificate.Subject } else { "" }
    Write-Host "$($dll.Name) • SHA-256 $hash • signature $($sig.Status) • signer $signer"

    if ($dll.Name.StartsWith("nvngx_", [StringComparison]::OrdinalIgnoreCase) -or
        $dll.Name.Equals("NvLowLatencyVk.dll", [StringComparison]::OrdinalIgnoreCase)) {
        if ($sig.Status -ne "Valid" -or $signer -notmatch "(?i)NVIDIA") {
            throw "$($dll.Name) is not a valid NVIDIA-signed runtime."
        }
    }
}

$manifest = [ordered]@{
    source_repository = "NVIDIA-RTX/Streamline"
    source_tag = $sourceTag
    source_commit = $sourceRef
    source_asset = $sourceAsset
    source_sha256 = $expectedSha256
    files = @($copiedDlls | Sort-Object Name | ForEach-Object {
        [ordered]@{
            name = $_.Name
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            authenticode = [string](Get-AuthenticodeSignature -LiteralPath $_.FullName).Status
        }
    })
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $package "STREAMLINE_RUNTIME.json") -Encoding UTF8

if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $package "*") -DestinationPath $zip -Force
if (!(Test-Path -LiteralPath $zip) -or (Get-Item -LiteralPath $zip).Length -lt 1MB) {
    throw "Manager-owned Streamline runtime package was not generated correctly."
}

$packageHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Uploading $zip to $Repository release $ReleaseTag..."
& gh release upload $ReleaseTag $zip --repo $Repository --clobber
if ($LASTEXITCODE -ne 0) { throw "Streamline runtime release upload failed." }

$managerJson = & gh api "repos/$Repository/releases/tags/$ReleaseTag"
if ($LASTEXITCODE -ne 0) { throw "Unable to verify manager release after Streamline upload." }
$manager = $managerJson | ConvertFrom-Json
$remote = @($manager.assets) | Where-Object { $_.name -eq ([IO.Path]::GetFileName($zip)) } | Select-Object -First 1
if (-not $remote) { throw "Uploaded manager-owned Streamline runtime asset was not found." }
if ([long]$remote.size -ne (Get-Item -LiteralPath $zip).Length) { throw "Remote Streamline asset size mismatch." }
if ($remote.digest -and ([string]$remote.digest).StartsWith("sha256:")) {
    $remoteHash = ([string]$remote.digest).Substring(7).ToLowerInvariant()
    if ($remoteHash -ne $packageHash) { throw "Remote Streamline asset digest mismatch." }
}

Write-Host "Published manager-owned Streamline runtime bundle: $($remote.name)"
Write-Host "SHA-256: $packageHash"
