$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$publisher = Join-Path $root "tools/publish-append-only-runtime-seed.ps1"
$temp = Join-Path ([IO.Path]::GetTempPath()) ("dlssnr-append-seed-test-" + [Guid]::NewGuid().ToString("N"))
$folder = Join-Path $temp "archive"
$zip = Join-Path $temp "temurin-25-jre-win-x64.zip"
$meta = Join-Path $temp "temurin-25-jre.json"
$global:FakeAssets = @()
$global:Uploads = 0
$global:RejectUpload = $false
$global:HidePublished = $false

function Assert-True([bool]$Value, [string]$Message) {
    if (-not $Value) { throw "Assertion failed: $Message" }
}
function Expect-Failure([scriptblock]$Action, [string]$Fragment) {
    $caught = ""
    try { & $Action } catch { $caught = $_.Exception.Message }
    if (-not $caught.Contains($Fragment)) {
        throw "Expected error '$Fragment', got '$caught'."
    }
}
function Make-Zip([string]$Revision) {
    Set-Content -LiteralPath (Join-Path $folder "bin.exe") -Value "binary-$Revision"
    Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
    [IO.Compression.ZipFile]::CreateFromDirectory($folder, $zip)
}
function global:gh {
    $argv = @($args)
    $global:LASTEXITCODE = 0
    if ($argv[0] -eq "api") {
        $assets = if ($global:HidePublished) { @() } else { @($global:FakeAssets) }
        return (ConvertTo-Json -InputObject @{ draft = $false; assets = $assets } -Depth 7 -Compress)
    }
    if ($argv[0] -eq "release" -and $argv[1] -eq "upload") {
        if ($global:RejectUpload) { $global:LASTEXITCODE = 1; return }
        $path = [string]$argv[3]
        $name = [IO.Path]::GetFileName($path)
        if (@($global:FakeAssets | Where-Object { $_.name -eq $name }).Count) {
            throw "Mock release refuses replacement."
        }
        $global:FakeAssets += [pscustomobject]@{
            name = $name
            digest = "sha256:" + (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            size = (Get-Item -LiteralPath $path).Length
        }
        $global:Uploads++
        return
    }
    throw "Unexpected mocked gh command: $($argv -join ' ')"
}
try {
    New-Item -Path $folder -ItemType Directory -Force | Out-Null
    $env:RUNNER_TEMP = $temp
    Make-Zip "one"
    $oldHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    & $publisher -Path $zip
    Assert-True ($global:Uploads -eq 1) "Initial canonical upload"
    & $publisher -Path $zip
    Assert-True ($global:Uploads -eq 1) "Idempotent repeat"

    Make-Zip "two"
    $newHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Expect-Failure { & $publisher -Path $zip -OnChanged Reject } "Refusing destructive replacement"
    Assert-True ($global:Uploads -eq 1) "Reject mode must not publish"
    & $publisher -Path $zip -OnChanged StageImmutable
    Assert-True ($global:Uploads -eq 2) "Staged content change"
    Assert-True ($global:FakeAssets[0].digest -eq "sha256:$oldHash") "Old canonical is unchanged"
    Assert-True ($global:FakeAssets[1].name -eq "temurin-25-jre-win-x64.sha256-$newHash.zip") "Immutable filename"
    & $publisher -Path $zip -OnChanged StageImmutable
    Assert-True ($global:Uploads -eq 2) "Immutable idempotence"

    $global:FakeAssets[1].digest = "sha256:$('f' * 64)"
    Expect-Failure { & $publisher -Path $zip -OnChanged StageImmutable } "conflicting SHA-256"
    Assert-True ($global:Uploads -eq 2) "Cannot overwrite conflicting asset"
    $global:FakeAssets = @($global:FakeAssets[0])

    $global:RejectUpload = $true
    Expect-Failure { & $publisher -Path $zip -OnChanged StageImmutable } "upload failed"
    Assert-True ($global:FakeAssets.Count -eq 1) "Failed upload preserved canonical"
    $global:RejectUpload = $false

    $global:FakeAssets = @()
    $global:HidePublished = $true
    Expect-Failure { & $publisher -Path $zip } "missing asset"
    $global:HidePublished = $false

    Set-Content -LiteralPath $zip -Value "corrupt zip"
    Expect-Failure { & $publisher -Path $zip } "ZIP validation failed"

    Set-Content -LiteralPath $meta -Value '{"schema":1,"version":"jdk-25.0.1"}'
    $global:FakeAssets = @()
    & $publisher -Path $meta
    Assert-True ($global:FakeAssets.Count -eq 1) "JSON provenance upload"
    Set-Content -LiteralPath $meta -Value '{"schema":1,"version":"jdk-25.0.2"}'
    & $publisher -Path $meta -OnChanged StageImmutable
    Assert-True ($global:FakeAssets.Count -eq 2) "Immutable JSON provenance"
    Write-Host "PASS: canonical, immutable staging, hash validation, rejected collision, failed upload, missing metadata and corrupted ZIP."
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item Function:\gh -ErrorAction SilentlyContinue
}
