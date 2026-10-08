param(
    [string]$OutputPath = "SBOM.spdx.json",
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"

function New-SpdxId {
    param([Parameter(Mandatory=$true)][string]$Value)

    $safe = ($Value -replace "[^A-Za-z0-9.-]", "-").Trim("-")
    if ([string]::IsNullOrWhiteSpace($safe)) {
        $safe = "component"
    }

    return "SPDXRef-$safe"
}

function Get-OptionalProperty {
    param(
        [Parameter(Mandatory=$true)]$Object,
        [Parameter(Mandatory=$true)][string]$Name,
        $Default = $null
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $Default
    }

    return $property.Value
}

[xml]$project = Get-Content -LiteralPath (Join-Path $Root "DlssNrManager.csproj")
$appVersion = [string]$project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($appVersion)) {
    throw "Application version is missing."
}

$packages = New-Object System.Collections.ArrayList
$appSpdxId = "SPDXRef-DlssNrManager"

[void]$packages.Add([ordered]@{
    SPDXID = $appSpdxId
    name = "DLSS NR Manager"
    versionInfo = $appVersion
    downloadLocation = "https://github.com/grg914/dlss-nr-manager"
    filesAnalyzed = $false
    licenseConcluded = "NOASSERTION"
    licenseDeclared = "MIT"
    copyrightText = "NOASSERTION"
})

$relationships = New-Object System.Collections.ArrayList

$packageReferences = @($project.Project.ItemGroup.PackageReference)
foreach ($reference in $packageReferences) {
    $name = [string]$reference.Include
    if ([string]::IsNullOrWhiteSpace($name)) { continue }

    $version = [string]$reference.Version
    $spdxId = New-SpdxId ("nuget-" + $name)

    [void]$packages.Add([ordered]@{
        SPDXID = $spdxId
        name = $name
        versionInfo = $version
        downloadLocation = "NOASSERTION"
        filesAnalyzed = $false
        licenseConcluded = "NOASSERTION"
        licenseDeclared = "NOASSERTION"
        copyrightText = "NOASSERTION"
        externalRefs = @(
            [ordered]@{
                referenceCategory = "PACKAGE-MANAGER"
                referenceType = "purl"
                referenceLocator = "pkg:nuget/$name@$version"
            }
        )
    })

    [void]$relationships.Add([ordered]@{
        spdxElementId = $appSpdxId
        relationshipType = "DEPENDS_ON"
        relatedSpdxElement = $spdxId
    })
}

$lock = Get-Content -LiteralPath (Join-Path $Root "third_party\DEPENDENCIES.lock.json") -Raw | ConvertFrom-Json
$locked = @($lock.sources) + @($lock.local_only)

foreach ($source in $locked) {
    $id = [string]$source.id
    if ([string]::IsNullOrWhiteSpace($id)) { continue }

    $ref = [string]$source.ref
    $url = [string]$source.url
    $license = [string](Get-OptionalProperty -Object $source -Name "license" -Default "NOASSERTION")
    if ([string]::IsNullOrWhiteSpace($license)) { $license = "NOASSERTION" }

    $spdxId = New-SpdxId ("source-" + $id)

    [void]$packages.Add([ordered]@{
        SPDXID = $spdxId
        name = $id
        versionInfo = $ref
        downloadLocation = if ([string]::IsNullOrWhiteSpace($url)) { "NOASSERTION" } else { $url }
        filesAnalyzed = $false
        licenseConcluded = "NOASSERTION"
        licenseDeclared = $license
        copyrightText = "NOASSERTION"
    })

    [void]$relationships.Add([ordered]@{
        spdxElementId = $appSpdxId
        relationshipType = "DEPENDS_ON"
        relatedSpdxElement = $spdxId
    })
}

$minecraftLockPath = Join-Path $Root "third_party\minecraft\RUNTIME.lock.json"
if (Test-Path -LiteralPath $minecraftLockPath) {
    $minecraft = Get-Content -LiteralPath $minecraftLockPath -Raw | ConvertFrom-Json

    foreach ($property in $minecraft.PSObject.Properties) {
        $value = $property.Value
        if ($null -eq $value) { continue }

        $id = "minecraft-" + [string]$property.Name
        $spdxId = New-SpdxId $id

        $version = ""
        foreach ($candidate in @("version", "loader", "tag", "release", "id")) {
            $candidateValue = Get-OptionalProperty -Object $value -Name $candidate
            if (-not [string]::IsNullOrWhiteSpace([string]$candidateValue)) {
                $version = [string]$candidateValue
                break
            }
        }

        if ([string]::IsNullOrWhiteSpace($version)) {
            $version = "locked"
        }

        [void]$packages.Add([ordered]@{
            SPDXID = $spdxId
            name = $id
            versionInfo = $version
            downloadLocation = "NOASSERTION"
            filesAnalyzed = $false
            licenseConcluded = "NOASSERTION"
            licenseDeclared = "NOASSERTION"
            copyrightText = "NOASSERTION"
        })

        [void]$relationships.Add([ordered]@{
            spdxElementId = $appSpdxId
            relationshipType = "DEPENDS_ON"
            relatedSpdxElement = $spdxId
        })
    }
}

$documentNamespace = "https://github.com/grg914/dlss-nr-manager/sbom/" +
    $appVersion + "/" + [Guid]::NewGuid().ToString("N")

$document = [ordered]@{
    spdxVersion = "SPDX-2.3"
    dataLicense = "CC0-1.0"
    SPDXID = "SPDXRef-DOCUMENT"
    name = "DLSS-NR-Manager-$appVersion"
    documentNamespace = $documentNamespace
    creationInfo = [ordered]@{
        created = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        creators = @("Tool: DLSS-NR-Manager/tools/generate-sbom.ps1")
    }
    packages = @($packages)
    relationships = @(
        [ordered]@{
            spdxElementId = "SPDXRef-DOCUMENT"
            relationshipType = "DESCRIBES"
            relatedSpdxElement = $appSpdxId
        }
    ) + @($relationships)
}

$destination = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
} else {
    Join-Path $Root $OutputPath
}

$parent = Split-Path -Parent $destination
if (-not [string]::IsNullOrWhiteSpace($parent)) {
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
}

[System.IO.File]::WriteAllText(
    $destination,
    ($document | ConvertTo-Json -Depth 12),
    [System.Text.UTF8Encoding]::new($false))

Get-Content -LiteralPath $destination -Raw | ConvertFrom-Json | Out-Null
Write-Host "SPDX SBOM generated: $destination"
Write-Host "Packages: $($packages.Count)"
