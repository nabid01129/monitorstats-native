param([Parameter(Mandatory=$true)][string]$Assembly)
$ErrorActionPreference='Stop'
try{
 $testPath=[IO.Path]::GetFullPath($Assembly)
 [Environment]::CurrentDirectory=[IO.Path]::GetDirectoryName($testPath)
 Set-Location -LiteralPath ([IO.Path]::GetDirectoryName($testPath))
 $tests=[Reflection.Assembly]::LoadFrom($testPath)
 $entry=$tests.GetType('Checks',$true).GetMethod('Main',[Reflection.BindingFlags]'Static,NonPublic')
 exit ([int]$entry.Invoke($null,@()))
}catch{Write-Error $_;exit 1}