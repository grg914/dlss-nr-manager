param(
    [string]$Repository = "grg914/dlss-nr-manager",
    [string]$ReleaseTag = "runtime-seed-v1",
    [Parameter(Mandatory=$true)][string[]]$Assets,
    [string]$Destination = "build-local/runtime-seed",
    [switch]$AllMatches
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$destinationRoot = if ([IO.Path]::IsPathRooted($Destination)) { $Destination } else { Join-Path $Root $Destination }
New-Item -ItemType Directory -Force -Path $destinationRoot | Out-Null

$headers = @{ Accept = "application/vnd.github+json"; "User-Agent" = "DlssNrManager-ManagerSeed/1.0" }
if (-not [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) { $headers.Authorization = "Bearer $env:GH_TOKEN" }
elseif (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) { $headers.Authorization = "Bearer $env:GITHUB_TOKEN" }

$release = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/$Repository/releases/tags/$ReleaseTag"
if ($release.draft) { throw "Manager-owned runtime seed '$ReleaseTag' is a draft." }

$downloaded = @()
foreach ($pattern in $Assets) {
    $matches = @($release.assets | Where-Object { [string]$_.name -like $pattern })
    if ($matches.Count -eq 0) { throw "Manager-owned runtime seed '$ReleaseTag' has no asset matching '$pattern'." }

    foreach ($asset in $matches) {
        $name = [string]$asset.name
        $url = [string]$asset.browser_download_url
        $digest = [string]$asset.digest
        if ([string]::IsNullOrWhiteSpace($url) -or -not $url.StartsWith("https://github.com/$Repository/releases/download/", [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unexpected manager-owned asset URL for '$name': $url"
        }
        if ([string]::IsNullOrWhiteSpace($digest) -or -not $digest.StartsWith("sha256:", [StringComparison]::OrdinalIgnoreCase)) {
            throw "Manager-owned seed asset '$name' has no GitHub SHA-256 digest."
        }

        $destinationPath = Join-Path $destinationRoot $name
        $temporaryPath = "$destinationPath.download"
        Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        Invoke-WebRequest -Headers $headers -Uri $url -OutFile $temporaryPath

        $expected = $digest.Substring(7).ToLowerInvariant()
        $actual = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $expected) {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
            throw "SHA-256 mismatch for manager-owned seed asset '$name'. Expected $expected, got $actual."
        }

        Move-Item -LiteralPath $temporaryPath -Destination $destinationPath -Force
        $downloaded += $destinationPath
        Write-Host "Validated manager-owned seed asset: $name ($actual)"
    }
}

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    "directory=$destinationRoot" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "assets=$($downloaded -join ';')" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}