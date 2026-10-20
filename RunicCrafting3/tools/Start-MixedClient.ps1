param([Parameter(Mandatory)][ValidateSet('vanilla','crafter')][string]$Role)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot
$fixture=Get-Content -LiteralPath (Join-Path $projectRoot 'evidence\mixed-fixture.json') -Raw | ConvertFrom-Json
$clientRoot=Join-Path $fixture.root $Role
$pidPath=Join-Path $clientRoot 'client.pid'
if(Test-Path -LiteralPath $pidPath) {
    $previous=Get-Process -Id ([int](Get-Content -LiteralPath $pidPath)) -ErrorAction SilentlyContinue
    if($previous) { throw 'This fixture client is still running.' }
}
if($Role -eq 'crafter') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeAccessProbe\bin\Release\netstandard2.1\RunicCrafting3.NativeAccessProbe.dll') -Destination (Join-Path $clientRoot 'BepInEx\plugins')
    [IO.File]::WriteAllText((Join-Path $clientRoot 'command.txt'),'')
}
$clientStyle=if($Role -eq 'vanilla'){'Normal'}else{'Hidden'}
$clientProcess=Start-Process -FilePath (Join-Path $clientRoot 'valheim.exe') -WorkingDirectory $clientRoot -ArgumentList @('-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-savedir',('"'+(Join-Path $clientRoot 'test-saves')+'"'),'-logFile',('"'+(Join-Path $clientRoot 'unity.log')+'"')) -WindowStyle $clientStyle -PassThru
$clientProcess.Id | Set-Content -LiteralPath $pidPath
[pscustomobject]@{role=$Role;pid=$clientProcess.Id;root=$clientRoot} | ConvertTo-Json
