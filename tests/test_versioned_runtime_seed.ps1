$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$publisher = Join-Path $root "tools/publish-versioned-runtime-seed.ps1"
$temporary = Join-Path ([IO.Path]::GetTempPath()) ("dlssnr-versioned-asset-test-" + [Guid]::NewGuid().ToString("N"))
$folder = Join-Path $temporary "zip-contents"
$package = Join-Path $temporary "OptiScaler-NR-v1.2.3-vendored-win-x64.zip"
$global:FakeAssets = @()
$global:FakeUploadCount = 0
$global:FakeHidePublished = $false
$global:FakeBadDigestOnUpload = $false
$global:FakeRejectUpload = $false

function Assert-True([bool]$Condition, [string]$Why) {
    if (-not $Condition) { throw "FAILED: $Why" }
}
function Expect-Failure([scriptblock]$Operation, [string]$Expected) {
    $actual = ""
    try { & $Operation } catch { $actual = $_.Exception.Message }
    if ($actual -notlike "*$Expected*") { throw "Expected '$Expected' error, observed '$actual'." }
}
function New-Package([string]$Version) {
    Set-Content -LiteralPath (Join-Path $folder "OptiScaler.dll") -Value "runtime-$Version"
    Remove-Item -LiteralPath $package -Force -ErrorAction SilentlyContinue
    [IO.Compression.ZipFile]::CreateFromDirectory($folder, $package)
}
function global:gh {
    $global:LASTEXITCODE = 0
    $argv = @($args)
    if ($argv[0] -eq "api") {
        $assets = if ($global:FakeHidePublished) { @() } else { @($global:FakeAssets) }
        return (ConvertTo-Json -InputObject @{ draft = $false; assets = $assets } -Depth 7 -Compress)
    }
    if ($argv[0] -eq "release" -and $argv[1] -eq "upload") {
        if ($global:FakeRejectUpload) { $global:LASTEXITCODE = 1; return }
        $file = [string]$argv[3]
        $name = [IO.Path]::GetFileName($file)
        if (@($global:FakeAssets | Where-Object { $_.name -eq $name }).Count -gt 0) {
            throw "Mock GitHub refused destructive duplicate publication."
        }
        $sha = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($global:FakeBadDigestOnUpload) { $sha = 'f' * 64 }
        $global:FakeAssets += [pscustomobject]@{
            name = $name
            digest = "sha256:$sha"
            size = (Get-Item -LiteralPath $file).Length
        }
        $global:FakeUploadCount++
        return
    }
    throw "Unexpected GitHub CLI command: $($argv -join ' ')"
}

try {
    New-Item -Path $folder -ItemType Directory -Force | Out-Null
    New-Package "initial"
    & $publisher -Path $package -Repository "grg914/dlss-nr-manager"
    Assert-True ($global:FakeUploadCount -eq 1) "First upload did not occur."
    & $publisher -Path $package -Repository "grg914/dlss-nr-manager"
    Assert-True ($global:FakeUploadCount -eq 1) "Unchanged asset was uploaded twice."

    New-Package "changed"
    Expect-Failure { & $publisher -Path $package -Repository "grg914/dlss-nr-manager" } "conflicting or unverifiable"
    Assert-True ($global:FakeUploadCount -eq 1) "Conflicting version overwrote healthy asset."

    # Only an explicitly opted-in producer may stage different same-version
    # native bytes. The existing canonical asset must remain untouched.
    $canonicalDigest = $global:FakeAssets[0].digest
    $canonicalSize = $global:FakeAssets[0].size
    $changedHash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
    $expectedStage = "OptiScaler-NR-v1.2.3-vendored-win-x64.sha256-$changedHash.zip"
    & $publisher -Path $package -Repository "grg914/dlss-nr-manager" -OnChanged StageImmutable
    Assert-True ($global:FakeUploadCount -eq 2) "Different OptiScaler build was not staged separately."
    Assert-True ($global:FakeAssets[1].name -ceq $expectedStage) "Staged revision name is not content-addressed."
    Assert-True ($global:FakeAssets[0].digest -ceq $canonicalDigest -and
        $global:FakeAssets[0].size -eq $canonicalSize) "Canonical asset was mutated."
    & $publisher -Path $package -Repository "grg914/dlss-nr-manager" -OnChanged StageImmutable
    Assert-True ($global:FakeUploadCount -eq 2) "Identical staged revision was uploaded twice."

    # Even the opted-in staging mode must fail if canonical GitHub metadata
    # cannot establish the original binary's integrity.
    $global:FakeAssets[0].digest = $null
    Expect-Failure { & $publisher -Path $package -Repository "grg914/dlss-nr-manager" -OnChanged StageImmutable } "conflicting or unverifiable"
    Assert-True ($global:FakeUploadCount -eq 2) "Unverifiable canonical metadata allowed a staged upload."
    $global:FakeAssets[0].digest = $canonicalDigest

    # Missing expected publication: remote metadata never confirms upload.
    $global:FakeAssets = @()
    $global:FakeHidePublished = $true
    Expect-Failure { & $publisher -Path $package -Repository "grg914/dlss-nr-manager" } "absent from release metadata"
    $global:FakeHidePublished = $false

    # Wrong GitHub SHA-256 must not be accepted after a successful upload.
    $global:FakeAssets = @()
    $global:FakeBadDigestOnUpload = $true
    Expect-Failure { & $publisher -Path $package -Repository "grg914/dlss-nr-manager" } "conflicting or unverifiable"
    $global:FakeBadDigestOnUpload = $false

    # A failed upload must never remove or replace existing assets.
    $global:FakeAssets = @()
    $global:FakeRejectUpload = $true
    Expect-Failure { & $publisher -Path $package -Repository "grg914/dlss-nr-manager" } "upload failed"
    Assert-True (@($global:FakeAssets).Count -eq 0) "Failed upload mutated asset list."
    $global:FakeRejectUpload = $false

    # Broken local archive is rejected before any GitHub API or mutation.
    Set-Content -LiteralPath $package -Value "corrupt-not-zip"
    Expect-Failure { & $publisher -Path $package -Repository "grg914/dlss-nr-manager" } "ZIP validation failed"

    # A second approved versioned component exercises the other filename.
    $package = Join-Path $temporary "ReShade-Setup-v7.0-dev-vendored.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($folder, $package)
    & $publisher -Path $package -Repository "grg914/dlss-nr-manager"
    Assert-True (@($global:FakeAssets).Count -eq 1) "ReShade package was not published."

    Write-Host "PASS: canonical idempotence, immutable stage/idempotence, corrupted metadata rejection, failed upload, corrupt ZIP, both names."
}
finally {
    Remove-Item -LiteralPath $temporary -Force -Recurse -ErrorAction SilentlyContinue
    Remove-Item Function:\gh -ErrorAction SilentlyContinue
}
