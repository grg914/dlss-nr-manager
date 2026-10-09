param(
    [Parameter(Mandatory = $true)][string]$Path,
    [string]$ReleaseTag = "runtime-seed-v1",
    [string]$Repository = $env:GITHUB_REPOSITORY
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($Repository)) {
    throw "GitHub repository is required for NuGet seed publication."
}

$source = (Resolve-Path -LiteralPath $Path).Path
$canonicalName = "nuget-offline.zip"
if ([IO.Path]::GetFileName($source) -ne $canonicalName) {
    throw "Expected a nuget-offline.zip archive."
}

# Fail closed before publication if the packaged seed is structurally invalid.
try {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($source)
    try {
        $names = @($archive.Entries | ForEach-Object { $_.FullName })
        if ($names -notcontains "manifest.json" -or
            @($names | Where-Object { $_ -like "*.nupkg" }).Count -eq 0) {
            throw "NuGet seed ZIP must contain manifest.json and at least one .nupkg."
        }
    }
    finally { $archive.Dispose() }
}
catch {
    throw "NuGet seed ZIP validation failed: $($_.Exception.Message)"
}

$localSha = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
$localSize = (Get-Item -LiteralPath $source).Length
$assetApi = "repos/$Repository/releases/tags/$ReleaseTag"

function Read-SeedRelease {
    $json = @(gh api $assetApi 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Unable to inspect manager-owned runtime seed release." }
    $release = ($json -join [Environment]::NewLine) | ConvertFrom-Json
    if ($release.draft) { throw "Refusing to publish into a draft runtime seed." }
    return $release
}

function Get-ExactAsset {
    param([Parameter(Mandatory)]$Release, [Parameter(Mandatory)][string]$Name)
    $matches = @($Release.assets | Where-Object { [string]$_.name -ceq $Name })
    if ($matches.Count -gt 1) { throw "Duplicate GitHub release asset '$Name'." }
    if ($matches.Count -eq 0) { return $null }
    return $matches[0]
}

function Assert-AssetIntegrity {
    param([Parameter(Mandatory)]$Asset)
    $digest = [string]$Asset.digest
    if ($digest -notmatch '^(?i:sha256):[0-9a-fA-F]{64}$') {
        throw "Release asset '$($Asset.name)' is missing a valid GitHub SHA-256 digest."
    }
    if ([long]$Asset.size -le 0) {
        throw "Release asset '$($Asset.name)' has invalid size."
    }
}

$release = Read-SeedRelease
$canonical = Get-ExactAsset -Release $release -Name $canonicalName
$publishName = $canonicalName
if ($null -ne $canonical) {
    Assert-AssetIntegrity -Asset $canonical
    if ([string]$canonical.digest -eq "sha256:$localSha" -and [long]$canonical.size -eq [long]$localSize) {
        Write-Host "SKIP unchanged NuGet seed ($localSha)."
        return
    }

    # The existing name stays available to in-flight builds. Stage revisions
    # under unique immutable names; activation needs a separate reviewed
    # consumer manifest change, not a destructive release-asset replacement.
    $publishName = "nuget-offline.sha256-$localSha.zip"
}

$existing = Get-ExactAsset -Release $release -Name $publishName
if ($null -ne $existing) {
    Assert-AssetIntegrity -Asset $existing
    if ([string]$existing.digest -ne "sha256:$localSha" -or [long]$existing.size -ne [long]$localSize) {
        throw "Immutable release asset '$publishName' has conflicting content."
    }
    Write-Host "SKIP previously staged immutable NuGet seed ($publishName)."
    return
}

$temporaryDir = $null
$uploadPath = $source
try {
    if ($publishName -ne $canonicalName) {
        $tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
        $temporaryDir = Join-Path $tempRoot ("nuget-seed-stage-" + [Guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Path $temporaryDir -Force | Out-Null
        $uploadPath = Join-Path $temporaryDir $publishName
        Copy-Item -LiteralPath $source -Destination $uploadPath
    }

    gh release upload $ReleaseTag $uploadPath --repo $Repository
    if ($LASTEXITCODE -ne 0) { throw "Non-destructive NuGet seed upload failed." }

    $verified = Get-ExactAsset -Release (Read-SeedRelease) -Name $publishName
    if ($null -eq $verified) { throw "New NuGet release asset is absent after upload." }
    Assert-AssetIntegrity -Asset $verified
    if ([string]$verified.digest -ne "sha256:$localSha" -or [long]$verified.size -ne [long]$localSize) {
        throw "New NuGet release asset failed SHA-256/size verification."
    }

    Write-Host "Verified append-only NuGet seed: $publishName ($localSha)."
    if ($publishName -ne $canonicalName) {
        Write-Warning "Canonical nuget-offline.zip remains pinned. Review and promote the immutable revision through a verified consumer manifest."
    }
}
finally {
    if ($null -ne $temporaryDir) {
        Remove-Item -LiteralPath $temporaryDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
