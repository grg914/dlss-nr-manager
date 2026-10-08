param(
    [Parameter(Mandatory=$true)][string]$Repository,
    [Parameter(Mandatory=$true)][string]$CommitSha,
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$ComponentManifestPath,
    [string]$RunId = "",
    [string]$RunAttempt = "",
    [string]$WorkflowRef = "",
    [string]$OutputPath = "release-provenance.json"
)

$ErrorActionPreference = "Stop"

if ($CommitSha -notmatch "^[0-9a-fA-F]{40}$") {
    throw "CommitSha must be a full 40-character Git commit SHA."
}

if (!(Test-Path -LiteralPath $ComponentManifestPath)) {
    throw "Component manifest is missing: $ComponentManifestPath"
}

$manifestSha256 =
    (Get-FileHash -LiteralPath $ComponentManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()

$record = [ordered]@{
    schema = 1
    project = "DLSS NR Manager"
    repository = $Repository
    version = $Version
    source_commit = $CommitSha.ToLowerInvariant()
    build = [ordered]@{
        github_actions_run_id = $RunId
        github_actions_run_attempt = $RunAttempt
        workflow_ref = $WorkflowRef
    }
    production_policy = [ordered]@{
        zero_upstream = $true
        manager_owned_runtime_seed = $true
        offline_nuget_restore = $true
        stable_release_assets_immutable = $true
    }
    component_manifest = [ordered]@{
        file = [System.IO.Path]::GetFileName($ComponentManifestPath)
        sha256 = $manifestSha256
    }
    generated_at_utc = [DateTime]::UtcNow.ToString("o")
}

$destination = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
} else {
    Join-Path $PWD $OutputPath
}

[System.IO.File]::WriteAllText(
    $destination,
    ($record | ConvertTo-Json -Depth 8),
    [System.Text.UTF8Encoding]::new($false))

Get-Content -LiteralPath $destination -Raw | ConvertFrom-Json | Out-Null
Write-Host "Release provenance generated: $destination"
Write-Host "Source commit: $CommitSha"
Write-Host "Component manifest SHA-256: $manifestSha256"
