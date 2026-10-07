param(
    [string]$Repository = "grg914/dlss-nr-manager",
    [string]$ReleaseTag = "runtime-seed-v1",
    [Parameter(Mandatory=$true)][string[]]$Assets,
    [string]$Destination = "build-local/runtime-seed",
    [switch]$AllMatches,
    [ValidateRange(1, 10)][int]$ConsistencyAttempts = 4
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

function Get-ValidatedAssetMetadata {
    param(
        [Parameter(Mandatory=$true)]$Release,
        [Parameter(Mandatory=$true)][string]$Name
    )

    $asset = @($Release.assets | Where-Object { [string]$_.name -eq $Name }) | Select-Object -First 1
    if (-not $asset) { return $null }

    $url = [string]$asset.browser_download_url
    $digest = [string]$asset.digest
    if ([string]::IsNullOrWhiteSpace($url) -or -not $url.StartsWith("https://github.com/$Repository/releases/download/", [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected manager-owned asset URL for '$Name': $url"
    }
    if ([string]::IsNullOrWhiteSpace($digest) -or -not $digest.StartsWith("sha256:", [StringComparison]::OrdinalIgnoreCase)) {
        throw "Manager-owned seed asset '$Name' has no GitHub SHA-256 digest."
    }

    [pscustomobject]@{
        Name = $Name
        Url = $url
        Expected = $digest.Substring(7).ToLowerInvariant()
    }
}

function Resolve-PatternMatches {
    param([Parameter(Mandatory=$true)][string]$Pattern)

    $lastError = $null
    for ($attempt = 1; $attempt -le $ConsistencyAttempts; $attempt++) {
        try {
            $release = Get-SeedRelease
            $matches = @($release.assets | Where-Object { [string]$_.name -like $Pattern })
            if ($matches.Count -gt 0) { return $matches }
            $lastError = "Manager-owned runtime seed '$ReleaseTag' has no asset matching '$Pattern'."
        }
        catch {
            $lastError = $_.Exception.Message
        }

        if ($attempt -lt $ConsistencyAttempts) {
            Write-Warning "Runtime seed metadata is temporarily inconsistent for '$Pattern' (attempt $attempt/$ConsistencyAttempts): $lastError"
            Start-Sleep -Seconds ([Math]::Min(2 * $attempt, 6))
        }
    }

    throw $lastError
}

$downloaded = @()
foreach ($pattern in $Assets) {
    $matches = @(Resolve-PatternMatches -Pattern $pattern)
    foreach ($initialAsset in $matches) {
        $name = [string]$initialAsset.name
        $destinationPath = Join-Path $destinationRoot $name
        $temporaryPath = "$destinationPath.download"
        $validated = $false
        $lastExpected = ""
        $lastActual = ""
        $lastError = ""

        for ($attempt = 1; $attempt -le $ConsistencyAttempts; $attempt++) {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue

            try {
                $release = Get-SeedRelease
                $metadata = Get-ValidatedAssetMetadata -Release $release -Name $name
                if (-not $metadata) {
                    throw "Manager-owned seed asset '$name' is temporarily absent while '$ReleaseTag' is being refreshed."
                }

                $lastExpected = [string]$metadata.Expected
                Invoke-WebRequest -Headers $headers -Uri $metadata.Url -OutFile $temporaryPath
                $lastActual = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash.ToLowerInvariant()

                if ($lastActual -eq $lastExpected) {
                    $validated = $true
                    break
                }

                # A release asset updated between metadata lookup and download is valid only
                # when a fresh GitHub digest now authenticates the bytes we actually received.
                $refreshedRelease = Get-SeedRelease
                $refreshedMetadata = Get-ValidatedAssetMetadata -Release $refreshedRelease -Name $name
                if ($refreshedMetadata -and $lastActual -eq [string]$refreshedMetadata.Expected) {
                    $lastExpected = [string]$refreshedMetadata.Expected
                    Write-Warning "Manager-owned seed asset '$name' rotated during download; accepted only after refreshed GitHub SHA-256 validation."
                    $validated = $true
                    break
                }

                $lastError = "SHA-256 mismatch for manager-owned seed asset '$name'. Expected $lastExpected, got $lastActual."
            }
            catch {
                $lastError = $_.Exception.Message
            }

            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
            if ($attempt -lt $ConsistencyAttempts) {
                Write-Warning "Runtime seed asset '$name' changed or was unavailable during download (attempt $attempt/$ConsistencyAttempts): $lastError"
                Start-Sleep -Seconds ([Math]::Min(2 * $attempt, 6))
            }
        }

        if (-not $validated) {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
            if ([string]::IsNullOrWhiteSpace($lastError)) {
                $lastError = "Unable to validate manager-owned seed asset '$name' after $ConsistencyAttempts consistency attempts."
            }
            throw $lastError
        }

        Move-Item -LiteralPath $temporaryPath -Destination $destinationPath -Force
        $downloaded += $destinationPath
        Write-Host "Validated manager-owned seed asset: $name ($lastActual)"
    }
}

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    "directory=$destinationRoot" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "assets=$($downloaded -join ';')" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
