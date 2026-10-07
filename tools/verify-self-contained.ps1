param(
    [switch]$Strict,
    [switch]$AllowLfsPointers
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$LockPath = Join-Path $Root "third_party/DEPENDENCIES.lock.json"

if (!(Test-Path -LiteralPath $LockPath)) {
    throw "Dependency lock file not found: $LockPath"
}

$Lock = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json

function Get-RelativePathCompat {
    param(
        [Parameter(Mandatory=$true)][string]$BasePath,
        [Parameter(Mandatory=$true)][string]$TargetPath
    )

    $baseFull = [IO.Path]::GetFullPath($BasePath).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $targetFull = [IO.Path]::GetFullPath($TargetPath)

    if ($targetFull.StartsWith($baseFull, [StringComparison]::OrdinalIgnoreCase)) {
        return $targetFull.Substring($baseFull.Length)
    }

    return $targetFull
}

$missing = @()
$metadataIssues = @()
$mutableRefs = @()
$nestedGit = @()
$lfsPointers = @()
$retentionIssues = @()
$forceIncludeIssues = @()

function Find-LfsPointers {
    param([Parameter(Mandatory=$true)][string]$Path)

    Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Length -le 2048 } |
        ForEach-Object {
            try {
                $firstLine = Get-Content -LiteralPath $_.FullName -TotalCount 1 -ErrorAction Stop
                if ($firstLine -eq "version https://git-lfs.github.com/spec/v1") {
                    $_.FullName
                }
            }
            catch {
                # Ignore unreadable/binary small files.
            }
        }
}

foreach ($source in @($Lock.sources)) {
    $id = [string]$source.id
    $relative = [string]$source.path
    $url = [string]$source.url
    $ref = [string]$source.ref

    if ($ref -notmatch "^[0-9a-fA-F]{40}$") {
        $mutableRefs += "$id -> $ref"
    }

    $path = Join-Path $Root $relative
    if (!(Test-Path -LiteralPath $path)) {
        $missing += $relative
        continue
    }

    $sourcePath = Join-Path $path "SOURCE.json"
    if (!(Test-Path -LiteralPath $sourcePath)) {
        $metadataIssues += "$relative -> SOURCE.json missing"
    }
    else {
        try {
            $metadata = Get-Content -LiteralPath $sourcePath -Raw | ConvertFrom-Json
            if ($metadata.url -ne $url -or $metadata.ref -ne $ref) {
                $metadataIssues += "$relative -> SOURCE.json does not match lock file"
            }

            if ($source.tree -and $metadata.tree -ne [string]$source.tree) {
                $metadataIssues += "$relative -> SOURCE.json tree does not match lock file"
            }
        }
        catch {
            $metadataIssues += "$relative -> SOURCE.json is invalid JSON"
        }
    }

    Get-ChildItem -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq ".git" } |
        ForEach-Object {
            $nestedGit += Get-RelativePathCompat -BasePath $Root -TargetPath $_.FullName
        }

    Find-LfsPointers -Path $path | ForEach-Object {
        $lfsPointers += Get-RelativePathCompat -BasePath $Root -TargetPath $_
    }

    if ($source.require_force_include_manifest) {
        $manifestPath = Join-Path $path "VENDORED_FORCE_INCLUDE.txt"
        if (!(Test-Path -LiteralPath $manifestPath)) {
            $forceIncludeIssues += "$relative -> VENDORED_FORCE_INCLUDE.txt missing"
        }
        else {
            Get-Content -LiteralPath $manifestPath |
                ForEach-Object {
                    $entry = ([string]$_).Trim()
                    if ([string]::IsNullOrWhiteSpace($entry) -or $entry.StartsWith("#")) {
                        return
                    }

                    $requiredPath = Join-Path $path $entry
                    if (!(Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
                        $forceIncludeIssues += "$relative -> ignored upstream file missing: $entry"
                    }
                }
        }
    }

    if ($source.retention) {
        $removeGlobs = @($source.retention.remove_globs)
        $keepGlobs = @($source.retention.keep_globs)

        Get-ChildItem -LiteralPath $path -Recurse -File -Force -ErrorAction SilentlyContinue |
            ForEach-Object {
                $relativeToSource = (Get-RelativePathCompat -BasePath $path -TargetPath $_.FullName).Replace('\', '/')
                $shouldRemove = $false

                foreach ($glob in $removeGlobs) {
                    if ($relativeToSource -like [string]$glob) {
                        $shouldRemove = $true
                        break
                    }
                }

                if (-not $shouldRemove) {
                    return
                }

                foreach ($glob in $keepGlobs) {
                    if ($relativeToSource -like [string]$glob) {
                        return
                    }
                }

                $retentionIssues += "$relative -> unexpected retained file: $relativeToSource"
            }
    }
}

foreach ($source in @($Lock.local_only)) {
    $id = [string]$source.id
    $ref = [string]$source.ref
    if ($ref -notmatch "^[0-9a-fA-F]{40}$") {
        $mutableRefs += "$id -> $ref"
    }
}

$patterns = @(
    "grg914/Caustica-RTX",
    "NVIDIA-RTX/Streamline",
    "NVIDIA/DLSS",
    "wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass",
    "DaniilSokolyuk/video2dlssnr",
    "BtbN/FFmpeg-Builds",
    "xinntao/Real-ESRGAN-ncnn-vulkan",
    "huggingface.co/onnx-community",
    "raw.githubusercontent.com/Tohrusky",
    "raw.githubusercontent.com/itsspin",
    "ScoopInstaller/Versions",
    "reshade.me",
    "api.modrinth.com",
    "cdn.modrinth.com",
    "meta.fabricmc.net",
    "maven.fabricmc.net",
    "api.nuget.org",
    "softprops/action-gh-release"
)

$refreshWorkflowAllowlist = @(
    "upstream-monitor.yml",
    "runtime-refresh.yml",
    "nuget-seed.yml",
    "minecraft-vendor.yml",
    "media-vendor.yml",
    "native-vendor.yml"
)

$scanFiles = @()
foreach ($scanRoot in @("Services", ".github/workflows")) {
    $base = Join-Path $Root $scanRoot
    if (Test-Path -LiteralPath $base) {
        $scanFiles += Get-ChildItem -LiteralPath $base -Recurse -File |
            Where-Object {
                $_.Extension -in ".cs", ".yml", ".yaml", ".ps1" -and
                -not (
                    $scanRoot -eq ".github/workflows" -and
                    $refreshWorkflowAllowlist -contains $_.Name
                )
            }
    }
}

$scanFiles += Get-ChildItem -LiteralPath $Root -File |
    Where-Object { $_.Extension -in ".cs", ".csproj", ".props", ".targets" }

$references = @()
foreach ($file in $scanFiles | Sort-Object FullName -Unique) {
    $lineNumber = 0
    Get-Content -LiteralPath $file.FullName | ForEach-Object {
        $lineNumber++
        $line = $_

        foreach ($pattern in $patterns) {
            if ($line -like "*$pattern*") {
                $references += [pscustomobject]@{
                    File = Get-RelativePathCompat -BasePath $Root -TargetPath $file.FullName
                    Line = $lineNumber
                    Pattern = $pattern
                    Text = $line.Trim()
                }
            }
        }
    }
}

Write-Host "Self-contained dependency audit"
Write-Host "==============================="
Write-Host ""

if ($mutableRefs.Count -eq 0) {
    Write-Host "Immutable dependency refs: OK"
}
else {
    Write-Host "Mutable dependency refs:"
    $mutableRefs | Sort-Object -Unique | ForEach-Object { Write-Host "  - $_" }
}

Write-Host ""
if ($missing.Count -eq 0) {
    Write-Host "Locked public source mirrors: OK"
}
else {
    Write-Host "Missing locked public source mirrors:"
    $missing | Sort-Object -Unique | ForEach-Object { Write-Host "  - $_" }
}

Write-Host ""
if ($metadataIssues.Count -eq 0) {
    Write-Host "SOURCE.json provenance metadata: OK"
}
else {
    Write-Host "Source provenance issues:"
    $metadataIssues | Sort-Object -Unique | ForEach-Object { Write-Host "  - $_" }
}

Write-Host ""
if ($nestedGit.Count -eq 0) {
    Write-Host "Nested Git metadata: NONE"
}
else {
    Write-Host "Nested Git metadata still present:"
    $nestedGit | Sort-Object -Unique | Select-Object -First 50 | ForEach-Object { Write-Host "  - $_" }
}

Write-Host ""
if ($lfsPointers.Count -eq 0) {
    Write-Host "Unmaterialized Git LFS pointers: NONE"
}
else {
    Write-Host "Unmaterialized Git LFS pointers:"
    $lfsPointers | Sort-Object -Unique | Select-Object -First 50 | ForEach-Object { Write-Host "  - $_" }
}

Write-Host ""
if ($retentionIssues.Count -eq 0) {
    Write-Host "Vendored retention policies: OK"
}
else {
    Write-Host "Vendored retention policy violations:"
    $retentionIssues | Sort-Object -Unique | Select-Object -First 50 | ForEach-Object { Write-Host "  - $_" }
}

Write-Host ""
if ($forceIncludeIssues.Count -eq 0) {
    Write-Host "Vendored ignored tracked files: OK"
}
else {
    Write-Host "Vendored ignored tracked file issues:"
    $forceIncludeIssues | Sort-Object -Unique | Select-Object -First 100 | ForEach-Object { Write-Host "  - $_" }
}

Write-Host ""
if ($references.Count -eq 0) {
    Write-Host "Direct upstream dependency references in runtime/release code: NONE"
}
else {
    Write-Host "Direct upstream dependency references still present:"
    $references | Sort-Object File, Line, Pattern | Format-Table File, Line, Pattern -AutoSize
}

Write-Host ""
$localSdkPath = Join-Path $Root "third_party-local/NVIDIA-DLSS"
if (Test-Path -LiteralPath $localSdkPath) {
    Write-Host "Local NVIDIA DLSS SDK: present (local-only / Git-ignored)"
}
else {
    Write-Host "Local NVIDIA DLSS SDK: absent (required only for local Caustica native rebuilds)"
}

$failed =
    $mutableRefs.Count -gt 0 -or
    $missing.Count -gt 0 -or
    $metadataIssues.Count -gt 0 -or
    $nestedGit.Count -gt 0 -or
    ((-not $AllowLfsPointers) -and $lfsPointers.Count -gt 0) -or
    $retentionIssues.Count -gt 0 -or
    $forceIncludeIssues.Count -gt 0 -or
    $references.Count -gt 0

if ($Strict -and $failed) {
    exit 1
}

exit 0
