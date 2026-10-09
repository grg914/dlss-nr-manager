param(
    [string]$SourcePath,
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$builder = Join-Path $PSScriptRoot "build-optiscaler.ps1"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root "build-local/optiscaler-repro"
}
if (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $root $OutputDirectory
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

function Get-PackageEvidence {
    param([string]$BuildDirectory)

    $archives = @(Get-ChildItem -LiteralPath $BuildDirectory -File -Filter "OptiScaler-NR-*-vendored-win-x64.zip")
    $folders = @(Get-ChildItem -LiteralPath $BuildDirectory -Directory -Filter "OptiScaler-NR-*-vendored-win-x64")
    if ($archives.Count -ne 1 -or $folders.Count -ne 1) {
        throw "Expected exactly one OptiScaler ZIP and expanded package under $BuildDirectory."
    }

    $files = @{}
    $folder = $folders[0]
    foreach ($file in @(Get-ChildItem -LiteralPath $folder.FullName -File -Recurse)) {
        $name = $file.FullName.Substring($folder.FullName.Length).TrimStart([char[]]@([char]92, [char]47)).Replace('\', '/')
        $files[$name] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    if ($files.Count -eq 0 -or -not $files.ContainsKey("OptiScaler.dll")) {
        throw "Native OptiScaler package contents are missing."
    }

    $zip = $archives[0]
    return [pscustomobject]@{
        ZipHash = (Get-FileHash -LiteralPath $zip.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        ZipSize = $zip.Length
        ZipName = $zip.Name
        Files = $files
    }
}

$evidence = @()
for ($run = 1; $run -le 2; $run++) {
    $directory = Join-Path $OutputDirectory "build-$run"
    Write-Host "OptiScaler native reproduction pass $run (fresh MSBuild Rebuild)"
    if ([string]::IsNullOrWhiteSpace($SourcePath)) {
        & $builder -OutputDirectory $directory -Clean | Out-Host
    }
    else {
        & $builder -SourcePath $SourcePath -OutputDirectory $directory -Clean | Out-Host
    }
    $result = Get-PackageEvidence -BuildDirectory $directory
    Write-Host "Pass $run ZIP $($result.ZipName) SHA256=$($result.ZipHash) size=$($result.ZipSize)"
    Write-Host "Pass $run DLL SHA256=$($result.Files['OptiScaler.dll'])"
    $evidence += $result
}

$first = $evidence[0]
$second = $evidence[1]
$names = @(@($first.Files.Keys) + @($second.Files.Keys) | Sort-Object -Unique)
$mismatched = @()
foreach ($name in $names) {
    if (-not $first.Files.ContainsKey($name) -or
        -not $second.Files.ContainsKey($name) -or
        $first.Files[$name] -cne $second.Files[$name]) {
        Write-Warning "Native package file differs: $name (first=$($first.Files[$name]), second=$($second.Files[$name]))"
        $mismatched += $name
    }
}
if ($mismatched.Count -gt 0) {
    throw "OptiScaler two-build native package contents differ ($($mismatched.Count) files). Inspect MSBuild/linker inputs before publishing."
}
if ($first.ZipName -cne $second.ZipName -or
    $first.ZipHash -cne $second.ZipHash -or
    $first.ZipSize -ne $second.ZipSize) {
    throw "OptiScaler package files match but ZIP bytes differ. Investigate archive ordering/timestamps/compression."
}
Write-Host "PASS: OptiScaler native inputs and final ZIP are identical across two clean rebuilds."
