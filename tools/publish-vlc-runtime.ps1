param(
    [string]$Repository = "grg914/dlss-nr-manager",
    [string]$ReleaseTag = "runtime-seed-v1"
)

$ErrorActionPreference = "Stop"

$Version = "3.0.24"
$RuntimeName = "vlc-$Version-win64.zip"
$SourceName = "vlc-$Version.tar.xz"
$ProvenanceName = "vlc-$Version-provenance.json"

$RuntimeUrl = "https://download.videolan.org/vlc/$Version/win64/$RuntimeName"
$RuntimeChecksumUrl = "$RuntimeUrl.sha256"
$SourceUrl = "https://download.videolan.org/vlc/$Version/$SourceName"
$SourceChecksumUrl = "$SourceUrl.sha256"

$SourceCommit =
    "6de05adcbaf2e8b85fe86aad4169393098628119"

$SourceMirrorRepository =
    "grg914/vlc"

if (!(Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) is required."
}

& gh auth status --hostname github.com 1>$null 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "GitHub CLI is not authenticated. Run 'gh auth login' first."
}

$mirroredCommit = (& gh api "repos/$SourceMirrorRepository/commits/$SourceCommit" --jq ".sha").Trim()
if ($LASTEXITCODE -ne 0 -or $mirroredCommit -ne $SourceCommit) {
    throw "Pinned VLC source commit $SourceCommit is not available from $SourceMirrorRepository."
}

$work = Join-Path $env:TEMP ("dlssnr-vlc-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $work | Out-Null

try {
    $runtime = Join-Path $work $RuntimeName
    $runtimeChecksum = Join-Path $work "$RuntimeName.sha256"
    $source = Join-Path $work $SourceName
    $sourceChecksum = Join-Path $work "$SourceName.sha256"
    $provenance = Join-Path $work $ProvenanceName

    Write-Host "Downloading official VLC $Version Windows x64 runtime..."
    Invoke-WebRequest -Uri $RuntimeUrl -OutFile $runtime -UseBasicParsing
    Invoke-WebRequest -Uri $RuntimeChecksumUrl -OutFile $runtimeChecksum -UseBasicParsing

    $runtimeChecksumText = Get-Content -LiteralPath $runtimeChecksum -Raw
    $runtimeMatch = [regex]::Match(
        $runtimeChecksumText,
        "(?im)\b([0-9a-f]{64})\b")

    if (-not $runtimeMatch.Success) {
        throw "Unable to parse VideoLAN runtime SHA-256 metadata."
    }

    $expectedRuntimeSha256 =
        $runtimeMatch.Groups[1].Value.ToLowerInvariant()

    $runtimeHash =
        (Get-FileHash -LiteralPath $runtime -Algorithm SHA256).Hash.ToLowerInvariant()

    if ($runtimeHash -ne $expectedRuntimeSha256) {
        throw "Official VLC runtime SHA-256 mismatch. Expected=$expectedRuntimeSha256 Actual=$runtimeHash"
    }

    Write-Host "Downloading corresponding VLC $Version source archive..."
    Invoke-WebRequest -Uri $SourceUrl -OutFile $source -UseBasicParsing
    Invoke-WebRequest -Uri $SourceChecksumUrl -OutFile $sourceChecksum -UseBasicParsing

    $checksumText = Get-Content -LiteralPath $sourceChecksum -Raw
    $match = [regex]::Match(
        $checksumText,
        "(?im)\b([0-9a-f]{64})\b")

    if (-not $match.Success) {
        throw "Unable to parse VideoLAN source SHA-256 metadata."
    }

    $expectedSourceSha256 = $match.Groups[1].Value.ToLowerInvariant()
    $sourceHash =
        (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()

    if ($sourceHash -ne $expectedSourceSha256) {
        throw "Official VLC source SHA-256 mismatch. Expected=$expectedSourceSha256 Actual=$sourceHash"
    }

    $metadata = [ordered]@{
        component = "vlc"
        version = $Version
        source_tag = $Version
        source_repository = "https://github.com/$SourceMirrorRepository"
        source_commit = $SourceCommit
        runtime_asset = $RuntimeName
        runtime_url = $RuntimeUrl
        runtime_checksum_url = $RuntimeChecksumUrl
        runtime_sha256 = $runtimeHash
        source_asset = $SourceName
        source_url = $SourceUrl
        source_sha256 = $sourceHash
        license = "VLC GPL-2.0-or-later; libVLC LGPL-2.1-or-later"
        integration = "External portable runtime; not linked into DlssNrManager.exe"
    }

    $metadata |
        ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $provenance -Encoding utf8

    Write-Host "Publishing manager-owned VLC runtime + corresponding source to $Repository $ReleaseTag..."

    # VLC inputs already include an upstream version in each asset name.
    # Reject changed bytes under that same name rather than deleting a
    # released archive. A new version uses a new, reviewed filename.
    foreach ($file in @($runtime, $source, $provenance)) {
        & (Join-Path $PSScriptRoot "publish-append-only-runtime-seed.ps1") `
            -Path $file -Repository $Repository -ReleaseTag $ReleaseTag -OnChanged Reject
    }

    Write-Host "Published and verified manager-owned VLC $Version runtime/source/provenance."
    Write-Host "Runtime SHA-256: $runtimeHash"
    Write-Host "Source SHA-256:  $sourceHash"}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
