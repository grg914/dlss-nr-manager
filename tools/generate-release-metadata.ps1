param(
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$CommitSha,
    [string]$Repository = "grg914/dlss-nr-manager",
    [string]$DependencyLockPath = "third_party/DEPENDENCIES.lock.json",
    [string]$ComponentsManifestPath = "release-assets/components-manifest.json",
    [string]$OutputDirectory = "."
)

$ErrorActionPreference = "Stop"

function Sha256-OrNull([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or !(Test-Path -LiteralPath $Path)) {
        return $null
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Safe-SpdxId([string]$Value) {
    $safe = [Regex]::Replace($Value, '[^A-Za-z0-9.-]+', '-')
    $safe = $safe.Trim('-')
    if ([string]::IsNullOrWhiteSpace($safe)) {
        $safe = "component"
    }
    return "SPDXRef-$safe"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$lock = Get-Content -LiteralPath $DependencyLockPath -Raw | ConvertFrom-Json
$allDependencies = @($lock.sources) + @($lock.local_only)

$appSpdxId = "SPDXRef-DlssNrManager"
$packages = [System.Collections.ArrayList]::new()
$relationships = [System.Collections.ArrayList]::new()

[void]$packages.Add([ordered]@{
    name = "DLSS NR Manager"
    SPDXID = $appSpdxId
    versionInfo = $Version
    downloadLocation = "https://github.com/$Repository/releases"
    filesAnalyzed = $false
    licenseConcluded = "MIT"
    licenseDeclared = "MIT"
    copyrightText = "NOASSERTION"
})

foreach ($dep in $allDependencies) {
    $id = [string]$dep.id
    $spdxId = Safe-SpdxId $id

    $version = if ($dep.source_tag) {
        [string]$dep.source_tag
    }
    elseif ($dep.base_tag) {
        [string]$dep.base_tag
    }
    elseif ($dep.release_tag) {
        [string]$dep.release_tag
    }
    else {
        [string]$dep.ref
    }

    $commentParts = [System.Collections.Generic.List[string]]::new()
    if ($dep.redistribution) {
        $commentParts.Add("Redistribution policy: $($dep.redistribution)")
    }
    if ($dep.reason) {
        $commentParts.Add("Policy note: $($dep.reason)")
    }
    if (@($lock.local_only.id) -contains $id) {
        $commentParts.Add("Build input classification: local_only / not a normal public vendored dependency")
    }

    [void]$packages.Add([ordered]@{
        name = $id
        SPDXID = $spdxId
        versionInfo = $version
        downloadLocation = if ($dep.url) { [string]$dep.url } else { "NOASSERTION" }
        filesAnalyzed = $false
        licenseConcluded = "NOASSERTION"
        licenseDeclared = "NOASSERTION"
        copyrightText = "NOASSERTION"
        comment = ($commentParts -join "; ")
        externalRefs = @(
            [ordered]@{
                referenceCategory = "OTHER"
                referenceType = "source-commit"
                referenceLocator = [string]$dep.ref
            }
        )
    })

    [void]$relationships.Add([ordered]@{
        spdxElementId = $appSpdxId
        relationshipType = "DEPENDS_ON"
        relatedSpdxElement = $spdxId
    })
}

$created = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
$namespace = "https://github.com/$Repository/spdx/$Version/$CommitSha"

$sbom = [ordered]@{
    spdxVersion = "SPDX-2.3"
    dataLicense = "CC0-1.0"
    SPDXID = "SPDXRef-DOCUMENT"
    name = "DLSS-NR-Manager-$Version-source-sbom"
    documentNamespace = $namespace
    creationInfo = [ordered]@{
        created = $created
        creators = @("Tool: DLSS NR Manager release workflow")
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

$sbomPath = Join-Path $OutputDirectory "SOURCE-SBOM.spdx.json"
$sbom | ConvertTo-Json -Depth 12 |
    Set-Content -LiteralPath $sbomPath -Encoding UTF8

$provenance = [ordered]@{
    schema = 1
    project = "DLSS NR Manager"
    repository = $Repository
    version = $Version
    commit = $CommitSha
    generated_at_utc = $created
    release_channel = "manager-owned GitHub Releases"
    dependency_lock = [ordered]@{
        path = $DependencyLockPath
        sha256 = Sha256-OrNull $DependencyLockPath
    }
    components_manifest = [ordered]@{
        path = $ComponentsManifestPath
        sha256 = Sha256-OrNull $ComponentsManifestPath
    }
    source_sbom = [ordered]@{
        path = [System.IO.Path]::GetFileName($sbomPath)
        sha256 = Sha256-OrNull $sbomPath
    }
    application_signing = [ordered]@{
        authenticode_signed_by_release_workflow = $false
        verification_note = "Until a project code-signing certificate is configured, verify release provenance through the GitHub release and SHA256SUMS.txt."
    }
}

$provenancePath = Join-Path $OutputDirectory "release-provenance.json"
$provenance | ConvertTo-Json -Depth 10 |
    Set-Content -LiteralPath $provenancePath -Encoding UTF8

Write-Host "Generated: $sbomPath"
Write-Host "Generated: $provenancePath"
Write-Host "SBOM SHA-256: $(Sha256-OrNull $sbomPath)"
Write-Host "Provenance SHA-256: $(Sha256-OrNull $provenancePath)"
