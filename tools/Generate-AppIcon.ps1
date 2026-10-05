param(
    [Parameter(Mandatory = $true)]
    [string]$Source,

    [Parameter(Mandatory = $true)]
    [string]$Output
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

$sourcePath = [System.IO.Path]::GetFullPath($Source)
$outputPath = [System.IO.Path]::GetFullPath($Output)

if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "Logo source not found: $sourcePath"
}

$outputDirectory = [System.IO.Path]::GetDirectoryName($outputPath)
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

$bitmap = New-Object System.Windows.Media.Imaging.BitmapImage
$bitmap.BeginInit()
$bitmap.CacheOption = [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad
$bitmap.UriSource = New-Object System.Uri($sourcePath)
$bitmap.EndInit()
$bitmap.Freeze()

# Find the actual non-transparent artwork bounds. The branding PNG contains
# transparent breathing room; using the full canvas makes the visible mark too small
# in Explorer/Desktop and Windows then scales that tiny mark again, which looks blurry.
$converted = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap(
    $bitmap,
    [System.Windows.Media.PixelFormats]::Bgra32,
    $null,
    0
)
$converted.Freeze()

$width = $converted.PixelWidth
$height = $converted.PixelHeight
$stride = $width * 4
$pixels = New-Object byte[] ($stride * $height)
$converted.CopyPixels($pixels, $stride, 0)

$minX = $width
$minY = $height
$maxX = -1
$maxY = -1

for ($y = 0; $y -lt $height; $y++) {
    $row = $y * $stride
    for ($x = 0; $x -lt $width; $x++) {
        $alpha = $pixels[$row + ($x * 4) + 3]
        if ($alpha -gt 8) {
            if ($x -lt $minX) { $minX = $x }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }
}

if ($maxX -lt $minX -or $maxY -lt $minY) {
    $minX = 0
    $minY = 0
    $maxX = $width - 1
    $maxY = $height - 1
}

$contentWidth = $maxX - $minX + 1
$contentHeight = $maxY - $minY + 1
$contentSize = [Math]::Max($contentWidth, $contentHeight)

# Keep a small amount of transparent padding so the mark does not touch the edge.
$padding = [int][Math]::Ceiling($contentSize * 0.06)
$cropSize = [Math]::Min(
    [Math]::Max($contentSize + ($padding * 2), 1),
    [Math]::Min($width, $height)
)

$centerX = ($minX + $maxX) / 2.0
$centerY = ($minY + $maxY) / 2.0
$cropX = [int][Math]::Round($centerX - ($cropSize / 2.0))
$cropY = [int][Math]::Round($centerY - ($cropSize / 2.0))

$cropX = [Math]::Max(0, [Math]::Min($cropX, $width - $cropSize))
$cropY = [Math]::Max(0, [Math]::Min($cropY, $height - $cropSize))

$cropRect = New-Object System.Windows.Int32Rect($cropX, $cropY, $cropSize, $cropSize)
$squareBitmap = New-Object System.Windows.Media.Imaging.CroppedBitmap($converted, $cropRect)
$squareBitmap.Freeze()

Write-Host ("Icon source: {0}x{1}; content bounds: {2}x{3}; crop: {4}x{4}" -f $width, $height, $contentWidth, $contentHeight, $cropSize)

$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 192, 256)
$frames = New-Object System.Collections.Generic.List[object]

foreach ($size in $sizes) {
    $target = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(
        $size,
        $size,
        96,
        96,
        [System.Windows.Media.PixelFormats]::Pbgra32
    )

    $visual = New-Object System.Windows.Media.DrawingVisual
    [System.Windows.Media.RenderOptions]::SetBitmapScalingMode(
        $visual,
        [System.Windows.Media.BitmapScalingMode]::HighQuality
    )
    [System.Windows.Media.RenderOptions]::SetEdgeMode(
        $visual,
        [System.Windows.Media.EdgeMode]::Unspecified
    )
    $context = $visual.RenderOpen()

    $context.DrawImage(
        $squareBitmap,
        (New-Object System.Windows.Rect(0.0, 0.0, [double]$size, [double]$size))
    )
    $context.Close()

    $target.Render($visual)
    $target.Freeze()

    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add(
        [System.Windows.Media.Imaging.BitmapFrame]::Create($target)
    )

    $stream = New-Object System.IO.MemoryStream
    $encoder.Save($stream)
    $frames.Add([PSCustomObject]@{
        Size = $size
        Data = $stream.ToArray()
    })
    $stream.Dispose()
}

$file = [System.IO.File]::Open(
    $outputPath,
    [System.IO.FileMode]::Create,
    [System.IO.FileAccess]::Write,
    [System.IO.FileShare]::None
)
$writer = New-Object System.IO.BinaryWriter($file)

try {
    # ICONDIR
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$frames.Count)

    $offset = 6 + (16 * $frames.Count)

    foreach ($frame in $frames) {
        $sizeByte = if ($frame.Size -eq 256) { [byte]0 } else { [byte]$frame.Size }

        # ICONDIRENTRY
        $writer.Write($sizeByte)
        $writer.Write($sizeByte)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$frame.Data.Length)
        $writer.Write([UInt32]$offset)

        $offset += $frame.Data.Length
    }

    foreach ($frame in $frames) {
        $writer.Write([byte[]]$frame.Data)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

$sizeList = ($sizes -join ", ")
Write-Host "Generated multi-resolution ICO: $outputPath"
Write-Host "Sizes: $sizeList"
Write-Host "Bytes: $((Get-Item -LiteralPath $outputPath).Length)"
