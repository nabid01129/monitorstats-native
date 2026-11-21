param([string]$BuildDirectory,[ValidateSet('x86','x64')][string]$Architecture='x64')
$ErrorActionPreference='Stop'
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$output=if($BuildDirectory){[IO.Path]::GetFullPath($BuildDirectory)}else{Join-Path $PSScriptRoot 'build'}
$exe=Join-Path $output 'MonitorStats Native.exe'
$check=@'
using System;
using System.Collections;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
class Checks {
    [STAThread] static int Main() {
        try {
            var assembly=Assembly.LoadFrom("MonitorStats Native.exe");
            var optionsType=assembly.GetType("MonitorStatsNative.Options",true);
            var options=Activator.CreateInstance(optionsType,true);
            optionsType.GetField("Poll").SetValue(options,27);
            optionsType.GetField("Width").SetValue(options,-10);
            optionsType.GetMethod("Normalize").Invoke(options,null);
            if((int)optionsType.GetField("Poll").GetValue(options)!=3000||(int)optionsType.GetField("Width").GetValue(options)!=25)throw new Exception("Settings validation failed");
            var canvasType=assembly.GetType("MonitorStatsNative.Canvas",true);
            var dataType=assembly.GetType("MonitorStatsNative.Data",true);
            Func<object[],object> data=values=>dataType.GetMethod("Of").Invoke(null,new object[]{values});
            string windows=(string)canvasType.GetMethod("WindowsLabel",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{"Windows 10 Enterprise","Professional","26300","26H2"});
            if(windows!="Windows 11 Pro \u00b7 26H2")throw new Exception("Windows edition detection failed: "+windows);
            Console.WriteLine("Windows edition uses Professional over conflicting product text");
            using(var canvas=(Control)Activator.CreateInstance(canvasType,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,null,new[]{options},null))
            using(var bitmap=new Bitmap(768,1280)) {
                canvas.Size=bitmap.Size;canvas.CreateControl();
                var paint=canvasType.GetMethod("OnPaint",BindingFlags.Instance|BindingFlags.NonPublic);
                foreach(string game in new[]{"", " ", null, "ExampleGame"}){
                    canvasType.GetField("Game").SetValue(canvas,game);
                    canvasType.GetField("Fps").SetValue(canvas,(double?)60);
                    canvasType.GetField("Stats").SetValue(canvas,Activator.CreateInstance(dataType));
                    for(int repaint=0;repaint<4;repaint++)using(var g=Graphics.FromImage(bitmap))using(var args=new PaintEventArgs(g,new Rectangle(Point.Empty,bitmap.Size)))paint.Invoke(canvas,new object[]{args});
                }
                canvasType.GetField("Game").SetValue(canvas,null);canvasType.GetField("Fps").SetValue(canvas,null);
                                for(int frame=0;frame<200;frame++){
                    canvasType.GetField("Game").SetValue(canvas,frame%3==0?"":frame%3==1?null:"ExampleGame");
                    canvasType.GetField("Fps").SetValue(canvas,frame%4==0?null:(object)60.0);
                    if(frame%7==0)canvasType.GetField("Stats").SetValue(canvas,Activator.CreateInstance(dataType));
                    using(var g=Graphics.FromImage(bitmap))using(var args=new PaintEventArgs(g,new Rectangle(Point.Empty,bitmap.Size)))paint.Invoke(canvas,new object[]{args});
                }
                canvasType.GetField("Game").SetValue(canvas,null);canvasType.GetField("Fps").SetValue(canvas,null);
                Console.WriteLine("200 repeated paints across game exit, missing name and telemetry transitions passed");
                int themes=0;
                foreach(object theme in (IEnumerable)assembly.GetType("MonitorStatsNative.Theme",true).GetField("All").GetValue(null)){
                    optionsType.GetField("Theme").SetValue(options,theme.GetType().GetField("Id").GetValue(theme));
                    canvas.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));themes++;
                }
                if(themes!=21)throw new Exception("Theme catalog incomplete");
                Console.WriteLine("Rendered all "+themes+" native themes");
                optionsType.GetField("Theme").SetValue(options,"fullblack");
                canvas.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                if(bitmap.GetPixel(0,0).ToArgb()!=Color.Black.ToArgb())throw new Exception("Full Black background is not black");
                object drive=data(new object[]{"id","C:","letter","C:","name","Example long NVMe drive model (C:)","type","NVMe","usedGB",100.0,"freeGB",900.0,"totalGB",1000.0,"readKBps",0.0,"writeKBps",22.0,"health","Healthy","tempC",38.0,"wearPercent",2.0,"powerOnHours",1000.0,"hostWritesTB",15.0});
                object rich=data(new object[]{"cpu",data(new object[]{"model","Intel Core i5-10400 CPU @ 2.90 GHz","loadPercent",12.0,"powerW",20.0,"cores",new double[12]}),
                    "gpu",data(new object[]{"model","NVIDIA GeForce RTX 4060","vramUsedGB",1.0,"vramTotalGB",8.0}),
                    "ram",data(new object[]{"spec","DDR4 - Dual channel","stickCapacitiesGB",new double[]{8,8,8,8},"stickCount",4,"totalGB",32.0}),
                    "drives",new[]{drive,drive,drive,drive}});
                canvasType.GetField("Stats").SetValue(canvas,rich);
                object richExtra=data(new object[]{"name","Network","adapter","Example adapter","routerModel","Example router","publicIPv4","192.0.2.1","localIP","192.0.2.2","publicIPv6","2001:db8:1234:5678:abcd:efab:cdef:1234","provider","Example ISP","rxOpticalPowerDbm",-24.5,"opticalUpdatedAt","10:00","dnsResolver",data(new object[]{"name","Example DNS","country","Example","location","Example city","pingMs",10})});
                canvasType.GetField("Extras").SetValue(canvas,richExtra);
                foreach(var size in new[]{new Size(480,800),new Size(768,1318),new Size(1280,720),new Size(1920,1080),new Size(2560,1440)}){
                    using(var scaled=new Bitmap(size.Width,size.Height)){
                        canvas.Size=size;
                        foreach(int scale in new[]{70,100,130}){
                            optionsType.GetField("Scale").SetValue(options,scale);
                            canvas.DrawToBitmap(scaled,new Rectangle(Point.Empty,size));
                            foreach(string scrollField in new[]{"leftScrollMax","rightScrollMax"})if((int)canvasType.GetField(scrollField,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(canvas)!=0)throw new Exception("Layout requires scrolling");
                        }
                        if(size.Width==768){
                            optionsType.GetField("Scale").SetValue(options,100);
                            canvas.DrawToBitmap(scaled,new Rectangle(Point.Empty,size));
                            foreach(string scrollField in new[]{"leftScrollMax","rightScrollMax"})if((int)canvasType.GetField(scrollField,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(canvas)!=0)throw new Exception("Layout requires scrolling");
                            int maximum=(int)canvasType.GetField("rightScrollMax",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(canvas);
                            if(maximum>0)throw new Exception("Portrait layout clips ordinary four-drive inventory");
                            scaled.Save("fullblack-preview.png",System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                }
                if(((IDictionary)canvasType.GetField("fonts",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(canvas)).Count>180)throw new Exception("Font cache grows without limit");
                canvas.Size=new Size(768,1318);
                optionsType.GetField("Scale").SetValue(options,100);
                canvas.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                var fontMethod=canvasType.GetMethod("FontFor",BindingFlags.Instance|BindingFlags.NonPublic);
                float normalFont=((Font)fontMethod.Invoke(canvas,new object[]{12f,false,true})).Size;
                optionsType.GetField("Scale").SetValue(options,120);
                canvas.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                float zoomFont=((Font)fontMethod.Invoke(canvas,new object[]{12f,false,true})).Size;
                bitmap.Save("text-120-preview.png",System.Drawing.Imaging.ImageFormat.Png);
                if(zoomFont<normalFont*1.15)throw new Exception("Text scale does not enlarge fonts");
                Console.WriteLine("Full Black, portrait/landscape scaling and visible 120% zoom passed");
                var originalStats=canvasType.GetField("Stats").GetValue(canvas);
                string firstOrder=null;
                foreach(bool reverse in new[]{false,true}){
                    object mouse=data(new object[]{"name","Mouse","category","Mouse","batteryPercent",75});
                    object earbuds=data(new object[]{"name","Earbuds","category","Earbuds","batteryPercent",90});
                    canvasType.GetField("Stats").SetValue(canvas,data(new object[]{"batteries",reverse?new[]{earbuds,mouse}:new[]{mouse,earbuds}}));
                    var built=(Array)canvasType.GetMethod("Build",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(canvas,null);
                    object system=((IEnumerable)built.GetValue(0)).Cast<object>().First();
                    var ordered=new System.Collections.Generic.List<string>();
                    foreach(var row in (IEnumerable)system.GetType().GetField("Rows").GetValue(system))foreach(object line in (IEnumerable)row){string icon=(string)line.GetType().GetField("Icon").GetValue(line);if(icon!=null)ordered.Add(icon);}
                    string order=String.Join("|",ordered);
                    if(firstOrder==null)firstOrder=order;else if(firstOrder!=order)throw new Exception("Battery icons change position when discovery order changes");
                }
                canvasType.GetField("Stats").SetValue(canvas,originalStats);
                Console.WriteLine("Battery icon order stays stable across shuffled discovery results");
                canvas.Size=bitmap.Size;
                optionsType.GetField("Scale").SetValue(options,100);
                for(int shift=0;shift<9;shift++){
                    canvasType.GetField("Shift").SetValue(canvas,shift);
                    canvas.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                    var button=(Rectangle)canvasType.GetField("hideButton",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(canvas);
                    if(button.IsEmpty)throw new Exception("IP visibility button is outside the viewport");
                    var visible=canvasType.GetField("showIps",BindingFlags.NonPublic|BindingFlags.Instance);
                    bool before=(bool)visible.GetValue(canvas);
                    canvasType.GetMethod("OnMouseDown",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(canvas,new object[]{new MouseEventArgs(MouseButtons.Left,1,button.Left+button.Width/2,button.Top+button.Height/2,0)});
                    if((bool)visible.GetValue(canvas)==before)throw new Exception("IP visibility button misses after OLED shift");
                }
                Console.WriteLine("IP visibility button works at all OLED offsets");
                var dataClass=assembly.GetType("MonitorStatsNative.Data",true);
                foreach(string badge in new[]{"Real IP","Shared IP"}){
                    object extra=dataClass.GetMethod("Of").Invoke(null,new object[]{new object[]{"publicIPv4","192.0.2.1","ipv4Type",badge}});
                    canvasType.GetField("Extras").SetValue(canvas,extra);
                    canvas.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                    var columns=(Array)canvasType.GetMethod("Build",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(canvas,null);
                    bool found=false;
                    foreach(object card in (IEnumerable)columns.GetValue(1)){
                        if((string)card.GetType().GetField("Title").GetValue(card)!="Network")continue;
                        foreach(var row in (IEnumerable)card.GetType().GetField("Rows").GetValue(card))foreach(object line in (IEnumerable)row){
                            if((string)line.GetType().GetField("Caption").GetValue(line)=="Public IPv4"&&(string)line.GetType().GetField("Badge").GetValue(line)==badge)found=true;
                        }
                    }
                    if(!found)throw new Exception("Missing "+badge+" badge");
                }
                Console.WriteLine("Rendered Real IP and Shared IP badges");
            }
            object cpu=data(new object[]{"model","Intel Core i5-10400","powerW",20.0,"loadPercent",20.0});
            object gpu=data(new object[]{"model","NVIDIA GeForce RTX 4060","watt",50.0,"coreLoadPercent",10.0});
            object ram=data(new object[]{"generation","DDR4","totalGB",32.0,"stickCount",4,"speedMTs",2666});
            object stats=data(new object[]{"cpu",cpu,"gpu",gpu,"ram",ram,"systemPowerW",123.0,"systemPowerKind","whole-system"});
            var estimate=assembly.GetType("MonitorStatsNative.Power",true).GetMethod("Read").Invoke(null,new object[]{stats,Screen.AllScreens});
            double total=(double)dataType.GetMethod("Number").Invoke(estimate,new object[]{"watts"});
            if(total!=123)throw new Exception("Measured total power was double counted");
            Console.WriteLine("Measured total power preserved");
            var bounds=assembly.GetType("MonitorStatsNative.Dashboard",true).GetMethod("WidgetBounds",BindingFlags.NonPublic|BindingFlags.Static);
            optionsType.GetField("Fill").SetValue(options,false);
            optionsType.GetField("Width").SetValue(options,25);
            optionsType.GetField("Height").SetValue(options,25);
            foreach(var area in new[]{new Rectangle(0,0,768,1318),new Rectangle(-1920,0,1920,1040),new Rectangle(0,0,320,240)}){
                foreach(string position in new[]{"left","center","right"}){
                    optionsType.GetField("Position").SetValue(options,position);
                    var placed=(Rectangle)bounds.Invoke(null,new object[]{area,options});
                    if(!area.Contains(placed))throw new Exception("Widget extends outside monitor working area");
                    if(position=="right"&&placed.Right!=area.Right||position=="left"&&placed.Left!=area.Left)throw new Exception("Widget position ignores minimum size");
                }
            }
            Console.WriteLine("Window placement stays within each monitor working area");
            var memory=assembly.GetType("MonitorStatsNative.VideoMemory",true).GetMethod("Read").Invoke(null,new object[]{"NVIDIA GeForce RTX 4060"});
            if(memory!=null)Console.WriteLine("Windows GPU memory record "+Convert.ToDouble(memory).ToString("0.00")+" GB");
            optionsType.GetField("Theme").SetValue(options,"fullblack");
            using(var settings=(Form)Activator.CreateInstance(assembly.GetType("MonitorStatsNative.Settings",true),BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,null,new object[]{options,stats,estimate,""},null)){
                if(settings.BackColor.ToArgb()!=SystemColors.Control.ToArgb())throw new Exception("Settings does not use the standard light appearance");
                var controls=(System.Collections.Generic.Dictionary<string,Control>)settings.GetType().GetField("inputs",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(settings);
                var themeChoice=(ComboBox)controls["theme"];
                foreach(object theme in (IEnumerable)assembly.GetType("MonitorStatsNative.Theme",true).GetField("All").GetValue(null)){
                    themeChoice.SelectedItem=theme.GetType().GetField("Id").GetValue(theme);
                    if(settings.BackColor.ToArgb()!=SystemColors.Control.ToArgb()||themeChoice.BackColor.ToArgb()!=SystemColors.Window.ToArgb())throw new Exception("Dashboard theme changes settings colors");
                }
                themeChoice.SelectedItem="fullblack"; ((ComboBox)controls["poll"]).SelectedItem="1000";
                settings.Show(); Application.DoEvents();
                var tabs=settings.Controls.OfType<TabControl>().First(); tabs.SelectedIndex=4; Application.DoEvents();
                var componentBox=tabs.TabPages[4].Controls[0].Controls.OfType<TextBox>().Single(c=>c.Name=="PowerComponents");
                if(!componentBox.Visible||componentBox.Height<200||componentBox.Width<300||componentBox.Text.Length==0)throw new Exception("Power breakdown is missing or collapsed");
                settings.Hide();
                settings.GetType().GetMethod("Save",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(settings,null);
                var saved=settings.GetType().GetField("Value").GetValue(settings);
                if((int)optionsType.GetField("Poll").GetValue(saved)!=1000)throw new Exception("Formatted polling choice does not save its millisecond value");
                if((string)optionsType.GetField("Theme").GetValue(saved)!="fullblack")throw new Exception("Full Black choice does not persist");
            }
            var newer=assembly.GetType("MonitorStatsNative.Services",true).GetMethod("IsNewer");
            foreach(string tag in new[]{"v1.0.15","v1.0.14","v1.0.13","v1.0.12","v1.0.11","v1.0.10","v1.0.9","v1.0.8","v1.0.7","v1.0.6","v1.0.5","v1.0.4","invalid",""})if((bool)newer.Invoke(null,new object[]{tag}))throw new Exception("Incorrect update notification");
            if(!(bool)newer.Invoke(null,new object[]{"v1.0.16"}))throw new Exception("Numeric update comparison failed");
            var serviceType=assembly.GetType("MonitorStatsNative.Services",true);
            var binding=BindingFlags.Instance|BindingFlags.NonPublic;
            using(var service=(IDisposable)Activator.CreateInstance(serviceType,new[]{options})){
                var deadline=DateTime.UtcNow.AddMinutes(20);
                serviceType.GetField("opticalNext",binding).SetValue(service,deadline);
                serviceType.GetField("details",binding).SetValue(service,data(new object[]{"rxOpticalPowerDbm",-25.0,"opticalUpdatedAt","test","opticalStale",true}));
                var changed=optionsType.GetMethod("Clone").Invoke(options,null);
                optionsType.GetField("Theme").SetValue(changed,"classic");
                serviceType.GetMethod("Configure").Invoke(service,new[]{changed});
                if((DateTime)serviceType.GetField("opticalNext",binding).GetValue(service)!=deadline)throw new Exception("Appearance changes restart ONU polling");
                optionsType.GetField("Username").SetValue(changed,"changed-test-account");
                serviceType.GetMethod("Configure").Invoke(service,new[]{changed});
                var snapshot=serviceType.GetMethod("Snapshot").Invoke(service,null);
                if(dataType.GetMethod("Get").Invoke(snapshot,new object[]{"rxOpticalPowerDbm"})!=null)throw new Exception("Changed ONU credentials retain old readings");
            }
            var batteryType=assembly.GetType("MonitorStatsPeripheralBattery",true);
            var batteryFlags=BindingFlags.Static|BindingFlags.NonPublic;
            var apply=batteryType.GetMethod("ApplyG3Report",batteryFlags);
            batteryType.GetField("g3Identified",batteryFlags).SetValue(null,true);
            var stamp=DateTime.UtcNow;
            apply.Invoke(null,new object[]{new byte[]{9,10,2,0,0,0,0,0},8,stamp});
            if((bool?)batteryType.GetField("g3Online",batteryFlags).GetValue(null)!=false)throw new Exception("Mouse disconnect ignored");
            apply.Invoke(null,new object[]{new byte[]{9,10,5,76,0,0,0,0},8,stamp});
            if((bool?)batteryType.GetField("g3Online",batteryFlags).GetValue(null)!=true||(int?)batteryType.GetField("g3Percent",batteryFlags).GetValue(null)!=76)throw new Exception("Fresh battery cannot restore connection");
            apply.Invoke(null,new object[]{new byte[]{9,10,5,255,0,0,0,0},8,stamp});
            if((int?)batteryType.GetField("g3Percent",batteryFlags).GetValue(null)!=76)throw new Exception("Invalid battery percentage accepted");
            batteryType.GetMethod("CloseG3",batteryFlags).Invoke(null,null);
            var readingType=assembly.GetType("MonitorStatsPeripheralBatteryReading",true);
            var connectedCheck=assembly.GetType("MonitorStatsNative.BluetoothState",true).GetMethod("IsConnected",BindingFlags.Static|BindingFlags.NonPublic);
            foreach(string category in new[]{"Earbuds","Keyboard","Audio","Controller","Mouse","Wireless device"}){
                var reading=Activator.CreateInstance(readingType);
                readingType.GetProperty("Name").SetValue(reading,"Example "+category,null);
                readingType.GetProperty("Category").SetValue(reading,category,null);
                readingType.GetProperty("IsBluetoothDevice").SetValue(reading,true,null);
                readingType.GetProperty("ContainerId").SetValue(reading,"test-container",null);
                var online=new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase){"test-container"};
                if(!(bool)connectedCheck.Invoke(null,new object[]{reading,online}))throw new Exception("Connected generic device omitted: "+category);
                online.Clear();
                if((bool)connectedCheck.Invoke(null,new object[]{reading,online}))throw new Exception("Disconnected generic device retained: "+category);
            }
            var bluetooth=assembly.GetType("MonitorStatsNative.BluetoothState",true);
            int compositeSize=System.Runtime.InteropServices.Marshal.SizeOf(bluetooth.GetNestedType("CompositeKey",BindingFlags.NonPublic));
            int propertySize=System.Runtime.InteropServices.Marshal.SizeOf(bluetooth.GetNestedType("Property",BindingFlags.NonPublic));
            int objectSize=System.Runtime.InteropServices.Marshal.SizeOf(bluetooth.GetNestedType("DeviceObject",BindingFlags.NonPublic));
            if(compositeSize!=(IntPtr.Size==8?32:28)||propertySize!=(IntPtr.Size==8?48:40)||objectSize!=(IntPtr.Size==8?32:16))throw new Exception("Windows device connection structures have incorrect alignment");
            var pollingDeadline=assembly.GetType("MonitorStatsNative.Dashboard",true).GetMethod("NextDeadline",BindingFlags.Static|BindingFlags.NonPublic);
            foreach(var scenario in new[]{new long[]{0,0,1000},new long[]{1000,1000,2000},new long[]{1000,3500,4000},new long[]{4000,3600,4000}})
                if((long)pollingDeadline.Invoke(null,new object[]{scenario[0],scenario[1],1000})!=scenario[2])throw new Exception("Polling deadline drifts or replays missed ticks");
            var asyncType=assembly.GetType("MonitorStatsNative.AsyncSnapshot`1",true).MakeGenericType(typeof(string));
            using(var entered=new System.Threading.ManualResetEvent(false))using(var gate=new System.Threading.ManualResetEvent(false))using(var released=new System.Threading.ManualResetEvent(false)){
                int calls=0,cleanups=0;
                Func<string> read=()=>{System.Threading.Interlocked.Increment(ref calls);entered.Set();gate.WaitOne(5000);return "fresh";};
                Action cleanup=()=>{System.Threading.Interlocked.Increment(ref cleanups);released.Set();};
                var provider=(IDisposable)Activator.CreateInstance(asyncType,new object[]{"initial",read,cleanup});
                try{
                    var watch=System.Diagnostics.Stopwatch.StartNew();asyncType.GetMethod("Request").Invoke(provider,null);watch.Stop();
                    if(watch.ElapsedMilliseconds>500||!entered.WaitOne(2000))throw new Exception("Slow provider blocks the polling caller");
                    for(int i=0;i<10;i++)asyncType.GetMethod("Request").Invoke(provider,null);
                    if(calls!=1||(string)asyncType.GetProperty("Value").GetValue(provider,null)!="initial")throw new Exception("Provider requests overlap or snapshot is unstable");
                    provider.Dispose();if(cleanups!=0)throw new Exception("Provider disposed while read still owns its handles");
                    gate.Set();if(!released.WaitOne(2000)||cleanups!=1)throw new Exception("Provider cleanup was lost or duplicated");
                    if((string)asyncType.GetProperty("Value").GetValue(provider,null)!="initial")throw new Exception("Disposed provider publishes stale data");
                }finally{gate.Set();provider.Dispose();}
            }

            Console.WriteLine("Polling deadlines and nonblocking provider lifecycle passed");
            Console.WriteLine("Settings, battery reconnect, generic devices and native connection layouts passed");
            return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
'@
$source=Join-Path $output 'Checks.cs'
[IO.File]::WriteAllText($source,$check)
& (Join-Path $framework 'csc.exe') /nologo /target:exe "/platform:$Architecture" /optimize+ "/out:$output/Checks.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll $source
if($LASTEXITCODE -ne 0){throw 'Validation compilation failed'}
Push-Location $output
try { & './Checks.exe'; if($LASTEXITCODE -ne 0){throw 'Native validation failed'} } finally { Pop-Location }

powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'validate-feed.ps1') -Assembly $exe
if($LASTEXITCODE -ne 0){throw 'Afterburner recovery validation failed'}
