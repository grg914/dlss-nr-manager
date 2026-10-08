param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath
)

$ErrorActionPreference = "Stop"

$exe = [IO.Path]::GetFullPath($ExecutablePath)
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw "Real-ESRGAN executable does not exist: $exe"
}

$dumpbin = Get-Command dumpbin.exe -ErrorAction SilentlyContinue
if ($null -eq $dumpbin) {
    throw "dumpbin.exe is not available. Run in a Visual Studio Developer PowerShell with the x64 native toolchain."
}

$output = @(& $dumpbin.Source /DEPENDENTS $exe 2>&1)
if ($LASTEXITCODE -ne 0) {
    throw "dumpbin /DEPENDENTS failed for $exe"
}

$dependencies = @(
    $output | ForEach-Object {
        if ($_ -match '(?i)^\s*([A-Z0-9_.-]+\.dll)\s*$') {
            $Matches[1]
        }
    }
)

if ($dependencies.Count -eq 0) {
    throw "No PE DLL imports were identified. Refusing to assume the audit succeeded."
}

Write-Host "Direct PE imports for $exe:"
$dependencies | ForEach-Object { Write-Host "  $_" }

$openMp = @($dependencies | Where-Object { $_ -match '(?i)^(vcomp\d*|libomp|libgomp|libiomp\d*)\.dll$' })
if ($openMp.Count -gt 0) {
    throw "OpenMP runtime import remains: $($openMp -join ', '). Do not ship the experimental binary without a licensed runtime."
}

Write-Host "PASS: no direct OpenMP runtime DLL import found. This does not audit transitive dependencies, GPU performance, or licensing."
