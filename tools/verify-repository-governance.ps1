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
    "third_party/README.md",
    "third_party/DEPENDENCIES.lock.json",
    "third_party/UPSTREAMS.json",
    "manifests/component-policy.json"
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
    "manifests/component-policy.json"
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

Write-Host "Repository governance validation passed."
Write-Host "Required policy files: $($required.Count)"
Write-Host "Component policy entries: $($components.Count)"
Write-Host "Workflows audited: $($workflows.Count)"
