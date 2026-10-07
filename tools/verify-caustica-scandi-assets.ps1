param(
    [Parameter(Mandatory = $true)]
    [string]$CausticaJar,

    [Parameter(Mandatory = $true)]
    [string]$SpbrZip,

    [string]$ExpectedMinecraftVersion = "26.2",

    [string]$ExpectedSpbrSha256 = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-ZipEntry {
    param(
        [System.IO.Compression.ZipArchive]$Archive,
        [string]$Name
    )

    return $Archive.Entries |
        Where-Object { $_.FullName -eq $Name } |
        Select-Object -First 1
}

function Read-ZipEntryText {
    param(
        [System.IO.Compression.ZipArchive]$Archive,
        [string]$Name
    )

    $entry = Get-ZipEntry -Archive $Archive -Name $Name
    Assert-Condition ($null -ne $entry) "Archive entry '$Name' is missing."

    $stream = $entry.Open()
    try {
        $reader = [System.IO.StreamReader]::new(
            $stream,
            [System.Text.Encoding]::UTF8,
            $true,
            4096,
            $true)
        try {
            return $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Test-ZipEntryContainsText {
    param(
        [System.IO.Compression.ZipArchive]$Archive,
        [string]$Name,
        [string]$Needle
    )

    $entry = Get-ZipEntry -Archive $Archive -Name $Name
    if ($null -eq $entry) {
        return $false
    }

    $stream = $entry.Open()
    try {
        $memory = [System.IO.MemoryStream]::new()
        try {
            $stream.CopyTo($memory)
            $text = [System.Text.Encoding]::UTF8.GetString($memory.ToArray())
            return $text.Contains($Needle)
        }
        finally {
            $memory.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-SafeArchivePaths {
    param(
        [System.IO.Compression.ZipArchive]$Archive,
        [string]$Label
    )

    foreach ($entry in $Archive.Entries) {
        $name = $entry.FullName.Replace("\\", "/")
        if ($name.StartsWith("/") -or $name -match "(^|/)\.\.(/|$)") {
            throw "$Label contains an unsafe archive path: $($entry.FullName)"
        }
    }
}

$causticaPath = (Resolve-Path -LiteralPath $CausticaJar).Path
$spbrPath = (Resolve-Path -LiteralPath $SpbrZip).Path

Assert-Condition ([System.IO.Path]::GetExtension($causticaPath) -ieq ".jar") "Caustica asset must be a JAR."
Assert-Condition ([System.IO.Path]::GetExtension($spbrPath) -ieq ".zip") "SPBRScandi asset must be a ZIP."
Assert-Condition ((Get-Item -LiteralPath $causticaPath).Length -gt 0) "Caustica JAR is empty."
Assert-Condition ((Get-Item -LiteralPath $spbrPath).Length -gt 0) "SPBRScandi ZIP is empty."

$caustica = [System.IO.Compression.ZipFile]::OpenRead($causticaPath)
try {
    Assert-SafeArchivePaths -Archive $caustica -Label "Caustica JAR"

    $fabricText = Read-ZipEntryText -Archive $caustica -Name "fabric.mod.json"
    $fabric = $fabricText | ConvertFrom-Json

    Assert-Condition ([string]$fabric.id -eq "caustica") "Caustica JAR fabric.mod.json id is not 'caustica'."
    Assert-Condition ([string]$fabric.name -eq "Caustica RTX") "Caustica JAR does not identify itself as 'Caustica RTX'."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$fabric.version)) "Caustica JAR version is missing."
    Assert-Condition ([string]$fabric.version -match "(?i)rtx") "Caustica JAR version '$($fabric.version)' is not an RTX fork version."

    $minecraftDependency = [string]$fabric.depends.minecraft
    Assert-Condition ($minecraftDependency -eq $ExpectedMinecraftVersion) "Caustica JAR targets Minecraft '$minecraftDependency', expected '$ExpectedMinecraftVersion'."

    $requiredEntries = @(
        "dev/comfyfluffy/caustica/CausticaConfig.class",
        "dev/comfyfluffy/caustica/ngx/NgxLibrary.class",
        "dev/comfyfluffy/caustica/rt/RtLookPackage.class",
        "dev/comfyfluffy/caustica/rt/pipeline/RtDlssRr.class",
        "dev/comfyfluffy/caustica/rt/pipeline/RtDlssFg.class",
        "dev/comfyfluffy/caustica/rt/pipeline/RtDlssNr.class"
    )

    foreach ($required in $requiredEntries) {
        Assert-Condition ($null -ne (Get-ZipEntry -Archive $caustica -Name $required)) "Caustica JAR is missing custom RTX entry '$required'."
    }

    $markerChecks = @(
        @{
            Entry = "dev/comfyfluffy/caustica/CausticaConfig.class"
            Marker = "caustica.rt.dlssNr"
            Description = "DLSS Neural Rendering config"
        },
        @{
            Entry = "dev/comfyfluffy/caustica/CausticaConfig.class"
            Marker = "post-fx.scandi-shader"
            Description = "native ScandiShader RTX look"
        },
        @{
            Entry = "dev/comfyfluffy/caustica/CausticaConfig.class"
            Marker = "performance.enabled"
            Description = "RTX Performance Mode"
        },
        @{
            Entry = "dev/comfyfluffy/caustica/CausticaConfig.class"
            Marker = "frame-generation.enabled"
            Description = "DLSS Frame Generation config"
        },
        @{
            Entry = "dev/comfyfluffy/caustica/ngx/NgxLibrary.class"
            Marker = "ngxshim_create_dlssnr"
            Description = "DLSS-NR native ABI"
        },
        @{
            Entry = "dev/comfyfluffy/caustica/rt/pipeline/RtDlssNr.class"
            Marker = "DLSS Neural Rendering"
            Description = "DLSS-NR Java pipeline"
        }
    )

    foreach ($check in $markerChecks) {
        $present = Test-ZipEntryContainsText -Archive $caustica -Name $check.Entry -Needle $check.Marker
        Assert-Condition $present "Caustica JAR is missing custom marker '$($check.Marker)' ($($check.Description)) in '$($check.Entry)'."
    }

    Write-Host "Caustica JAR structural/custom-marker audit: PASSED"
    Write-Host "  Version: $($fabric.version)"
    Write-Host "  Minecraft: $minecraftDependency"
    Write-Host "  Custom markers: DLSS-RR / DLSS-FG / DLSS-NR / RTX Performance / ScandiShader RTX"
}
finally {
    $caustica.Dispose()
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedSpbrSha256)) {
    $expected = $ExpectedSpbrSha256.Replace("sha256:", "").Trim().ToUpperInvariant()
    Assert-Condition ($expected -match "^[0-9A-F]{64}$") "Expected SPBR SHA-256 is invalid."
    $actual = (Get-FileHash -LiteralPath $spbrPath -Algorithm SHA256).Hash.ToUpperInvariant()
    Assert-Condition ($actual -eq $expected) "SHA-256 mismatch for SPBRScandi.zip. Expected $expected, got $actual."
}

$spbr = [System.IO.Compression.ZipFile]::OpenRead($spbrPath)
try {
    Assert-SafeArchivePaths -Archive $spbr -Label "SPBRScandi ZIP"

    $packEntries = @($spbr.Entries | Where-Object { $_.FullName -eq "pack.mcmeta" })
    Assert-Condition ($packEntries.Count -eq 1) "SPBRScandi must contain exactly one top-level pack.mcmeta."

    $packText = Read-ZipEntryText -Archive $spbr -Name "pack.mcmeta"
    $pack = $packText | ConvertFrom-Json
    Assert-Condition ($null -ne $pack.pack) "SPBRScandi pack.mcmeta has no 'pack' object."

    $textures = @(
        $spbr.Entries |
        Where-Object {
            $_.FullName -match "(?i)^assets/minecraft/textures/.+\.png$"
        }
    )
    Assert-Condition ($textures.Count -gt 0) "SPBRScandi contains no Minecraft texture PNG files."

    $normalMaps = @($textures | Where-Object { $_.FullName -match "(?i)_n\.png$" })
    $specularMaps = @($textures | Where-Object { $_.FullName -match "(?i)_s\.png$" })
    Assert-Condition ($normalMaps.Count -gt 0) "SPBRScandi contains no LabPBR normal maps (*_n.png)."
    Assert-Condition ($specularMaps.Count -gt 0) "SPBRScandi contains no LabPBR specular maps (*_s.png)."

    Write-Host "SPBRScandi resource-pack audit: PASSED"
    Write-Host "  Texture PNG files: $($textures.Count)"
    Write-Host "  LabPBR normal maps: $($normalMaps.Count)"
    Write-Host "  LabPBR specular maps: $($specularMaps.Count)"
}
finally {
    $spbr.Dispose()
}

Write-Host "Caustica / SPBRScandi asset verification: PASSED"
