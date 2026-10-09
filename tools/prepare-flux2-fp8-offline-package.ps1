[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AssetsDirectory,
    [string]$Version = "0.1.0",
    [string]$StagingManifest = "",
    [switch]$KeepExpandedFiles
)
# Offline publisher preparation only: never contacts GitHub or publishes.
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if (-not $StagingManifest) {
    $StagingManifest = Join-Path $root "manifests/ai-studio-staging-assets-20261009.json"
}
$assetsRoot = (Resolve-Path -LiteralPath $AssetsDirectory).Path
$lock = Get-Content -LiteralPath $StagingManifest -Raw | ConvertFrom-Json
if ($lock.schema -ne 1 -or $lock.status -ne "STAGING_ONLY_NOT_INSTALLABLE" -or $lock.asset_count -ne 13) {
    throw "Expected the immutable 13-file staging asset lock."
}
$expectedByName = @{}
foreach ($asset in $lock.assets) {
    if ($expectedByName.ContainsKey($asset.name)) { throw "Duplicate locked asset name." }
    $expectedByName[$asset.name] = $asset
}

function Assert-Asset([string]$Name) {
    if (-not $expectedByName.ContainsKey($Name) -or
        $Name -notmatch '^[A-Za-z0-9_.-]+$') { throw "Unexpected asset: $Name" }
    $path = Join-Path $assetsRoot $Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing local asset: $Name. Download it separately from your draft release first."
    }
    $file = Get-Item -LiteralPath $path
    if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $file.Length -ne [int64]$expectedByName[$Name].bytes) {
        throw "Size or reparse-point mismatch for $Name."
    }
    $digest = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($digest -ne [string]$expectedByName[$Name].github_asset_sha256) {
        throw "Staging asset SHA-256 mismatch: $Name."
    }
    return $path
}

$tools = Assert-Asset "7zr.exe"
foreach ($entry in $lock.assets) { [void](Assert-Asset $entry.name) }
$work = Join-Path $root ("build-local/ai-studio-offline-work-" + [Guid]::NewGuid().ToString("N"))
$expanded = Join-Path $work "flux2-klein-4b"
New-Item -ItemType Directory -Path $expanded -Force | Out-Null

try {
    $models = @(
        @{
            Volume = "flux-2-klein-4b-fp8.7z.001"
            File = "flux-2-klein-4b-fp8.safetensors"
            Subdir = "diffusion_models"
            Sha256 = "97ed34fe0567e436200f2faee3939b88f2b5d99f8af2a4dc16532c4245c0ccb6"
        },
        @{
            Volume = "qwen_3_4b.7z.001"
            File = "qwen_3_4b.safetensors"
            Subdir = "text_encoders"
            Sha256 = "6c671498573ac2f7a5501502ccce8d2b08ea6ca2f661c458e708f36b36edfc5a"
        }
    )

    foreach ($model in $models) {
        $source = Join-Path $assetsRoot $model.Volume
        $extractTo = Join-Path $work ("extract-" + $model.Subdir)
        New-Item -ItemType Directory -Path $extractTo -Force | Out-Null
        $listing = & $tools l -slt $source 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Cannot read 7z volume: $($model.Volume)" }
        $paths = @($listing | Where-Object { $_ -like "Path = *" } |
                   ForEach-Object { $_.Substring(7).Trim() })
        if ($paths.Count -ne 2 -or $paths[1] -cne $model.File) {
            throw "7z archive contains an unexpected filename or multiple members: $($model.Volume)"
        }

        & $tools x -y -bd "-o$extractTo" $source | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "7z extraction failed: $($model.Volume)" }
        $files = @(Get-ChildItem -LiteralPath $extractTo -Force -Recurse)
        if ($files.Count -ne 1 -or $files[0].Name -cne $model.File -or
            ($files[0].Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            $files[0].PSIsContainer) { throw "Unexpected 7z extraction tree." }
        $digest = (Get-FileHash -LiteralPath $files[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($digest -ne $model.Sha256) {
            throw "Extracted model SHA-256 does NOT match upstream Hugging Face: $($model.File)"
        }
        $targetDir = Join-Path $expanded $model.Subdir
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
        Move-Item -LiteralPath $files[0].FullName -Destination (Join-Path $targetDir $model.File)
    }

    $vae = Assert-Asset "flux2-vae.safetensors"
    if ((Get-FileHash -LiteralPath $vae -Algorithm SHA256).Hash.ToLowerInvariant() -ne
        "868fe7b343cc8f3a19dbcfcafbc3d5f888802be3f89bd81b65b3621a066ce8f3") {
        throw "VAE differs from official Hugging Face reference."
    }
    $vaeDir = Join-Path $expanded "vae"
    New-Item -ItemType Directory -Path $vaeDir -Force | Out-Null
    Copy-Item -LiteralPath $vae -Destination (Join-Path $vaeDir "flux2-vae.safetensors")

    # Preserve UI examples only as references, not API-ready prompt graphs.
    $examples = Join-Path $expanded "workflow-examples-not-api"
    New-Item -ItemType Directory -Path $examples -Force | Out-Null
    foreach ($name in @("image_flux2_klein_text_to_image.json",
                        "image_flux2_klein_image_edit_4b_distilled.json")) {
        Copy-Item -LiteralPath (Join-Path $assetsRoot $name) -Destination (Join-Path $examples $name)
    }

    $options = @{
        SourcePath = $expanded
        PackageId = "flux2-klein-4b"
        DisplayName = "FLUX.2 Klein 4B FP8 (verified offline staging)"
        Version = $Version
        License = "Apache-2.0 (requires final NOTICE review)"
        SourceUri = "https://huggingface.co/black-forest-labs/FLUX.2-klein-4b-fp8"
        ReleaseTag = "ai-studio-flux2-klein-4b-$Version"
        InstallRelativePath = "models/flux2-klein-4b"
    }
    & (Join-Path $PSScriptRoot "publish-ai-studio-package.ps1") @options
    if ($LASTEXITCODE -ne 0) { throw "ZIP64 manager-owned package creation failed." }

    Write-Host "OFFLINE STAGING ONLY. ZIP64 chunks and manifest are in build-local/ai-studio-packages/flux2-klein-4b/$Version"
    Write-Host "Nothing was published, executed, or installed in AI Studio."
}
finally {
    if ($KeepExpandedFiles) {
        Write-Warning "Expanded original model files retained for inspection: $work"
    }
    else {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
