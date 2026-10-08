param(
    [Parameter(Mandatory=$true)][string]$InputDirectory,
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [Parameter(Mandatory=$true)][string]$TimestampUtc,
    [ValidateSet("Store", "Optimal")][string]$Compression = "Store"
)

$ErrorActionPreference = "Stop"

$inputRoot = (Resolve-Path -LiteralPath $InputDirectory).Path
$destination = if ([IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
}
else {
    Join-Path (Get-Location).Path $OutputPath
}

$nestedDirectories = @(Get-ChildItem -LiteralPath $inputRoot -Directory -ErrorAction SilentlyContinue)
if ($nestedDirectories.Count -gt 0) {
    throw "Deterministic flat ZIP input must not contain subdirectories: $($nestedDirectories.Name -join ', ')"
}

$inputs = @(Get-ChildItem -LiteralPath $inputRoot -File | Sort-Object Name)
if ($inputs.Count -eq 0) {
    throw "Deterministic flat ZIP input directory is empty: $inputRoot"
}

$duplicateNames = @($inputs | Group-Object { $_.Name.ToLowerInvariant() } | Where-Object Count -gt 1)
if ($duplicateNames.Count -gt 0) {
    throw "Deterministic flat ZIP contains case-insensitive duplicate names: $($duplicateNames.Name -join ', ')"
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

# ZIP timestamps cannot represent dates before 1980-01-01.
if ($archiveTimestamp.UtcDateTime -lt [DateTime]::SpecifyKind([DateTime]"1980-01-01T00:00:00", [DateTimeKind]::Utc)) {
    throw "Deterministic ZIP timestamp must be on or after 1980-01-01 UTC."
}

$parent = Split-Path -Parent $destination
if (-not [string]::IsNullOrWhiteSpace($parent)) {
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
}
if (Test-Path -LiteralPath $destination) {
    Remove-Item -LiteralPath $destination -Force
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$compressionLevel = if ($Compression -eq "Optimal") {
    [IO.Compression.CompressionLevel]::Optimal
}
else {
    [IO.Compression.CompressionLevel]::NoCompression
}

$fileStream = [IO.File]::Open(
    $destination,
    [IO.FileMode]::CreateNew,
    [IO.FileAccess]::ReadWrite,
    [IO.FileShare]::None)

try {
    $archive = [IO.Compression.ZipArchive]::new(
        $fileStream,
        [IO.Compression.ZipArchiveMode]::Create,
        $false)

    try {
        foreach ($inputFile in $inputs) {
            $entry = $archive.CreateEntry($inputFile.Name, $compressionLevel)
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

if (!(Test-Path -LiteralPath $destination) -or (Get-Item -LiteralPath $destination).Length -le 0) {
    throw "Deterministic flat ZIP was not generated correctly."
}

$verifyStream = [IO.File]::OpenRead($destination)
try {
    $verifyArchive = [IO.Compression.ZipArchive]::new(
        $verifyStream,
        [IO.Compression.ZipArchiveMode]::Read,
        $false)
    try {
        $actualNames = @($verifyArchive.Entries | ForEach-Object FullName | Sort-Object)
        $expectedNames = @($inputs | ForEach-Object Name | Sort-Object)

        if (($actualNames -join "|") -ne ($expectedNames -join "|")) {
            throw "Deterministic flat ZIP entry set mismatch. Expected '$($expectedNames -join ", ")', got '$($actualNames -join ", ")'."
        }

        foreach ($entry in $verifyArchive.Entries) {
            if ($entry.LastWriteTime.UtcDateTime -ne $archiveTimestamp.UtcDateTime) {
                throw "Deterministic flat ZIP timestamp mismatch for $($entry.FullName)."
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

$sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Deterministic flat ZIP generated: $destination"
Write-Host "SHA-256: $sha256"

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    "sha256=$sha256" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
