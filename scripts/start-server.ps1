param([switch]$NoBrowser)
$ErrorActionPreference = 'Stop'
$taskPackage = if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'MineSafety.Web.exe')) { $PSScriptRoot } else { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../package')) }
$taskExe = Join-Path $taskPackage 'MineSafety.Web.exe'
if (!(Test-Path -LiteralPath $taskExe)) { throw 'Package not found. Run build-web.cmd first.' }
$taskConfiguration = Get-Content -Raw -LiteralPath (Join-Path $taskPackage 'appsettings.json') | ConvertFrom-Json
$taskLocal = Join-Path $taskPackage 'appsettings.Local.json'
$taskUrls = $taskConfiguration.Urls
if (Test-Path -LiteralPath $taskLocal) { $taskOverride=Get-Content -Raw -LiteralPath $taskLocal | ConvertFrom-Json; if($taskOverride.Urls){$taskUrls=$taskOverride.Urls} }
if ($env:ASPNETCORE_URLS) { $taskUrls=$env:ASPNETCORE_URLS }
$taskUri=[uri](($taskUrls -split ';')[0].Replace('*','0.0.0.0'))
$taskBrowserHost=if($taskUri.Host -in @('0.0.0.0','::','[::]')){'127.0.0.1'}else{$taskUri.Host}
$taskUrl=$taskUri.Scheme+'://'+$taskBrowserHost+':'+$taskUri.Port
$taskRuntime=Join-Path $taskPackage 'private/runtime'
New-Item -ItemType Directory -Path $taskRuntime -Force | Out-Null
$taskPidFile=Join-Path $taskRuntime 'server.json'
$taskRunning=$null
if(Test-Path -LiteralPath $taskPidFile){
    try { $taskInfo=Get-Content -Raw -LiteralPath $taskPidFile | ConvertFrom-Json; $taskCandidate=Get-Process -Id $taskInfo.ProcessId -ErrorAction Stop
        if($taskCandidate.Path -eq $taskExe -and $taskCandidate.StartTime.ToUniversalTime().Ticks -eq [long]$taskInfo.StartTicks){$taskRunning=$taskCandidate} } catch { }
}
if(!$taskRunning){
    $taskListener=Get-NetTCPConnection -State Listen -LocalPort $taskUri.Port -ErrorAction SilentlyContinue
    if($taskListener){throw ('Port '+$taskUri.Port+' is occupied by another process.')}
    $taskRunning=Start-Process -FilePath $taskExe -WorkingDirectory $taskPackage -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskRuntime 'server.log') -RedirectStandardError (Join-Path $taskRuntime 'server-error.log')
    $null=$taskRunning.Handle
    @{ProcessId=$taskRunning.Id;Executable=$taskExe;StartTicks=$taskRunning.StartTime.ToUniversalTime().Ticks;Url=$taskUrl} | ConvertTo-Json | Set-Content -LiteralPath $taskPidFile -Encoding UTF8
}
$taskReady=$false
for($taskAttempt=0;$taskAttempt -lt 40;$taskAttempt++){
    if($taskRunning.HasExited){throw ('Server exited. See '+(Join-Path $taskRuntime 'server-error.log'))}
    try { $taskResponse=Invoke-WebRequest -UseBasicParsing -Uri ($taskUrl+'/api/setup-info') -TimeoutSec 2; if($taskResponse.StatusCode -eq 200){$taskReady=$true;break} } catch { Start-Sleep -Milliseconds 150 }
}
if(!$taskReady){throw 'Server did not become ready. Check private/runtime logs.'}
Write-Output ('Local: '+$taskUrl)
Write-Output ('Process: '+$taskRunning.Id)
if(!$NoBrowser){Start-Process -FilePath $taskUrl}
