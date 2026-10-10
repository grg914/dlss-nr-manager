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

# Read only PE header metadata (no binary upload or mutation). A changed
# COFF timestamp is evidence to investigate, not proof of the sole root cause.
function Get-PeTimestamp {
    param([string]$Path)
    $stream = [IO.File]::OpenRead($Path)
    try {
        $reader = [IO.BinaryReader]::new($stream)
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw "Invalid DOS MZ header: $Path" }
        $stream.Position = 0x3c
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 64 -or $peOffset -ge $stream.Length - 12) {
            throw "Invalid PE offset: $Path"
        }
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x4550) { throw "Invalid PE signature: $Path" }
        $stream.Position = $peOffset + 8
        return ("0x{0:x8}" -f $reader.ReadUInt32())
    }
    finally { $stream.Dispose() }
}

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
    if ($files.Count -eq 0 -or
        -not $files.ContainsKey("OptiScaler.dll") -or
        -not $files.ContainsKey("nvngx.dll_dlssnr.dll")) {
        throw "Native OptiScaler DLL package contents are missing."
    }
    if ($files.ContainsKey("nvngx.dll_dlssnr.pdb")) {
        throw "Unstable native forwarder debug PDB must remain in the build tree, not the runtime ZIP."
    }
    # Only the staging package is scanned; do not remove native debug outputs.
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
    $nativeFolder = @(Get-ChildItem -LiteralPath $directory -Directory -Filter "OptiScaler-NR-*-vendored-win-x64")
    foreach ($name in @("OptiScaler.dll", "nvngx.dll_dlssnr.dll")) {
        $nativePath = Join-Path $nativeFolder[0].FullName $name
        Write-Host "Pass $run PE $name COFF_TimeDateStamp=$(Get-PeTimestamp -Path $nativePath)"
    }
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

# Windows PowerShell GitHub Actions propagates a stale native $LASTEXITCODE
# from successful robocopy (1 = files copied) even after this full audit
# reports PASS. Explicitly return success only after every hash comparison.
exit 0
