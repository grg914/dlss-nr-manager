param(
    [string]$Repository = "grg914/dlss-nr-manager",
    [string]$ReleaseTag,
    [string]$MinecraftVersion = "26.2",
    [switch]$NoUpload
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "minecraft-fabric-profile.ps1")
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$RuntimeLockPath = Join-Path $Root "third_party\minecraft\RUNTIME.lock.json"
$UserAgent = "DlssNrManager-MinecraftBootstrap/1.0 (+https://github.com/grg914/dlss-nr-manager)"

if (!(Test-Path -LiteralPath $RuntimeLockPath)) {
    throw "Minecraft runtime lock file not found: $RuntimeLockPath"
}

$RuntimeLock = Get-Content -LiteralPath $RuntimeLockPath -Raw | ConvertFrom-Json

if ($RuntimeLock.schema_version -ne 1) {
    throw "Unsupported Minecraft runtime lock schema: $($RuntimeLock.schema_version)"
}

if ([string]$RuntimeLock.minecraft_version -ne $MinecraftVersion) {
    throw "Minecraft runtime lock targets $($RuntimeLock.minecraft_version), requested $MinecraftVersion."
}

$RuntimeLockSha256 = (Get-FileHash -LiteralPath $RuntimeLockPath -Algorithm SHA256).Hash.ToLowerInvariant()

if (-not $NoUpload) {
    if (!(Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI (gh) is required to publish the Minecraft runtime bundle."
    }

    & gh auth status --hostname github.com 1>$null 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI is not authenticated. Run gh auth login first."
    }

    if ([string]::IsNullOrWhiteSpace($ReleaseTag)) {
        $ReleaseTag = (& gh release view --repo $Repository --json tagName --jq ".tagName").Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($ReleaseTag)) {
            throw "Unable to resolve the latest release tag for $Repository."
        }
    }
}

$headers = @{ "User-Agent" = $UserAgent; Accept = "application/json" }
$work = Join-Path $Root "build-local\minecraft-runtime-$MinecraftVersion"
$package = Join-Path $work "package"
$filesDir = Join-Path $package "files"
$licensesDir = Join-Path $package "licenses"
$zip = Join-Path $work "minecraft-runtime-$MinecraftVersion.zip"

if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $filesDir, $licensesDir | Out-Null

function Invoke-Json {
    param([string]$Uri)
    return Invoke-RestMethod -Uri $Uri -Headers $headers -Method Get
}

function Copy-WithSha512 {
    param(
        [string]$Url,
        [string]$Destination,
        [string]$ExpectedSha512,
        [string]$Label
    )

    if ($ExpectedSha512 -notmatch "^[0-9a-fA-F]{128}$") {
        throw "$Label has an invalid SHA-512 hash."
    }

    Invoke-WebRequest -Uri $Url -Headers $headers -OutFile $Destination
    $actual = (Get-FileHash -LiteralPath $Destination -Algorithm SHA512).Hash.ToLowerInvariant()
    if ($actual -ne $ExpectedSha512.ToLowerInvariant()) {
        throw "$Label SHA-512 mismatch. Expected=$ExpectedSha512 Actual=$actual"
    }

    return $actual
}

function Copy-WithSha1 {
    param(
        [string]$Url,
        [string]$Destination,
        [string]$ExpectedSha1,
        [string]$Label
    )

    if ($ExpectedSha1 -notmatch "^[0-9a-fA-F]{40}$") {
        throw "$Label has an invalid SHA-1 hash."
    }

    $parent = Split-Path -Parent $Destination
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    Invoke-WebRequest -Uri $Url -Headers $headers -OutFile $Destination
    $actual = (Get-FileHash -LiteralPath $Destination -Algorithm SHA1).Hash.ToLowerInvariant()
    if ($actual -ne $ExpectedSha1.ToLowerInvariant()) {
        throw "$Label SHA-1 mismatch. Expected=$ExpectedSha1 Actual=$actual"
    }

    return $actual
}

function Resolve-ModrinthComponent {
    param(
        [Parameter(Mandatory=$true)]$Component
    )

    $name = [string]$Component.name
    $slug = [string]$Component.slug
    $kind = [string]$Component.kind
    $loader = [string]$Component.loader
    $pinnedVersion = [string]$Component.version
    $pinnedFileName = [string]$Component.file_name
    $pinnedSha512 = ([string]$Component.sha512).ToLowerInvariant()

    if ([string]::IsNullOrWhiteSpace($name) -or
        [string]::IsNullOrWhiteSpace($slug) -or
        [string]::IsNullOrWhiteSpace($pinnedVersion) -or
        [string]::IsNullOrWhiteSpace($pinnedFileName) -or
        $pinnedSha512 -notmatch "^[0-9a-f]{128}$") {
        throw "Minecraft runtime lock contains invalid component metadata for $name."
    }

    $gameVersions = [Uri]::EscapeDataString(('["' + $MinecraftVersion + '"]'))
    $uri = "https://api.modrinth.com/v2/project/$slug/version?game_versions=$gameVersions&include_changelog=false"
    if (-not [string]::IsNullOrWhiteSpace($loader)) {
        $loaders = [Uri]::EscapeDataString(('["' + $loader + '"]'))
        $uri += "&loaders=$loaders"
    }

    $rawVersions = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
    $version = $null
    foreach ($candidate in $rawVersions) {
        if ([string]$candidate.version_number -eq $pinnedVersion) {
            $version = $candidate
            break
        }
    }

    if (-not $version) {
        throw "Pinned Modrinth version $pinnedVersion was not found for $name / Minecraft $MinecraftVersion."
    }

    if ($version.version_type -and $version.version_type -ne "release") {
        throw "Pinned Modrinth version $pinnedVersion for $name is no longer marked as a stable release."
    }

    $file = @($version.files) |
        Where-Object { [string]$_.filename -eq $pinnedFileName } |
        Select-Object -First 1

    if (-not $file) {
        throw "Pinned file $pinnedFileName was not found in $name $pinnedVersion."
    }

    $apiSha512 = ([string]$file.hashes.sha512).ToLowerInvariant()
    if ($apiSha512 -ne $pinnedSha512) {
        throw "Pinned Modrinth SHA-512 changed for $name $pinnedVersion. Lock=$pinnedSha512 API=$apiSha512"
    }

    $safeName = [IO.Path]::GetFileName([string]$file.filename)
    if ([string]::IsNullOrWhiteSpace($safeName) -or $safeName -ne $pinnedFileName) {
        throw "Unsafe or unexpected Modrinth filename returned for $name."
    }

    $extension = if ($kind -eq "resourcepack") { ".zip" } else { ".jar" }
    if (-not $safeName.EndsWith($extension, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Pinned file for $name has unexpected extension: $safeName"
    }

    $downloadUri = [Uri][string]$file.url
    $allowedModrinthHosts = @("cdn.modrinth.com", "api.modrinth.com")
    if ($downloadUri.Scheme -ne "https" -or
        $allowedModrinthHosts -notcontains $downloadUri.Host.ToLowerInvariant()) {
        throw "Unexpected Modrinth download origin for ${name}: $($file.url)"
    }

    $destination = Join-Path $filesDir $safeName
    Write-Host "Downloading pinned $name $pinnedVersion..."
    $actual = Copy-WithSha512 -Url $downloadUri.AbsoluteUri -Destination $destination -ExpectedSha512 $pinnedSha512 -Label $name

    return [ordered]@{
        Name = $name
        Slug = $slug
        Kind = $kind
        Loader = $loader
        Version = $pinnedVersion
        FileName = $safeName
        RelativePath = "files/$safeName"
        Sha512 = $actual
    }
}

$loaderVersion = [string]$RuntimeLock.fabric_loader.version
$parsedLoaderVersion = $null
if ([string]::IsNullOrWhiteSpace($loaderVersion) -or
    -not [Version]::TryParse($loaderVersion, [ref]$parsedLoaderVersion)) {
    throw "Pinned Fabric Loader version is invalid: $loaderVersion"
}

Write-Host "Using pinned Fabric Loader $loaderVersion for Minecraft $MinecraftVersion..."

Write-Host "Resolving deterministic Fabric Loader profile and Maven libraries..."
$escapedMinecraftVersion = [Uri]::EscapeDataString($MinecraftVersion)
$loaderRows = @(Invoke-Json "https://meta.fabricmc.net/v2/versions/loader/$escapedMinecraftVersion")
$loaderInfo = $loaderRows |
    Where-Object { [string]$_.loader.version -eq $loaderVersion } |
    Select-Object -First 1

if (-not $loaderInfo) {
    throw "Pinned Fabric Loader $loaderVersion is no longer exposed by Fabric Meta for Minecraft $MinecraftVersion."
}

$profile = New-DlssNrFabric262ClientProfile -LoaderInfo $loaderInfo -MinecraftVersion $MinecraftVersion
$profilePath = Join-Path $package "fabric-profile.json"
Write-DlssNrFabricProfile -Profile $profile -Path $profilePath

$expectedProfileSha256 = ([string]$RuntimeLock.fabric_loader.profile_sha256).ToLowerInvariant()
$actualProfileSha256 = (Get-FileHash -LiteralPath $profilePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($expectedProfileSha256 -notmatch "^[0-9a-f]{64}$" -or $actualProfileSha256 -ne $expectedProfileSha256) {
    throw "Fabric Loader profile SHA-256 mismatch. Lock=$expectedProfileSha256 Actual=$actualProfileSha256"
}
$profileId = [string]$profile.id
$expectedProfileId = [string]$RuntimeLock.fabric_loader.profile_id
if ([string]::IsNullOrWhiteSpace($profileId) -or $profileId -ne $expectedProfileId) {
    throw "Fabric Loader profile id mismatch. Lock=$expectedProfileId Actual=$profileId"
}

$fabricLibraries = @()
$librariesRoot = Join-Path $filesDir "libraries"
New-Item -ItemType Directory -Force -Path $librariesRoot | Out-Null

foreach ($library in @($profile.libraries)) {
    $coordinate = [string]$library.name
    $baseUrl = [string]$library.url
    $parts = @($coordinate -split ":")

    if ($parts.Count -ne 3 -or
        [string]::IsNullOrWhiteSpace($baseUrl)) {
        throw "Unsupported Fabric library coordinate: $coordinate"
    }

    $group = $parts[0]
    $artifact = $parts[1]
    $version = $parts[2]
    $groupPath = $group.Replace(".", "/")
    $mavenPath = "$groupPath/$artifact/$version/$artifact-$version.jar"

    $baseUri = [Uri]($baseUrl.TrimEnd("/") + "/")
    $allowedMavenHosts = @(
        "maven.fabricmc.net",
        "repo.maven.apache.org",
        "repo1.maven.org"
    )
    if ($baseUri.Scheme -ne "https" -or
        $allowedMavenHosts -notcontains $baseUri.Host.ToLowerInvariant()) {
        throw "Fabric library uses an unexpected Maven repository: $baseUrl"
    }

    $libraryUrl = ([Uri]::new($baseUri, $mavenPath)).AbsoluteUri

    $lockedLibrary = @($RuntimeLock.fabric_loader.libraries) | Where-Object { [string]$_.name -eq $coordinate } | Select-Object -First 1
    if (-not $lockedLibrary) {
        throw "Fabric profile contains an unpinned library: $coordinate"
    }

    $sha1 = ([string]$lockedLibrary.sha1).ToLowerInvariant()
    if ($sha1 -notmatch "^[0-9a-f]{40}$") {
        throw "Minecraft runtime lock has invalid SHA-1 for $coordinate."
    }

    $sha1Response = Invoke-RestMethod -Uri ($libraryUrl + ".sha1") -Headers $headers -Method Get
    $upstreamSha1 = ([string]$sha1Response).Trim().Split()[0].ToLowerInvariant()
    if ($upstreamSha1 -ne $sha1) {
        throw "Fabric library SHA-1 changed for $coordinate. Lock=$sha1 Upstream=$upstreamSha1"
    }

    $relativeLocal = $mavenPath.Replace("/", [IO.Path]::DirectorySeparatorChar)
    $destination = Join-Path $librariesRoot $relativeLocal
    $actualSha1 = Copy-WithSha1 -Url $libraryUrl -Destination $destination -ExpectedSha1 $sha1 -Label $coordinate

    $fabricLibraries += [ordered]@{
        Name = $coordinate
        MavenPath = $mavenPath
        RelativePath = "files/libraries/$mavenPath"
        Sha1 = $actualSha1
    }
}

if ($fabricLibraries.Count -ne @($RuntimeLock.fabric_loader.libraries).Count) {
    throw "Fabric profile library count does not match runtime lock. Profile=$($fabricLibraries.Count) Lock=$(@($RuntimeLock.fabric_loader.libraries).Count)"
}

$FabricInstallerVersion = [string]$RuntimeLock.fabric_installer.version
Write-Host "Using pinned Fabric Installer $FabricInstallerVersion..."
$installerName = "fabric-installer-$FabricInstallerVersion.jar"
$installerUrl = "https://maven.fabricmc.net/net/fabricmc/fabric-installer/$FabricInstallerVersion/$installerName"
$installerUri = [Uri]$installerUrl
if ($installerUri.Scheme -ne "https" -or $installerUri.Host -ne "maven.fabricmc.net") {
    throw "Unexpected Fabric Installer origin: $installerUrl"
}
$installerPath = Join-Path $filesDir $installerName
$installerSha256 = ([string]$RuntimeLock.fabric_installer.sha256).ToLowerInvariant()
if ($installerSha256 -notmatch "^[0-9a-f]{64}$") {
    throw "Minecraft runtime lock has invalid Fabric Installer SHA-256."
}
$installerSidecar = (([string](Invoke-RestMethod -Uri ($installerUrl + ".sha256") -Headers $headers -Method Get)).Trim().Split()[0]).ToLowerInvariant()
if ($installerSidecar -ne $installerSha256) {
    throw "Fabric Installer SHA-256 changed. Lock=$installerSha256 Upstream=$installerSidecar"
}
Invoke-WebRequest -Uri $installerUrl -Headers $headers -OutFile $installerPath
$actualInstallerSha = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualInstallerSha -ne $installerSha256) {
    throw "Fabric Installer SHA-256 mismatch. Expected=$installerSha256 Actual=$actualInstallerSha"
}

$components = @()
foreach ($project in @($RuntimeLock.components)) {
    $components += Resolve-ModrinthComponent -Component $project
}

$licenseSources = @(
    @{ Id = "fabric-installer"; Path = "third_party/minecraft/fabric-installer" },
    @{ Id = "fabric-api"; Path = "third_party/minecraft/fabric-api" },
    @{ Id = "lithium"; Path = "third_party/minecraft/lithium" },
    @{ Id = "ferritecore"; Path = "third_party/minecraft/ferritecore" },
    @{ Id = "krypton"; Path = "third_party/minecraft/krypton" },
    @{ Id = "c2me"; Path = "third_party/minecraft/c2me" },
    @{ Id = "badoptimizations"; Path = "third_party/minecraft/badoptimizations" },
    @{ Id = "dynamic-fps"; Path = "third_party/minecraft/dynamic-fps" }
)
foreach ($item in $licenseSources) {
    $root = Join-Path $Root $item.Path
    if (!(Test-Path -LiteralPath $root)) { continue }
    $license = Get-ChildItem -LiteralPath $root -File -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -match "^(?i)(LICENSE|COPYING)(\..*)?$"
    } | Select-Object -First 1
    if ($license) {
        Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $licensesDir ($item.Id + "-" + $license.Name)) -Force
    }
}

$manifest = [ordered]@{
    SchemaVersion = 1
    MinecraftVersion = $MinecraftVersion
    CreatedAtUtc = [DateTime]::UtcNow.ToString("o")
    RuntimeLockSha256 = $RuntimeLockSha256
    FabricLoaderVersion = $loaderVersion
    FabricProfileId = $profileId
    FabricProfileRelativePath = "fabric-profile.json"
    FabricLibraries = $fabricLibraries
    FabricInstaller = [ordered]@{
        Version = $FabricInstallerVersion
        FileName = $installerName
        RelativePath = "files/$installerName"
        Sha256 = $actualInstallerSha
    }
    Components = $components
}
$manifestPath = Join-Path $package "minecraft-runtime.json"
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$notice = @(
    "This bundle mirrors exact upstream release artifacts selected once during the bootstrap.",
    "DLSS NR Manager verifies Fabric Installer SHA-256 and Modrinth SHA-512 before packaging.",
    "The bundle also contains the exact Fabric Loader profile JSON and all referenced Maven libraries so Fabric installation is offline after bootstrap.",
    "The application later consumes only this manager-owned bundle, not Fabric Meta, Fabric Maven or Modrinth APIs.",
    "Each included component remains subject to its upstream license."
) -join [Environment]::NewLine
[IO.File]::WriteAllText((Join-Path $package "NOTICE.txt"), $notice + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))

if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $package "*") -DestinationPath $zip -Force
if (!(Test-Path -LiteralPath $zip) -or (Get-Item -LiteralPath $zip).Length -lt 1MB) {
    throw "Minecraft runtime bundle was not generated correctly."
}

$bundleSha = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()

if ($NoUpload) {
    Write-Host "Minecraft runtime bundle validation completed without release upload."
    Write-Host "Bundle: $zip"
    Write-Host "Fabric Loader: $loaderVersion"
    Write-Host "Fabric Installer: $FabricInstallerVersion"
    Write-Host "Components: $($components.Count)"
    Write-Host "SHA-256: $bundleSha"
    exit 0
}

Write-Host "Uploading minecraft-runtime-$MinecraftVersion.zip to $Repository $ReleaseTag..."
& gh release upload $ReleaseTag $zip --repo $Repository --clobber
if ($LASTEXITCODE -ne 0) { throw "Minecraft runtime release upload failed." }

$releaseJson = & gh api "repos/$Repository/releases/tags/$ReleaseTag"
if ($LASTEXITCODE -ne 0) { throw "Unable to verify manager release after Minecraft upload." }
$release = $releaseJson | ConvertFrom-Json
$remote = @($release.assets) | Where-Object { $_.name -eq "minecraft-runtime-$MinecraftVersion.zip" } | Select-Object -First 1
if (-not $remote) { throw "Uploaded Minecraft runtime asset was not found." }
if ([long]$remote.size -ne (Get-Item -LiteralPath $zip).Length) { throw "Remote Minecraft runtime size mismatch." }
if ($remote.digest -and ([string]$remote.digest).StartsWith("sha256:")) {
    $remoteHash = ([string]$remote.digest).Substring(7).ToLowerInvariant()
    if ($remoteHash -ne $bundleSha) { throw "Remote Minecraft runtime digest mismatch." }
}

Write-Host "Published minecraft-runtime-$MinecraftVersion.zip"
Write-Host "Fabric Loader: $loaderVersion"
Write-Host "Fabric Installer: $FabricInstallerVersion"
Write-Host "Components: $($components.Count)"
Write-Host "SHA-256: $bundleSha"
