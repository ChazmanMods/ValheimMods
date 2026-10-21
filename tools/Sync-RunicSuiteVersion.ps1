[CmdletBinding()]
param(
    # Verify only; exit 1 and list every drifted file instead of rewriting.
    [switch]$Check
)

# Propagates the single RunicSuiteVersion in Shared/RunicSuite.props to the package files of
# every module that embeds Shared/InventorySafety. Those modules negotiate with each other at
# runtime and refuse to run against a different embedded core, so they always ship together at
# one version. Code reads the version from the generated RunicAutomation.RunicSuite constant;
# only the Thunderstore-facing text files need this script.

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$repo = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$modules = @(
    @{ Directory = 'RunicStorage';    Title = 'Runic Storage' }
    @{ Directory = 'RunicProduction'; Title = 'Runic Production' }
    @{ Directory = 'RunicCrafting';   Title = 'Runic Crafting' }
)

[xml]$props = Get-Content -Raw -LiteralPath (Join-Path $repo 'Shared/RunicSuite.props')
$version = [string]$props.Project.PropertyGroup.RunicSuiteVersion
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "RunicSuiteVersion '$version' is not Major.Minor.Patch." }

$drifted = New-Object System.Collections.Generic.List[string]
function Update-Text {
    param([string]$Path, [scriptblock]$Transform)
    $original = Get-Content -Raw -LiteralPath $Path
    $updated = & $Transform $original
    if ($updated -ceq $original) { return }
    $drifted.Add($Path)
    if (-not $Check) {
        [IO.File]::WriteAllText($Path, $updated, (New-Object System.Text.UTF8Encoding($false)))
    }
}

foreach ($module in $modules) {
    $root = Join-Path $repo $module.Directory
    $title = [regex]::Escape($module.Title)

    Update-Text (Join-Path $root 'manifest.json') {
        param($text)
        [regex]::Replace($text, '"version_number":\s*"\d+\.\d+\.\d+"', ('"version_number": "' + $version + '"'), 1)
    }
    Update-Text (Join-Path $root 'README.md') {
        param($text)
        $text = [regex]::Replace($text, "^# $title \d+\.\d+\.\d+", ('# ' + $module.Title + ' ' + $version), 1)
        [regex]::Replace($text, 'Install matching \d+\.\d+\.\d+ versions', ('Install matching ' + $version + ' versions'))
    }
    $changelog = Join-Path $root 'CHANGELOG.md'
    $changelogText = Get-Content -Raw -LiteralPath $changelog
    if ($changelogText -notmatch ("(?m)^## " + [regex]::Escape($version) + "\s*$")) {
        $drifted.Add("$changelog (missing a '## $version' section)")
    }
}

if ($drifted.Count -eq 0) {
    Write-Output "Runic suite version $version: every module package file is in sync."
    exit 0
}
if ($Check) {
    Write-Output "Runic suite version $version: out-of-sync files:"
    $drifted | ForEach-Object { Write-Output "  $_" }
    exit 1
}
Write-Output "Runic suite version $version: updated"
$drifted | ForEach-Object { Write-Output "  $_" }
