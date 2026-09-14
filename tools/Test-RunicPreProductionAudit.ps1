[CmdletBinding()]
param([string]$EvidenceDirectory, [string]$CharacterFixture)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $EvidenceDirectory) { throw 'Supply a new evidence directory.' }
if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Evidence directory must be new.' }
$null = New-Item -ItemType Directory -Path $EvidenceDirectory
$env:BEPINEX_PROFILE = 'C:\Users\Charles Sammons\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Valheim 1.0\BepInEx'
$env:VALHEIM_INSTALL = 'E:\SteamLibrary\steamapps\common\Valheim'
if ($CharacterFixture) {
    if (-not (Test-Path -LiteralPath $CharacterFixture -PathType Leaf)) { throw 'Missing backup character fixture.' }
    $env:RUNIC_CHARACTER_FIXTURE = [IO.Path]::GetFullPath($CharacterFixture)
}
$results = @()
Push-Location $repo
try {
    $modules = Get-ChildItem -Directory -Filter Runic* | Where-Object { Test-Path (Join-Path $_.FullName 'manifest.json') }
    foreach ($module in $modules) {
        $project = Join-Path $module.FullName ($module.Name + '.csproj')
        if (-not (Test-Path -LiteralPath $project)) { continue }
        $log = Join-Path $EvidenceDirectory ($module.Name + '-build.log')
        & dotnet build $project -c Release --no-restore -v quiet *> $log
        $code = $LASTEXITCODE
        $results += [pscustomobject]@{Target=$module.Name;Check='build';ExitCode=$code;Log=$log}
        Write-Output "$($module.Name) build: $code"
    }
    $projects = & rg --files -g '*Tests.csproj' -g '!**/obj/**' -g '!**/bin/**'
    foreach ($project in $projects) {
        $name = [IO.Path]::GetFileNameWithoutExtension($project)
        $log = Join-Path $EvidenceDirectory ($name + '.log')
        & dotnet run --project $project -c Release --no-restore *> $log
        $code = $LASTEXITCODE
        $results += [pscustomobject]@{Target=$name;Check='tests';ExitCode=$code;Log=$log}
        Write-Output "$name tests: $code"
    }
    $displayRoot = 'E:\Valheim Mods\StandaloneItemStands'
    $log = Join-Path $EvidenceDirectory 'RunicDisplayStands-build.log'
    & dotnet build (Join-Path $displayRoot 'RunicDisplayStands.csproj') -c Release --no-restore -v quiet *> $log
    $code = $LASTEXITCODE
    $results += [pscustomobject]@{Target='RunicDisplayStands';Check='build';ExitCode=$code;Log=$log}
    Write-Output "RunicDisplayStands build: $code"
    $log = Join-Path $EvidenceDirectory 'RunicDisplayStands.ContractTests.log'
    & dotnet run --project (Join-Path $displayRoot 'Tests\RunicDisplayStands.ContractTests.csproj') -c Release --no-restore *> $log
    $code = $LASTEXITCODE
    $results += [pscustomobject]@{Target='RunicDisplayStands.ContractTests';Check='tests';ExitCode=$code;Log=$log}
    Write-Output "RunicDisplayStands tests: $code"
    $results | ConvertTo-Json | Out-File -LiteralPath (Join-Path $EvidenceDirectory 'results.json') -Encoding utf8
}
finally { Pop-Location }
