param(
    [string]$OutputZip = "build-local/nuget-offline.zip",
    [string]$PackagesDirectory = "build-local/nuget-global",
    [string]$FeedDirectory = "build-local/nuget-feed",
    [string]$TimestampUtc = ""
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$packages = if ([IO.Path]::IsPathRooted($PackagesDirectory)) { $PackagesDirectory } else { Join-Path $Root $PackagesDirectory }
$feed = if ([IO.Path]::IsPathRooted($FeedDirectory)) { $FeedDirectory } else { Join-Path $Root $FeedDirectory }
$zip = if ([IO.Path]::IsPathRooted($OutputZip)) { $OutputZip } else { Join-Path $Root $OutputZip }

Remove-Item -LiteralPath $packages -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $feed -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $packages, $feed, (Split-Path -Parent $zip) | Out-Null

if ([string]::IsNullOrWhiteSpace($TimestampUtc)) {
    Push-Location $Root
    try {
        $TimestampUtc = (git show -s --format=%cI HEAD).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($TimestampUtc)) {
            throw "Unable to resolve source commit timestamp for deterministic NuGet seed packaging."
        }
    }
    finally {
        Pop-Location
    }
}

$sourceTimestamp = ([DateTimeOffset]::Parse($TimestampUtc)).ToUniversalTime()
$zipSecond = $sourceTimestamp.Second - ($sourceTimestamp.Second % 2)
$archiveTimestamp = [DateTimeOffset]::new(
    $sourceTimestamp.Year,
    $sourceTimestamp.Month,
    $sourceTimestamp.Day,
    $sourceTimestamp.Hour,
    $sourceTimestamp.Minute,
    $zipSecond,
    [TimeSpan]::Zero)

Push-Location $Root
try {
    dotnet restore DlssNrManager.csproj --packages "$packages" --runtime win-x64
    if ($LASTEXITCODE -ne 0) { throw "Application NuGet refresh restore failed." }
    dotnet restore tests/DlssNrManager.Tests/DlssNrManager.Tests.csproj --packages "$packages" --runtime win-x64
    if ($LASTEXITCODE -ne 0) { throw "Tests NuGet refresh restore failed." }
}
finally {
    Pop-Location
}

$wanted = [ordered]@{}
function Add-Package {
    param([string]$Id, [string]$Version)
    if ([string]::IsNullOrWhiteSpace($Id) -or [string]::IsNullOrWhiteSpace($Version)) { return }
    $clean = $Version.Trim()
    $match = [regex]::Match($clean, "(?<version>\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?)")
    if (-not $match.Success) { throw "Unable to resolve exact NuGet version from '$Version' for package '$Id'." }
    $resolved = $match.Groups["version"].Value
    $key = "$Id/$resolved".ToLowerInvariant()
    if (-not $wanted.Contains($key)) { $wanted[$key] = [pscustomobject]@{ Id = $Id; Version = $resolved } }
}

foreach ($assetPath in @("obj/project.assets.json", "tests/DlssNrManager.Tests/obj/project.assets.json")) {
    $full = Join-Path $Root $assetPath
    if (!(Test-Path -LiteralPath $full)) { throw "NuGet assets file missing after restore: $assetPath" }
    $json = Get-Content -LiteralPath $full -Raw | ConvertFrom-Json

    foreach ($property in $json.libraries.PSObject.Properties) {
        if ([string]$property.Value.type -ne "package") { continue }
        $parts = $property.Name -split "/", 2
        if ($parts.Count -eq 2) { Add-Package -Id $parts[0] -Version $parts[1] }
    }

    foreach ($frameworkProperty in $json.project.frameworks.PSObject.Properties) {
        foreach ($dependency in @($frameworkProperty.Value.downloadDependencies)) {
            Add-Package -Id ([string]$dependency.name) -Version ([string]$dependency.version)
        }
    }
}

foreach ($package in $wanted.Values) {
    $idLower = ([string]$package.Id).ToLowerInvariant()
    $versionLower = ([string]$package.Version).ToLowerInvariant()
    $name = "$idLower.$versionLower.nupkg"
    $destination = Join-Path $feed $name
    $cached = Join-Path (Join-Path (Join-Path $packages $idLower) $versionLower) $name

    if (Test-Path -LiteralPath $cached) {
        Copy-Item -LiteralPath $cached -Destination $destination -Force
    }
    else {
        $url = "https://api.nuget.org/v3-flatcontainer/$idLower/$versionLower/$name"
        Invoke-WebRequest -Uri $url -OutFile $destination
    }

    if (!(Test-Path -LiteralPath $destination) -or (Get-Item -LiteralPath $destination).Length -lt 512) {
        throw "NuGet package is unexpectedly small or missing: $name"
    }
}

$manifest = [ordered]@{
    schema = 2
    generated_at_utc = $sourceTimestamp.ToString("o")
    runtime_identifier = "win-x64"
    packages = @(
        Get-ChildItem -LiteralPath $feed -Filter "*.nupkg" -File | Sort-Object Name | ForEach-Object {
            [ordered]@{
                name = $_.Name
                sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
    )
}

$manifestPath = Join-Path $feed "manifest.json"
$manifestJson = ($manifest | ConvertTo-Json -Depth 8) -replace "`r`n", "`n"
[IO.File]::WriteAllText($manifestPath, $manifestJson + "`n", [Text.UTF8Encoding]::new($false))

# NuGet packages are ZIP containers already. Store them without an additional
# deflate pass so the manager-owned outer archive is faster to build/extract and
# independent of compression-library implementation details.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$archiveInputs = @(Get-ChildItem -LiteralPath $feed -File | Sort-Object Name)
if ($archiveInputs.Count -eq 0) { throw "Offline NuGet feed is empty." }

$fileStream = [IO.File]::Open(
    $zip,
    [IO.FileMode]::CreateNew,
    [IO.FileAccess]::ReadWrite,
    [IO.FileShare]::None)

try {
    $archive = [IO.Compression.ZipArchive]::new(
        $fileStream,
        [IO.Compression.ZipArchiveMode]::Create,
        $false)

    try {
        foreach ($inputFile in $archiveInputs) {
            $entry = $archive.CreateEntry(
                $inputFile.Name,
                [IO.Compression.CompressionLevel]::NoCompression)
            $entry.LastWriteTime = $archiveTimestamp

            $input = [IO.File]::OpenRead($inputFile.FullName)
            try {
                $output = $entry.Open()
                try {
                    $input.CopyTo($output)
                }
                finally {
                    $output.Dispose()
                }
            }
            finally {
                $input.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $fileStream.Dispose()
}

if (!(Test-Path -LiteralPath $zip) -or (Get-Item -LiteralPath $zip).Length -lt 1MB) {
    throw "Offline NuGet seed ZIP was not generated correctly."
}

# Verify exact entry set and normalized timestamps before publishing.
$verifyStream = [IO.File]::OpenRead($zip)
try {
    $verifyArchive = [IO.Compression.ZipArchive]::new(
        $verifyStream,
        [IO.Compression.ZipArchiveMode]::Read,
        $false)
    try {
        $actualNames = @($verifyArchive.Entries | ForEach-Object FullName | Sort-Object)
        $expectedNames = @($archiveInputs | ForEach-Object Name | Sort-Object)

        if (($actualNames -join "|") -ne ($expectedNames -join "|")) {
            throw "Offline NuGet ZIP entry set mismatch."
        }

        foreach ($entry in $verifyArchive.Entries) {
            if ($entry.LastWriteTime.UtcDateTime -ne $archiveTimestamp.UtcDateTime) {
                throw "Offline NuGet ZIP timestamp mismatch for $($entry.FullName)."
            }
        }
    }
    finally {
        $verifyArchive.Dispose()
    }
}
finally {
    $verifyStream.Dispose()
}

$sha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Offline NuGet seed generated with $($manifest.packages.Count) packages: $zip"
Write-Host "Deterministic seed SHA-256: $sha256"
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    "zip=$zip" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "package_count=$($manifest.packages.Count)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "sha256=$sha256" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
