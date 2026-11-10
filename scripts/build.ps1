param([switch]$Browser,[switch]$Launch)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $taskRoot
$taskDotnet=Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
if(!(Test-Path -LiteralPath $taskDotnet)){& (Join-Path $PSScriptRoot 'get-sdk.ps1');if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}}
$env:DOTNET_ROOT=Split-Path $taskDotnet
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:NUGET_PACKAGES=Join-Path $taskRoot '.tools/nuget'
& $taskDotnet build 'MineSafety.Web.slnx' -c Release --verbosity minimal
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
$taskArguments=@('run','--project','tests/MineSafety.Web.Tests','-c','Release','--no-build','--','--output',(Join-Path $taskRoot '.verification/build'))
if($Browser){$taskArguments+='--browser'}
& $taskDotnet @taskArguments
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
$taskStage=[IO.Path]::GetFullPath((Join-Path $taskRoot '.tools/publish-staging'))
$taskExpected=[IO.Path]::GetFullPath((Join-Path $taskRoot '.tools')).TrimEnd('\')+'\'
if(!$taskStage.StartsWith($taskExpected,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe staging path.'}
if(Test-Path -LiteralPath $taskStage){
    if((Get-Item -LiteralPath $taskStage).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Staging directory cannot be a link.'}
    Remove-Item -LiteralPath $taskStage -Recurse -Force
}
& $taskDotnet publish 'src/MineSafety.Web/MineSafety.Web.csproj' -c Release -r win-x64 --self-contained true -o $taskStage -p:PublishReadyToRun=false --verbosity minimal
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
$taskPackage=Join-Path $taskRoot 'package'
$taskWasRunning=$false
$taskPidFile=Join-Path $taskPackage 'private/runtime/server.json'
if(Test-Path -LiteralPath $taskPidFile){
    try{$taskInfo=Get-Content -Raw -LiteralPath $taskPidFile|ConvertFrom-Json;$taskRunning=Get-Process -Id $taskInfo.ProcessId -ErrorAction Stop;$taskWasRunning=$taskRunning.Path -eq (Join-Path $taskPackage 'MineSafety.Web.exe')}catch{}
}
if($taskWasRunning){& (Join-Path $PSScriptRoot 'stop-server.ps1')}
New-Item -ItemType Directory -Path $taskPackage -Force | Out-Null
Get-ChildItem -LiteralPath $taskStage -Force | Copy-Item -Destination $taskPackage -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'start-server.ps1'),(Join-Path $PSScriptRoot 'stop-server.ps1'),(Join-Path $PSScriptRoot 'set-mysql-connection.ps1') -Destination $taskPackage -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination $taskPackage -Force
New-Item -ItemType Directory -Path (Join-Path $taskPackage 'docs') -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $taskRoot 'docs') -Force | Copy-Item -Destination (Join-Path $taskPackage 'docs') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'MYSQL_IMPLEMENTATION.md') -Destination $taskPackage -Force
[IO.File]::WriteAllText((Join-Path $taskPackage 'start.cmd'),"@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0start-server.ps1`"`r`nif errorlevel 1 pause`r`n",[Text.Encoding]::ASCII)
[IO.File]::WriteAllText((Join-Path $taskPackage 'stop.cmd'),"@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0stop-server.ps1`"`r`nif errorlevel 1 pause`r`n",[Text.Encoding]::ASCII)
[IO.File]::WriteAllText((Join-Path $taskPackage 'configure.cmd'),"@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0set-mysql-connection.ps1`" %*`r`nif errorlevel 1 pause`r`n",[Text.Encoding]::ASCII)
@{Version='2.0.0';BuiltAt=(Get-Date -Format o);Sdk=(& $taskDotnet --version);Architecture='win-x64';SelfContained=$true;ReadonlyMeasurements=$true;Database='MySQL';HtmlSHA256=(Get-FileHash -LiteralPath (Join-Path $taskPackage 'wwwroot/index.html')).Hash} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskPackage 'build-info.json') -Encoding UTF8
Write-Output ('Package: '+$taskPackage)
if($Launch){& (Join-Path $PSScriptRoot 'start-server.ps1')}
elseif($taskWasRunning){& (Join-Path $PSScriptRoot 'start-server.ps1') -NoBrowser}

