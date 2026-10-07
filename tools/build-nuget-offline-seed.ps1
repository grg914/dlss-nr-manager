param(
    [string]$OutputZip = "build-local/nuget-offline.zip",
    [string]$PackagesDirectory = "build-local/nuget-global",
    [string]$FeedDirectory = "build-local/nuget-feed"
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
    generated_at_utc = [DateTime]::UtcNow.ToString("o")
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
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $feed "manifest.json") -Encoding UTF8
Compress-Archive -Path (Join-Path $feed "*") -DestinationPath $zip -Force
if (!(Test-Path -LiteralPath $zip) -or (Get-Item -LiteralPath $zip).Length -lt 1MB) { throw "Offline NuGet seed ZIP was not generated correctly." }

Write-Host "Offline NuGet seed generated with $($manifest.packages.Count) packages: $zip"
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    "zip=$zip" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "package_count=$($manifest.packages.Count)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}