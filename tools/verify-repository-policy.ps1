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
$workflowFiles = @(Get-ChildItem -LiteralPath $workflowRoot -File -Include *.yml,*.yaml)
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

$releaseWorkflow = Get-Content (Join-Path $Root ".github/workflows/release.yml") -Raw
foreach ($requiredReleaseMarker in @(
    "SHA256SUMS.txt",
    "components-manifest.json",
    "release-provenance.json"
)) {
    if ($releaseWorkflow -notmatch [regex]::Escape($requiredReleaseMarker)) {
        Fail "Release workflow is missing required provenance marker '$requiredReleaseMarker'."
    }
}

$tracked = @()
git -C $Root ls-files -z | ForEach-Object {
    if ($_ -is [string]) {
        $tracked += ($_ -split [char]0 | Where-Object { $_ })
    }
}
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
