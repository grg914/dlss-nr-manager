param(
    [Parameter(Mandatory = $true)][string]$PackageDirectory,
    [string]$ReportPath = ''
)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Windows-only PE import audit; no check performed.' }
$package = [IO.Path]::GetFullPath($PackageDirectory)
if (!(Test-Path -LiteralPath $package -PathType Container)) { throw "Missing package: $package" }
$dumpbin = Get-Command dumpbin.exe -ErrorAction SilentlyContinue
if (!$dumpbin) { throw 'dumpbin.exe required: use a Visual Studio x64 Developer PowerShell.' }
$binaries = @(Get-ChildItem -LiteralPath $package -File -Recurse | Where-Object {
    $_.Extension -in '.exe', '.dll'
})
if ($binaries.Count -lt 1) { throw 'No shipped PE .exe/.dll files found; cannot approve audit.' }

$inspected = @()
$openMpImports = @()
foreach ($binary in $binaries) {
    $output = @(& $dumpbin.Source /DEPENDENTS $binary.FullName 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "dumpbin failed for $($binary.Name)" }
    $dependencies = @(
        $output | ForEach-Object {
            if ($_ -match '(?i)^\s*([A-Z0-9_.-]+\.dll)\s*$') { $Matches[1] }
        } | Sort-Object -Unique
    )
    if ($dependencies.Count -eq 0) { throw "No PE imports identified in $($binary.Name); refuse an inconclusive audit." }
    $relative = $binary.FullName.Substring($package.TrimEnd('\','/').Length + 1).Replace('\','/')
    $inspected += [ordered]@{ file = $relative; imported_dlls = @($dependencies) }
    foreach ($dependency in $dependencies) {
        if ($dependency -match '(?i)^(vcomp[0-9a-z_-]*|libomp[0-9a-z_.-]*|libgomp[0-9a-z_.-]*|libiomp[0-9a-z_.-]*)\.dll$') {
            $openMpImports += [ordered]@{ file = $relative; dependency = $dependency }
        }
    }
}
$receipt = [ordered]@{
    schema_version = 1
    result = if ($openMpImports.Count -gt 0) { 'OPENMP_IMPORT_FOUND' } else { 'NO_SHIPPED_PE_OPENMP_IMPORT_FOUND' }
    scanned_pe_count = $binaries.Count
    inspected = @($inspected)
    openmp_imports = @($openMpImports)
    limitation = 'Examines direct PE imports of all shipped EXEs and DLLs only. Does not establish license rights, dynamically loaded runtime usage, system DLL transitive imports, GPU correctness, or performance.'
}
if ($ReportPath) {
    $target = [IO.Path]::GetFullPath($ReportPath)
    $parent = Split-Path -Parent $target
    if (!(Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    [IO.File]::WriteAllText($target, (($receipt | ConvertTo-Json -Depth 8) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    Write-Host "Read-only PE evidence: $target"
}
if ($openMpImports.Count -gt 0) { throw "OpenMP DLL imports remain in $($openMpImports.Count) shipped PE dependency occurrence(s)." }
Write-Host "PASS: $($binaries.Count) shipped PE binaries have no direct OpenMP import. This is NOT redistribution or RTX approval."
