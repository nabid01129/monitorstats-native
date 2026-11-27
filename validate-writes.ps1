param([string]$BuildDirectory)
$ErrorActionPreference='Stop'
$output=if($BuildDirectory){[IO.Path]::GetFullPath($BuildDirectory)}else{Join-Path $PSScriptRoot 'build'}
$source=@'
using System;
using System.IO;
using System.Collections;
using System.Linq;
using System.Reflection;
class WriteChecks {
    static Type history;
    static BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static object New(string path){return Activator.CreateInstance(history,flags,null,new object[]{path},null);}
    static object Call(object target,string method,params object[] args){return history.GetMethod(method,flags).Invoke(target,args);}
    static object Sample(object target,string id,ulong write,ulong clock,DateTime now){return Call(target,"Sample",id,write,write,clock,(ulong)1000,now);}
    static double Number(object data,string name){return (double?)data.GetType().GetMethod("Number").Invoke(data,new object[]{name})??-1;}
    static void Expect(object data,double day,double month){if(Number(data,"writes24hBytes")!=day||Number(data,"writes30dBytes")!=month)throw new Exception("Incorrect rolling totals: "+Number(data,"writes24hBytes")+", "+Number(data,"writes30dBytes"));}
    static void Close(object target){((IDisposable)target).Dispose();}
    static Type appType,itemType;
    static object Item(int pid,ulong created,string name,ulong bytes){var item=Activator.CreateInstance(itemType);itemType.GetField("Id").SetValue(item,pid);itemType.GetField("Created").SetValue(item,created);itemType.GetField("Name").SetValue(item,name);itemType.GetField("Bytes").SetValue(item,bytes);return item;}
    static object Observe(object tracker,DateTime now,params object[] items){var array=Array.CreateInstance(itemType,items.Length);for(int i=0;i<items.Length;i++)array.SetValue(items[i],i);appType.GetMethod("Observe",flags).Invoke(tracker,new object[]{array,now});return appType.GetProperty("Snapshot",flags).GetValue(tracker,null);}
    static object[] Leaders(object snapshot,string window){return ((IEnumerable)snapshot.GetType().GetMethod("Rows").Invoke(snapshot,new object[]{window})).Cast<object>().ToArray();}
    static string Name(object item){return (string)item.GetType().GetMethod("Text").Invoke(item,new object[]{"name"});}
    static int Main(){
        string folder=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"write-history-fixture-"+Guid.NewGuid().ToString("N"));
        try{
            var assembly=Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"MonitorStats Native.exe"));
            history=assembly.GetType("MonitorStatsNative.DriveWriteHistory",true);
            DateTime start=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc);
            var tracker=New(folder);
            Expect(Sample(tracker,"volume-A",1000000,1000,start),0,0);
            var burst=Sample(tracker,"volume-A",1006000,2000,start.AddSeconds(1));
            Expect(burst,6000,6000);
            if(Math.Abs(Number(burst,"writeKBps")-6000.0/1024)>0.000001)throw new Exception("Raw counter rate is incorrect");
            Expect(Sample(tracker,"volume-B",900000000,1000,start),0,0);
            Expect(Sample(tracker,"volume-B",900000500,2000,start.AddSeconds(1)),500,500);
            // Provider reset must establish a baseline, not manufacture a huge delta.
            Expect(Sample(tracker,"volume-A",10,10,start.AddSeconds(2)),6000,6000);
            Expect(Sample(tracker,"volume-A",210,1010,start.AddSeconds(3)),6200,6200);
            Expect(Call(tracker,"Snapshot","volume-A",start.AddDays(1)),0,6200);
            Expect(Call(tracker,"Snapshot","volume-A",start.AddDays(30)),0,0);
            Expect(Call(tracker,"Snapshot","volume-A",start.AddSeconds(4)),6200,6200);
            Close(tracker);
            tracker=New(folder);
            // No baseline is loaded: writes during app closure are not backfilled
            // into an invented time bucket. The stored totals still survive.
            Expect(Sample(tracker,"volume-A",5000000000,200000,start.AddMinutes(5)),6200,6200);
            Expect(Sample(tracker,"volume-A",5000001000,201000,start.AddMinutes(5).AddSeconds(1)),7200,7200);
            Call(tracker,"Retain",(object)new string[]{"volume-B"});
            // Reinsert the same volume (including a different drive letter) by GUID.
            Expect(Sample(tracker,"volume-A",7000000000,300000,start.AddMinutes(6)),7200,7200);
            // A replacement volume on the same letter uses a different GUID.
            Expect(Sample(tracker,"replacement-volume",9999999999,300000,start.AddMinutes(6)),0,0);
            Expect(Sample(tracker,"volume-A",7000009000,301000,start.AddMinutes(5)),7200,7200);
            // Backwards wall-clock movement does not record the uncertain interval.
            Expect(Sample(tracker,"volume-A",7000010000,302000,start.AddMinutes(5).AddSeconds(1)),8200,8200);
            Close(tracker);
            var writeBytes=assembly.GetType("MonitorStatsNative.Canvas",true).GetMethod("WriteBytes",BindingFlags.Static|BindingFlags.NonPublic);
            if((string)writeBytes.Invoke(null,new object[]{(double?)0})!="0 B"||(string)writeBytes.Invoke(null,new object[]{(double?)Double.NaN})!="")throw new Exception("Write-byte display fails for zero or invalid values");
            // Damaged files reset history safely; no partially loaded totals.
            foreach(string path in Directory.GetFiles(folder,"*.bin"))File.WriteAllBytes(path,new byte[]{1,2,3});
            tracker=New(folder);
            Expect(Sample(tracker,"volume-A",1,1,start.AddHours(1)),0,0);Close(tracker);
            string blocked=Path.Combine(folder,"blocked");File.WriteAllText(blocked,"not a directory");
            tracker=New(blocked);Sample(tracker,"volume-A",1,1,start);
            Expect(Sample(tracker,"volume-A",101,1001,start.AddSeconds(1)),100,100);Close(tracker);
            appType=assembly.GetType("MonitorStatsNative.AppWriteHistory",true);itemType=assembly.GetType("ProcessWriteItem",true);
            string appPath=Path.Combine(folder,"app-writes.bin");
            var apps=Activator.CreateInstance(appType,flags,null,new object[]{appPath,start},null);
            Observe(apps,start,Item(1,10,"chrome",1000),Item(2,20,"Chrome",2000),Item(3,30,"backup",500));
            var result=Observe(apps,start.AddSeconds(30),Item(1,10,"chrome",1600),Item(2,20,"Chrome",2900),Item(3,30,"backup",2500));
            var leaders=Leaders(result,"last24h");
            if(leaders.Length!=2||Name(leaders[0])!="backup"||Number(leaders[0],"bytes")!=2000||Number(leaders[1],"bytes")!=1500)throw new Exception("App instances not aggregated or ranked correctly");
            // PID reuse and process restarts must never count lifetime counters.
            result=Observe(apps,start.AddSeconds(60),Item(1,99,"other-app",999999999));
            if(Leaders(result,"last24h").Length!=2)throw new Exception("PID reuse manufactured app writes");
            result=Observe(apps,start.AddSeconds(90),Item(1,99,"other-app",1000000099));
            if(Leaders(result,"last24h").Length!=3)throw new Exception("New app delta not recorded");
            ((IDisposable)apps).Dispose();
            apps=Activator.CreateInstance(appType,flags,null,new object[]{appPath,start.AddMinutes(5)},null);
            result=Observe(apps,start.AddMinutes(5),Item(1,99,"other-app",9999999999));
            if(Number(Leaders(result,"last24h")[0],"bytes")!=2000)throw new Exception("App history did not survive restart or counted closed-app interval");
            result=Observe(apps,start.AddDays(1));
            if(Leaders(result,"last24h").Length!=0||Leaders(result,"last30d").Length!=3)throw new Exception("App 24-hour window did not expire independently");
            result=Observe(apps,start.AddDays(30));
            if(Leaders(result,"last30d").Length!=0)throw new Exception("App 30-day window did not expire");
            ((IDisposable)apps).Dispose();
            File.WriteAllBytes(appPath,new byte[]{1,2,3});
            apps=Activator.CreateInstance(appType,flags,null,new object[]{appPath,start},null);
            result=Observe(apps,start,Item(1,1,"app",100));
            if(Leaders(result,"last24h").Length!=0)throw new Exception("Corrupt app history was retained");
            ((IDisposable)apps).Dispose();
            Console.WriteLine("App write I/O aggregation, top-three ranking, PID reuse, restart persistence, independent rolling windows and corrupt-file recovery passed");
            Console.WriteLine("Write bursts, independent volumes, rolling expiry, restart persistence, drive replacement, counter/clock resets, malformed files and read-only history passed");
            return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
}
'@
[IO.File]::WriteAllText((Join-Path $output 'WriteChecks.cs'),$source)
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /optimize+ ("/out:"+(Join-Path $output 'WriteChecks.exe')) (Join-Path $output 'WriteChecks.cs')
if($LASTEXITCODE -ne 0){throw 'Write history validation compilation failed'}
& (Join-Path $output 'WriteChecks.exe')
if($LASTEXITCODE -ne 0){throw 'Write history validation failed'}
