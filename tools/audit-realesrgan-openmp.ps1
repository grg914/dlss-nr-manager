param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'

$exe = [IO.Path]::GetFullPath($ExecutablePath)
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw "Real-ESRGAN executable does not exist: $exe"
}
if ([IO.Path]::GetExtension($exe) -ne '.exe') {
    throw 'Expected a Real-ESRGAN Windows executable for PE import audit.'
}

# Keep the documented standalone audit in sync with the audited package path.
# It also finds the Visual Studio x64 dumpbin outside the Developer Shell.
$script = Join-Path $PSScriptRoot 'audit-realesrgan-package-openmp.ps1'
& $script -PackageDirectory (Split-Path -Parent $exe)
if ($LASTEXITCODE -ne 0) {
    throw 'Real-ESRGAN package PE import audit failed.'
}
