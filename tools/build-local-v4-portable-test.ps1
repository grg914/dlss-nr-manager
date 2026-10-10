param(
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'

# Produces an application-only, disposable Windows test package. No release
# publishing, runtime download, installer execution, or process termination.
if ($env:OS -ne 'Windows_NT') {
    throw 'V4 portable local build requires Windows x64.'
}
if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) {
    throw 'Git for Windows is required. Install it from the official vendor first.'
}
if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue)) {
    throw 'The .NET 8 SDK is required to compile the V4 test application. No toolchain is downloaded automatically.'
}
$sdks = @(dotnet --list-sdks)
if ($LASTEXITCODE -ne 0 -or -not ($sdks | Where-Object { $_ -match '^8\.' })) {
    throw 'A preinstalled .NET 8 SDK is required. This script does not install SDKs.'
}

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$head = (& git -C $root rev-parse HEAD | Out-String).Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[a-f0-9]{40}$') {
    throw 'Use a Git checkout of grg914/dlss-nr-manager rather than a source ZIP.'
}
$branch = (& git -C $root symbolic-ref --short HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $branch -cne 'main') {
    throw 'Checkout the protected main branch in a dedicated fresh local clone.'
}
$dirty = @(& git -C $root status --porcelain --untracked-files=no)
if ($LASTEXITCODE -ne 0 -or $dirty.Count -gt 0) {
    throw 'Tracked source files have local changes; use a clean checkout before building.'
}

$short = $head.Substring(0, 12)
$work = Join-Path $root ('build-local/v4-portable-' + $short)
if (Test-Path -LiteralPath $work) {
    throw "Existing local build directory must be reviewed before reuse; refusing overwrite: $work"
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $desktop = [Environment]::GetFolderPath('DesktopDirectory')
    if ([string]::IsNullOrWhiteSpace($desktop)) { throw 'Unable to resolve the Windows desktop.' }
    $OutputDirectory = Join-Path $desktop ('DLSSNR-V4-Test-' + $short)
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) {
    throw "Output folder already exists. No user files will be overwritten: $output"
}
New-Item -ItemType Directory -Path $work -Force | Out-Null

Push-Location $root
try {
    # Existing manager-owned pinned seed; refuses a changed package digest and
    # creates a NuGet.Config containing only the extracted local feed.
    & (Join-Path $PSScriptRoot 'prepare-offline-dotnet.ps1') -Repository 'grg914/dlss-nr-manager'
    if ($LASTEXITCODE -ne 0) { throw 'Pinned offline NuGet preparation failed.' }

    $config = Join-Path $root 'build-local/offline-dotnet/NuGet.Config'
    if (!(Test-Path -LiteralPath $config -PathType Leaf)) { throw 'Offline-only NuGet config is missing.' }

    & dotnet restore 'DlssNrManager.csproj' --configfile $config
    if ($LASTEXITCODE -ne 0) { throw 'Application offline restore failed.' }
    & dotnet restore 'tests/DlssNrManager.Tests/DlssNrManager.Tests.csproj' --configfile $config
    if ($LASTEXITCODE -ne 0) { throw 'Tests offline restore failed.' }
    & dotnet test 'tests/DlssNrManager.Tests/DlssNrManager.Tests.csproj' -c Release --no-restore -p:TreatWarningsAsErrors=true
    if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed; no test package generated.' }

    $publish = Join-Path $work 'publish'
    & dotnet publish 'DlssNrManager.csproj' -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -warnaserror -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Portable Windows executable build failed.' }
    $exe = Join-Path $publish 'DlssNrManager.exe'
    if (!(Test-Path -LiteralPath $exe -PathType Leaf) -or (Get-Item -LiteralPath $exe).Length -lt 262144) {
        throw 'No valid single-file Windows application was generated.'
    }

    $timestamp = (& git show -s --format=%cI $head | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($timestamp)) {
        throw 'Unable to identify the source commit timestamp.'
    }
    $zip = Join-Path $work 'DlssNrManager-V4-portable-test.zip'
    & (Join-Path $PSScriptRoot 'create-application-package.ps1') -ExecutablePath $exe -OutputPath $zip -TimestampUtc $timestamp
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $zip -PathType Leaf)) {
        throw 'Deterministic portable package generation failed.'
    }

    # All work above happens before creating the final desktop folder.
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    Expand-Archive -LiteralPath $zip -DestinationPath $output -Force
    $finalExe = Join-Path $output 'DlssNrManager.exe'
    if (!(Test-Path -LiteralPath $finalExe -PathType Leaf)) { throw 'Extracted portable executable is missing.' }
    $publishedHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    $extractedHash = (Get-FileHash -LiteralPath $finalExe -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($publishedHash -cne $extractedHash) { throw 'Portable executable changed when extracted.' }

    $signature = Get-AuthenticodeSignature -LiteralPath $finalExe
    $receipt = @(
        'DLSS NR Manager V4 — local developer test build; NOT a published or signed application release',
        "source_commit=$head",
        "portable_exe_sha256=$extractedHash",
        "package_zip_sha256=$((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant())",
        "authenticode_status=$($signature.Status)",
        'scope=application-only; Real-ESRGAN models/OpenMP/third-party runtimes not distributed by this build',
        'physical_windows_rtx_acceptance=NOT_RUN'
    )
    [IO.File]::WriteAllLines((Join-Path $output 'BUILD-RECEIPT.txt'), $receipt, [Text.UTF8Encoding]::new($false))
    Write-Host "Portable V4 test folder: $output"
    Write-Host "Local EXE SHA-256: $extractedHash"
    Write-Host 'No application has been started or installed; review the generated receipt before launching.'
}
finally {
    Pop-Location
}
