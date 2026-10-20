param([string]$Configuration='Release')
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
& dotnet build (Join-Path $root 'ArcaneWaterWorks.csproj') -c $Configuration --nologo
if($LASTEXITCODE -ne 0){throw 'Build failed'}
$stage=Join-Path $root 'package-natural'
$plugins=Join-Path $stage 'plugins/ArcaneNaturalWaters'
New-Item -ItemType Directory -Force $plugins | Out-Null
Copy-Item -LiteralPath (Join-Path $root "bin/$Configuration/netstandard2.1/ArcaneNaturalWaters.dll") -Destination $plugins
foreach($name in @('README.md','CHANGELOG.md','THIRD_PARTY.md','manifest.json','icon.png')){
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $stage
}
$dist=Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$zip=Join-Path $dist 'Chazman-ArcaneNaturalWaters-0.9.18.zip'
$releaseFiles=@('plugins','README.md','CHANGELOG.md','THIRD_PARTY.md','manifest.json','icon.png') | ForEach-Object { Join-Path $stage $_ }
Compress-Archive -Path $releaseFiles -DestinationPath $zip -Force
(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash | Set-Content -LiteralPath ($zip+'.sha256')
Write-Output $zip




