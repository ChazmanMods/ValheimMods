param([string]$Group = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$checks = Join-Path $projectRoot 'tests/RunicCrafting.Core.Checks/RunicCrafting.Core.Checks.csproj'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$evidence = Join-Path $projectRoot 'evidence'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$buildLog = Join-Path $evidence "core-build-$stamp.log"
& dotnet build $checks -c Release --nologo 2>&1 | Tee-Object -FilePath $buildLog
if ($LASTEXITCODE -ne 0) { throw "Core build failed; see $buildLog" }
$report = Join-Path $evidence "core-checks-$stamp.json"
$checkArgs = @('--evidence', $report)
if ($Group) { $checkArgs += @('--group', $Group) }
$runner = Join-Path $projectRoot 'tests/RunicCrafting.Core.Checks/bin/Release/net9.0/RunicCrafting.Core.Checks.dll'
& dotnet $runner @checkArgs
if ($LASTEXITCODE -ne 0) { throw "Core checks failed; see $report" }
$sourceRoots = @((Join-Path $projectRoot 'src/RunicCrafting.Core'), (Join-Path $projectRoot 'tests/RunicCrafting.Core.Checks'))
$hashes = @(foreach ($sourceRoot in $sourceRoots) {
    Get-ChildItem -LiteralPath $sourceRoot -File | Where-Object { $_.Extension -in @('.cs', '.csproj') } | ForEach-Object {
        [ordered]@{ path = $_.FullName.Substring($projectRoot.Length + 1); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }
})
$dll = Join-Path $projectRoot 'src/RunicCrafting.Core/bin/Release/netstandard2.1/RunicCrafting.Core.dll'
[ordered]@{ report = $report; buildLog = $buildLog; assemblySha256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash; sources = $hashes } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence "core-build-$stamp.json") -Encoding utf8
Write-Output "Core development evidence: $report"
Write-Output 'This library is not an installable BepInEx client or server plugin.'
