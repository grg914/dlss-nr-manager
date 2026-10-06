param(
    [switch]$Replace,
    [switch]$IncludeMinecraftSources,
    [switch]$IncludeRestrictedNvidiaSdk
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$LockPath = Join-Path $Root "third_party/DEPENDENCIES.lock.json"

if (!(Test-Path -LiteralPath $LockPath)) {
    throw "Dependency lock file not found: $LockPath"
}

$Lock = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json

function Assert-ImmutableRef {
    param(
        [Parameter(Mandatory=$true)][string]$Id,
        [Parameter(Mandatory=$true)][string]$Ref
    )

    if ($Ref -notmatch "^[0-9a-fA-F]{40}$") {
        throw "Dependency '$Id' is not pinned to an immutable 40-character commit SHA: '$Ref'"
    }
}

function Remove-GitMetadata {
    param([Parameter(Mandatory=$true)][string]$Path)

    Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq ".git" } |
        Sort-Object { $_.FullName.Length } -Descending |
        ForEach-Object {
            if ($_.PSIsContainer) {
                Remove-Item -LiteralPath $_.FullName -Recurse -Force
            }
            else {
                Remove-Item -LiteralPath $_.FullName -Force
            }
        }
}

function Get-LfsPointers {
    param([Parameter(Mandatory=$true)][string]$Path)

    $pointers = @()
    Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Length -le 2048 } |
        ForEach-Object {
            try {
                $firstLine = Get-Content -LiteralPath $_.FullName -TotalCount 1 -ErrorAction Stop
                if ($firstLine -eq "version https://git-lfs.github.com/spec/v1") {
                    $pointers += $_.FullName
                }
            }
            catch {
                # Binary or unreadable small files are not Git LFS pointer candidates.
            }
        }

    return $pointers
}

function Test-ExistingImport {
    param(
        [Parameter(Mandatory=$true)][string]$Target,
        [Parameter(Mandatory=$true)][string]$Url,
        [Parameter(Mandatory=$true)][string]$Ref
    )

    $sourcePath = Join-Path $Target "SOURCE.json"
    if (!(Test-Path -LiteralPath $sourcePath)) {
        return $false
    }

    try {
        $source = Get-Content -LiteralPath $sourcePath -Raw | ConvertFrom-Json
        return $source.url -eq $Url -and $source.ref -eq $Ref
    }
    catch {
        return $false
    }
}

function Import-Repo {
    param(
        [Parameter(Mandatory=$true)][string]$Id,
        [Parameter(Mandatory=$true)][string]$Url,
        [Parameter(Mandatory=$true)][string]$Ref,
        [Parameter(Mandatory=$true)][string]$Destination,
        [string]$Group = "core"
    )

    Assert-ImmutableRef -Id $Id -Ref $Ref

    $target = Join-Path $Root $Destination
    if (Test-Path -LiteralPath $target) {
        if (-not $Replace) {
            if (Test-ExistingImport -Target $target -Url $Url -Ref $Ref) {
                Write-Host "SKIP $Destination (already matches lock file; use -Replace to refresh)"
                return
            }

            throw "Existing import '$Destination' does not match the lock file. Re-run with -Replace."
        }

        Remove-Item -LiteralPath $target -Recurse -Force
    }

    $temp = Join-Path $env:TEMP ("dlssnr-vendor-" + [Guid]::NewGuid().ToString("N"))
    try {
        Write-Host "IMPORT [$Id] $Url @ $Ref -> $Destination"
        git clone --filter=blob:none --no-checkout $Url $temp
        if ($LASTEXITCODE -ne 0) {
            throw "git clone failed: $Url"
        }

        git -C $temp checkout --detach $Ref
        if ($LASTEXITCODE -ne 0) {
            throw "git checkout failed: $Url @ $Ref"
        }

        $actualRef = (git -C $temp rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0 -or $actualRef -ne $Ref) {
            throw "Pinned ref verification failed for '$Id'. Expected $Ref, got $actualRef."
        }

        git -C $temp submodule update --init --recursive
        if ($LASTEXITCODE -ne 0) {
            throw "git submodule update failed: $Url @ $Ref"
        }

        if (Get-Command git-lfs -ErrorAction SilentlyContinue) {
            git -C $temp lfs pull
            if ($LASTEXITCODE -ne 0) {
                throw "git lfs pull failed: $Url @ $Ref"
            }

            git -C $temp submodule foreach --recursive "git lfs pull"
            if ($LASTEXITCODE -ne 0) {
                throw "git lfs pull failed in a submodule: $Url @ $Ref"
            }
        }

        New-Item -ItemType Directory -Force -Path $target | Out-Null
        robocopy $temp $target /MIR /XD .git /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) {
            throw "robocopy failed with exit code $LASTEXITCODE"
        }

        Remove-GitMetadata -Path $target

        $nestedGit = Get-ChildItem -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -eq ".git" } |
            Select-Object -First 1
        if ($nestedGit) {
            throw "Nested Git metadata remains after import: $($nestedGit.FullName)"
        }

        $lfsPointers = @(Get-LfsPointers -Path $target)
        if ($lfsPointers.Count -gt 0) {
            $sample = ($lfsPointers | Select-Object -First 5) -join ", "
            throw "Unmaterialized Git LFS pointer(s) remain in '$Destination'. Install Git LFS and retry with -Replace. Example(s): $sample"
        }

        $source = [ordered]@{
            id = $Id
            group = $Group
            url = $Url
            ref = $Ref
            imported_at_utc = [DateTime]::UtcNow.ToString("o")
        } | ConvertTo-Json

        Set-Content -LiteralPath (Join-Path $target "SOURCE.json") -Value $source -Encoding UTF8
    }
    finally {
        if (Test-Path -LiteralPath $temp) {
            Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

foreach ($source in @($Lock.sources)) {
    $group = if ($source.group) { [string]$source.group } else { "core" }

    if ($group -eq "minecraft" -and -not $IncludeMinecraftSources) {
        Write-Host "SKIP $($source.path) (Minecraft source mirror not requested)"
        continue
    }

    $importArgs = @{
        Id = [string]$source.id
        Url = [string]$source.url
        Ref = [string]$source.ref
        Destination = [string]$source.path
        Group = $group
    }
    Import-Repo @importArgs
}

if ($IncludeRestrictedNvidiaSdk) {
    Write-Warning "NVIDIA/DLSS is imported only into third_party-local because its SDK license restricts standalone redistribution."

    foreach ($source in @($Lock.local_only)) {
        $importArgs = @{
            Id = [string]$source.id
            Url = [string]$source.url
            Ref = [string]$source.ref
            Destination = [string]$source.path
            Group = "local-only"
        }
        Import-Repo @importArgs
    }
}

Write-Host ""
Write-Host "Vendor import complete."
Write-Host "All imported public sources were resolved from third_party/DEPENDENCIES.lock.json."
Write-Host "Review third_party/README.md and all upstream licenses before redistribution."
