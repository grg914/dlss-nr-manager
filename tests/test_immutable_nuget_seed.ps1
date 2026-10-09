$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$publisher = Join-Path $repoRoot "tools/publish-immutable-nuget-seed.ps1"
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("dlssnr-nuget-publish-test-" + [Guid]::NewGuid().ToString("N"))
$sourceDir = Join-Path $testRoot "source"
$zipPath = Join-Path $testRoot "nuget-offline.zip"

function Assert-Equal {
    param($Expected, $Actual, [string]$Description)
    if ($Expected -ne $Actual) {
        throw "$Description expected '$Expected', got '$Actual'."
    }
}

function Expect-Failure {
    param([scriptblock]$Action, [string]$MessageFragment)
    $caught = $null
    try { & $Action }
    catch { $caught = $_.Exception.Message }
    if (-not $caught -or -not $caught.Contains($MessageFragment)) {
        throw "Expected '$MessageFragment' failure; actual: '$caught'."
    }
}

function New-TestZip {
    param([string]$Revision)
    Set-Content -LiteralPath (Join-Path $sourceDir "fixture.1.0.nupkg") -Value "package-$Revision"
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    [IO.Compression.ZipFile]::CreateFromDirectory($sourceDir, $zipPath)
}

# Fake GitHub CLI: no network access and no release mutation.
$global:FakeReleaseAssets = @()
$global:FakeUploadNames = @()
$global:CorruptNextUpload = $false
function global:gh {
    $words = @($args)
    $global:LASTEXITCODE = 0
    if ($words.Count -gt 0 -and $words[0] -eq "api") {
        return (ConvertTo-Json -InputObject @{ draft = $false; assets = @($global:FakeReleaseAssets) } -Depth 6 -Compress)
    }
    if ($words.Count -ge 4 -and $words[0] -eq "release" -and $words[1] -eq "upload") {
        $file = [string]$words[3]
        $name = [IO.Path]::GetFileName($file)
        if (@($global:FakeReleaseAssets | Where-Object { $_.name -eq $name }).Count -ne 0) {
            throw "Mock release asset already exists; publisher attempted destructive reuse: $name"
        }
        $sha = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        $digest = if ($global:CorruptNextUpload) { "sha256:$('0' * 64)" } else { "sha256:$sha" }
        $global:CorruptNextUpload = $false
        $global:FakeReleaseAssets += [pscustomobject]@{
            id = 1000 + $global:FakeReleaseAssets.Count
            name = $name
            size = (Get-Item -LiteralPath $file).Length
            digest = $digest
        }
        $global:FakeUploadNames += $name
        return
    }
    throw "Unexpected fake gh command: $($words -join ' ')"
}

try {
    New-Item -ItemType Directory -Path $sourceDir -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $sourceDir "manifest.json") -Value '{"schema":2,"packages":[]}'
    $env:RUNNER_TEMP = $testRoot

    # The first upload installs the canonical asset without clobber.
    New-TestZip "v1"
    $oldSha = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    & $publisher -Path $zipPath -Repository "grg914/dlss-nr-manager"
    Assert-Equal 1 $global:FakeUploadNames.Count "Canonical upload count"
    Assert-Equal "nuget-offline.zip" $global:FakeUploadNames[0] "Canonical name"
    Assert-Equal "sha256:$oldSha" $global:FakeReleaseAssets[0].digest "Canonical digest"

    # Idempotent publish must not mutate the existing release.
    & $publisher -Path $zipPath -Repository "grg914/dlss-nr-manager"
    Assert-Equal 1 $global:FakeUploadNames.Count "Idempotent upload count"

    # Changed content is staged with a full content-addressed name.
    New-TestZip "v2"
    $newSha = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    & $publisher -Path $zipPath -Repository "grg914/dlss-nr-manager"
    Assert-Equal 2 $global:FakeUploadNames.Count "Immutable upload count"
    Assert-Equal "nuget-offline.sha256-$newSha.zip" $global:FakeUploadNames[1] "Immutable name"
    Assert-Equal "sha256:$oldSha" $global:FakeReleaseAssets[0].digest "Pinned canonical unchanged"
    & $publisher -Path $zipPath -Repository "grg914/dlss-nr-manager"
    Assert-Equal 2 $global:FakeUploadNames.Count "Previously staged asset idempotence"

    # Tampered immutable metadata must be rejected, not overwritten.
    $global:FakeReleaseAssets[1].digest = "sha256:$('f' * 64)"
    Expect-Failure { & $publisher -Path $zipPath -Repository "grg914/dlss-nr-manager" } "conflicting content"
    Assert-Equal 2 $global:FakeUploadNames.Count "No overwrite after conflicting digest"

    # Broken input is rejected before attempting an upload.
    Remove-Item -LiteralPath (Join-Path $sourceDir "manifest.json")
    New-TestZip "invalid"
    Expect-Failure { & $publisher -Path $zipPath -Repository "grg914/dlss-nr-manager" } "NuGet seed ZIP validation failed"
    Assert-Equal 2 $global:FakeUploadNames.Count "No upload of invalid ZIP"

    Write-Host "PASS: canonical, idempotence, SHA-256 staging, collision, malformed ZIP."
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item Function:\gh -ErrorAction SilentlyContinue
}
