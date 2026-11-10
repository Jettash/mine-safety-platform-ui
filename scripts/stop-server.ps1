$ErrorActionPreference='Stop'
$taskPackage=if(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'MineSafety.Web.exe')){$PSScriptRoot}else{[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../package'))}
$taskExe=Join-Path $taskPackage 'MineSafety.Web.exe'
$taskPidFile=Join-Path $taskPackage 'private/runtime/server.json'
if(!(Test-Path -LiteralPath $taskPidFile)){Write-Output 'Server is not running.';exit 0}
$taskInfo=Get-Content -Raw -LiteralPath $taskPidFile | ConvertFrom-Json
$taskProcess=Get-Process -Id $taskInfo.ProcessId -ErrorAction SilentlyContinue
if($taskProcess -and $taskProcess.Path -eq $taskExe -and $taskProcess.StartTime.ToUniversalTime().Ticks -eq [long]$taskInfo.StartTicks){
    Stop-Process -Id $taskProcess.Id
    $taskProcess.WaitForExit(10000) | Out-Null
    Write-Output 'Server stopped.'
}else{Write-Output 'Recorded server process has already stopped.'}
Remove-Item -LiteralPath $taskPidFile -Force
