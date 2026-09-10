$ErrorActionPreference = 'Stop'

# Render the Better SS vector mark into the Windows application icon.
Add-Type -AssemblyName PresentationCore, WindowsBase, System.Drawing

$brandPath = 'M197.9,330.5v-163.9h62.8c11.8,0,21.7,1.9,29.6,5.6,7.9,3.7,13.8,8.8,17.7,15.2,3.9,6.4,5.9,13.7,5.9,21.8s-1.3,12.4-3.8,17.1-6,8.6-10.3,11.5c-4.3,2.9-9.2,5-14.5,6.3v1.6c5.8.3,11.4,2.1,16.8,5.3,5.4,3.2,9.8,7.8,13.2,13.6,3.4,5.9,5.1,13,5.1,21.3s-2.1,16.1-6.2,22.9c-4.1,6.8-10.3,12.1-18.6,16-8.3,3.9-18.7,5.8-31.2,5.8h-66.4ZM227.6,236.3h29.4c5.1,0,9.7-.9,13.8-2.8,4.1-1.9,7.4-4.6,9.7-8.1,2.4-3.5,3.6-7.6,3.6-12.4s-2.2-11.6-6.7-15.7c-4.5-4.1-11.1-6.2-19.8-6.2h-30v45.2ZM227.6,305.7h31.9c10.8,0,18.6-2.1,23.3-6.2,4.8-4.1,7.2-9.5,7.2-16s-1.2-9.2-3.6-13.2c-2.4-3.9-5.8-7-10.2-9.3-4.4-2.3-9.7-3.4-15.8-3.4h-32.7v48Z'
$cornerPaths = @(
    'M170.1,66.2 L170.1,105 L105.1,105 L105.1,170.7 L66.3,170.7 L66.3,66.2 Z',
    'M170.1,433.8 L170.1,395 L105.1,395 L105.1,329.3 L66.3,329.3 L66.3,433.8 Z',
    'M329.9,66.2 L329.9,105 L394.9,105 L394.9,170.7 L433.7,170.7 L433.7,66.2 Z',
    'M329.9,433.8 L329.9,395 L394.9,395 L394.9,329.3 L433.7,329.3 L433.7,433.8 Z'
)

$visual = [System.Windows.Media.DrawingVisual]::new()
$context = $visual.RenderOpen()
$context.DrawRectangle([System.Windows.Media.Brushes]::Black, $null, [System.Windows.Rect]::new(0, 0, 500, 500))
$context.DrawGeometry([System.Windows.Media.Brushes]::White, $null, [System.Windows.Media.Geometry]::Parse($brandPath))
foreach ($cornerPath in $cornerPaths) {
    $context.DrawGeometry([System.Windows.Media.Brushes]::White, $null, [System.Windows.Media.Geometry]::Parse($cornerPath))
}
$context.Close()

$rendered = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(500, 500, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
$rendered.Render($visual)
$pngEncoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
$pngEncoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rendered))
$pngStream = [System.IO.MemoryStream]::new()
$pngEncoder.Save($pngStream)
$pngStream.Position = 0

$sourceBitmap = [System.Drawing.Bitmap]::FromStream($pngStream)
$iconBitmap = [System.Drawing.Bitmap]::new(256, 256)
$graphics = [System.Drawing.Graphics]::FromImage($iconBitmap)
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.DrawImage($sourceBitmap, 0, 0, 256, 256)
$graphics.Dispose()
$sourceBitmap.Dispose()
$pngStream.Dispose()

$icon = [System.Drawing.Icon]::FromHandle($iconBitmap.GetHicon())
$iconPath = Join-Path $PSScriptRoot 'BetterSS\Assets\better-ss.ico'
$iconStream = [System.IO.File]::Create($iconPath)
$icon.Save($iconStream)
$iconStream.Dispose()
$icon.Dispose()
$iconBitmap.Dispose()
