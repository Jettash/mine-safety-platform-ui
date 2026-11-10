$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskTools = Join-Path $taskRoot '.tools'
$taskSdk = Join-Path $taskTools 'dotnet'
if (Test-Path -LiteralPath (Join-Path $taskSdk 'dotnet.exe')) { & (Join-Path $taskSdk 'dotnet.exe') --version; exit $LASTEXITCODE }
New-Item -ItemType Directory -Path $taskTools -Force | Out-Null
$taskMetadataPath = Join-Path $taskTools 'sdk-download.json'
if (!(Test-Path -LiteralPath $taskMetadataPath)) {
    $taskRelease = Invoke-RestMethod -Uri 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json' -TimeoutSec 40
    $taskVersion = (Get-Content -Raw -LiteralPath (Join-Path $taskRoot 'global.json') | ConvertFrom-Json).sdk.version
    $taskItem = $taskRelease.releases | Where-Object { $_.sdk.version -eq $taskVersion } | Select-Object -First 1
    $taskFile = $taskItem.sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
    if (!$taskFile) { throw 'Pinned SDK release not found in official release metadata.' }
    @{Version=$taskVersion;Url=$taskFile.url;Sha512=$taskFile.hash} | ConvertTo-Json | Set-Content -LiteralPath $taskMetadataPath -Encoding UTF8
}
$taskMetadata = Get-Content -Raw -LiteralPath $taskMetadataPath | ConvertFrom-Json
$taskUrl = $taskMetadata.Url.Replace('https://builds.dotnet.microsoft.com','https://dotnetcli.azureedge.net')
$taskParts = Join-Path $taskTools 'sdk-parts'
New-Item -ItemType Directory -Path $taskParts -Force | Out-Null
$taskHeaders = Join-Path $taskParts 'size.headers'
& curl.exe --fail --location --retry 3 --connect-timeout 15 --max-time 40 --range '0-0' --dump-header $taskHeaders --output (Join-Path $taskParts 'size.byte') --silent --show-error ($taskUrl+'?size=1')
if ($LASTEXITCODE -ne 0) { throw 'Could not read SDK archive size.' }
$taskMatch = [regex]::Match((Get-Content -Raw -LiteralPath $taskHeaders),'(?im)^Content-Range:\s*bytes\s+0-0/(\d+)')
if (!$taskMatch.Success) { throw 'Server did not return a valid byte range.' }
[long]$taskTotal = $taskMatch.Groups[1].Value
[long]$taskChunk = [Math]::Ceiling($taskTotal / 8.0)
$taskDownloads = @()
for ($taskIndex=0; $taskIndex -lt 8; $taskIndex++) {
    [long]$taskBegin = $taskIndex * $taskChunk
    [long]$taskEnd = [Math]::Min($taskTotal-1,$taskBegin+$taskChunk-1)
    $taskPart = Join-Path $taskParts ('part-'+$taskIndex+'.bin')
    $taskHeader = Join-Path $taskParts ('part-'+$taskIndex+'.headers')
    if ((Test-Path -LiteralPath $taskPart) -and (Get-Item -LiteralPath $taskPart).Length -eq ($taskEnd-$taskBegin+1)) { continue }
    $taskArguments = @('--fail','--location','--retry','3','--retry-all-errors','--connect-timeout','15','--max-time','600','--range',($taskBegin.ToString()+'-'+$taskEnd.ToString()),'--dump-header',('"'+$taskHeader+'"'),'--output',('"'+$taskPart+'"'),'--silent','--show-error',($taskUrl+'?chunk='+$taskBegin))
    $taskProcess = Start-Process -FilePath 'curl.exe' -ArgumentList $taskArguments -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $taskParts ('part-'+$taskIndex+'.log'))
    $null = $taskProcess.Handle
    $taskDownloads += @{Process=$taskProcess;Part=$taskPart;Begin=$taskBegin;End=$taskEnd;Header=$taskHeader}
}
foreach($taskDownload in $taskDownloads) {
    $taskDownload.Process.WaitForExit()
    if ($taskDownload.Process.ExitCode -ne 0) { throw ('SDK part failed: '+$taskDownload.Part) }
    if ((Get-Item -LiteralPath $taskDownload.Part).Length -ne ($taskDownload.End-$taskDownload.Begin+1)) { throw 'SDK part size mismatch.' }
    $taskExpected = 'bytes '+$taskDownload.Begin+'-'+$taskDownload.End+'/'+$taskTotal
    if (!(Get-Content -Raw -LiteralPath $taskDownload.Header).Contains($taskExpected)) { throw 'SDK part range mismatch.' }
}
$taskArchive = Join-Path $taskTools 'dotnet-sdk-complete.zip'
$taskOutput = [IO.File]::Create($taskArchive)
try { for($taskIndex=0;$taskIndex -lt 8;$taskIndex++) { $taskInput=[IO.File]::OpenRead((Join-Path $taskParts ('part-'+$taskIndex+'.bin'))); try { $taskInput.CopyTo($taskOutput) } finally { $taskInput.Dispose() } } }
finally { $taskOutput.Dispose() }
if ((Get-FileHash -LiteralPath $taskArchive -Algorithm SHA512).Hash -ne $taskMetadata.Sha512) { throw 'Official SDK SHA512 verification failed.' }
New-Item -ItemType Directory -Path $taskSdk -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($taskArchive,$taskSdk)
& (Join-Path $taskSdk 'dotnet.exe') --version
exit $LASTEXITCODE
