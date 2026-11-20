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
                        }
                        if(size.Width==768){
                            optionsType.GetField("Scale").SetValue(options,100);
                            canvas.DrawToBitmap(scaled,new Rectangle(Point.Empty,size));
                            int maximum=(int)canvasType.GetField("rightScrollMax",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(canvas);
                            if(maximum>0)throw new Exception("Portrait layout clips ordinary four-drive inventory");
                            scaled.Save("fullblack-preview.png",System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                }
                if(((IDictionary)canvasType.GetField("fonts",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(canvas)).Count>180)throw new Exception("Font cache grows without limit");
                Console.WriteLine("Full Black and portrait/landscape scaling passed");
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
                themeChoice.SelectedItem="fullblack";
                settings.Show(); Application.DoEvents();
                var tabs=settings.Controls.OfType<TabControl>().First(); tabs.SelectedIndex=4; Application.DoEvents();
                var componentBox=tabs.TabPages[4].Controls[0].Controls.OfType<TextBox>().Single(c=>c.Name=="PowerComponents");
                if(!componentBox.Visible||componentBox.Height<200||componentBox.Width<300||componentBox.Text.Length==0)throw new Exception("Power breakdown is missing or collapsed");
                settings.Hide();
                settings.GetType().GetMethod("Save",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(settings,null);
                var saved=settings.GetType().GetField("Value").GetValue(settings);
                if((string)optionsType.GetField("Theme").GetValue(saved)!="fullblack")throw new Exception("Full Black choice does not persist");
            }
            var newer=assembly.GetType("MonitorStatsNative.Services",true).GetMethod("IsNewer");
            foreach(string tag in new[]{"v1.0.5","v1.0.4","invalid",""})if((bool)newer.Invoke(null,new object[]{tag}))throw new Exception("Incorrect update notification");
            if(!(bool)newer.Invoke(null,new object[]{"v1.0.10"}))throw new Exception("Numeric update comparison failed");
            Console.WriteLine("Settings layout and native update comparison passed");
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
