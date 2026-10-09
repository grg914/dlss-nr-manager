param(
    [string]$Repository = "grg914/dlss-nr-manager",
    [string]$ReleaseTag,
    [string]$NeuralStreamlineRuntimePath,
    [string]$NeuralNgxRuntimePath
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ExpectedSlDlssNrSha256 = "9f6672e5e0170dc118a3188d21bda187e1fc1aa3502895b21ab846d23165c11d"
$ExpectedNvngxDlssNrSha256 = "e16bcf15e16e13f527491cdf7845b2fe6521a738d8f7c9c721866a8496e1fc8e"

if (!(Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) is required to bootstrap the manager-owned Streamline runtime."
}

& gh auth status --hostname github.com 1>$null 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "GitHub CLI is not authenticated. Run gh auth login first."
}

$lockPath = Join-Path $Root "third_party\DEPENDENCIES.lock.json"
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

$work = Join-Path $Root "build-local\streamline-bootstrap"
$download = Join-Path $work $sourceAsset
$extract = Join-Path $work "extract"
$package = Join-Path $work ("streamline-runtime-" + $sourceTag + "-win-x64")
$zip = "$package.zip"
$preserve = Join-Path $work "preserve"

if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work | Out-Null

function Test-ControlledRuntime {
    param([string]$Path, [string]$ExpectedName, [string]$ExpectedSha256)

    if ([string]::IsNullOrWhiteSpace($Path) -or !(Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    if (-not ([IO.Path]::GetFileName($resolved)).Equals($ExpectedName, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Controlled runtime must be named ${ExpectedName}: $resolved"
    }

    $actual = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $ExpectedSha256) {
        throw "$ExpectedName controlled-input SHA-256 mismatch. Expected=$ExpectedSha256 Actual=$actual"
    }

    return $resolved
}

function Try-RecoverManagerRuntime {
    param([string]$FileName, [string]$ExpectedSha256)

    $releaseJson = & gh api "repos/$Repository/releases/tags/$ReleaseTag" 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($releaseJson)) {
        return $null
    }

    $release = $releaseJson | ConvertFrom-Json
    $existing = @($release.assets) |
        Where-Object { $_.name -like "streamline-runtime-v*-win-x64.zip" } |
        Sort-Object name -Descending |
        Select-Object -First 1

    if (-not $existing) { return $null }

    $preserveZip = Join-Path $work ("preserve-" + [string]$existing.name)
    Invoke-WebRequest -Uri ([string]$existing.browser_download_url) -OutFile $preserveZip

    if (Test-Path -LiteralPath $preserve) {
        Remove-Item -LiteralPath $preserve -Recurse -Force
    }

    Expand-Archive -LiteralPath $preserveZip -DestinationPath $preserve -Force
    $found = Get-ChildItem -LiteralPath $preserve -Filter $FileName -File -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1

    if (-not $found) { return $null }

    $actual = (Get-FileHash -LiteralPath $found.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $ExpectedSha256) {
        throw "Preserved manager-owned $FileName SHA-256 mismatch. Expected=$ExpectedSha256 Actual=$actual"
    }

    return $found.FullName
}

$slCandidates = @(
    $NeuralStreamlineRuntimePath,
    $env:DLSS_NR_STREAMLINE_PLUGIN,
    (Join-Path $Root "third_party-local\NVIDIA-DLSS\sl.dlss_nr.dll")
)
$ngxCandidates = @(
    $NeuralNgxRuntimePath,
    $env:DLSS_NR_RUNTIME,
    (Join-Path $Root "third_party-local\NVIDIA-DLSS\nvngx_dlssnr.dll")
)

$controlledSl = $null
foreach ($candidate in $slCandidates) {
    $controlledSl = Test-ControlledRuntime $candidate "sl.dlss_nr.dll" $ExpectedSlDlssNrSha256
    if ($controlledSl) { break }
}

$controlledNgx = $null
foreach ($candidate in $ngxCandidates) {
    $controlledNgx = Test-ControlledRuntime $candidate "nvngx_dlssnr.dll" $ExpectedNvngxDlssNrSha256
    if ($controlledNgx) { break }
}

if (-not $controlledSl) {
    $controlledSl = Try-RecoverManagerRuntime "sl.dlss_nr.dll" $ExpectedSlDlssNrSha256
}
if (-not $controlledNgx) {
    $controlledNgx = Try-RecoverManagerRuntime "nvngx_dlssnr.dll" $ExpectedNvngxDlssNrSha256
}

if (-not $controlledSl -or -not $controlledNgx) {
    $currentAssets = @(gh release view $ReleaseTag --repo $Repository --json assets --jq ".assets[].name" 2>$null)
    $hasExisting = @($currentAssets | Where-Object { $_ -like "streamline-runtime-v*-win-x64.zip" }).Count -gt 0

    if ($hasExisting) {
        Write-Warning "Restricted Neural Rendering pair is unavailable in this runner. Preserving the existing manager-owned Streamline asset unchanged. First NR-pair publication must be run locally with -NeuralStreamlineRuntimePath and -NeuralNgxRuntimePath."
        exit 0
    }

    throw "First manager-owned Streamline Neural Rendering publication requires controlled local inputs sl.dlss_nr.dll and nvngx_dlssnr.dll."
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

Write-Host "Downloading official NVIDIA Streamline $sourceTag..."
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $download

$actualSha256 = (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualSha256 -ne $expectedSha256) {
    throw "Downloaded Streamline SHA-256 mismatch. Expected=$expectedSha256 Actual=$actualSha256"
}

Expand-Archive -LiteralPath $download -DestinationPath $extract -Force
New-Item -ItemType Directory -Force -Path $package | Out-Null

$required = @("sl.interposer.dll", "sl.common.dll", "sl.dlss.dll", "nvngx_dlss.dll")
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

Copy-Item -LiteralPath $controlledSl -Destination (Join-Path $package "sl.dlss_nr.dll") -Force
Copy-Item -LiteralPath $controlledNgx -Destination (Join-Path $package "nvngx_dlssnr.dll") -Force

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

$finalSlHash = (Get-FileHash -LiteralPath (Join-Path $package "sl.dlss_nr.dll") -Algorithm SHA256).Hash.ToLowerInvariant()
$finalNgxHash = (Get-FileHash -LiteralPath (Join-Path $package "nvngx_dlssnr.dll") -Algorithm SHA256).Hash.ToLowerInvariant()
if ($finalSlHash -ne $ExpectedSlDlssNrSha256 -or $finalNgxHash -ne $ExpectedNvngxDlssNrSha256) {
    throw "Restricted Neural Rendering pair changed during package assembly."
}

$manifest = [ordered]@{
    source_repository = "NVIDIA-RTX/Streamline"
    source_tag = $sourceTag
    source_commit = $sourceRef
    source_asset = $sourceAsset
    source_sha256 = $expectedSha256
    restricted_inputs = @(
        [ordered]@{
            name = "sl.dlss_nr.dll"
            sha256 = $ExpectedSlDlssNrSha256
            provenance = "controlled-local-input-or-preserved-manager-owned-asset"
        },
        [ordered]@{
            name = "nvngx_dlssnr.dll"
            sha256 = $ExpectedNvngxDlssNrSha256
            provenance = "controlled-local-input-or-preserved-manager-owned-asset"
        }
    )
    files = @($copiedDlls | Sort-Object Name | ForEach-Object {
        [ordered]@{
            name = $_.Name
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            authenticode = [string](Get-AuthenticodeSignature -LiteralPath $_.FullName).Status
        }
    })
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $package "STREAMLINE_RUNTIME.json") -Encoding UTF8

# Archive metadata must not depend on the current clock or ZIP input mtimes.
# The manager-owned package always contains the same pinned SDK inputs and
# verified Neural Rendering pair until the source lock/reviewed hashes change.
$deterministicZip = Join-Path $PSScriptRoot "create-deterministic-flat-zip.ps1"
$zipTimestampUtc = "2000-01-01T00:00:00Z"
& $deterministicZip -InputDirectory $package -OutputPath $zip `
    -TimestampUtc $zipTimestampUtc -Compression Optimal
if (!(Test-Path -LiteralPath $zip) -or (Get-Item -LiteralPath $zip).Length -lt 1MB) {
    throw "Manager-owned Streamline runtime package was not generated correctly."
}

# Repackage the exact same inputs independently and require byte-for-byte
# reproducibility before considering any publication.
$verifyZip = Join-Path $work "streamline-verify.zip"
try {
    & $deterministicZip -InputDirectory $package -OutputPath $verifyZip `
        -TimestampUtc $zipTimestampUtc -Compression Optimal
    $packageHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $verifyHash = (Get-FileHash -LiteralPath $verifyZip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($packageHash -ne $verifyHash) {
        throw "Streamline runtime ZIP is not reproducible for identical inputs."
    }
}
finally {
    Remove-Item -LiteralPath $verifyZip -Force -ErrorAction SilentlyContinue
}

# The existing Streamline guard verifies the two pinned Neural Rendering DLLs
# and the NVIDIA signature before any manager-owned asset is uploaded.
& (Join-Path $PSScriptRoot "verify-streamline-runtime-asset.ps1") -ZipPath $zip

# Never clobber the canonical asset. If it differs, the shared publisher
# creates a content-addressed staging asset, leaving existing consumers alone.
& (Join-Path $PSScriptRoot "publish-append-only-release-zip.ps1") `
    -Path $zip -Repository $Repository -ReleaseTag $ReleaseTag

Write-Host "Verified deterministic manager-owned Streamline runtime bundle: $([IO.Path]::GetFileName($zip))"
Write-Host "SHA-256: $packageHash"
