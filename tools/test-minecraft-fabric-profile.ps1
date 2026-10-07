param()

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "minecraft-fabric-profile.ps1")

$fixture = [pscustomobject]@{
    loader = [pscustomobject]@{
        version = "0.19.3"
        maven = "net.fabricmc:fabric-loader:0.19.3"
        stable = $true
    }
    launcherMeta = [pscustomobject]@{
        mainClass = [pscustomobject]@{
            client = "net.fabricmc.loader.impl.launch.knot.KnotClient"
            server = "net.fabricmc.loader.impl.launch.knot.KnotServer"
        }
        libraries = [pscustomobject]@{
            common = @(
                [pscustomobject]@{
                    name = "org.ow2.asm:asm:9.10.1"
                    url = "https://maven.fabricmc.net/"
                },
                [pscustomobject]@{
                    name = "net.fabricmc:sponge-mixin:0.17.3+mixin.0.8.7"
                    url = "https://maven.fabricmc.net/"
                }
            )
            client = @()
        }
    }
}

$profileA = New-DlssNrFabric262ClientProfile -LoaderInfo $fixture -MinecraftVersion "26.2"
$profileB = New-DlssNrFabric262ClientProfile -LoaderInfo $fixture -MinecraftVersion "26.2"
$jsonA = ConvertTo-DlssNrCanonicalFabricProfileJson -Profile $profileA
$jsonB = ConvertTo-DlssNrCanonicalFabricProfileJson -Profile $profileB

if ($jsonA -cne $jsonB) {
    throw "Deterministic Fabric profile generation produced different JSON for identical metadata."
}

if ([string]$profileA.id -ne "fabric-loader-0.19.3-26.2") {
    throw "Unexpected deterministic Fabric profile id: $($profileA.id)"
}

if ([string]$profileA.inheritsFrom -ne "26.2") {
    throw "Unexpected Fabric inheritsFrom: $($profileA.inheritsFrom)"
}

if ([string]$profileA.releaseTime -ne "1970-01-01T00:00:00+0000" -or
    [string]$profileA.time -ne "1970-01-01T00:00:00+0000") {
    throw "Fabric profile timestamps are not deterministic."
}

$names = @($profileA.libraries | ForEach-Object { [string]$_.name })
if ($names -notcontains "net.fabricmc:fabric-loader:0.19.3") {
    throw "Fabric loader library is missing from deterministic profile."
}

if ($names | Where-Object { $_ -like "net.fabricmc:intermediary:*" }) {
    throw "Minecraft 26.2 deterministic profile must not add intermediary."
}

if ($names.Count -ne 3) {
    throw "Unexpected deterministic Fabric profile library count: $($names.Count)"
}

$firstHash = [Security.Cryptography.SHA256]::Create().ComputeHash(
    [Text.Encoding]::UTF8.GetBytes($jsonA))
$secondHash = [Security.Cryptography.SHA256]::Create().ComputeHash(
    [Text.Encoding]::UTF8.GetBytes($jsonB))

if (-not [Linq.Enumerable]::SequenceEqual([byte[]]$firstHash, [byte[]]$secondHash)) {
    throw "Deterministic Fabric profile hash changed for identical metadata."
}

Write-Host "Deterministic Fabric 26.2 profile test: PASSED"
