$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$publisher = Join-Path $root "tools/publish-append-only-release-zip.ps1"
$temp = Join-Path ([IO.Path]::GetTempPath()) ("dlssnr-video-release-test-" + [Guid]::NewGuid().ToString("N"))
$files = Join-Path $temp "contents"
$zip = Join-Path $temp "video2dlssnr_release.zip"
$global:FakeAssets = @()
$global:Uploads = 0
$global:RejectUpload = $false
$global:HideAfterUpload = $false

function Assert-True([bool]$Value, [string]$Message) {
    if (-not $Value) { throw "Assertion failed: $Message" }
}
function Expect-Failure([scriptblock]$Action, [string]$Substring) {
    $failure = ""
    try { & $Action } catch { $failure = $_.Exception.Message }
    if (-not $failure.Contains($Substring)) {
        throw "Expected error '$Substring', got '$failure'"
    }
}
function Make-Zip([string]$Revision) {
    Set-Content -LiteralPath (Join-Path $files "video.exe") -Value "video-$Revision"
    Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
    [IO.Compression.ZipFile]::CreateFromDirectory($files, $zip)
}
function global:gh {
    $argv = @($args)
    $global:LASTEXITCODE = 0
    if ($argv[0] -eq "api") {
        $assets = if ($global:HideAfterUpload) { @() } else { @($global:FakeAssets) }
        return (ConvertTo-Json -InputObject @{ draft = $false; assets = $assets } -Depth 6 -Compress)
    }
    if ($argv[0] -eq "release" -and $argv[1] -eq "upload") {
        if ($global:RejectUpload) { $global:LASTEXITCODE = 1; return }
        $path = [string]$argv[3]
        $name = [IO.Path]::GetFileName($path)
        if (@($global:FakeAssets | Where-Object { $_.name -eq $name }).Count -gt 0) {
            throw "Mock refused overwrite of existing release asset"
        }
        $global:FakeAssets += [pscustomobject]@{
            name = $name
            size = (Get-Item -LiteralPath $path).Length
            digest = "sha256:" + (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        $global:Uploads++
        return
    }
    throw "Unexpected fake gh command: $($argv -join ' ')"
}

try {
    New-Item -Path $files -ItemType Directory -Force | Out-Null
    Make-Zip "a"
    $old = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    & $publisher -Path $zip -Repository "grg914/dlss-nr-manager" -ReleaseTag "runtime-seed-v1" -MinimumSizeBytes 1
    Assert-True ($global:Uploads -eq 1) "Initial canonical upload"
    Assert-True ($global:FakeAssets[0].name -eq "video2dlssnr_release.zip") "Canonical name"
    & $publisher -Path $zip -Repository "grg914/dlss-nr-manager" -ReleaseTag "runtime-seed-v1" -MinimumSizeBytes 1
    Assert-True ($global:Uploads -eq 1) "Idempotence"

    Make-Zip "b"
    $new = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    & $publisher -Path $zip -Repository "grg914/dlss-nr-manager" -ReleaseTag "runtime-seed-v1" -MinimumSizeBytes 1
    Assert-True ($global:Uploads -eq 2) "New immutable revision"
    Assert-True ($global:FakeAssets[1].name -eq "video2dlssnr_release.sha256-$new.zip") "Content-addressed filename"
    Assert-True ($global:FakeAssets[0].digest -eq "sha256:$old") "Canonical content preserved"
    & $publisher -Path $zip -Repository "grg914/dlss-nr-manager" -ReleaseTag "runtime-seed-v1" -MinimumSizeBytes 1
    Assert-True ($global:Uploads -eq 2) "Immutable revision idempotence"

    $global:FakeAssets[1].digest = "sha256:$('f' * 64)"
    Expect-Failure { & $publisher -Path $zip -Repository "grg914/dlss-nr-manager" -ReleaseTag "runtime-seed-v1" -MinimumSizeBytes 1 } "conflicting content"
    Assert-True ($global:Uploads -eq 2) "Never overwrite conflicting content"

    # A failed upload preserves the canonical asset.
    $global:FakeAssets = @($global:FakeAssets[0])
    $global:RejectUpload = $true
    Expect-Failure { & $publisher -Path $zip -Repository "grg914/dlss-nr-manager" -ReleaseTag "runtime-seed-v1" -MinimumSizeBytes 1 } "upload failed"
    Assert-True ($global:FakeAssets.Count -eq 1) "Failed upload retains canonical"
    $global:RejectUpload = $false

    # Missing uploaded asset in a release snapshot must fail closed.
    $global:FakeAssets = @()
    $global:HideAfterUpload = $true
    Expect-Failure { & $publisher -Path $zip -Repository "grg914/dlss-nr-manager" -ReleaseTag "runtime-seed-v1" -MinimumSizeBytes 1 } "Published release asset missing"
    $global:HideAfterUpload = $false

    Set-Content -LiteralPath $zip -Value "not a ZIP"
    Expect-Failure { & $publisher -Path $zip -Repository "grg914/dlss-nr-manager" -ReleaseTag "runtime-seed-v1" -MinimumSizeBytes 1 } "ZIP integrity validation failed"
    Write-Host "PASS: canonical, immutable, idempotence, conflict, failure, missing metadata, corruption."
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item Function:\gh -ErrorAction SilentlyContinue
}
