param([string]$BuildDirectory,[ValidateSet('x86','x64')][string]$Architecture='x64')
$ErrorActionPreference='Stop'
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$testHost=if($Architecture -eq 'x86'){Join-Path $env:WINDIR 'SysWOW64/WindowsPowerShell/v1.0/powershell.exe'}else{Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'}
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
            if((bool)optionsType.GetField("LowPowerWhenHidden").GetValue(options))throw new Exception("Background mode must be opt-in");
            optionsType.GetField("Poll").SetValue(options,27);
            optionsType.GetField("Width").SetValue(options,-10);
            optionsType.GetMethod("Normalize").Invoke(options,null);
            if((int)optionsType.GetField("Poll").GetValue(options)!=3000||(int)optionsType.GetField("Width").GetValue(options)!=25)throw new Exception("Settings validation failed");
            var canvasType=assembly.GetType("MonitorStatsNative.Canvas",true);
            var dataType=assembly.GetType("MonitorStatsNative.Data",true);
            Func<object[],object> data=values=>dataType.GetMethod("Of").Invoke(null,new object[]{values});
            var numeric=data(new object[]{"integer",-12,"unsigned",UInt64.MaxValue,"float",0.1f,"decimal",12.75m,"precise",1.2345678901234567,"text","1.25e2","zero",0,"nan",Double.NaN,"infinity",Double.PositiveInfinity,"boolean",true,"bad","1,25","character",'7',"enum",DayOfWeek.Monday,"unnamedEnum",(DayOfWeek)17});
            var readNumber=(Func<string,double?>)Delegate.CreateDelegate(typeof(Func<string,double?>),numeric,dataType.GetMethod("Number"));
            var culture=System.Globalization.CultureInfo.CurrentCulture;
            try{
                System.Globalization.CultureInfo.CurrentCulture=new System.Globalization.CultureInfo("de-DE");
                foreach(var expected in new[]{new{Key="integer",Value=-12.0},new{Key="unsigned",Value=(double)UInt64.MaxValue},new{Key="float",Value=(double)0.1f},new{Key="decimal",Value=12.75},new{Key="precise",Value=1.2345678901234567},new{Key="text",Value=125.0},new{Key="zero",Value=0.0},new{Key="character",Value=7.0},new{Key="unnamedEnum",Value=17.0}})
                    if(readNumber(expected.Key)!=expected.Value)throw new Exception("Numeric reading loses precision or depends on regional settings: "+expected.Key);
                foreach(string key in new[]{"missing","nan","infinity","boolean","bad","enum"})if(readNumber(key).HasValue)throw new Exception("Invalid numeric reading accepted: "+key);
            }finally{System.Globalization.CultureInfo.CurrentCulture=culture;}
            Console.WriteLine("Numeric precision, zero values, invalid readings and regional settings passed");
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
                var updateType=assembly.GetType("MonitorStatsNative.WindowsUpdates",true);
                var notice=updateType.GetMethod("Notice",BindingFlags.Static|BindingFlags.NonPublic);
                var emptyNotice=notice.Invoke(null,new object[]{0,false,new string[0]});
                if((string)dataType.GetMethod("Text").Invoke(emptyNotice,new object[]{"heading"})!="")throw new Exception("Update notice shown without pending updates");
                var pendingNotice=notice.Invoke(null,new object[]{3,false,new[]{"Windows security update (KB0000001)",".NET update"}});
                if(!((string)dataType.GetMethod("Text").Invoke(pendingNotice,new object[]{"summary"})).Contains("+2 more"))throw new Exception("Update summary does not account for remaining updates");
                canvasType.GetField("WindowsUpdate").SetValue(canvas,pendingNotice);
                int updateClicks=0;
                canvasType.GetEvent("WindowsUpdateClicked").AddEventHandler(canvas,(Action)(()=>updateClicks++));
                for(int offset=0;offset<9;offset++){
                    canvasType.GetField("Shift").SetValue(canvas,offset);
                    canvas.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                    var button=(Rectangle)canvasType.GetField("windowsUpdateButton",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(canvas);
                    if(button.IsEmpty)throw new Exception("Windows Update notice has no visible click target");
                    canvasType.GetMethod("OnMouseDown",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(canvas,new object[]{new MouseEventArgs(MouseButtons.Left,1,button.Left+button.Width/2,button.Top+button.Height/2,0)});
                    var summaryButton=(Rectangle)canvasType.GetField("windowsUpdateSummaryButton",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(canvas);
                    if(summaryButton.IsEmpty)throw new Exception("Update summary is not clickable");
                    canvasType.GetMethod("OnMouseDown",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(canvas,new object[]{new MouseEventArgs(MouseButtons.Left,1,summaryButton.Left+2,summaryButton.Top+summaryButton.Height/2,0)});
                    canvasType.GetMethod("OnMouseDown",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(canvas,new object[]{new MouseEventArgs(MouseButtons.Right,1,button.Left+2,button.Top+2,0)});
                }
                if(updateClicks!=18)throw new Exception("Windows Update click target drifts during OLED shifting or responds to right clicks");
                var restartNotice=notice.Invoke(null,new object[]{0,true,new string[0]});
                if(!((string)dataType.GetMethod("Text").Invoke(restartNotice,new object[]{"heading"})).Contains("restart"))throw new Exception("Restart requirement is omitted");
                foreach(object theme in (IEnumerable)assembly.GetType("MonitorStatsNative.Theme",true).GetField("All").GetValue(null)){
                    optionsType.GetField("Theme").SetValue(options,theme.GetType().GetField("Id").GetValue(theme));
                    canvas.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                }
                canvasType.GetField("Shift").SetValue(canvas,0);
                Console.WriteLine("Windows Update summary, restart notice and OLED click targets passed");
                Console.WriteLine("Rendered all "+themes+" native themes");
                optionsType.GetField("Theme").SetValue(options,"fullblack");
                canvas.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                if(bitmap.GetPixel(0,0).ToArgb()!=Color.Black.ToArgb())throw new Exception("Full Black background is not black");
                object drive=data(new object[]{"id","C:","letter","C:","name","Example long NVMe drive model (C:)","type","NVMe","usedGB",100.0,"freeGB",900.0,"totalGB",1000.0,"readKBps",0.0,"writeKBps",22.0,"health","Healthy","tempC",38.0,"wearPercent",2.0,"powerOnHours",1000.0,"hostWritesTB",15.0});
                object rich=data(new object[]{"cpu",data(new object[]{"model","Example CPU","loadPercent",12.0,"powerW",20.0,"cores",new double[12]}),
                    "gpu",data(new object[]{"model","Example GPU","vramUsedGB",1.0,"vramTotalGB",8.0}),
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
                            using(var overflow=new Bitmap(size.Width,size.Height+100))using(var g=Graphics.FromImage(overflow))using(var args=new PaintEventArgs(g,new Rectangle(0,0,overflow.Width,overflow.Height))){
                                paint.Invoke(canvas,new object[]{args});
                                for(int y=size.Height;y<overflow.Height;y++)for(int x=0;x<overflow.Width;x+=3)if(overflow.GetPixel(x,y).ToArgb()!=Color.Black.ToArgb())throw new Exception("Dashboard draws content below monitor bounds");
                            }
                        }
                        if(size.Width==768){
                            optionsType.GetField("Scale").SetValue(options,100);
                            canvas.DrawToBitmap(scaled,new Rectangle(Point.Empty,size));
                            using(var overflow=new Bitmap(size.Width,size.Height+100))using(var g=Graphics.FromImage(overflow))using(var args=new PaintEventArgs(g,new Rectangle(0,0,overflow.Width,overflow.Height))){
                                paint.Invoke(canvas,new object[]{args});
                                for(int y=size.Height;y<overflow.Height;y++)for(int x=0;x<overflow.Width;x+=3)if(overflow.GetPixel(x,y).ToArgb()!=Color.Black.ToArgb())throw new Exception("Dashboard draws content below monitor bounds");
                            }
                            scaled.Save("fullblack-preview.png",System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                }
                if(((IDictionary)canvasType.GetField("fonts",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(canvas)).Count>180)throw new Exception("Font cache grows without limit");
                if(((IDictionary)canvasType.GetField("brushes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(canvas)).Count>16)throw new Exception("Drawing resource cache grows across theme changes");
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
            object cpu=data(new object[]{"model","Example CPU","powerW",20.0,"loadPercent",20.0});
            object gpu=data(new object[]{"model","Example GPU","watt",50.0,"coreLoadPercent",10.0});
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
            optionsType.GetField("Theme").SetValue(options,"fullblack");
            var originalNames=(System.Collections.Generic.Dictionary<string,string>)optionsType.GetField("Names").GetValue(options);
            originalNames["drive:Z:"]="Offline archive";
            var screens=(Screen[])assembly.GetType("MonitorStatsNative.NativeDisplay",true).GetMethod("Screens").Invoke(null,null);
            var selectScreen=assembly.GetType("MonitorStatsNative.NativeDisplay",true).GetMethod("ScreenIndex",BindingFlags.Static|BindingFlags.NonPublic);
            int automaticScreen=(int)selectScreen.Invoke(null,new object[]{screens,""});
            int expectedScreen=Array.FindIndex(screens,screen=>!screen.Primary);if(expectedScreen<0)expectedScreen=0;
            if(automaticScreen!=expectedScreen||(int)selectScreen.Invoke(null,new object[]{screens,"missing-monitor"})!=expectedScreen)throw new Exception("Automatic monitor selection is inconsistent");
            for(int index=0;index<screens.Length;index++)if((int)selectScreen.Invoke(null,new object[]{screens,screens[index].DeviceName})!=index)throw new Exception("Explicit monitor choice ignored");
            var statusType=assembly.GetType("MonitorStatsNative.SensorStatus",true);
            var diagnosticStats=data(new object[]{"gpu",data(new object[]{"fanRPM",0.0,"fanRPMSource","NVIDIA driver (NVML)"}),"cpu",data(new object[]{"loadPercent",3.0,"loadPercentSource","Windows"})});
            string statusText=(string)statusType.GetMethod("Describe").Invoke(null,new object[]{diagnosticStats,estimate,data(new object[]{"publicIPv4","203.0.113.9"}),data(new object[]{"status","cached"}),1000,false,false,true,false});
            if(!statusText.Contains("GPU fan speed: Available \u2014 NVIDIA driver (NVML)")||!statusText.Contains("CPU clock: Unavailable")||statusText.Contains("203.0.113.9"))throw new Exception("Sensor status is inaccurate or exposes an IP address");
            int statusReads=0;Func<string> readStatus=()=>{statusReads++;return statusText;};
            using(var settings=(Form)Activator.CreateInstance(assembly.GetType("MonitorStatsNative.Settings",true),BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,null,new object[]{options,stats,estimate,"",readStatus},null)){
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
                if(statusReads!=0)throw new Exception("Sensor diagnostics runs before its tab is opened");
                if(tabs.TabPages.Count!=6)throw new Exception("Sensors tab is missing");
                tabs.SelectedIndex=5;Application.DoEvents();
                var statusBox=tabs.TabPages[5].Controls[0].Controls.OfType<TextBox>().Single(c=>c.Name=="SensorStatus");
                if(statusReads!=1||statusBox.Text!=statusText||!statusBox.ReadOnly||!statusBox.Visible||statusBox.Height<200||statusBox.Width<300||statusBox.AutoSize)throw new Exception("Sensors tab does not show current snapshot status");
                tabs.TabPages[5].Controls[0].Controls.OfType<Button>().Single(c=>c.Text=="Refresh status").PerformClick();
                if(statusReads!=2)throw new Exception("Manual status refresh failed");
                ((CheckBox)controls["lowPower"]).Checked=true;
                settings.Hide();
                settings.GetType().GetMethod("Save",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(settings,null);
                var saved=settings.GetType().GetField("Value").GetValue(settings);
                if((int)optionsType.GetField("Poll").GetValue(saved)!=1000)throw new Exception("Formatted polling choice does not save its millisecond value");
                if((string)optionsType.GetField("Theme").GetValue(saved)!="fullblack")throw new Exception("Full Black choice does not persist");
                if(!(bool)optionsType.GetField("LowPowerWhenHidden").GetValue(saved))throw new Exception("Background option does not save");
                if((string)optionsType.GetField("Display").GetValue(saved)!=screens[automaticScreen].DeviceName)throw new Exception("Saving settings changes the automatic widget monitor");
                var savedNames=(System.Collections.Generic.Dictionary<string,string>)optionsType.GetField("Names").GetValue(saved);
                if(!savedNames.ContainsKey("drive:Z:")||savedNames["drive:Z:"]!="Offline archive")throw new Exception("Saving settings erased an offline drive name");
            }
            using(var settings=(Form)Activator.CreateInstance(assembly.GetType("MonitorStatsNative.Settings",true),BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,null,new object[]{options,stats,estimate,""},null)){
                settings.Show();Application.DoEvents();var tabs=settings.Controls.OfType<TabControl>().First();tabs.SelectedIndex=3;Application.DoEvents();
                tabs.TabPages[3].Controls[0].Controls.OfType<Button>().Single(button=>button.Text=="Reset names").PerformClick();
                var reset=optionsType.GetField("Names").GetValue(settings.GetType().GetField("Value").GetValue(settings)) as System.Collections.Generic.Dictionary<string,string>;
                if(reset.Count!=0||!originalNames.ContainsKey("drive:Z:"))throw new Exception("Reset names misses offline entries or changes settings before Save");
                settings.Close();
            }
            Console.WriteLine("Automatic monitor consistency and offline device-name persistence passed");
            Console.WriteLine("On-demand sensor diagnostics, source labels and background option passed");
            var newer=assembly.GetType("MonitorStatsNative.Services",true).GetMethod("IsNewer");
            foreach(string tag in new[]{"v1.0.24","v1.0.23","v1.0.22","v1.0.21","v1.0.20","v1.0.19","v1.0.18","v1.0.17","v1.0.16","v1.0.15","v1.0.14","v1.0.13","v1.0.12","v1.0.11","v1.0.10","v1.0.9","v1.0.8","v1.0.7","v1.0.6","v1.0.5","v1.0.4","invalid",""})if((bool)newer.Invoke(null,new object[]{tag}))throw new Exception("Incorrect update notification");
            if(!(bool)newer.Invoke(null,new object[]{"v1.0.25"}))throw new Exception("Numeric update comparison failed");
            var serviceType=assembly.GetType("MonitorStatsNative.Services",true);
            var networkSnapshot=assembly.GetType("MonitorStatsNative.Telemetry",true).GetMethod("NetworkSnapshot",BindingFlags.Static|BindingFlags.NonPublic);
            var networkDelegate=typeof(Func<>).MakeGenericType(dataType);
            foreach(var error in new Exception[]{new System.Net.NetworkInformation.NetworkInformationException(),new System.Net.Sockets.SocketException(),new InvalidOperationException("Adapter changed")}){
                var read=System.Linq.Expressions.Expression.Lambda(networkDelegate,System.Linq.Expressions.Expression.Throw(System.Linq.Expressions.Expression.Constant(error),dataType)).Compile();
                var isolated=networkSnapshot.Invoke(null,new object[]{read});
                if((int)dataType.GetProperty("Count").GetValue(isolated,null)!=0)throw new Exception("Failed network provider returns stale readings");
            }
            var goodNetwork=data(new object[]{"adapter","Example adapter"});
            var goodRead=System.Linq.Expressions.Expression.Lambda(networkDelegate,System.Linq.Expressions.Expression.Constant(goodNetwork,dataType)).Compile();
            if(networkSnapshot.Invoke(null,new object[]{goodRead})!=goodNetwork)throw new Exception("Network recovery loses fresh readings");
            Console.WriteLine("Network provider failure isolation and recovery passed");
            var binding=BindingFlags.Instance|BindingFlags.NonPublic;
            var merge=serviceType.GetMethod("MergePublicAddresses",BindingFlags.Static|BindingFlags.NonPublic);
            var addresses=data(new object[]{"publicIPv4","203.0.113.7","publicIPv6","2001:db8::7"});
            merge.Invoke(null,new object[]{addresses,null,"2001:db8::8"});
            if((string)dataType.GetMethod("Text").Invoke(addresses,new object[]{"publicIPv4"})!="203.0.113.7")throw new Exception("Transient IPv4 failure erased the last valid address");
            merge.Invoke(null,new object[]{addresses,"203.0.113.8",null});
            if((string)dataType.GetMethod("Text").Invoke(addresses,new object[]{"publicIPv6"})!="2001:db8::8")throw new Exception("Transient IPv6 failure erased the last valid address");
            if((string)dataType.GetMethod("Text").Invoke(addresses,new object[]{"publicIPv4"})!="203.0.113.8")throw new Exception("Fresh public address ignored");
            Console.WriteLine("Independent IPv4/IPv6 failure recovery passed");
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
                serviceType.GetField("details",binding).SetValue(service,addresses);
                serviceType.GetMethod("Changed",binding).Invoke(service,new object[]{null,EventArgs.Empty});
                snapshot=serviceType.GetMethod("Snapshot").Invoke(service,null);
                if((string)dataType.GetMethod("Text").Invoke(snapshot,new object[]{"publicIPv4"})!="")throw new Exception("Network change retains old public addresses");
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
            apply.Invoke(null,new object[]{new byte[]{9,10,2,0,0,0,0,0},8,stamp.AddSeconds(60)});
            if((DateTime)batteryType.GetField("g3NextQuery",batteryFlags).GetValue(null)>stamp.AddSeconds(65))throw new Exception("Sleeping mouse retains five-minute query deadline");
            var currentG3=batteryType.GetMethod("G3Reading",batteryFlags);
            var sleeping=currentG3.Invoke(null,new object[]{stamp.AddSeconds(61)});
            if(sleeping==null||(int?)sleeping.GetType().GetProperty("BatteryPercent").GetValue(sleeping,null)!=76||Convert.ToString(sleeping.GetType().GetProperty("BatteryLevel").GetValue(sleeping,null))!="Cached")throw new Exception("Sleeping mouse loses its marked last-known battery");
            apply.Invoke(null,new object[]{new byte[]{9,10,2,1,0,0,0,0},8,stamp.AddSeconds(62)});
            var wakeReading=currentG3.Invoke(null,new object[]{stamp.AddSeconds(62)});
            if(wakeReading==null||(int?)wakeReading.GetType().GetProperty("BatteryPercent").GetValue(wakeReading,null)!=76)throw new Exception("Wake loses recent battery before next percentage report");
            apply.Invoke(null,new object[]{new byte[]{9,10,5,74,0,0,0,0},8,stamp.AddSeconds(66)});
            if((bool?)batteryType.GetField("g3Online",batteryFlags).GetValue(null)!=true||(int?)batteryType.GetField("g3Percent",batteryFlags).GetValue(null)!=74)throw new Exception("Wake battery response requires reconnect packet");
            apply.Invoke(null,new object[]{new byte[]{9,10,2,1,0,0,0,0},8,stamp.AddMinutes(13)});
            var staleReading=currentG3.Invoke(null,new object[]{stamp.AddMinutes(13)});
            if(staleReading==null||Convert.ToString(staleReading.GetType().GetProperty("BatteryLevel").GetValue(staleReading,null))!="Cached")throw new Exception("Long sleep deletes last-known battery or labels it live");
            if(currentG3.Invoke(null,new object[]{stamp.AddDays(8)})!=null)throw new Exception("Expired battery cache remains visible");
            batteryType.GetMethod("CloseG3",batteryFlags).Invoke(null,null);
            if(currentG3.Invoke(null,new object[]{DateTime.UtcNow})!=null)throw new Exception("Removed receiver retains cached battery");
            var cacheType=assembly.GetType("G3BatteryCache",true);
            var parseCache=cacheType.GetMethod("Parse",BindingFlags.Static|BindingFlags.NonPublic);
            foreach(string invalidCache in new[]{"","101|"+stamp.Ticks,"-1|"+stamp.Ticks,"93|"+stamp.AddDays(1).Ticks,"93|"+stamp.AddDays(-8).Ticks,"93|9223372036854775807"}){
                object[] cacheArgs={invalidCache,stamp,0,DateTime.MinValue};
                if((bool)parseCache.Invoke(null,cacheArgs))throw new Exception("Invalid or expired battery cache accepted");
            }
            object[] goodCache={"93|"+stamp.AddHours(-12).Ticks,stamp,0,DateTime.MinValue};
            if(!(bool)parseCache.Invoke(null,goodCache)||(int)goodCache[2]!=93)throw new Exception("Overnight battery cache cannot restore");
            var batteryText=canvasType.GetMethod("BatteryText",BindingFlags.Static|BindingFlags.NonPublic);
            if((string)batteryText.Invoke(null,new object[]{data(new object[]{"batteryPercent",93,"batteryLevel","Cached"})})!="~93%")throw new Exception("Cached battery is not visibly marked");
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
            var pollInterval=assembly.GetType("MonitorStatsNative.Dashboard",true).GetMethod("PollInterval",BindingFlags.Static|BindingFlags.NonPublic);
            foreach(int interval in new[]{1000,2000,3000,5000,10000}){
                if((int)pollInterval.Invoke(null,new object[]{interval,true})!=10000||(int)pollInterval.Invoke(null,new object[]{interval,false})!=interval)throw new Exception("Background polling changes the selected foreground interval");
            }
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
& (Join-Path $framework 'csc.exe') /nologo /target:library "/platform:$Architecture" /optimize+ "/out:$output/Checks.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll $source
if($LASTEXITCODE -ne 0){throw 'Validation compilation failed'}
Push-Location $output
try { & $testHost -NoProfile -STA -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'validate-host.ps1') -Assembly (Join-Path $output 'Checks.dll'); if($LASTEXITCODE -ne 0){throw 'Native validation failed'} } finally { Pop-Location }

& $testHost -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'validate-feed.ps1') -Assembly $exe
if($LASTEXITCODE -ne 0){throw 'Afterburner recovery validation failed'}
