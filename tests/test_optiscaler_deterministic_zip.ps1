$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$packager = Join-Path $root "tools/create-deterministic-flat-zip.ps1"
$temp = Join-Path ([IO.Path]::GetTempPath()) ("dlssnr-optiscaler-zip-test-" + [guid]::NewGuid().ToString("N"))

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

try {
    $input = Join-Path $temp "input"
    $licenses = Join-Path $input "Licenses"
    New-Item -ItemType Directory -Force -Path $licenses | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $input "OptiScaler.dll"), [byte[]](1, 2, 3, 4, 5))
    [IO.File]::WriteAllText((Join-Path $licenses "OptiScaler_LICENSE.txt"), "synthetic license text")

    $z1 = Join-Path $temp "first.zip"
    $z2 = Join-Path $temp "second.zip"
    $z3 = Join-Path $temp "changed.zip"
    & $packager -InputDirectory $input -OutputPath $z1 -TimestampUtc "1980-01-01T00:00:00Z" -Compression Optimal -Recursive

    # Different source timestamps must not alter the output ZIP bytes.
    Get-ChildItem -LiteralPath $input -Recurse -File | ForEach-Object {
        $_.LastWriteTimeUtc = [datetime]"2026-10-09T04:05:06Z"
    }
    & $packager -InputDirectory $input -OutputPath $z2 -TimestampUtc "1980-01-01T00:00:00Z" -Compression Optimal -Recursive
    $hash1 = (Get-FileHash -LiteralPath $z1 -Algorithm SHA256).Hash
    $hash2 = (Get-FileHash -LiteralPath $z2 -Algorithm SHA256).Hash
    Assert-True ($hash1 -ceq $hash2) "Identical OptiScaler inputs produced different ZIP bytes."

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($z1)
    try {
        $names = @($archive.Entries | ForEach-Object FullName | Sort-Object)
        Assert-True (($names -join '|') -ceq 'Licenses/OptiScaler_LICENSE.txt|OptiScaler.dll') "Nested entry layout changed."
        foreach ($entry in $archive.Entries) {
            Assert-True ($entry.LastWriteTime.UtcDateTime -eq [datetime]"1980-01-01T00:00:00Z") "ZIP timestamp is not normalized."
        }
    }
    finally { $archive.Dispose() }

    # Changing native binary bytes must change the archive checksum.
    [IO.File]::WriteAllBytes((Join-Path $input "OptiScaler.dll"), [byte[]](1, 2, 3, 4, 6))
    & $packager -InputDirectory $input -OutputPath $z3 -TimestampUtc "1980-01-01T00:00:00Z" -Compression Optimal -Recursive
    $hash3 = (Get-FileHash -LiteralPath $z3 -Algorithm SHA256).Hash
    Assert-True ($hash1 -cne $hash3) "A changed OptiScaler binary did not change the archive digest."

    # Existing Streamline flat packaging must still reject nested inputs.
    $flatRejected = $false
    try {
        & $packager -InputDirectory $input -OutputPath (Join-Path $temp "invalid-flat.zip") -TimestampUtc "1980-01-01T00:00:00Z"
    }
    catch { $flatRejected = $true }
    Assert-True $flatRejected "Flat ZIP compatibility guard accepted nested inputs."

    Write-Host "PASS: deterministic nested ZIP, byte sensitivity, stable timestamps and flat-mode safety."
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
