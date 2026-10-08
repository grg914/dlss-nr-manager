param(
    [Parameter(Mandatory=$true)][string]$ExecutablePath,
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [Parameter(Mandatory=$true)][string]$TimestampUtc,
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$exe = (Resolve-Path -LiteralPath $ExecutablePath).Path
$parsedTimestamp = ([DateTimeOffset]::Parse($TimestampUtc)).ToUniversalTime()
$zipSecond = $parsedTimestamp.Second - ($parsedTimestamp.Second % 2)
$timestamp = [DateTimeOffset]::new(
    $parsedTimestamp.Year,
    $parsedTimestamp.Month,
    $parsedTimestamp.Day,
    $parsedTimestamp.Hour,
    $parsedTimestamp.Minute,
    $zipSecond,
    [TimeSpan]::Zero)

$entries = @(
    @{ Source = $exe; Name = "DlssNrManager.exe" },
    @{ Source = (Join-Path $Root "README.md"); Name = "README.md" },
    @{ Source = (Join-Path $Root "LICENSE"); Name = "LICENSE" },
    @{ Source = (Join-Path $Root "docs\THIRD_PARTY_NOTICES.md"); Name = "THIRD_PARTY_NOTICES.md" },
    @{ Source = (Join-Path $Root "docs\DATA_PRIVACY.md"); Name = "DATA_PRIVACY.md" }
)

foreach ($entry in $entries) {
    if (!(Test-Path -LiteralPath $entry.Source)) {
        throw "Application package input is missing: $($entry.Source)"
    }
}

$destination = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
} else {
    Join-Path $Root $OutputPath
}

$parent = Split-Path -Parent $destination
if (-not [string]::IsNullOrWhiteSpace($parent)) {
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
}
if (Test-Path -LiteralPath $destination) {
    Remove-Item -LiteralPath $destination -Force
}

$fileStream = [System.IO.File]::Open(
    $destination,
    [System.IO.FileMode]::CreateNew,
    [System.IO.FileAccess]::ReadWrite,
    [System.IO.FileShare]::None)

try {
    $archive = [System.IO.Compression.ZipArchive]::new(
        $fileStream,
        [System.IO.Compression.ZipArchiveMode]::Create,
        $false)

    try {
        foreach ($item in $entries) {
            $zipEntry = $archive.CreateEntry(
                [string]$item.Name,
                [System.IO.Compression.CompressionLevel]::Optimal)
            $zipEntry.LastWriteTime = $timestamp

            $input = [System.IO.File]::OpenRead([string]$item.Source)
            try {
                $output = $zipEntry.Open()
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

if (!(Test-Path -LiteralPath $destination) -or (Get-Item -LiteralPath $destination).Length -lt 1024) {
    throw "Application ZIP was not generated correctly."
}

# Verify exact expected entry set and deterministic timestamps.
$verifyStream = [System.IO.File]::OpenRead($destination)
try {
    $verifyArchive = [System.IO.Compression.ZipArchive]::new(
        $verifyStream,
        [System.IO.Compression.ZipArchiveMode]::Read,
        $false)
    try {
        $actualNames = @($verifyArchive.Entries | ForEach-Object FullName | Sort-Object)
        $expectedNames = @($entries | ForEach-Object Name | Sort-Object)

        if (($actualNames -join "|") -ne ($expectedNames -join "|")) {
            throw "Application ZIP entry set mismatch. Expected '$($expectedNames -join ", ")', got '$($actualNames -join ", ")'."
        }

        foreach ($zipEntry in $verifyArchive.Entries) {
            if ($zipEntry.LastWriteTime.UtcDateTime -ne $timestamp.UtcDateTime) {
                throw "Application ZIP timestamp mismatch for $($zipEntry.FullName)."
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
Write-Host "Deterministic application ZIP generated: $destination"
Write-Host "SHA-256: $sha256"
