param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"

$required = @(
    "LICENSE",
    "README.md",
    "SECURITY.md",
    "CONTRIBUTING.md",
    "docs/THIRD_PARTY_NOTICES.md",
    "docs/DATA_PRIVACY.md",
    "docs/RELEASE_ARTIFACT_POLICY.md",
    "docs/AI_RUNTIME_SECURITY.md",
    "docs/CODE_SIGNING_POLICY.md",
    "docs/PC_MAINTENANCE_SAFETY.md",
    "docs/BACKUP_ROLLBACK_POLICY.md",
    "docs/FEATURE_POLICY_MATRIX.md",
    "docs/GITHUB_REPOSITORY_SETTINGS.md",
    "third_party/README.md",
    "third_party/DEPENDENCIES.lock.json",
    "third_party/UPSTREAMS.json",
    "manifests/component-policy.json",
    "manifests/feature-policy.json"
)

$missing = @()
foreach ($relative in $required) {
    $path = Join-Path $Root $relative
    if (!(Test-Path -LiteralPath $path)) {
        $missing += $relative
        continue
    }

    if ((Get-Item -LiteralPath $path).Length -eq 0) {
        throw "Required governance file is empty: $relative"
    }
}

if ($missing.Count -gt 0) {
    throw "Missing required governance files: $($missing -join ', ')"
}

foreach ($relative in @(
    "third_party/DEPENDENCIES.lock.json",
    "third_party/UPSTREAMS.json",
    "manifests/component-policy.json",
    "manifests/feature-policy.json"
)) {
    $path = Join-Path $Root $relative
    try {
        Get-Content -LiteralPath $path -Raw | ConvertFrom-Json | Out-Null
    }
    catch {
        throw "Invalid JSON in $($relative): $($_.Exception.Message)"
    }
}

$componentPolicy = Get-Content -LiteralPath (Join-Path $Root "manifests/component-policy.json") -Raw | ConvertFrom-Json
if ([int]$componentPolicy.schema -ne 1) {
    throw "Unsupported manifests/component-policy.json schema."
}

$components = @($componentPolicy.components)
$duplicateIds = @($components | Group-Object id | Where-Object Count -gt 1)
if ($duplicateIds.Count -gt 0) {
    throw "Duplicate component-policy ids: $($duplicateIds.Name -join ', ')"
}

foreach ($component in $components) {
    if ([string]::IsNullOrWhiteSpace([string]$component.id) -or
        [string]::IsNullOrWhiteSpace([string]$component.category) -or
        [string]::IsNullOrWhiteSpace([string]$component.distribution) -or
        [string]::IsNullOrWhiteSpace([string]$component.integrity) -or
        [string]::IsNullOrWhiteSpace([string]$component.status)) {
        throw "Incomplete component-policy entry detected."
    }
}

$featurePolicy = Get-Content -LiteralPath (Join-Path $Root "manifests/feature-policy.json") -Raw | ConvertFrom-Json
if ([int]$featurePolicy.schema -ne 1) {
    throw "Unsupported manifests/feature-policy.json schema."
}

$features = @($featurePolicy.features)
$duplicateFeatureIds = @($features | Group-Object id | Where-Object Count -gt 1)
if ($duplicateFeatureIds.Count -gt 0) {
    throw "Duplicate feature-policy ids: $($duplicateFeatureIds.Name -join ', ')"
}

foreach ($feature in $features) {
    foreach ($field in @("id", "ui_en", "ui_fr", "status", "network", "mutations", "integrity", "rollback", "canonical_download_surface")) {
        $property = $feature.PSObject.Properties[$field]
        if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            throw "Incomplete feature-policy entry '$($feature.id)': missing $field."
        }
    }
}

$workflowRoot = Join-Path $Root ".github\workflows"
$workflows = @(Get-ChildItem -LiteralPath $workflowRoot -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in @(".yml", ".yaml") })

foreach ($workflow in $workflows) {
    $raw = Get-Content -LiteralPath $workflow.FullName -Raw

    if ($raw -notmatch "(?m)^permissions:\s*$") {
        throw "Workflow has no explicit top-level permissions block: $($workflow.Name)"
    }

    foreach ($match in [regex]::Matches($raw, "uses:\s*([^\s#]+)")) {
        $uses = [string]$match.Groups[1].Value

        if ($uses.StartsWith("./")) {
            continue
        }

        if ($uses -notmatch "@[0-9a-fA-F]{40}$") {
            throw "Workflow action is not pinned to a full commit SHA in $($workflow.Name): $uses"
        }
    }
}

$runtimeRefreshPath = Join-Path $Root ".github\workflows\runtime-refresh.yml"
$runtimeRefresh = Get-Content -LiteralPath $runtimeRefreshPath -Raw
$dispatchJob = [regex]::Match(
    $runtimeRefresh,
    "(?ms)^  dispatch-release:\s*.*?(?=^  [A-Za-z0-9_-]+:\s*$|\z)"
).Value

if ([string]::IsNullOrWhiteSpace($dispatchJob)) {
    throw "runtime-refresh.yml has no dispatch-release job."
}

$checkoutMatch = [regex]::Match(
    $dispatchJob,
    "uses:\s*actions/checkout@[0-9a-fA-F]{40}"
)
$helperMatch = [regex]::Match(
    $dispatchJob,
    "bash\s+\.\/tools\/dispatch-release-if-needed\.sh"
)

if (-not $checkoutMatch.Success) {
    throw "runtime-refresh dispatch-release must checkout the repository with a full-SHA-pinned actions/checkout before invoking the local helper."
}
if (-not $helperMatch.Success) {
    throw "runtime-refresh dispatch-release does not invoke tools/dispatch-release-if-needed.sh."
}
if ($checkoutMatch.Index -gt $helperMatch.Index) {
    throw "runtime-refresh dispatch-release checkout must occur before the local dispatch helper is invoked."
}

$releaseWorkflowPath = Join-Path $Root ".github\workflows\release.yml"
$releaseWorkflow = Get-Content -LiteralPath $releaseWorkflowPath -Raw
$dispatchHelperPath = Join-Path $Root "tools\dispatch-release-if-needed.sh"
$dispatchHelper = Get-Content -LiteralPath $dispatchHelperPath -Raw

if ($releaseWorkflow -notmatch "(?ms)^\s*workflow_dispatch:\s*\r?\n\s*inputs:\s*\r?\n\s*source_sha:") {
    throw "release.yml must expose workflow_dispatch input source_sha for exact-source automated releases."
}

if ($releaseWorkflow -notmatch "id:\s*source" -or
    $releaseWorkflow -notmatch "steps\.source\.outputs\.sha") {
    throw "release.yml must resolve and consume an exact source SHA."
}

if ($releaseWorkflow -notmatch 'git/ref/heads/main' -or
    $releaseWorkflow -notmatch 'Release source .* is stale; current main is' -or
    $releaseWorkflow -notmatch 'Reconfirm release source before publication') {
    throw "release.yml must fail closed unless workflow_dispatch source_sha remains the current main SHA through publication."
}

if ($releaseWorkflow -notmatch 'git fetch --no-tags --prune --depth=1 origin "\$target"') {
    throw "release.yml must fetch the exact resolved release source SHA."
}

if ($releaseWorkflow -notmatch 'gh release create \$tag .*--target "\$\{\{ steps\.source\.outputs\.sha \}\}"') {
    throw "release.yml must create a new stable release against the exact resolved source SHA."
}

if ($releaseWorkflow -notmatch '-SourceCommit "\$\{\{ steps\.source\.outputs\.sha \}\}"' -or
    $releaseWorkflow -notmatch '-CommitSha "\$\{\{ steps\.source\.outputs\.sha \}\}"') {
    throw "release.yml SBOM and provenance generation must use the exact resolved source SHA."
}

if ($dispatchHelper -notmatch 'git/ref/heads/main' -or
    $dispatchHelper -notmatch 'main_sha.*source_sha' -or
    $dispatchHelper -notmatch 'gh workflow run release\.yml --repo "\$repo" --ref main -f "source_sha=\$source_sha"') {
    throw "dispatch-release-if-needed.sh must reject stale main state and pass the validated source_sha into release.yml."
}

Write-Host "Repository governance validation passed."
Write-Host "Required policy files: $($required.Count)"
Write-Host "Component policy entries: $($components.Count)"
Write-Host "Feature policy entries: $($features.Count)"
Write-Host "Workflows audited: $($workflows.Count)"
