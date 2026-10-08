param(
    [switch]$Strict
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Fail([string]$Message) {
    throw $Message
}

function Require-File([string]$RelativePath) {
    $path = Join-Path $Root $RelativePath
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        Fail "Required repository policy file is missing: $RelativePath"
    }
}

$requiredFiles = @(
    "LICENSE",
    "SECURITY.md",
    "CONTRIBUTING.md",
    "THIRD_PARTY_NOTICES.md",
    "SOURCE_AVAILABILITY.md",
    "docs/PRIVACY_AND_DATA.md",
    "docs/DOWNLOAD_RUNTIME_POLICY.md",
    "docs/RELEASE_SECURITY.md",
    "docs/THREAT_MODEL.md",
    "docs/DEPENDENCY_UPDATE_POLICY.md",
    "manifests/component-policy.json",
    ".github/CODEOWNERS",
    ".github/pull_request_template.md",
    "third_party/DEPENDENCIES.lock.json",
    "third_party/UPSTREAMS.json",
    "tools/verify-self-contained.ps1",
    ".github/workflows/build.yml",
    ".github/workflows/release.yml"
)

$requiredFiles | ForEach-Object { Require-File $_ }

$license = Get-Content (Join-Path $Root "LICENSE") -Raw
if ($license -notmatch "(?m)^MIT License\s*$") {
    Fail "Root LICENSE is not the expected MIT license."
}

$gitignore = Get-Content (Join-Path $Root ".gitignore") -Raw
foreach ($requiredIgnore in @("build-local/", "third_party-local/")) {
    if ($gitignore -notmatch [regex]::Escape($requiredIgnore)) {
        Fail ".gitignore must contain '$requiredIgnore'."
    }
}

$lock = Get-Content (Join-Path $Root "third_party/DEPENDENCIES.lock.json") -Raw | ConvertFrom-Json
$allDependencies = @($lock.sources) + @($lock.local_only)
$duplicateIds = @($allDependencies | Group-Object id | Where-Object Count -gt 1)
if ($duplicateIds.Count -gt 0) {
    Fail "Duplicate dependency ids: $($duplicateIds.Name -join ', ')"
}

foreach ($dependency in $allDependencies) {
    $ref = [string]$dependency.ref
    if ($ref -notmatch "^[0-9a-fA-F]{40}$") {
        Fail "Dependency '$($dependency.id)' is not pinned to an immutable 40-character commit SHA: '$ref'."
    }
}

$workflowRoot = Join-Path $Root ".github/workflows"
$workflowFiles = @(
    Get-ChildItem -LiteralPath $workflowRoot -File |
        Where-Object { $_.Extension -in @(".yml", ".yaml") }
)
foreach ($workflow in $workflowFiles) {
    $lines = Get-Content -LiteralPath $workflow.FullName
    foreach ($line in $lines) {
        if ($line -match "^\s*-?\s*uses:\s*([^\s#]+)") {
            $uses = $matches[1]

            if ($uses.StartsWith("./") -or $uses.StartsWith("docker://")) {
                continue
            }

            if ($uses -notmatch "@([0-9a-fA-F]{40})$") {
                Fail "GitHub Action is not pinned to an immutable 40-character commit SHA: $($workflow.Name): $uses"
            }
        }
    }
}

[xml]$project = Get-Content (Join-Path $Root "DlssNrManager.csproj")
$projectVersion = [string]$project.Project.PropertyGroup.Version
$readme = Get-Content (Join-Path $Root "README.md") -Raw
$versionMatch = [regex]::Match($readme, 'Current application version:\s*\*\*v([^*]+)\*\*')
if (-not $versionMatch.Success) {
    Fail "README.md has no parsable current application version."
}
if ($versionMatch.Groups[1].Value.Trim() -ne $projectVersion.Trim()) {
    Fail "README/project version mismatch. README v$($versionMatch.Groups[1].Value), project v$projectVersion."
}

$componentPolicy = Get-Content (Join-Path $Root "manifests/component-policy.json") -Raw | ConvertFrom-Json
if ([int]$componentPolicy.schema -ne 1) {
    Fail "Unsupported component-policy schema."
}
$componentIds = @($componentPolicy.components | ForEach-Object { [string]$_.id })
$componentDuplicates = @($componentIds | Group-Object | Where-Object Count -gt 1)
if ($componentDuplicates.Count -gt 0) {
    Fail "Duplicate component policy ids: $($componentDuplicates.Name -join ', ')"
}
foreach ($component in @($componentPolicy.components)) {
    foreach ($field in @("id", "category", "distribution", "source_policy", "license", "runtime_source")) {
        if ([string]::IsNullOrWhiteSpace([string]$component.$field)) {
            Fail "Component policy entry '$($component.id)' is missing '$field'."
        }
    }
}

foreach ($workflowPath in @(
    ".github/workflows/media-vendor.yml",
    ".github/workflows/runtime-refresh.yml"
)) {
    $workflowText = Get-Content (Join-Path $Root $workflowPath) -Raw
    if ($workflowText -notmatch "(?m)\bxz-utils\b") {
        Fail "$workflowPath must install xz-utils for XZ/tarball handling."
    }
}

$releaseWorkflow = Get-Content (Join-Path $Root ".github/workflows/release.yml") -Raw
foreach ($requiredReleaseMarker in @(
    "SHA256SUMS.txt",
    "components-manifest.json",
    "release-provenance.json",
    "THIRD_PARTY_NOTICES.md",
    "LICENSE",
    "component-policy.json"
)) {
    if ($releaseWorkflow -notmatch [regex]::Escape($requiredReleaseMarker)) {
        Fail "Release workflow is missing required provenance marker '$requiredReleaseMarker'."
    }
}

$tracked = @(git -C $Root ls-files)
if ($LASTEXITCODE -ne 0) {
    Fail "Unable to enumerate tracked files."
}

$forbiddenTracked = @(
    ".env",
    ".env.local",
    ".env.production"
)

foreach ($path in $tracked) {
    $normalized = $path.Replace("\", "/")

    if ($normalized.StartsWith("third_party-local/", [StringComparison]::OrdinalIgnoreCase)) {
        Fail "License-restricted local-only content is tracked by Git: $path"
    }

    $leaf = [System.IO.Path]::GetFileName($normalized)
    if ($forbiddenTracked -contains $leaf -or
        $leaf -match "(?i)\.(pfx|p12|pem|key)$") {
        Fail "Potential secret/private-key material is tracked by Git: $path"
    }
}

if ($Strict) {
    $readme = Get-Content (Join-Path $Root "README.md") -Raw
    foreach ($policy in @(
        "SECURITY.md",
        "THIRD_PARTY_NOTICES.md",
        "SOURCE_AVAILABILITY.md",
        "docs/PRIVACY_AND_DATA.md",
        "docs/DOWNLOAD_RUNTIME_POLICY.md",
        "docs/RELEASE_SECURITY.md",
        "docs/THREAT_MODEL.md",
        "docs/DEPENDENCY_UPDATE_POLICY.md",
        "manifests/component-policy.json"
    )) {
        if ($readme -notmatch [regex]::Escape($policy)) {
            Fail "README.md does not link required policy document '$policy'."
        }
    }
}

Write-Host "Repository governance/security policy audit passed."
