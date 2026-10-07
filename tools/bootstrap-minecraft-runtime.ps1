param(
    [string]$Repository = "grg914/dlss-nr-manager",
    [string]$ReleaseTag,
    [string]$MinecraftVersion = "26.2",
    [string]$FabricLoaderVersion = "0.19.3",
    [string]$FabricInstallerVersion = "1.1.2",
    [switch]$NoUpload
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$UserAgent = "DlssNrManager-MinecraftBootstrap/1.0 (+https://github.com/grg914/dlss-nr-manager)"

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
        [string]$Name,
        [string]$Slug,
        [string]$Kind,
        [string]$Loader
    )

    $gameVersions = [Uri]::EscapeDataString(("[`"$MinecraftVersion`"]"))
    $uri = "https://api.modrinth.com/v2/project/$Slug/version?game_versions=$gameVersions"
    if (-not [string]::IsNullOrWhiteSpace($Loader)) {
        $loaders = [Uri]::EscapeDataString(("[`"$Loader`"]"))
        $uri += "&loaders=$loaders"
    }

    $versions = @(Invoke-Json $uri)
    $version = $versions | Where-Object {
        -not $_.version_type -or $_.version_type -eq "release"
    } | Select-Object -First 1

    if (-not $version) {
        throw "No stable Modrinth release found for $Name / Minecraft $MinecraftVersion."
    }

    $extension = if ($Kind -eq "resourcepack") { ".zip" } else { ".jar" }
    $candidates = @($version.files | Where-Object {
        $_.filename -and
        $_.filename.EndsWith($extension, [StringComparison]::OrdinalIgnoreCase) -and
        $_.filename -notmatch "(?i)(sources|source|dev|javadoc)"
    })

    $file = $candidates | Where-Object { $_.primary -eq $true } | Select-Object -First 1
    if (-not $file) { $file = $candidates | Select-Object -First 1 }
    if (-not $file) { throw "No downloadable $extension file found for $Name." }

    $sha512 = [string]$file.hashes.sha512
    $safeName = [IO.Path]::GetFileName([string]$file.filename)
    if ([string]::IsNullOrWhiteSpace($safeName) -or $safeName -ne [string]$file.filename) {
        throw "Unsafe Modrinth filename returned for $Name."
    }

    $downloadUri = [Uri][string]$file.url
    $allowedModrinthHosts = @("cdn.modrinth.com", "api.modrinth.com")
    if ($downloadUri.Scheme -ne "https" -or
        $allowedModrinthHosts -notcontains $downloadUri.Host.ToLowerInvariant()) {
        throw "Unexpected Modrinth download origin for ${Name}: $($file.url)"
    }

    $destination = Join-Path $filesDir $safeName
    Write-Host "Downloading $Name $($version.version_number)..."
    $actual = Copy-WithSha512 -Url $downloadUri.AbsoluteUri -Destination $destination -ExpectedSha512 $sha512 -Label $Name

    return [ordered]@{
        Name = $Name
        Slug = $Slug
        Kind = $Kind
        Loader = $Loader
        Version = [string]$version.version_number
        FileName = $safeName
        RelativePath = "files/$safeName"
        Sha512 = $actual
    }
}

$loaderVersion = $FabricLoaderVersion
$parsedLoaderVersion = $null
if ([string]::IsNullOrWhiteSpace($loaderVersion) -or
    -not [Version]::TryParse($loaderVersion, [ref]$parsedLoaderVersion)) {
    throw "Pinned Fabric Loader version is invalid: $loaderVersion"
}

Write-Host "Using pinned Fabric Loader $loaderVersion for Minecraft $MinecraftVersion..."

Write-Host "Resolving Fabric Loader profile and Maven libraries..."
$profileUrl = "https://meta.fabricmc.net/v2/versions/loader/$MinecraftVersion/$loaderVersion/profile/json"
$profilePath = Join-Path $package "fabric-profile.json"
Invoke-WebRequest -Uri $profileUrl -Headers $headers -OutFile $profilePath

$profile = Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json
$profileId = [string]$profile.id
if ([string]::IsNullOrWhiteSpace($profileId)) {
    throw "Fabric Meta profile has no id."
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
    $sha1Response = Invoke-RestMethod -Uri ($libraryUrl + ".sha1") -Headers $headers -Method Get
    $sha1 = ([string]$sha1Response).Trim().Split()[0].ToLowerInvariant()
    if ($sha1 -notmatch "^[0-9a-f]{40}$") {
        throw "Fabric library SHA-1 sidecar is invalid for $coordinate."
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

Write-Host "Using pinned Fabric Installer $FabricInstallerVersion..."
$installerName = "fabric-installer-$FabricInstallerVersion.jar"
$installerUrl = "https://maven.fabricmc.net/net/fabricmc/fabric-installer/$FabricInstallerVersion/$installerName"
$installerUri = [Uri]$installerUrl
if ($installerUri.Scheme -ne "https" -or $installerUri.Host -ne "maven.fabricmc.net") {
    throw "Unexpected Fabric Installer origin: $installerUrl"
}
$installerPath = Join-Path $filesDir $installerName
$installerSha256 = (([string](Invoke-RestMethod -Uri ($installerUrl + ".sha256") -Headers $headers -Method Get)).Trim().Split()[0]).ToLowerInvariant()
if ($installerSha256 -notmatch "^[0-9a-f]{64}$") { throw "Fabric Installer SHA-256 sidecar is invalid." }
Invoke-WebRequest -Uri $installerUrl -Headers $headers -OutFile $installerPath
$actualInstallerSha = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualInstallerSha -ne $installerSha256) {
    throw "Fabric Installer SHA-256 mismatch. Expected=$installerSha256 Actual=$actualInstallerSha"
}

$projects = @(
    @{ Name = "Fabric API"; Slug = "fabric-api"; Kind = "mod"; Loader = "fabric"; Required = $true },
    @{ Name = "Lithium"; Slug = "lithium"; Kind = "mod"; Loader = "fabric"; Required = $false },
    @{ Name = "FerriteCore"; Slug = "ferrite-core"; Kind = "mod"; Loader = "fabric"; Required = $false },
    @{ Name = "Krypton"; Slug = "krypton"; Kind = "mod"; Loader = "fabric"; Required = $false },
    @{ Name = "C2ME"; Slug = "c2me-fabric"; Kind = "mod"; Loader = "fabric"; Required = $false },
    @{ Name = "BadOptimizations"; Slug = "badoptimizations"; Kind = "mod"; Loader = "fabric"; Required = $false },
    @{ Name = "Dynamic FPS"; Slug = "dynamic-fps"; Kind = "mod"; Loader = "fabric"; Required = $false },
    @{ Name = "SPBR LabPBR"; Slug = "spbr"; Kind = "resourcepack"; Loader = ""; Required = $false }
)

$components = @()
foreach ($project in $projects) {
    try {
        $components += Resolve-ModrinthComponent -Name $project.Name -Slug $project.Slug -Kind $project.Kind -Loader $project.Loader
    }
    catch {
        if ($project.Required) {
            throw
        }

        Write-Warning "Optional Minecraft component $($project.Name) was not bundled: $($_.Exception.Message)"
    }
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
