param(
    [switch]$Replace,
    [switch]$NormalizeExisting,
    [switch]$IncludeMinecraftSources,
    [switch]$IncludeRestrictedNvidiaSdk,
    [switch]$StageImported,
    [string[]]$Only
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$LockPath = Join-Path $Root "third_party/DEPENDENCIES.lock.json"

if (!(Test-Path -LiteralPath $LockPath)) {
    throw "Dependency lock file not found: $LockPath"
}

$Lock = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json

function Test-SourceSelected {
    param([Parameter(Mandatory=$true)][string]$Id)

    if (-not $Only -or $Only.Count -eq 0) {
        return $true
    }

    return @($Only) -contains $Id
}

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

function Write-ForceIncludeManifest {
    param(
        [Parameter(Mandatory=$true)][string]$Target,
        [Parameter(Mandatory=$true)][string]$Destination
    )

    $manifestPath = Join-Path $Target "VENDORED_FORCE_INCLUDE.txt"
    $rootPrefix = [IO.Path]::GetFullPath($Root).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

    $repoRelativeFiles = @(
        Get-ChildItem -LiteralPath $Target -Recurse -File -Force -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -ne "SOURCE.json" -and
                $_.Name -ne "VENDORED_FORCE_INCLUDE.txt"
            } |
            ForEach-Object {
                $full = [IO.Path]::GetFullPath($_.FullName)
                if ($full.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                    $full.Substring($rootPrefix.Length).Replace([IO.Path]::DirectorySeparatorChar, [char]'/')
                }
            } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )

    $ignored = @()
    if ($repoRelativeFiles.Count -gt 0) {
        $ignored = @(
            $repoRelativeFiles |
                git -C $Root check-ignore --no-index --stdin 2>$null
        )

        if ($LASTEXITCODE -notin 0, 1) {
            throw "git check-ignore failed while auditing '$Destination'."
        }
    }

    $destinationPrefix = $Destination.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar).Replace([IO.Path]::DirectorySeparatorChar, [char]'/') + "/"
    $relativeIgnored = @(
        $ignored |
            ForEach-Object { ([string]$_).Replace([IO.Path]::DirectorySeparatorChar, [char]'/') } |
            Where-Object { $_.StartsWith($destinationPrefix, [StringComparison]::OrdinalIgnoreCase) } |
            ForEach-Object { $_.Substring($destinationPrefix.Length) } |
            Sort-Object -Unique
    )

    $lines = @(
        "# Files tracked by the upstream source but ignored by vendored .gitignore rules."
        "# These paths must be staged with git add -f so the monorepo snapshot stays complete."
    ) + $relativeIgnored

    Set-Content -LiteralPath $manifestPath -Value $lines -Encoding UTF8

    if ($relativeIgnored.Count -gt 0) {
        Write-Host "FORCE-INCLUDE manifest: $($relativeIgnored.Count) ignored tracked/materialized file(s) in $Destination"
    }
    else {
        Write-Host "FORCE-INCLUDE manifest: none for $Destination"
    }
}

function Stage-PublicImport {
    param(
        [Parameter(Mandatory=$true)][string]$Destination,
        [Parameter(Mandatory=$true)][string]$Group
    )

    if (-not $StageImported -or $Group -eq "local-only") {
        return
    }

    git -C $Root add -f -- $Destination
    if ($LASTEXITCODE -ne 0) {
        throw "git add -f failed for '$Destination'."
    }

    $indexedIgnored = @(
        git -C $Root ls-files --cached --ignored --exclude-standard -- $Destination
    )
    if ($LASTEXITCODE -ne 0) {
        throw "git ls-files failed while auditing forced files in '$Destination'."
    }

    $destinationPrefix = $Destination.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar).Replace(
            [IO.Path]::DirectorySeparatorChar,
            [char]'/') + "/"

    $relativeIgnored = @(
        $indexedIgnored |
            ForEach-Object {
                ([string]$_).Replace(
                    [IO.Path]::DirectorySeparatorChar,
                    [char]'/')
            } |
            Where-Object {
                $_.StartsWith(
                    $destinationPrefix,
                    [StringComparison]::OrdinalIgnoreCase)
            } |
            ForEach-Object {
                $_.Substring($destinationPrefix.Length)
            } |
            Where-Object {
                $_ -ne "VENDORED_FORCE_INCLUDE.txt"
            } |
            Sort-Object -Unique
    )

    $manifestPath = Join-Path (Join-Path $Root $Destination) "VENDORED_FORCE_INCLUDE.txt"
    $manifestLines = @(
        "# Files tracked in the monorepo with git add -f because vendored .gitignore rules would otherwise hide them."
        "# Their presence is verified by tools/verify-self-contained.ps1."
    ) + $relativeIgnored

    Set-Content -LiteralPath $manifestPath -Value $manifestLines -Encoding UTF8

    git -C $Root add -f -- $manifestPath
    if ($LASTEXITCODE -ne 0) {
        throw "git add -f failed for '$manifestPath'."
    }

    Write-Host "FORCE-INCLUDE indexed manifest: $($relativeIgnored.Count) ignored tracked file(s) in $Destination"
    Write-Host "STAGED $Destination (forced so tracked upstream binaries ignored by nested .gitignore files are preserved)"
}


function Apply-RetentionPolicy {
    param(
        [Parameter(Mandatory=$true)]$Source,
        [Parameter(Mandatory=$true)][string]$Target
    )

    if (-not $Source.retention) {
        return
    }

    $removeGlobs = @($Source.retention.remove_globs)
    $keepGlobs = @($Source.retention.keep_globs)
    if ($removeGlobs.Count -eq 0) {
        return
    }

    $prefix = [IO.Path]::GetFullPath($Target).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

    Get-ChildItem -LiteralPath $Target -Recurse -File -Force -ErrorAction SilentlyContinue |
        ForEach-Object {
            $full = [IO.Path]::GetFullPath($_.FullName)
            if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
                return
            }

            $relative = $full.Substring($prefix.Length).Replace('\', '/')

            $shouldRemove = $false
            foreach ($glob in $removeGlobs) {
                if ($relative -like [string]$glob) {
                    $shouldRemove = $true
                    break
                }
            }

            if (-not $shouldRemove) {
                return
            }

            foreach ($glob in $keepGlobs) {
                if ($relative -like [string]$glob) {
                    return
                }
            }

            Write-Host "PRUNE $relative"
            Remove-Item -LiteralPath $_.FullName -Force
        }
}

function Test-ExistingImport {
    param(
        [Parameter(Mandatory=$true)][string]$Target,
        [Parameter(Mandatory=$true)][string]$Url,
        [Parameter(Mandatory=$true)][string]$Ref,
        [string]$ExpectedTree
    )

    $sourcePath = Join-Path $Target "SOURCE.json"
    if (!(Test-Path -LiteralPath $sourcePath)) {
        return $false
    }

    try {
        $source = Get-Content -LiteralPath $sourcePath -Raw | ConvertFrom-Json
        if ($source.url -ne $Url -or $source.ref -ne $Ref) {
            return $false
        }

        if (-not [string]::IsNullOrWhiteSpace($ExpectedTree)) {
            return $source.tree -eq $ExpectedTree
        }

        return $true
    }
    catch {
        return $false
    }
}


function Normalize-ExistingImport {
    param(
        [Parameter(Mandatory=$true)]$Source,
        [Parameter(Mandatory=$true)][string]$Group
    )

    $id = [string]$Source.id
    $url = [string]$Source.url
    $ref = [string]$Source.ref
    $expectedTree = if ($Source.tree) { [string]$Source.tree } else { $null }
    $destination = [string]$Source.path
    $target = Join-Path $Root $destination

    Assert-ImmutableRef -Id $id -Ref $ref

    if (!(Test-Path -LiteralPath $target)) {
        Write-Warning "SKIP $destination (folder not present)"
        return
    }

    Apply-RetentionPolicy -Source $Source -Target $target
    Remove-GitMetadata -Path $target

    $nestedGit = Get-ChildItem -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq ".git" } |
        Select-Object -First 1
    if ($nestedGit) {
        throw "Nested Git metadata remains after normalization: $($nestedGit.FullName)"
    }

    $lfsPointers = @(Get-LfsPointers -Path $target)
    if ($lfsPointers.Count -gt 0) {
        $sample = ($lfsPointers | Select-Object -First 5) -join ", "
        throw "Unmaterialized Git LFS pointer(s) remain in '$destination'. Example(s): $sample"
    }

    $sourcePath = Join-Path $target "SOURCE.json"
    if (!(Test-Path -LiteralPath $sourcePath)) {
        throw "Cannot normalize '$destination' without existing SOURCE.json provenance. Re-import with -Replace."
    }

    try {
        $existing = Get-Content -LiteralPath $sourcePath -Raw | ConvertFrom-Json
    }
    catch {
        throw "Cannot normalize '$destination' because SOURCE.json is invalid. Re-import with -Replace."
    }

    if ($existing.url -ne $url -or $existing.ref -ne $ref) {
        throw "Cannot normalize '$destination': existing provenance does not match the lock file. Re-import with -Replace."
    }

    if (-not [string]::IsNullOrWhiteSpace($expectedTree) -and
        $existing.tree -ne $expectedTree) {
        throw "Cannot normalize '$destination': exact source tree provenance is missing or mismatched. Re-import with -Replace."
    }

    $metadata = [ordered]@{
        id = $id
        group = $Group
        url = $url
        ref = $ref
        imported_at_utc = if ($existing.imported_at_utc) { [string]$existing.imported_at_utc } else { [DateTime]::UtcNow.ToString("o") }
    }

    if (-not [string]::IsNullOrWhiteSpace($expectedTree)) {
        $metadata.tree = $expectedTree
    }

    Set-Content -LiteralPath $sourcePath -Value ($metadata | ConvertTo-Json) -Encoding UTF8
    Write-ForceIncludeManifest -Target $target -Destination $destination
    Stage-PublicImport -Destination $destination -Group $Group

    Write-Host "NORMALIZED $destination -> $ref"
}

function Import-Repo {
    param(
        [Parameter(Mandatory=$true)][string]$Id,
        [Parameter(Mandatory=$true)][string]$Url,
        [Parameter(Mandatory=$true)][string]$Ref,
        [Parameter(Mandatory=$true)][string]$Destination,
        [string]$Group = "core",
        [string]$ExpectedTree,
        $Retention = $null
    )

    Assert-ImmutableRef -Id $Id -Ref $Ref

    $target = Join-Path $Root $Destination
    if (Test-Path -LiteralPath $target) {
        if (-not $Replace) {
            if (Test-ExistingImport -Target $target -Url $Url -Ref $Ref -ExpectedTree $ExpectedTree) {
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

        $actualTree = (git -C $temp rev-parse "HEAD^{tree}").Trim()
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to resolve Git tree for '$Id' at $Ref."
        }

        if (-not [string]::IsNullOrWhiteSpace($ExpectedTree) -and
            $actualTree -ne $ExpectedTree) {
            throw "Pinned tree verification failed for '$Id'. Expected $ExpectedTree, got $actualTree."
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

        Apply-RetentionPolicy -Source ([pscustomobject]@{ retention = $Retention }) -Target $target
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
            tree = $actualTree
            imported_at_utc = [DateTime]::UtcNow.ToString("o")
        } | ConvertTo-Json

        Set-Content -LiteralPath (Join-Path $target "SOURCE.json") -Value $source -Encoding UTF8
        Write-ForceIncludeManifest -Target $target -Destination $Destination
        Stage-PublicImport -Destination $Destination -Group $Group
    }
    finally {
        if (Test-Path -LiteralPath $temp) {
            Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}


if ($NormalizeExisting) {
    foreach ($source in @($Lock.sources)) {
        if (-not (Test-SourceSelected -Id ([string]$source.id))) {
            Write-Host "SKIP $($source.path) (not selected by -Only)"
            continue
        }

        $group = if ($source.group) { [string]$source.group } else { "core" }
        Normalize-ExistingImport -Source $source -Group $group
    }

    if ($IncludeRestrictedNvidiaSdk) {
        foreach ($source in @($Lock.local_only)) {
            if (-not (Test-SourceSelected -Id ([string]$source.id))) {
                continue
            }

            Normalize-ExistingImport -Source $source -Group "local-only"
        }
    }

    Write-Host ""
    Write-Host "Existing vendored tree normalized."
    Write-Host "Run tools/verify-self-contained.ps1 again before committing."
    exit 0
}

foreach ($source in @($Lock.sources)) {
    if (-not (Test-SourceSelected -Id ([string]$source.id))) {
        Write-Host "SKIP $($source.path) (not selected by -Only)"
        continue
    }

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
        ExpectedTree = if ($source.tree) { [string]$source.tree } else { $null }
        Retention = $source.retention
    }
    Import-Repo @importArgs
}

if ($IncludeRestrictedNvidiaSdk) {
    Write-Warning "NVIDIA/DLSS is imported only into third_party-local because its SDK license restricts standalone redistribution."

    foreach ($source in @($Lock.local_only)) {
        if (-not (Test-SourceSelected -Id ([string]$source.id))) {
            continue
        }

        $importArgs = @{
            Id = [string]$source.id
            Url = [string]$source.url
            Ref = [string]$source.ref
            Destination = [string]$source.path
            Group = "local-only"
            ExpectedTree = if ($source.tree) { [string]$source.tree } else { $null }
            Retention = $source.retention
        }
        Import-Repo @importArgs
    }
}

Write-Host ""
Write-Host "Vendor import complete."
Write-Host "All imported public sources were resolved from third_party/DEPENDENCIES.lock.json."
Write-Host "Review third_party/README.md and all upstream licenses before redistribution."
