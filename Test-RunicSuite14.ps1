[CmdletBinding()]
param([string]$EvidencePath = '')

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$transcriptStarted = $false
if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    $resolvedEvidencePath = [System.IO.Path]::GetFullPath($EvidencePath)
    $evidenceParent = [System.IO.Path]::GetDirectoryName($resolvedEvidencePath)
    if (-not (Test-Path -LiteralPath $evidenceParent -PathType Container)) {
        throw "Audit evidence directory is missing: $evidenceParent"
    }
    if (Test-Path -LiteralPath $resolvedEvidencePath) {
        throw "Refusing to overwrite existing audit evidence: $resolvedEvidencePath"
    }
    Start-Transcript -LiteralPath $resolvedEvidencePath | Out-Null
    $transcriptStarted = $true
}

$projects = @(
    'RunicStorage\Tests\RunicStorage.Tests.csproj'
    'RunicCrafting\Tests\RunicCrafting.Tests.csproj'
    'RunicAgriculture\Tests\RunicAgriculture.Tests.csproj'
    'RunicProduction\Tests\RunicProduction.Tests.csproj'
    'RunicPrecisionBuildTool.Tests\RunicPrecisionBuildTool.Tests.csproj'
    'RunicInventory.Tests\RunicInventory.Tests.csproj'
    'RunicPortals.Tests\RunicPortals.Tests.csproj'
    'RunicExploration.Tests\RunicExploration.Tests.csproj'
    'RunicAwareness.Tests\RunicAwareness.Tests.csproj'
    'RunicInteraction.Tests\RunicInteraction.Tests.csproj'
    'RunicSafety.Tests\RunicSafety.Tests.csproj'
    'RunicVelocity.Tests\RunicVelocity.Tests.csproj'
    'RunicSentinel.Tests\RunicSentinel.Tests.csproj'
    'RunicSentinelClient.Tests\RunicSentinelClient.Tests.csproj'
    'RunicSentinelServer.Tests\RunicSentinelServer.Tests.csproj'
    'RunicWorldEngine.Tests\RunicWorldEngine.Tests.csproj'
    'RunicBuildCamera.Tests\RunicBuildCamera.Tests.csproj'
    'RunicSuite14.Tests\RunicSuite14.Tests.csproj'
    '..\StandaloneItemStands\Tests\RunicDisplayStands.ContractTests.csproj'
)

Push-Location -LiteralPath $PSScriptRoot
try {
    foreach ($project in $projects) {
        $full = Join-Path $PSScriptRoot $project
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
            throw "Missing audit project: $project"
        }
        Write-Output "AUDIT_START $project"
        & dotnet build $full -c Release --no-restore -p:TreatWarningsAsErrors=true
        if ($LASTEXITCODE -ne 0) {
            throw "Audit project warning/error gate failed with exit code $LASTEXITCODE`: $project"
        }
        & dotnet run --project $full -c Release --no-build
        if ($LASTEXITCODE -ne 0) {
            throw "Audit project failed with exit code $LASTEXITCODE`: $project"
        }
        Write-Output "AUDIT_PASS $project"
    }
    Write-Output "RUNIC_THUNDERSTORE_RELEASE_AUDIT_PASS projects=$($projects.Count)"
}
finally {
    Pop-Location
    if ($transcriptStarted) { Stop-Transcript | Out-Null }
}
