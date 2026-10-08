param(
    [Parameter(Mandatory=$true)][string]$Repository,
    [Parameter(Mandatory=$true)][string]$CommitSha,
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [Parameter(Mandatory=$true)][string[]]$Files
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

if ($CommitSha -notmatch "^[0-9a-fA-F]{40}$") {
    throw "CommitSha must be an immutable 40-character Git commit SHA."
}

$assets = @()
foreach ($file in $Files) {
    if ([string]::IsNullOrWhiteSpace($file)) { continue }

    $resolved = Resolve-Path -LiteralPath $file -ErrorAction Stop
    $item = Get-Item -LiteralPath $resolved

    if ($item.PSIsContainer) {
        throw "Release provenance input must be a file, not a directory: $file"
    }

    $assets += [ordered]@{
        name = $item.Name
        size = [int64]$item.Length
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$duplicateNames = @($assets | Group-Object name | Where-Object Count -gt 1)
if ($duplicateNames.Count -gt 0) {
    throw "Duplicate release asset names in provenance input: $($duplicateNames.Name -join ', ')"
}

$dependencyLockPath = Join-Path $Root "third_party/DEPENDENCIES.lock.json"
$upstreamPolicyPath = Join-Path $Root "third_party/UPSTREAMS.json"
$minecraftLockPath = Join-Path $Root "third_party/minecraft/RUNTIME.lock.json"

$dependencyLock = Get-Content -LiteralPath $dependencyLockPath -Raw | ConvertFrom-Json
$dependencies = @(
    foreach ($entry in @($dependencyLock.sources) + @($dependencyLock.local_only)) {
        [ordered]@{
            id = [string]$entry.id
            group = if ($entry.group) { [string]$entry.group } elseif (@($dependencyLock.local_only) -contains $entry) { "local-only" } else { "core" }
            ref = [string]$entry.ref
            path = [string]$entry.path
        }
    }
)

$document = [ordered]@{
    schema = 1
    generated_at_utc = [DateTime]::UtcNow.ToString("o")
    repository = $Repository
    source_commit = $CommitSha.ToLowerInvariant()
    version = $Version
    policy = [ordered]@{
        dependency_lock_sha256 = (Get-FileHash -LiteralPath $dependencyLockPath -Algorithm SHA256).Hash.ToLowerInvariant()
        upstream_policy_sha256 = (Get-FileHash -LiteralPath $upstreamPolicyPath -Algorithm SHA256).Hash.ToLowerInvariant()
        minecraft_runtime_lock_sha256 = if (Test-Path -LiteralPath $minecraftLockPath) {
            (Get-FileHash -LiteralPath $minecraftLockPath -Algorithm SHA256).Hash.ToLowerInvariant()
        } else { $null }
    }
    dependencies = $dependencies | Sort-Object id
    assets = $assets | Sort-Object name
}

$outputFullPath = [System.IO.Path]::GetFullPath((Join-Path $Root $OutputPath))
$outputDirectory = Split-Path -Parent $outputFullPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$json = $document | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText(
    $outputFullPath,
    $json + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false)
)

Write-Host "Release provenance written: $outputFullPath"
Write-Host "Assets recorded: $($assets.Count)"
