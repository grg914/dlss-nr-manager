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
    $context = $visual.RenderOpen()

    $sourceRatio = $bitmap.PixelWidth / [double]$bitmap.PixelHeight
    if ($sourceRatio -ge 1.0) {
        $drawWidth = [double]$size
        $drawHeight = [double]$size / $sourceRatio
        $x = 0.0
        $y = ([double]$size - $drawHeight) / 2.0
    }
    else {
        $drawHeight = [double]$size
        $drawWidth = [double]$size * $sourceRatio
        $x = ([double]$size - $drawWidth) / 2.0
        $y = 0.0
    }

    $context.DrawImage(
        $bitmap,
        (New-Object System.Windows.Rect($x, $y, $drawWidth, $drawHeight))
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
