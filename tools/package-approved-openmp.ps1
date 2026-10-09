param(
    [Parameter(Mandatory=$true)][string]$PackageDirectory,
    [string]$VisualStudioRoot = $env:VSINSTALLDIR,
    [switch]$ApprovedForRedistribution
)

$ErrorActionPreference = "Stop"

# Windows PowerShell 5.1 runs on .NET Framework and does not expose
# [IO.Path]::GetRelativePath (available in modern .NET only).
function Get-DescendantRelativePath {
    param(
        [Parameter(Mandatory=$true)][string]$RootDirectory,
        [Parameter(Mandatory=$true)][string]$CandidatePath
    )
    $fullRoot = [IO.Path]::GetFullPath($RootDirectory).TrimEnd([char[]]@([char]92, [char]47))
    $fullCandidate = [IO.Path]::GetFullPath($CandidatePath)
    $prefix = $fullRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $fullCandidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "OpenMP REDIST candidate is outside the approved Visual Studio directory."
    }
    return $fullCandidate.Substring($prefix.Length).Replace('\', '/')
}

# This guard is intentionally fail-closed. Redistributing a DLL copied from
# Windows System32 does NOT prove the producer holds an eligible VS license.
# Only the *licensed* build/release operator may enable this flag after
# reviewing the Microsoft terms associated with the actual VS installation.
if (-not $ApprovedForRedistribution) {
    throw "OpenMP redistribution is NOT approved. Supply -ApprovedForRedistribution only after validating your licensed Visual Studio REDIST rights."
}
if ([string]::IsNullOrWhiteSpace($VisualStudioRoot)) {
    throw "VSINSTALLDIR is required. An untracked System32 DLL is not a REDIST source."
}
if (!(Test-Path -LiteralPath $PackageDirectory -PathType Container)) {
    throw "Real-ESRGAN package directory is missing."
}
$root = [IO.Path]::GetFullPath($VisualStudioRoot).TrimEnd('\', '/')
if (!(Test-Path -LiteralPath $root -PathType Container)) {
    throw "Validated Visual Studio installation directory does not exist."
}
$redist = Join-Path $root "VC/Redist/MSVC"
if (!(Test-Path -LiteralPath $redist -PathType Container)) {
    throw "Visual Studio VC/Redist/MSVC source does not exist. Refusing to copy System32."
}
# Exclude non-distributable debug-only and wrong-architecture paths.
$candidates = @(Get-ChildItem -LiteralPath $redist -File -Filter 'vcomp140.dll' -Recurse |
    Where-Object {
        $relative = Get-DescendantRelativePath -RootDirectory $redist -CandidatePath $_.FullName
        $relative -match '^[0-9][^/]*/x64/Microsoft\.VC[^/]*\.OpenMP/vcomp140\.dll$' -and
        $relative -notmatch '(?i)debug_nonredist|onecore'
    } | Sort-Object FullName -Descending)
if ($candidates.Count -eq 0) {
    throw "No x64 vcomp140.dll from the licensed Visual Studio VC/Redist tree was found."
}
$source = $candidates[0]
$signature = Get-AuthenticodeSignature -LiteralPath $source.FullName
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
    $null -eq $signature.SignerCertificate -or
    $signature.SignerCertificate.Subject -notmatch 'Microsoft') {
    throw "OpenMP REDIST input has no valid Microsoft Authenticode signature."
}
$sha = (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$version = [string]$source.VersionInfo.FileVersion
if ($sha -notmatch '^[a-f0-9]{64}$' -or [string]::IsNullOrWhiteSpace($version)) {
    throw "OpenMP source SHA-256/version could not be verified."
}
$destination = Join-Path $PackageDirectory 'vcomp140.dll'
Copy-Item -LiteralPath $source.FullName -Destination $destination -Force
if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sha) {
    throw "OpenMP output hash changed during packaging."
}
$meta = [ordered]@{
    filename = 'vcomp140.dll'
    source = 'licensed Visual Studio VC/Redist/MSVC/x64'
    source_path = (Get-DescendantRelativePath -RootDirectory $root -CandidatePath $source.FullName)
    file_version = $version
    sha256 = $sha
    authenticode_status = 'Valid'
    publisher_subject = [string]$signature.SignerCertificate.Subject
    license_review_attested = $true
    terms = 'https://learn.microsoft.com/en-us/visualstudio/releases/2026/redistribution'
}
$meta | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PackageDirectory 'MICROSOFT_OPENMP_PROVENANCE.json') -Encoding utf8
Write-Host "Validated Microsoft OpenMP REDIST source $($meta.source_path); sha256=$sha; version=$version"
