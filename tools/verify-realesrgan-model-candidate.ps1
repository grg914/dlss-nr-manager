param(
    [string]$AssetDirectory = 'release-assets',
    [string]$SourceFile = 'Services/AiUpscaleService.cs',
    [string]$ManifestPath = 'release-assets/realesrgan-model-manifest.json'
)

$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($SourceFile)
$directory = [IO.Path]::GetFullPath($AssetDirectory)
if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing application model pins: $source" }
if (!(Test-Path -LiteralPath $directory -PathType Container)) { throw "Missing staged model assets: $directory" }

# Read the same locked 12 model records consumed by AiUpscaleService.
# Do not silently repin the application to a damaged public release.
$sourceText = [IO.File]::ReadAllText($source)
$pattern = '(?s)new\(\s*"(?<name>realesr[^"]+\.(?:param|bin))"\s*,\s*"(?<url>https://github\.com/grg914/dlss-nr-manager/releases/latest/download/[^"]+)"\s*,\s*(?<size>[0-9]+)\s*,\s*"(?<blob>[a-fA-F0-9]{40})"\s*\)'
$models = @([regex]::Matches($sourceText, $pattern))
if ($models.Count -ne 12) {
    throw "Expected exactly 12 pinned Real-ESRGAN model records in AiUpscaleService; found $($models.Count)."
}
$seen = @{}
$verified = @(
    foreach ($record in $models) {
        $name = $record.Groups['name'].Value
        $url = $record.Groups['url'].Value
        $expectedSize = [long]::Parse($record.Groups['size'].Value, [Globalization.CultureInfo]::InvariantCulture)
        $expectedBlob = $record.Groups['blob'].Value.ToLowerInvariant()
        if ($name -notmatch '^realesr[a-z0-9-]*\.(param|bin)$' -or
            $url -cne ("https://github.com/grg914/dlss-nr-manager/releases/latest/download/" + $name) -or
            $seen.ContainsKey($name)) {
            throw "Invalid or duplicate source-pinned model name/URL: $name"
        }
        $seen[$name] = $true
        $file = Join-Path $directory $name
        if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Staged model missing: $name" }
        $actualSize = (Get-Item -LiteralPath $file).Length
        if ($actualSize -ne $expectedSize) {
            throw "Staged model size mismatch: $name expected=$expectedSize actual=$actualSize"
        }
        $actualBlob = ((& git hash-object --no-filters -- $file) | Out-String).Trim().ToLowerInvariant()
        if ($LASTEXITCODE -ne 0 -or $actualBlob -cne $expectedBlob) {
            throw "Staged model raw Git blob mismatch: $name expected=$expectedBlob actual=$actualBlob"
        }
        $sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($sha256 -notmatch '^[a-f0-9]{64}$') { throw "Invalid SHA-256 for $name" }
        [ordered]@{
            name = $name
            size_bytes = $expectedSize
            git_blob_sha1 = $expectedBlob
            sha256 = $sha256
        }
    }
)
# Refuse unexpected model-looking extras as well as omissions.
$extra = @(Get-ChildItem -LiteralPath $directory -File | Where-Object {
    $_.Name -match '^realesr.*\.(param|bin)$' -and !$seen.ContainsKey($_.Name)
})
if ($extra.Count -gt 0) { throw "Unexpected Real-ESRGAN model asset: $($extra[0].Name)" }

$commit = ((& git rev-parse HEAD) | Out-String).Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') {
    throw 'Unable to identify the exact source commit for this model candidate.'
}
$receipt = [ordered]@{
    schema_version = 1
    status = 'candidate-only-not-a-release-approval'
    source_commit = $commit
    verification = 'raw-bytes-size-git-blob-sha1-sha256'
    files = @($verified)
}
$output = [IO.Path]::GetFullPath($ManifestPath)
if ($output.StartsWith($directory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -eq $false) {
    throw 'Manifest output must remain inside the staged release-assets directory.'
}
$json = $receipt | ConvertTo-Json -Depth 6
[IO.File]::WriteAllText($output, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Host "PASS: 12/12 source-pinned Real-ESRGAN model assets verified by size, raw Git blob and SHA-256."
Write-Host "Candidate-only receipt: $output"
