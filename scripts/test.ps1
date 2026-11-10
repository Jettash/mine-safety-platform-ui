param([switch]$Browser)
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
$taskArguments=@('run','--project','tests/MineSafety.Web.Tests','-c','Release','--no-build','--','--output',(Join-Path $taskRoot '.verification/tests'))
if($Browser){$taskArguments+='--browser'}
& $taskDotnet @taskArguments
exit $LASTEXITCODE
