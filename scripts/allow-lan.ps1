param([int]$Port=8081)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskExe=Join-Path $taskRoot 'package/MineSafety.Web.exe'
if(!(Test-Path -LiteralPath $taskExe)){throw 'Build and verify the package before configuring LAN access.'}
if($Port -lt 1 -or $Port -gt 65535){throw 'Invalid port.'}
$taskRule='MineSafety-Web-TCP-'+$Port
try{
    $taskExisting=Get-NetFirewallRule -Name $taskRule -ErrorAction SilentlyContinue
    if(!$taskExisting){New-NetFirewallRule -Name $taskRule -DisplayName ('MineSafety Web LAN '+$Port) -Direction Inbound -Action Allow -Enabled True -Protocol TCP -LocalPort $Port -Program $taskExe -RemoteAddress LocalSubnet -Profile Any | Out-Null}
    $taskResult=@{Success=$true;Port=$Port;RemoteAddress='LocalSubnet';Program=$taskExe;Rule=$taskRule}
}catch{$taskResult=@{Success=$false;Error=$_.Exception.Message}}
$taskResult|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $taskRoot '.verification/firewall-result.json') -Encoding UTF8
if(!$taskResult.Success){exit 1}
