# Export the generated master at Thunderstore's 256x256 package size.
# Original placeholder retained in icon.svg and icon.legacy-placeholder.png.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$source = [Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'icon-master.png'))
$bitmap = [Drawing.Bitmap]::new(256,256)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$attributes = [Drawing.Imaging.ImageAttributes]::new()
try {
    $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $attributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
    $graphics.DrawImage($source,[Drawing.Rectangle]::new(0,0,256,256),0,0,$source.Width,$source.Height,[Drawing.GraphicsUnit]::Pixel,$attributes)
    $bitmap.Save((Join-Path $PSScriptRoot 'icon.png'),[Drawing.Imaging.ImageFormat]::Png)
} finally { $attributes.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $source.Dispose() }
