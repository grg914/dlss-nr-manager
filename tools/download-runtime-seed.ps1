param(
    [string]$Repository = "grg914/dlss-nr-manager",
    [string]$ReleaseTag = "runtime-seed-v1",
    [Parameter(Mandatory=$true)][string[]]$Assets,
    [string]$Destination = "build-local/runtime-seed",
    [switch]$AllMatches,
    [ValidateRange(1, 10)][int]$ConsistencyRetries = 4,
    [ValidateRange(0, 30)][int]$ConsistencyRetryDelaySeconds = 2
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$destinationRoot = if ([IO.Path]::IsPathRooted($Destination)) { $Destination } else { Join-Path $Root $Destination }
New-Item -ItemType Directory -Force -Path $destinationRoot | Out-Null

$headers = @{ Accept = "application/vnd.github+json"; "User-Agent" = "DlssNrManager-ManagerSeed/1.0" }
if (-not [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) { $headers.Authorization = "Bearer $env:GH_TOKEN" }
elseif (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) { $headers.Authorization = "Bearer $env:GITHUB_TOKEN" }

function Get-SeedRelease {
    $release = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/$Repository/releases/tags/$ReleaseTag"
    if ($release.draft) { throw "Manager-owned runtime seed '$ReleaseTag' is a draft." }
    return $release
}

function Resolve-SeedAssets {
    param([Parameter(Mandatory=$true)]$Release)

    $selected = @()
    $seen = @{}
    foreach ($pattern in $Assets) {
        $matches = @($Release.assets | Where-Object { [string]$_.name -like $pattern })
        if ($matches.Count -eq 0) { throw "Manager-owned runtime seed '$ReleaseTag' has no asset matching '$pattern'." }

        foreach ($asset in $matches) {
            $name = [string]$asset.name
            if ($seen.ContainsKey($name)) { continue }

            $url = [string]$asset.browser_download_url
            $digest = [string]$asset.digest
            if ([string]::IsNullOrWhiteSpace($url) -or -not $url.StartsWith("https://github.com/$Repository/releases/download/", [StringComparison]::OrdinalIgnoreCase)) {
                throw "Unexpected manager-owned asset URL for '$name': $url"
            }
            if ([string]::IsNullOrWhiteSpace($digest) -or -not $digest.StartsWith("sha256:", [StringComparison]::OrdinalIgnoreCase)) {
                throw "Manager-owned seed asset '$name' has no GitHub SHA-256 digest."
            }

            $selected += [pscustomobject]@{
                Name = $name
                Id = [string]$asset.id
                Url = $url
                Digest = $digest.ToLowerInvariant()
            }
            $seen[$name] = $true
        }
    }

    return @($selected)
}

$downloaded = @()
$completed = $false
for ($attempt = 1; $attempt -le $ConsistencyRetries; $attempt++) {
    $attemptFiles = @()
    $retryReason = $null

    try {
        $release = Get-SeedRelease
        $selectedAssets = @(Resolve-SeedAssets -Release $release)

        foreach ($asset in $selectedAssets) {
            $destinationPath = Join-Path $destinationRoot $asset.Name
            $temporaryPath = "$destinationPath.download.$attempt.$([Guid]::NewGuid().ToString('N'))"
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue

            Invoke-WebRequest -UseBasicParsing -Headers $headers -Uri $asset.Url -OutFile $temporaryPath

            $expected = $asset.Digest.Substring(7)
            $actual = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($actual -ne $expected) {
                Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
                $retryReason = "Seed asset '$($asset.Name)' changed while downloading. Snapshot expected $expected, downloaded $actual."
                break
            }

            $attemptFiles += [pscustomobject]@{
                Name = $asset.Name
                Id = $asset.Id
                Digest = $asset.Digest
                TemporaryPath = $temporaryPath
                DestinationPath = $destinationPath
                Sha256 = $actual
            }
        }

        if ([string]::IsNullOrWhiteSpace($retryReason)) {
            $confirmedRelease = Get-SeedRelease
            foreach ($entry in $attemptFiles) {
                $confirmedMatches = @($confirmedRelease.assets | Where-Object { [string]$_.name -eq $entry.Name })
                if ($confirmedMatches.Count -ne 1) {
                    $retryReason = "Seed asset '$($entry.Name)' changed identity while validating the download snapshot."
                    break
                }

                $confirmed = $confirmedMatches[0]
                if ([string]$confirmed.id -ne $entry.Id -or ([string]$confirmed.digest).ToLowerInvariant() -ne $entry.Digest) {
                    $retryReason = "Seed asset '$($entry.Name)' was replaced while the download snapshot was in progress."
                    break
                }
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($retryReason)) {
            foreach ($entry in $attemptFiles) {
                Remove-Item -LiteralPath $entry.TemporaryPath -Force -ErrorAction SilentlyContinue
            }

            if ($attempt -ge $ConsistencyRetries) {
                throw "Manager-owned runtime seed did not remain stable after $ConsistencyRetries attempts. Last reason: $retryReason"
            }

            Write-Warning "$retryReason Retrying the complete seed snapshot ($($attempt + 1)/$ConsistencyRetries)."
            if ($ConsistencyRetryDelaySeconds -gt 0) { Start-Sleep -Seconds $ConsistencyRetryDelaySeconds }
            continue
        }

        foreach ($entry in $attemptFiles) {
            Move-Item -LiteralPath $entry.TemporaryPath -Destination $entry.DestinationPath -Force
            $downloaded += $entry.DestinationPath
            Write-Host "Validated manager-owned seed asset: $($entry.Name) ($($entry.Sha256))"
        }

        $completed = $true
        break
    }
    catch {
        foreach ($entry in $attemptFiles) {
            Remove-Item -LiteralPath $entry.TemporaryPath -Force -ErrorAction SilentlyContinue
        }
        throw
    }
}

if (-not $completed) {
    throw "Manager-owned runtime seed download did not complete."
}

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    "directory=$destinationRoot" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "assets=$($downloaded -join ';')" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
