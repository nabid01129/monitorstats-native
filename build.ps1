param([switch]$Installer,[string]$OutputDirectory,[ValidateSet('x86','x64')][string]$Architecture='x64',[string]$NsisPath)
$ErrorActionPreference='Stop'
$output=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $PSScriptRoot 'build'}
New-Item -ItemType Directory -Force -Path $output | Out-Null
$sources=@(Get-ChildItem $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName)+@(Get-ChildItem (Join-Path $PSScriptRoot 'shared') -Filter '*.cs' | ForEach-Object FullName)
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$exe=Join-Path $output 'MonitorStats Native.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:winexe "/platform:$Architecture" /optimize+ /main:MonitorStatsNative.Program "/out:$exe" "/win32icon:$PSScriptRoot/assets/monitorstats.ico" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll /r:System.Web.Extensions.dll /r:System.Security.dll /r:System.Net.Http.dll /r:System.Runtime.WindowsRuntime.dll $sources
if($LASTEXITCODE -ne 0){throw 'Native build failed'}
Copy-Item (Join-Path $PSScriptRoot 'app.config') ($exe+'.config')
if($Installer){
 if(!$NsisPath){$compiler=Get-Command makensis.exe -ErrorAction SilentlyContinue; if($compiler){$NsisPath=$compiler.Source}}
 if(!$NsisPath){$NsisPath=Join-Path ${env:ProgramFiles(x86)} 'NSIS/makensis.exe'}
 if(!(Test-Path -LiteralPath $NsisPath)){throw 'Install NSIS 3 and pass -NsisPath to its makensis.exe'}
 & $NsisPath /V2 "/DPROJECT=$PSScriptRoot" "/DBUILD=$output" "/DARCH=$Architecture" (Join-Path $PSScriptRoot 'installer.nsi')
 if($LASTEXITCODE -ne 0){throw 'Native installer build failed'}
}
Write-Output $exe