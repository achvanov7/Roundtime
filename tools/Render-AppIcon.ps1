# Rebuild the Windows icon and PNG preview from the editable WPF vector mark.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$assetDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\Roundtime.App\Assets'
$markFile = [System.IO.File]::OpenRead((Join-Path $assetDirectory 'Roundtime.Mark.xaml'))
try { $mark = [System.Windows.Markup.XamlReader]::Load($markFile) } finally { $markFile.Dispose() }
$mark.Freeze()
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $context.DrawImage($mark, (New-Object System.Windows.Rect(0, 0, $size, $size)))
    $context.Close()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32))
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $buffer = New-Object System.IO.MemoryStream
    try {
        $encoder.Save($buffer)
        $frames += ,$buffer.ToArray()
    } finally { $buffer.Dispose() }
}
$iconFile = [System.IO.File]::Create((Join-Path $assetDirectory 'Roundtime.ico'))
$writer = New-Object System.IO.BinaryWriter($iconFile)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
[System.IO.File]::WriteAllBytes((Join-Path $assetDirectory 'Roundtime.png'), $frames[-1])
Write-Output 'Rendered Roundtime.ico (16–256px) and Roundtime.png'
