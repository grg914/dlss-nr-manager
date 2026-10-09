$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$zipper = Join-Path $root "tools/create-deterministic-flat-zip.ps1"
$publisher = Join-Path $root "tools/publish-append-only-release-zip.ps1"
$producer = Join-Path $root "tools/publish-streamline-runtime.ps1"
$temp = Join-Path ([IO.Path]::GetTempPath()) ("dlssnr-streamline-immutable-" + [Guid]::NewGuid().ToString("N"))
$first = Join-Path $temp "first"
$second = Join-Path $temp "second"
$zipA = Join-Path $temp "streamline-runtime-v2.14.1-win-x64.zip"
$zipB = Join-Path $temp "check.zip"
$epoch = "2000-01-01T00:00:00Z"

$global:FakeAssets = @()
$global:FakeUploads = 0

function Assert-True([bool]$Condition, [string]$Reason) {
    if (-not $Condition) { throw "Streamline immutable test failed: $Reason" }
}
function Expect-Failure([scriptblock]$Action, [string]$Expected) {
    $observed = ""
    try { & $Action } catch { $observed = $_.Exception.Message }
    if (-not $observed.Contains($Expected)) {
        throw "Expected '$Expected', got '$observed'"
    }
}
function global:gh {
    $argv = @($args)
    $global:LASTEXITCODE = 0
    if ($argv[0] -eq "api") {
        return (ConvertTo-Json -InputObject @{ draft = $false; assets = @($global:FakeAssets) } -Depth 8 -Compress)
    }
    if ($argv[0] -eq "release" -and $argv[1] -eq "upload") {
        Assert-True ($argv -notcontains "--clobber") "Never send a clobber upload"
        $path = [string]$argv[3]
        $name = [IO.Path]::GetFileName($path)
        if (@($global:FakeAssets | Where-Object { $_.name -ceq $name }).Count -ne 0) {
            throw "Mock refused overwrite of an existing asset"
        }
        $global:FakeAssets += [pscustomobject]@{
            name = $name
            size = (Get-Item -LiteralPath $path).Length
            digest = "sha256:" + (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        $global:FakeUploads++
        return
    }
    throw "Unexpected gh invocation: $($argv -join ' ')"
}

try {
    New-Item -ItemType Directory -Path $first, $second -Force | Out-Null
    $names = @(
        "sl.interposer.dll",
        "sl.dlss_nr.dll",
        "nvngx_dlssnr.dll",
        "STREAMLINE_RUNTIME.json",
        "SOURCE.json",
        "license.txt"
    )
    foreach ($name in $names) {
        # Synthetic bytes, never actual NVIDIA or restricted binaries.
        [IO.File]::WriteAllText((Join-Path $first $name), "synthetic fixture: $name")
    }
    foreach ($name in @($names | Sort-Object -Descending)) {
        [IO.File]::WriteAllBytes(
            (Join-Path $second $name),
            [IO.File]::ReadAllBytes((Join-Path $first $name)))
    }
    foreach ($file in (Get-ChildItem -LiteralPath $first -File)) {
        $file.LastWriteTimeUtc = [datetime]"2024-01-01T01:02:03Z"
    }
    foreach ($file in (Get-ChildItem -LiteralPath $second -File)) {
        $file.LastWriteTimeUtc = [datetime]"2026-10-09T05:06:07Z"
    }

    & $zipper -InputDirectory $first -OutputPath $zipA -TimestampUtc $epoch -Compression Optimal
    & $zipper -InputDirectory $second -OutputPath $zipB -TimestampUtc $epoch -Compression Optimal
    $hashA = (Get-FileHash -LiteralPath $zipA -Algorithm SHA256).Hash.ToLowerInvariant()
    $hashB = (Get-FileHash -LiteralPath $zipB -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-True ($hashA -ceq $hashB) "ZIP bytes must be identical regardless of file creation order or timestamps"

    $archive = [IO.Compression.ZipFile]::OpenRead($zipA)
    try {
        Assert-True ($archive.Entries.Count -eq $names.Count) "Every source entry is preserved"
        Assert-True ((@($archive.Entries | ForEach-Object FullName | Sort-Object) -join '|') -ceq
            (@($names | Sort-Object) -join '|')) "Exact filename set"
        foreach ($entry in $archive.Entries) {
            $expected = [IO.File]::ReadAllBytes((Join-Path $first $entry.FullName))
            $memory = [IO.MemoryStream]::new()
            try {
                $inputStream = $entry.Open()
                try { $inputStream.CopyTo($memory) }
                finally { $inputStream.Dispose() }
                Assert-True ([Convert]::ToBase64String($memory.ToArray()) -ceq
                    [Convert]::ToBase64String($expected)) "Payload unchanged: $($entry.FullName)"
            }
            finally { $memory.Dispose() }
        }
    }
    finally { $archive.Dispose() }

    $code = Get-Content -LiteralPath $producer -Raw
    Assert-True ($code.Contains("create-deterministic-flat-zip.ps1")) "Producer must call deterministic helper"
    Assert-True ($code.Contains("publish-append-only-release-zip.ps1")) "Producer must use append-only publisher"
    Assert-True ($code.Contains("verify-streamline-runtime-asset.ps1")) "Pinned Neural Rendering verification retained"
    Assert-True ($code.Contains("Streamline runtime ZIP is not reproducible")) "Actual producer must rebuild and compare"
    Assert-True (-not $code.Contains("Compress-Archive")) "Unstable packaging removed"
    Assert-True (-not $code.Contains("gh release upload $ReleaseTag $zip --repo $Repository --clobber")) "No destructive publication"
    Assert-True ($code.Contains("9f6672e5e0170dc118a3188d21bda187e1fc1aa3502895b21ab846d23165c11d")) "Locally controlled sl.dlss_nr SHA preserved"
    Assert-True ($code.Contains("e16bcf15e16e13f527491cdf7845b2fe6521a738d8f7c9c721866a8496e1fc8e")) "Locally controlled nvngx_dlssnr SHA preserved"

    # Simulate the real v3.2.0 canonical asset remaining on GitHub. Its
    # ZIP bytes predate the deterministic packaging, so they must NEVER change.
    $stableHash = "fa6ada7fa759b977849900256316c3ee84a95e848e744ab260cef24b0a0361db"
    $global:FakeAssets = @([pscustomobject]@{
        name = "streamline-runtime-v2.14.1-win-x64.zip"
        size = 171509289
        digest = "sha256:$stableHash"
    })
    & $publisher -Path $zipA -Repository "grg914/dlss-nr-manager" -ReleaseTag "runtime-seed-v1" -MinimumSizeBytes 1
    Assert-True ($global:FakeUploads -eq 1) "Changed ZIP staged exactly once"
    Assert-True ($global:FakeAssets[0].digest -ceq "sha256:$stableHash") "Published canonical byte pin preserved"
    Assert-True ($global:FakeAssets[1].name -ceq "streamline-runtime-v2.14.1-win-x64.sha256-$hashA.zip") "Immutable staging filename is content-addressed"
    & $publisher -Path $zipA -Repository "grg914/dlss-nr-manager" -ReleaseTag "runtime-seed-v1" -MinimumSizeBytes 1
    Assert-True ($global:FakeUploads -eq 1) "Staged exact bytes never uploaded twice"

    # A conflicting digest on an immutable staged asset must fail closed.
    $global:FakeAssets[1].digest = "sha256:" + ("f" * 64)
    Expect-Failure {
        & $publisher -Path $zipA -Repository "grg914/dlss-nr-manager" -ReleaseTag "runtime-seed-v1" -MinimumSizeBytes 1
    } "conflicting content"
    Assert-True ($global:FakeUploads -eq 1) "Failed conflict does not publish"

    # Changing a DLL must change the archive digest and cannot overwrite the
    # immutable snapshot or the existing canonical release asset.
    [IO.File]::WriteAllText((Join-Path $second "sl.interposer.dll"), "changed synthetic binary")
    & $zipper -InputDirectory $second -OutputPath $zipB -TimestampUtc $epoch -Compression Optimal
    $changed = (Get-FileHash -LiteralPath $zipB -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-True ($changed -cne $hashA) "Changed DLL content must affect the ZIP hash"
    Write-Host "PASS: deterministic bytes, preserved entries, locally pinned NR hashes, immutable staging and conflict rejection."
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item Function:\gh -ErrorAction SilentlyContinue
}
