param(
    [string]$DestinationDirectory,
    [switch]$SkipDownload
)
$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$pin = Get-Content -LiteralPath (Join-Path $root "manifests/optiscaler-v4-legacy-candidate.json") -Raw | ConvertFrom-Json
function Assert-Valid {
    param([bool]$Condition, [string]$Description)
    if (-not $Condition) { throw $Description }
}
Assert-Valid ($pin.schema -eq 1 -and $pin.usage -ceq "qualification_only_not_automatically_activated") "Invalid manifest schema/policy."
Assert-Valid ($pin.source_release -ceq "v3.2.0" -and $pin.asset_name -ceq "OptiScaler-NR-v0.7.7-pre0-vendored-win-x64.zip") "Unexpected release or asset."
Assert-Valid ($pin.sha256 -cmatch '^[0-9a-f]{64}$' -and [long]$pin.size -gt 1024KB -and [long]$pin.size -lt 1GB) "Invalid SHA-256/size receipt."
$expectedUrl = "https://github.com/grg914/dlss-nr-manager/releases/download/$($pin.source_release)/$($pin.asset_name)"
Assert-Valid ($pin.url -ceq $expectedUrl) "Asset URL is not canonical."
$headers = @{ "Accept" = "application/vnd.github+json"; "User-Agent" = "dlss-nr-manager-v4-legacy-audit" }
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/grg914/dlss-nr-manager/releases/tags/v3.2.0" -Headers $headers -TimeoutSec 45
Assert-Valid ($release.tag_name -ceq $pin.source_release -and -not $release.draft -and -not $release.prerelease -and [long]$release.id -eq [long]$pin.source_release_id) "GitHub release does not match immutable source pin."
$assets = @($release.assets | Where-Object { [long]$_.id -eq [long]$pin.asset_id -and $_.name -ceq $pin.asset_name })
Assert-Valid ($assets.Count -eq 1) "Original release does not have exactly one matching pinned asset."
$asset = $assets[0]
Assert-Valid ($asset.browser_download_url -ceq $pin.url -and [long]$asset.size -eq [long]$pin.size -and $asset.digest -ceq ("sha256:" + $pin.sha256)) "Original release metadata URL/size/SHA-256 changed."
if ([string]::IsNullOrWhiteSpace($DestinationDirectory)) { $DestinationDirectory = Join-Path $root "build-local/optiscaler-legacy-v3.2.0" }
if (-not [IO.Path]::IsPathRooted($DestinationDirectory)) { $DestinationDirectory = Join-Path $root $DestinationDirectory }
$DestinationDirectory = [IO.Path]::GetFullPath($DestinationDirectory)
New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null
$archivePath = Join-Path $DestinationDirectory $pin.asset_name
if (-not $SkipDownload) {
    $tempPath = Join-Path $DestinationDirectory ($pin.asset_name + ".partial")
    try {
        if (Test-Path -LiteralPath $tempPath) { Remove-Item -LiteralPath $tempPath -Force }
        & curl.exe --fail --location --show-error --silent --retry 3 --connect-timeout 30 --max-time 900 --output $tempPath --url $pin.url
        if ($LASTEXITCODE -ne 0) { throw "Original GitHub release download failed (curl exit $LASTEXITCODE)." }
        Assert-Valid ((Get-Item -LiteralPath $tempPath).Length -eq [long]$pin.size) "Downloaded asset size mismatch."
        Assert-Valid ((Get-FileHash -LiteralPath $tempPath -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $pin.sha256) "Downloaded asset SHA-256 mismatch."
        Move-Item -LiteralPath $tempPath -Destination $archivePath -Force
    }
    finally { if (Test-Path -LiteralPath $tempPath) { Remove-Item -LiteralPath $tempPath -Force } }
}
Assert-Valid (Test-Path -LiteralPath $archivePath -PathType Leaf) "Original archive missing."
Assert-Valid ((Get-Item -LiteralPath $archivePath).Length -eq [long]$pin.size) "Original archive size mismatch."
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Assert-Valid ($archiveHash -ceq $pin.sha256) "Original archive SHA-256 mismatch."
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $names = @{}
    foreach ($entry in $zip.Entries) {
        $name = $entry.FullName.Replace([char]92, [char]47)
        Assert-Valid (-not [string]::IsNullOrWhiteSpace($name)) "ZIP entry name missing."
        Assert-Valid (-not ($name.StartsWith("/") -or $name -match '^[A-Za-z]:' -or $name -match '(^|/)\.\.(/|$)')) "ZIP unsafe path: $name"
        Assert-Valid (-not $names.ContainsKey($name)) "ZIP duplicate entry: $name"
        $names[$name] = $entry
    }
    foreach ($dll in @($pin.expected_native_dlls)) {
        $entry = $names[$dll]
        Assert-Valid ($null -ne $entry) "Missing native DLL: $dll"
        $stream = $entry.Open()
        try {
            # ZIP entry decompression streams are forward-only. Read a small
            # bounded header into memory, then seek in that memory buffer.
            # Never extract or execute any DLL from the downloaded archive.
            $sourceReader = [IO.BinaryReader]::new($stream)
            $headerBytes = $sourceReader.ReadBytes([int][Math]::Min([long]$entry.Length, [long]65536))
            Assert-Valid ($headerBytes.Length -ge 64) "Truncated Win64 PE header: $dll"
            $header = [IO.MemoryStream]::new($headerBytes)
            try {
                $reader = [IO.BinaryReader]::new($header)
                Assert-Valid ($reader.ReadUInt16() -eq 0x5A4D) "Not MZ/PE: $dll"
                $header.Position = 0x3c
                $peOffset = $reader.ReadInt32()
                Assert-Valid ($peOffset -ge 64 -and $peOffset -le $headerBytes.Length - 6) "Invalid PE offset: $dll"
                $header.Position = $peOffset
                Assert-Valid ($reader.ReadUInt32() -eq 0x4550) "Invalid PE: $dll"
                Assert-Valid ($reader.ReadUInt16() -eq 0x8664) "Not Win64 PE: $dll"
                Write-Host "Verified Win64 PE native: $dll ($($entry.Length) bytes)"
            }
            finally { $header.Dispose() }
        }
        finally { $stream.Dispose() }
    }
    Write-Host "Verified original ZIP entries: $($names.Count)"
}
finally { $zip.Dispose() }
Write-Host "PASS: original v3.2.0 OptiScaler source release, SHA-256, ZIP and Win64 DLLs."
Write-Host "Original asset: $archivePath"
Write-Host "SHA-256: $archiveHash"
Write-Host "Bytes: $($pin.size)"
Write-Host "QUALIFICATION ONLY: not installed, activated or runtime-tested on RTX."
$global:LASTEXITCODE = 0
