param(
    [Parameter(Mandatory=$true)][string]$SourceDirectory,
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [Parameter(Mandatory=$true)][string]$TimestampUtc
)

$ErrorActionPreference = "Stop"

$source = (Resolve-Path -LiteralPath $SourceDirectory).Path
$timestamp = [DateTimeOffset]::Parse($TimestampUtc).ToUniversalTime()

# ZIP timestamps cannot represent dates before 1980.
if ($timestamp.Year -lt 1980) {
    $timestamp = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
}

$destination = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
} else {
    Join-Path $PWD $OutputPath
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

$files = @(
    Get-ChildItem -LiteralPath $source -File -Recurse |
        Sort-Object { [System.IO.Path]::GetRelativePath($source, $_.FullName).Replace("\", "/") }
)

if ($files.Count -eq 0) {
    throw "Deterministic ZIP source directory is empty: $source"
}

$stream = [System.IO.File]::Open(
    $destination,
    [System.IO.FileMode]::CreateNew,
    [System.IO.FileAccess]::ReadWrite,
    [System.IO.FileShare]::None)

try {
    $zip = [System.IO.Compression.ZipArchive]::new(
        $stream,
        [System.IO.Compression.ZipArchiveMode]::Create,
        $false,
        [System.Text.Encoding]::UTF8)

    try {
        foreach ($file in $files) {
            $relative = [System.IO.Path]::GetRelativePath(
                $source,
                $file.FullName).Replace("\", "/")

            $entry = $zip.CreateEntry(
                $relative,
                [System.IO.Compression.CompressionLevel]::Optimal)

            $entry.LastWriteTime = $timestamp

            $input = [System.IO.File]::OpenRead($file.FullName)
            $output = $entry.Open()
            try {
                $input.CopyTo($output)
            }
            finally {
                $output.Dispose()
                $input.Dispose()
            }
        }
    }
    finally {
        $zip.Dispose()
    }
}
finally {
    $stream.Dispose()
}

if (!(Test-Path -LiteralPath $destination) -or
    (Get-Item -LiteralPath $destination).Length -le 0) {
    throw "Deterministic ZIP was not generated."
}

Write-Host "Deterministic ZIP generated: $destination"
Write-Host "SHA-256: $((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant())"
