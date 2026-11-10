param(
    [string]$DefaultsFile='',
    [string]$HostName='127.0.0.1',
    [int]$Port=3306,
    [string]$Database='mine_truck',
    [string]$UserName='mine_reader',
    [string]$PackageDirectory=''
)
$ErrorActionPreference='Stop'
if(!$PackageDirectory){$PackageDirectory=if(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'MineSafety.Web.exe')){$PSScriptRoot}else{Join-Path $PSScriptRoot '../package'}}
$taskPackage=[IO.Path]::GetFullPath($PackageDirectory)
$taskConfigPath=Join-Path $taskPackage 'appsettings.json'
if(!(Test-Path -LiteralPath $taskConfigPath)){throw 'Build the web package first.'}
$taskState='private'
$taskFile='mysql.connection.dpapi'
foreach($taskConfig in @($taskConfigPath,(Join-Path $taskPackage 'appsettings.Local.json'))){
    if(Test-Path -LiteralPath $taskConfig){$taskSettings=Get-Content -Raw -Encoding UTF8 -LiteralPath $taskConfig|ConvertFrom-Json
        if($taskSettings.Web.StateDirectory){$taskState=$taskSettings.Web.StateDirectory}
        if($taskSettings.Web.MySqlConnectionFile){$taskFile=$taskSettings.Web.MySqlConnectionFile}
    }
}
if(![IO.Path]::IsPathRooted($taskState)){$taskState=Join-Path $taskPackage $taskState}
if(![IO.Path]::IsPathRooted($taskFile)){$taskFile=Join-Path $taskState $taskFile}
$taskFile=[IO.Path]::GetFullPath($taskFile)
$taskPublic=[IO.Path]::GetFullPath((Join-Path $taskPackage 'wwwroot')).TrimEnd('\')
if($taskFile.Equals($taskPublic,[StringComparison]::OrdinalIgnoreCase) -or $taskFile.StartsWith($taskPublic+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Connection file must not be public.'}
$taskSecret=$null
$taskBytes=$null
try {
    if($DefaultsFile){
        # Read the existing MySQL client file in memory; never echo its contents.
        $taskValues=@{}
        $taskSection=''
        foreach($taskLine in [IO.File]::ReadAllLines([IO.Path]::GetFullPath($DefaultsFile))){
            $taskLine=$taskLine.Trim()
            if($taskLine -match '^\[([^\]]+)\]$'){$taskSection=$Matches[1];continue}
            if($taskSection -eq 'client' -and $taskLine -match '^(host|port|user|password|database)\s*=\s*(.*)$'){
                $taskKey=$Matches[1];$taskValue=$Matches[2]
                if($taskValue.Length -ge 2 -and (($taskValue[0] -eq '"' -and $taskValue[-1] -eq '"') -or ($taskValue[0] -eq "'" -and $taskValue[-1] -eq "'"))){$taskValue=$taskValue.Substring(1,$taskValue.Length-2)}
                # Standard MySQL option-file escapes, including generated client passwords.
                $taskValue=[regex]::Replace($taskValue,'\\([bnrt s\\"''])',{param($m) switch($m.Groups[1].Value){'b'{[string][char]8};'n'{"`n"};'r'{"`r"};'t'{"`t"};'s'{' '};default{$m.Groups[1].Value}}})
                $taskValues[$taskKey]=$taskValue
            }
        }
        if(!$taskValues.ContainsKey('password')){throw 'No client password found in the supplied defaults file.'}
        if($taskValues.host){$HostName=$taskValues.host};if($taskValues.port){$Port=[int]$taskValues.port};if($taskValues.user){$UserName=$taskValues.user};if($taskValues.database){$Database=$taskValues.database}
        $taskSecret=$taskValues.password
    } else {
        $taskSecure=Read-Host ('MySQL password for '+$UserName) -AsSecureString
        $taskPointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($taskSecure)
        try{$taskSecret=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($taskPointer)}finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($taskPointer)}
    }
    if($Port -lt 1 -or $Port -gt 65535 -or !$Database -or !$UserName){throw 'Invalid MySQL connection settings.'}
    Add-Type -AssemblyName System.Data
    Add-Type -AssemblyName System.Security
    $taskBuilder=New-Object System.Data.Common.DbConnectionStringBuilder
    $taskBuilder['Server']=$HostName;$taskBuilder['Port']=$Port;$taskBuilder['Database']=$Database
    $taskBuilder['User ID']=$UserName;$taskBuilder['Password']=$taskSecret;$taskBuilder['SSL Mode']='Preferred'
    if($HostName -in @('127.0.0.1','localhost','::1')){$taskBuilder['Allow Public Key Retrieval']=$true}
    $taskBytes=[Text.Encoding]::UTF8.GetBytes($taskBuilder.ConnectionString)
    $taskEncrypted=[Security.Cryptography.ProtectedData]::Protect($taskBytes,[Text.Encoding]::UTF8.GetBytes('MineSafety.Web.MySql.v1'),[Security.Cryptography.DataProtectionScope]::CurrentUser)
    New-Item -ItemType Directory -Path (Split-Path $taskFile) -Force|Out-Null
    [IO.File]::WriteAllBytes($taskFile,$taskEncrypted)
    $taskAcl=Get-Acl -LiteralPath $taskFile
    $taskAcl.SetAccessRuleProtection($true,$false)
    foreach($taskSid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,[Security.Principal.SecurityIdentifier]::new('S-1-5-18'))){
        $taskRule=[Security.AccessControl.FileSystemAccessRule]::new($taskSid,'FullControl','Allow');$taskAcl.AddAccessRule($taskRule)
    }
    Set-Acl -LiteralPath $taskFile -AclObject $taskAcl
    Write-Output ('Encrypted MySQL configuration saved: '+$taskFile)
    Write-Output 'Run start-web.cmd, then sign in and check the home page. Keep the SELECT-only MySQL account.'
} finally {
    if($taskBytes){[Array]::Clear($taskBytes,0,$taskBytes.Length)}
    $taskSecret=$null;$taskValues=$null;$taskBuilder=$null;$taskValue=$null;$taskLine=$null
}
