param([string]$Assembly)
$ErrorActionPreference='Stop'
$a=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Assembly))
$t=$a.GetType('MonitorStatsNative.Telemetry',$true)
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$reader=[Activator]::CreateInstance($t,$true)
try{
 foreach($live in @($false,$true)){
  $map=[IO.MemoryMappedFiles.MemoryMappedFile]::CreateNew([NullString]::Value,[long]4096)
  $view=$map.CreateViewAccessor()
  try{
   if($live){
    $view.Write([long]0,[uint32]0x4D41484D);$view.Write([long]8,[uint32]32);$view.Write([long]12,[uint32]1);$view.Write([long]16,[uint32]1324)
    [byte[]]$label=[Text.Encoding]::ASCII.GetBytes('CPU clock');for($i=0;$i -lt $label.Length;$i++){$view.Write([long](32+$i),$label[$i])}
    $view.Write([long](32+1300),[single]3500);$view.Write([long](32+1316),[uint32]::MaxValue)
   }else{$view.Write([long]0,[uint32]0xDEAD)}
   $t.GetField('mahMap',$flags).SetValue($reader,$map);$t.GetField('mah',$flags).SetValue($reader,$view)
   $rows=$t.GetMethod('Sensors',$flags).Invoke($reader,@())
   if(!$live){if($null -ne $t.GetField('mah',$flags).GetValue($reader) -or $null -ne $t.GetField('mahMap',$flags).GetValue($reader)){throw 'Dead Afterburner mapping remains attached'}}
   elseif($rows.Count -ne 1 -or $rows[0]['name'] -ne 'CPU clock' -or $rows[0]['value'] -ne 3500){throw 'Valid sensor feed does not recover correctly'}
  }finally{$view.Dispose();$map.Dispose()}
 }
 Write-Output 'Afterburner dead-feed release and valid-feed recovery passed'
}finally{$t.GetMethod('Dispose').Invoke($reader,@())}