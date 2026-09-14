# Export the suite-style master to Thunderstore's 256x256 icon size.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$clockMasterPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../assets/icon-master.png'))
$clockIconPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../icon.png'))
$clockMaster = [Drawing.Image]::FromFile($clockMasterPath)
$clockOutput = [Drawing.Bitmap]::new(256, 256)
$clockGraphics = [Drawing.Graphics]::FromImage($clockOutput)
try {
    $clockGraphics.Clear([Drawing.Color]::Black)
    $clockGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $clockGraphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $clockGraphics.DrawImage($clockMaster, 0, 0, 256, 256)
    $clockOutput.Save($clockIconPath, [Drawing.Imaging.ImageFormat]::Png)
} finally { $clockGraphics.Dispose(); $clockOutput.Dispose(); $clockMaster.Dispose() }
Get-Item -LiteralPath $clockIconPath | Select-Object FullName,Length
