$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$packager = Join-Path $root "tools/package-approved-openmp.ps1"
$source = Get-Content -LiteralPath $packager -Raw

# Execute only the pure descendant-path helper, never a licensed binary
# publication or an OpenMP REDIST copy.
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput(
    $source, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw "OpenMP packager has invalid PowerShell syntax." }
$functionAst = $ast.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq "Get-DescendantRelativePath"
}, $true)
if (-not $functionAst) { throw "Windows PowerShell 5.1 relative path helper is absent." }
. ([scriptblock]::Create($functionAst.Extent.Text))

$work = Join-Path ([IO.Path]::GetTempPath()) ("dlssnr-openmp-path-" + [Guid]::NewGuid().ToString("N"))
try {
    $vs = Join-Path $work "VS"
    $redist = Join-Path $vs "VC/Redist/MSVC/14.50.0/x64/Microsoft.VC143.OpenMP"
    New-Item -ItemType Directory -Path $redist -Force | Out-Null
    $input = Join-Path $redist "vcomp140.dll"
    [IO.File]::WriteAllText($input, "synthetic-not-a-Microsoft-binary")
    $relative = Get-DescendantRelativePath -RootDirectory $vs -CandidatePath $input
    if ($relative -cne "VC/Redist/MSVC/14.50.0/x64/Microsoft.VC143.OpenMP/vcomp140.dll") {
        throw "Incorrect relative path generated: $relative"
    }

    $outside = Join-Path $work "VS-other/vcomp140.dll"
    $rejected = $false
    try {
        Get-DescendantRelativePath -RootDirectory $vs -CandidatePath $outside | Out-Null
    }
    catch { $rejected = $true }
    if (-not $rejected) { throw "Adjacent outside path was accepted." }

    $traversal = Join-Path $vs "../VS-other/vcomp140.dll"
    $rejected = $false
    try {
        Get-DescendantRelativePath -RootDirectory $vs -CandidatePath $traversal | Out-Null
    }
    catch { $rejected = $true }
    if (-not $rejected) { throw "Parent traversal outside approved REDIST root was accepted." }

    $denied = $false
    try {
        & $packager -PackageDirectory $vs -VisualStudioRoot $vs
    }
    catch {
        $denied = $_.Exception.Message -like "*redistribution is NOT approved*"
    }
    if (-not $denied) { throw "OpenMP licensing gate did not fail closed." }

    Write-Host "PASS: Windows PowerShell 5.1 path helper, path-boundary defense, and OpenMP license refusal."
}
finally {
    if (Test-Path -LiteralPath $work) {
        Remove-Item -LiteralPath $work -Recurse -Force
    }
}
