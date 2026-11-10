param([string]$PackageDirectory="",[string]$OutputDirectory="")
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskPackage=if($PackageDirectory){[IO.Path]::GetFullPath($PackageDirectory)}else{Join-Path $taskRoot 'package'}
$taskOutput=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $taskRoot '.verification/package'}
$taskState=Join-Path $taskOutput ('isolated-state-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskState -Force | Out-Null
$taskExe=Join-Path $taskPackage 'MineSafety.Web.exe'
foreach($taskFile in @('MineSafety.Web.exe','coreclr.dll','hostfxr.dll','MySqlConnector.dll','System.Security.Cryptography.ProtectedData.dll')){
    if(!(Test-Path -LiteralPath (Join-Path $taskPackage $taskFile))){throw ('Missing published dependency: '+$taskFile)}
}
$taskListener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$taskListener.Start()
$taskPort=$taskListener.LocalEndpoint.Port
$taskListener.Stop()
$taskUrl='http://127.0.0.1:'+$taskPort
$taskOldRoot=$env:DOTNET_ROOT
$taskOldPath=$env:PATH
$taskOldState=$env:Web__StateDirectory
$taskOldConnectionFile=$env:Web__MySqlConnectionFile
$taskProcess=$null
try{
    Remove-Item Env:DOTNET_ROOT -ErrorAction SilentlyContinue
    $env:PATH=Join-Path $env:SystemRoot 'System32'
    $env:Web__StateDirectory=$taskState
    $env:Web__MySqlConnectionFile=Join-Path $taskPackage 'private/mysql.connection.dpapi'
    $taskProcess=Start-Process -FilePath $taskExe -WorkingDirectory $taskPackage -WindowStyle Hidden -ArgumentList @('--urls',$taskUrl) -PassThru -RedirectStandardOutput (Join-Path $taskOutput 'server.log') -RedirectStandardError (Join-Path $taskOutput 'server-error.log')
    $null=$taskProcess.Handle
    $taskSetup=$null
    for($taskAttempt=0;$taskAttempt -lt 40;$taskAttempt++){
        if($taskProcess.HasExited){throw 'Published application exited.'}
        try{$taskSetup=Invoke-RestMethod -Uri ($taskUrl+'/api/setup-info') -TimeoutSec 2;break}catch{Start-Sleep -Milliseconds 150}
    }
    if(!$taskSetup -or $taskSetup.initialized -or !$taskSetup.canSetup){throw 'Published application did not show fresh local setup.'}
    try{Invoke-WebRequest -UseBasicParsing -Uri ($taskUrl+'/api/home') -ErrorAction Stop | Out-Null;throw 'Anonymous query succeeded.'}
    catch{if(!$_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 401){throw}}
    $taskAdmin=[Guid]::NewGuid().ToString('N')
    $taskViewer=[Guid]::NewGuid().ToString('N')
    $taskBody=@{adminPassword=$taskAdmin;viewerPassword=$taskViewer}|ConvertTo-Json
    Invoke-RestMethod -Uri ($taskUrl+'/api/setup') -Method Post -ContentType 'application/json' -Headers @{'X-Setup-Token'=$taskSetup.setupToken} -Body $taskBody | Out-Null
    $taskLogin=@{username='admin';password=$taskAdmin}|ConvertTo-Json
    Invoke-WebRequest -UseBasicParsing -Uri ($taskUrl+'/api/login') -Method Post -ContentType 'application/json' -Body $taskLogin -SessionVariable taskSession | Out-Null
    $taskHome=Invoke-RestMethod -Uri ($taskUrl+'/api/home') -WebSession $taskSession
    if($null -eq $taskHome.summary.total){throw 'Published application failed to read the actual database.'}
    $taskDay=[DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(8)).ToString('yyyy-MM-dd')
    $taskQuery=@{start=$taskDay+'T00:00';end=$taskDay+'T23:59';vehicle=''}|ConvertTo-Json
    $taskSnapshot=Invoke-RestMethod -Uri ($taskUrl+'/api/queries') -Method Post -ContentType 'application/json' -Body $taskQuery -WebSession $taskSession
    $taskExcel=Join-Path $taskOutput 'actual-database.xlsx'
    Invoke-WebRequest -UseBasicParsing -Uri ($taskUrl+'/api/queries/'+$taskSnapshot.snapshotId+'/export.xlsx') -WebSession $taskSession -OutFile $taskExcel
    $taskBytes=[IO.File]::ReadAllBytes($taskExcel)
    if($taskBytes.Length -lt 1000 -or $taskBytes[0] -ne 80 -or $taskBytes[1] -ne 75){throw 'Published Excel response was not an XLSX file.'}
    $taskAccounts=[IO.File]::ReadAllText((Join-Path $taskState 'accounts.json'))
    if($taskAccounts.Contains($taskAdmin) -or $taskAccounts.Contains($taskViewer)){throw 'Plaintext password was persisted.'}
    $taskResult=@{passed=$true;selfContained=$true;withoutDeveloperPath=$true;actualDatabaseReadonly=$true;today=$taskDay;total=$taskHome.summary.total;queryCount=$taskSnapshot.totalCount;excelBytes=$taskBytes.Length;isolatedAccountState=$true;at=(Get-Date -Format o)}
    $taskResult|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $taskOutput 'report.json') -Encoding UTF8
    Write-Output ('Published package passed: actual records today='+$taskHome.summary.total+', XLSX bytes='+$taskBytes.Length)
}finally{
    if($taskProcess -and !$taskProcess.HasExited){Stop-Process -Id $taskProcess.Id;$taskProcess.WaitForExit(10000)|Out-Null}
    $env:DOTNET_ROOT=$taskOldRoot
    $env:PATH=$taskOldPath
    $env:Web__StateDirectory=$taskOldState
    $env:Web__MySqlConnectionFile=$taskOldConnectionFile
}
