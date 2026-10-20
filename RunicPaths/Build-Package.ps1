$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
dotnet build (Join-Path $projectRoot 'RunicPaths.csproj') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
dotnet run --project (Join-Path $projectRoot 'Tests\RunicPaths.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Behavioral tests failed' }
$stage = Join-Path $projectRoot 'dist\RunicPaths-0.3.1'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'bin\Release\netstandard2.1\RunicPaths.dll') -Destination $stage
foreach ($file in @('README.md', 'CHANGELOG.md', 'manifest.json', 'VALIDATION.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $stage
}
# A simple code-drawn trail icon; no external artwork or game assets are bundled.
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object Drawing.Bitmap 256,256
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([Drawing.Color]::Transparent)
$trail = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(195,166,108)),22
$trail.StartCap = [Drawing.Drawing2D.LineCap]::Round
$trail.EndCap = [Drawing.Drawing2D.LineCap]::Round
$curve = New-Object Drawing.Drawing2D.GraphicsPath
$curve.AddBezier(42,205,180,220,70,48,214,50)
$graphics.DrawPath($trail,$curve)
$marker = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(237,226,190))
$graphics.FillEllipse($marker,29,192,26,26)
$graphics.FillEllipse($marker,201,37,26,26)
$bitmap.Save((Join-Path $stage 'icon.png'),[Drawing.Imaging.ImageFormat]::Png)
$curve.Dispose(); $trail.Dispose(); $marker.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
$archive = Join-Path $projectRoot 'dist\Chazman-RunicPaths-0.3.1.zip'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -Force
Get-FileHash -LiteralPath $archive -Algorithm SHA256 | Format-List
Write-Output $archive



