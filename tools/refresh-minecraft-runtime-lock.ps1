param(
    [string]$MinecraftVersion = "26.2"
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$LockPath = Join-Path $Root "third_party\minecraft\RUNTIME.lock.json"\n$FabricProfileScript = Join-Path $PSScriptRoot "fabric-profile.ps1"\nif (!(Test-Path -LiteralPath $FabricProfileScript)) { throw "Missing Fabric profile helper: $FabricProfileScript" }\n. $FabricProfileScript
$UserAgent = "DlssNrManager-MinecraftLockRefresh/1.0 (+https://github.com/grg914/dlss-nr-manager)"
$headers = @{ "User-Agent" = $UserAgent; Accept = "application/json" }

if (!(Test-Path -LiteralPath $LockPath)) { throw "Missing Minecraft runtime lock: $LockPath" }

$lock = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json
if ([string]$lock.minecraft_version -ne $MinecraftVersion) {
    throw "Runtime lock targets $($lock.minecraft_version), requested $MinecraftVersion."
}

function Invoke-Json {
    param([Parameter(Mandatory=$true)][string]$Uri)
    return Invoke-RestMethod -Uri $Uri -Headers $headers -Method Get
}

function Select-LatestModrinthRelease {
    param(
        [Parameter(Mandatory=$true)][string]$Slug,
        [string]$Loader,
        [Parameter(Mandatory=$true)][string]$Kind
    )

    $gameVersions = [Uri]::EscapeDataString(('["' + $MinecraftVersion + '"]'))
    $uri = "https://api.modrinth.com/v2/project/$Slug/version?game_versions=$gameVersions&include_changelog=false"
    if (-not [string]::IsNullOrWhiteSpace($Loader)) {
        $loaders = [Uri]::EscapeDataString(('["' + $Loader + '"]'))
        $uri += "&loaders=$loaders"
    }

    $versions = @(Invoke-Json $uri) |
        Where-Object { -not $_.version_type -or [string]$_.version_type -eq "release" } |
        Sort-Object { [DateTimeOffset]$_.date_published } -Descending

    foreach ($version in $versions) {
        $extension = if ($Kind -eq "resourcepack") { ".zip" } else { ".jar" }
        $file = @($version.files) |
            Where-Object {
                ([string]$_.filename).EndsWith($extension, [StringComparison]::OrdinalIgnoreCase)
            } |
            Sort-Object @{ Expression = { if ($_.primary) { 0 } else { 1 } } }, filename |
            Select-Object -First 1

        if ($file -and [string]$file.hashes.sha512 -match "^[0-9a-fA-F]{128}$") {
            return [pscustomobject]@{
                Version = [string]$version.version_number
                FileName = [IO.Path]::GetFileName([string]$file.filename)
                Sha512 = ([string]$file.hashes.sha512).ToLowerInvariant()
            }
        }
    }

    return $null
}

Write-Host "Refreshing Fabric Loader metadata for Minecraft $MinecraftVersion..."
$loaderRows = @(Invoke-Json "https://meta.fabricmc.net/v2/versions/loader/$MinecraftVersion")
$loader = $loaderRows |
    Where-Object { [bool]$_.loader.stable } |
    Select-Object -First 1

if (-not $loader) { throw "No stable Fabric Loader was found for Minecraft $MinecraftVersion." }

$loaderVersion = [string]$loader.loader.version
$encodedMinecraft = [Uri]::EscapeDataString($MinecraftVersion)
$encodedLoader = [Uri]::EscapeDataString($loaderVersion)
$loaderInfo = Invoke-Json "https://meta.fabricmc.net/v2/versions/loader/$encodedMinecraft/$encodedLoader"
if (-not $loaderInfo -or [string]$loaderInfo.loader.version -ne $loaderVersion) {
    throw "Fabric loader detail metadata did not resolve $MinecraftVersion / $loaderVersion."
}

$tempProfile = Join-Path $env:TEMP ("fabric-profile-" + [Guid]::NewGuid().ToString("N") + ".json")
try {
    $profile = Write-DeterministicFabricProfile -LoaderInfo $loaderInfo -MinecraftVersion $MinecraftVersion -LoaderVersion $loaderVersion -OutputPath $tempProfile
    $profileSha256 = (Get-FileHash -LiteralPath $tempProfile -Algorithm SHA256).Hash.ToLowerInvariant()
}
finally {
    if (Test-Path -LiteralPath $tempProfile) { Remove-Item -LiteralPath $tempProfile -Force }
}

$libraries = @()
foreach ($library in @($profile.libraries)) {
    $coordinate = [string]$library.name
    $baseUrl = [string]$library.url
    $parts = @($coordinate -split ":")
    if ($parts.Count -ne 3 -or [string]::IsNullOrWhiteSpace($baseUrl)) {
        throw "Unsupported Fabric library coordinate: $coordinate"
    }

    $groupPath = $parts[0].Replace(".", "/")
    $artifact = $parts[1]
    $version = $parts[2]
    $mavenPath = "$groupPath/$artifact/$version/$artifact-$version.jar"
    $libraryUrl = $baseUrl.TrimEnd("/") + "/" + $mavenPath
    $sha1 = ([string](Invoke-RestMethod -Uri ($libraryUrl + ".sha1") -Headers $headers -Method Get)).Trim().Split()[0].ToLowerInvariant()

    if ($sha1 -notmatch "^[0-9a-f]{40}$") {
        throw "Invalid SHA-1 sidecar for Fabric library $coordinate."
    }

    $libraries += [ordered]@{
        name = $coordinate
        sha1 = $sha1
    }
}

$lock.fabric_loader.version = $loaderVersion
$lock.fabric_loader.profile_id = [string]$profile.id
$lock.fabric_loader.profile_sha256 = $profileSha256
$lock.fabric_loader.libraries = @($libraries)

Write-Host "Fabric Loader -> $loaderVersion ($($libraries.Count) libraries)"

Write-Host "Refreshing Fabric Installer metadata..."
$metadata = [xml](Invoke-WebRequest -Uri "https://maven.fabricmc.net/net/fabricmc/fabric-installer/maven-metadata.xml" -Headers $headers).Content
$installerVersion = [string]$metadata.metadata.versioning.release
if ([string]::IsNullOrWhiteSpace($installerVersion)) {
    $installerVersion = [string]$metadata.metadata.versioning.latest
}
if ([string]::IsNullOrWhiteSpace($installerVersion)) {
    throw "Fabric Installer Maven metadata exposes no release/latest version."
}

$installerName = "fabric-installer-$installerVersion.jar"
$installerUrl = "https://maven.fabricmc.net/net/fabricmc/fabric-installer/$installerVersion/$installerName"
$installerSha = ([string](Invoke-RestMethod -Uri ($installerUrl + ".sha256") -Headers $headers -Method Get)).Trim().Split()[0].ToLowerInvariant()
if ($installerSha -notmatch "^[0-9a-f]{64}$") { throw "Invalid Fabric Installer SHA-256." }

$lock.fabric_installer.version = $installerVersion
$lock.fabric_installer.sha256 = $installerSha
Write-Host "Fabric Installer -> $installerVersion"

Write-Host "Refreshing stable Minecraft components from Modrinth..."
foreach ($component in @($lock.components)) {
    $latest = Select-LatestModrinthRelease -Slug ([string]$component.slug) -Loader ([string]$component.loader) -Kind ([string]$component.kind)
    if (-not $latest) {
        throw "No stable compatible Modrinth release found for $($component.name)."
    }

    $component.version = $latest.Version
    $component.file_name = $latest.FileName
    $component.sha512 = $latest.Sha512
    Write-Host "$($component.name) -> $($latest.Version)"
}

$remainingExcluded = @()
foreach ($excluded in @($lock.excluded_components)) {
    $slug = [string]$excluded.slug
    if ([string]::IsNullOrWhiteSpace($slug)) {
        $remainingExcluded += $excluded
        continue
    }

    $latest = Select-LatestModrinthRelease -Slug $slug -Loader "fabric" -Kind "mod"
    if (-not $latest) {
        $remainingExcluded += $excluded
        continue
    }

    Write-Host "Previously excluded $($excluded.name) now has a stable $MinecraftVersion release: $($latest.Version)"
    $lock.components += [pscustomobject]@{
        name = [string]$excluded.name
        slug = $slug
        kind = "mod"
        loader = "fabric"
        version = $latest.Version
        file_name = $latest.FileName
        sha512 = $latest.Sha512
        install_required = $false
    }
}

$lock.excluded_components = @($remainingExcluded)

$lock | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $LockPath -Encoding UTF8
Write-Host "Minecraft runtime lock refreshed: $LockPath"
