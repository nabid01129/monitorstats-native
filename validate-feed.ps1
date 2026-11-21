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
    $view.Write([long]0,[uint32]0x4D41484D);$view.Write([long]8,[uint32]32);$view.Write([long]12,[uint32]3);$view.Write([long]16,[uint32]1324)
    [byte[]]$label=[Text.Encoding]::ASCII.GetBytes('CPU clock');for($i=0;$i -lt $label.Length;$i++){$view.Write([long](32+$i),$label[$i])}
    $view.Write([long](32+1300),[single]3500);$view.Write([long](32+1316),[uint32]::MaxValue)
    [byte[]]$core=[Text.Encoding]::ASCII.GetBytes('CPU1 temperature');for($i=0;$i -lt $core.Length;$i++){$view.Write([long](32+1324+$i),$core[$i])}
    $view.Write([long](32+1324+1300),[single]50);$view.Write([long](32+1324+1316),[uint32]0)
    [byte[]]$gpu=[Text.Encoding]::ASCII.GetBytes('core clock');for($i=0;$i -lt $gpu.Length;$i++){$view.Write([long](32+2648+$i),$gpu[$i])}
    $view.Write([long](32+2648+1300),[single]1200);$view.Write([long](32+2648+1316),[uint32]0)
   }else{$view.Write([long]0,[uint32]0xDEAD)}
   $t.GetField('mahMap',$flags).SetValue($reader,$map);$t.GetField('mah',$flags).SetValue($reader,$view)
   $rows=$t.GetMethod('Sensors',$flags).Invoke($reader,@())
   if(!$live){if($null -ne $t.GetField('mah',$flags).GetValue($reader) -or $null -ne $t.GetField('mahMap',$flags).GetValue($reader)){throw 'Dead Afterburner mapping remains attached'}}
   elseif($rows.Count -ne 3 -or $rows[0]['name'] -ne 'CPU clock' -or $rows[0]['value'] -ne 3500 -or $rows[1]['name'] -ne 'CPU1 temperature' -or $rows[2]['name'] -ne 'GPU1 core clock'){throw 'Valid sensor feed does not recover or labels CPU/GPU sensors incorrectly'}
  }finally{$view.Dispose();$map.Dispose()}
 }
 Write-Output 'Afterburner dead-feed release and valid-feed recovery passed'
}finally{$t.GetMethod('Dispose').Invoke($reader,@())}

$rtss=$a.GetType('ForegroundMonitor',$true)
$static=[Reflection.BindingFlags]'Static,NonPublic'
foreach($scenario in @('normal','wrap','other-process','dead')){
 $map=[IO.MemoryMappedFiles.MemoryMappedFile]::CreateNew([NullString]::Value,[long]4096)
 $view=$map.CreateViewAccessor()
 try{
  $view.Write([long]0,[uint32]0x52545353);$view.Write([long]4,[uint32]0x00020000)
  $view.Write([long]8,[uint32]280);$view.Write([long]12,[uint32]32);$view.Write([long]16,[uint32]1)
  $view.Write([long]32,[uint32]12345)
  if($scenario -eq 'wrap'){$start=[uint32]4294967280;$end=[uint32]16;$frames=[uint32]4;$expectedFps=125;$expectedMs=8.0}
  else{$start=[uint32]1000;$end=[uint32]2000;$frames=[uint32]60;$expectedFps=60;$expectedMs=16.667}
  $view.Write([long](32+268),$start);$view.Write([long](32+272),$end);$view.Write([long](32+276),$frames)
  if($scenario -eq 'dead'){$view.Write([long]0,[uint32]0xDEAD)}
  $rtss.GetField('rtssMap',$static).SetValue($null,$map);$rtss.GetField('rtssView',$static).SetValue($null,$view)
  $pidToRead=if($scenario -eq 'other-process'){[uint32]54321}else{[uint32]12345}
  [object[]]$values=@($pidToRead,0,0.0,[uint32]0,[uint32]0,[uint32]0)
  $rtss.GetMethod('ReadRtss',$static).Invoke($null,$values)
  if($scenario -eq 'other-process' -or $scenario -eq 'dead'){
   if($values[1] -ne 0 -or $values[2] -ne 0){throw ('Invalid RTSS frames accepted: '+$scenario)}
   if($scenario -eq 'dead' -and $null -ne $rtss.GetField('rtssView',$static).GetValue($null)){throw 'Dead RTSS feed remains attached'}
  }elseif($values[1] -ne $expectedFps -or [Math]::Abs($values[2]-$expectedMs) -gt 0.001){throw ('RTSS FPS/frame time incorrect: '+$scenario)}
 }finally{$rtss.GetMethod('CloseRtss').Invoke($null,@());$view.Dispose();$map.Dispose()}
}
Write-Output 'RTSS frame calculations, timestamp wrap, process isolation and dead-feed release passed'
