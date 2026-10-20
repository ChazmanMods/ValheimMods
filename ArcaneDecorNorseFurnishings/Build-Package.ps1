$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
 dotnet build ArcaneDecorNorseFurnishings.csproj -c Release --nologo -v:q
 if($LASTEXITCODE -ne 0){throw 'Build failed'}
 python tools/check_assets.py
 if($LASTEXITCODE -ne 0){throw 'Asset check failed'}
 python ../tools/check_icon_backgrounds.py icon.png Assets/bed-icon.png
 if($LASTEXITCODE -ne 0){throw 'Icon check failed'}
 python tools/package.py
 if($LASTEXITCODE -ne 0){throw 'Packaging failed'}
 python ../tools/check_icon_backgrounds.py dist/Chazman-ArcaneDecor_NorseFurnishings-0.1.1-PRIVATE-TEST.zip dist/extracted-icon.png
 if($LASTEXITCODE -ne 0){throw 'Final icon check failed'}
} finally { Pop-Location }
