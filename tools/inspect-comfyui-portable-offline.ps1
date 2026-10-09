[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AssetsDirectory,
    [string]$LockPath = ""
)

# GPT C v4.5 — offline inspection ONLY. Never installs/runs ComfyUI or Python.
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($LockPath)) {
    $LockPath = Join-Path $repoRoot "manifests/ai-studio-staging-assets-20261009.json"
}
$assetRoot = (Resolve-Path -LiteralPath $AssetsDirectory).Path
$lock = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json
if ($lock.schema -ne 1 -or $lock.status -ne "STAGING_ONLY_NOT_INSTALLABLE" -or
    $lock.asset_count -ne 13) {
    throw "Unexpected AI Studio staging lock; refuse to inspect."
}

function Get-LockedAsset([string]$name) {
    $records = @($lock.assets | Where-Object { $_.name -ceq $name })
    if ($records.Count -ne 1) { throw "Missing or duplicated locked asset: $name" }
    $file = Join-Path $assetRoot $name
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "File missing: $file"
    }
    $item = Get-Item -LiteralPath $file
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing redirected file: $name"
    }
    if ($item.Length -ne [int64]$records[0].bytes -or
        $records[0].github_asset_sha256 -notmatch '^[a-f0-9]{64}$') {
        throw "Size or trusted SHA-256 metadata mismatch: $name"
    }
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne [string]$records[0].github_asset_sha256) {
        throw "SHA-256 mismatch: $name"
    }
    return $file
}

# Fail closed: 7zr is third-party executable code. Check SHA against the
# independently verified official 7-Zip v26.04 GitHub asset before running it.
$extractor = Get-LockedAsset "7zr.exe"
$archive = Get-LockedAsset "ComfyUI_windows_portable_nvidia.7z"
$outputRoot = Join-Path $repoRoot "build-local/ai-studio-comfyui-inspection"
$work = Join-Path $outputRoot ([Guid]::NewGuid().ToString("N"))
$portable = Join-Path $work "ComfyUI_windows_portable"
New-Item -ItemType Directory -Path $work -Force | Out-Null

try {
    # Verify every member BEFORE extraction. '7zr l -slt' output includes
    # a header; the file list begins after the 7-Zip dashed separator.
    $listing = & $extractor l -slt $archive 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Unable to list pinned ComfyUI archive." }

    $entriesStarted = $false
    $members = 0
    [int64]$expanded = 0
    [int64]$maxExpanded = 24L * 1024 * 1024 * 1024
    $safeRoot = "ComfyUI_windows_portable"
    $hasPython = $false
    $hasMain = $false
    foreach ($line in $listing) {
        if (-not $entriesStarted) {
            if ($line -match '^-{5,}$') { $entriesStarted = $true }
            continue
        }
        if ($line -match '^Path = (.+)$') {
            $member = $Matches[1].Replace('\', '/')
            $members++
            if ($members -gt 120000 -or
                ($member -ne $safeRoot -and -not $member.StartsWith("$safeRoot/", [StringComparison]::Ordinal)) -or
                $member.StartsWith('/') -or $member.Contains(':') -or
                $member.ToCharArray() | Where-Object { [char]::IsControl($_) }) {
                throw "Unexpected archive entry path: $member"
            }
            $segments = $member.Split('/')
            if (@($segments | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' }).Count -gt 0) {
                throw "Unsafe path segment in ComfyUI archive."
            }
            if ($member -ceq "$safeRoot/python_embeded/python.exe") { $hasPython = $true }
            if ($member -ceq "$safeRoot/ComfyUI/main.py") { $hasMain = $true }
        }
        elseif ($line -match '^Size = ([0-9]+)$') {
            $bytes = [int64]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
            if ($bytes -lt 0 -or $expanded -gt $maxExpanded - $bytes) {
                throw "ComfyUI archive exceeds the 24 GiB offline extraction budget."
            }
            $expanded += $bytes
        }
    }
    if (-not $entriesStarted -or $members -lt 20 -or -not $hasPython -or -not $hasMain) {
        throw "Official ComfyUI portable layout is missing required Python/main.py members."
    }

    $rootDrive = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($work))
    $drive = [IO.DriveInfo]::new($rootDrive)
    if (-not $drive.IsReady -or $drive.AvailableFreeSpace -lt $expanded + 1073741824L) {
        throw "Not enough free disk space for verified offline ComfyUI inspection."
    }

    # The archive is NOT trusted to execute any scripts. Extract only to
    # a new private build-local directory, never to the installed runtime.
    & $extractor x -y -bd "-o$work" $archive | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Pinned ComfyUI archive extraction failed." }
    if (-not (Test-Path -LiteralPath (Join-Path $portable "python_embeded/python.exe") -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $portable "ComfyUI/main.py") -PathType Leaf)) {
        throw "Extracted ComfyUI portable layout does not match listing."
    }

    # We do not bundle model weights here. Inventory all runtime files for
    # independent binary and redistribution review before signing any pin.
    $files = @(Get-ChildItem -LiteralPath $portable -File -Recurse -Force)
    $inventory = [System.Collections.Generic.List[object]]::new()
    foreach ($file in $files) {
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Portable runtime contains a redirected file: $($file.FullName)"
        }
        $relative = $file.FullName.Substring($portable.Length + 1).Replace('\', '/')
        $inventory.Add([ordered]@{
            path = $relative
            size = [int64]$file.Length
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        })
    }
    foreach ($dir in @(Get-ChildItem -LiteralPath $portable -Directory -Recurse -Force)) {
        if (($dir.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Portable runtime contains a redirected directory: $($dir.FullName)"
        }
    }

    $report = [ordered]@{
        schema = 1
        status = "CANDIDATE_ONLY_UNREVIEWED_DO_NOT_EXECUTE_OR_DISTRIBUTE"
        source_repository = "Comfy-Org/ComfyUI"
        source_tag = "v0.39.0"
        archive_name = "ComfyUI_windows_portable_nvidia.7z"
        archive_sha256 = ([IO.Path]::GetFileName($archive) | ForEach-Object {
            [string](@($lock.assets | Where-Object name -EQ $_)[0].github_asset_sha256)
        })
        file_count = $inventory.Count
        total_expanded_bytes = $expanded
        files = @($inventory)
    }
    $manifest = Join-Path $work "comfyui-portable-candidate-inventory.json"
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifest -Encoding UTF8
    Write-Host "ComfyUI portable archive verified and inspected offline."
    Write-Host "Audit candidate inventory: $manifest"
    Write-Host "This is NOT a trusted runtime pin, licensed redistributable package, or executable installation."
}
catch {
    # Fail-closed: do not leave an ambiguous partially verified extracted runtime.
    if (Test-Path -LiteralPath $work) {
        Remove-Item -LiteralPath $work -Force -Recurse -ErrorAction SilentlyContinue
    }
    throw
}
