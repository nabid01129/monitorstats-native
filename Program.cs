using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MonitorStatsNative {
    internal static class Program {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string className,string title);
        [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
        [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
        public const string Version = "1.0.24";
        [STAThread] static void Main(string[] args) {
            System.Net.ServicePointManager.SecurityProtocol=System.Net.SecurityProtocolType.Tls12;
            SetProcessDPIAware();
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            AppDomain.CurrentDomain.UnhandledException += (s,e) => Log(e.ExceptionObject.ToString());
            Application.ThreadException += (s,e) => { Log(e.Exception.ToString()); MessageBox.Show("MonitorStats Native encountered an error. Details were saved in its local error log."); };
            bool first;
            bool measuring=args.Length>=3&&args[0]=="--measure";
            using(var instance=new Mutex(true,measuring?@"Local\MonitorStatsNative.Measure":@"Local\MonitorStatsNative",out first)) {
                if(!first){if(!measuring){IntPtr window=FindWindow(null,"MonitorStats Native");if(window!=IntPtr.Zero){ShowWindow(window,9);SetForegroundWindow(window);}}return;}
                try { Application.Run(new Dashboard(args)); } catch(Exception error) { Log(error.ToString()); }
            }
        }
        internal static void Log(string message) {
            try { Directory.CreateDirectory(Options.Folder); File.AppendAllText(Path.Combine(Options.Folder,"errors.log"),DateTime.Now+" "+message+Environment.NewLine); } catch { }
        }
    }
    internal static class NativeDisplay {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        struct DisplayDevice {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Description;
            public int State;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Id;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Key;
        }
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool EnumDisplayDevices(string device,uint number,ref DisplayDevice data,uint flags);
        static DateTime modelsUntil;
        static System.Collections.Generic.List<Data> models=new System.Collections.Generic.List<Data>();
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        struct Mode {
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Device;
            public short Spec,Driver,Size,Extra;public uint Fields;public int X,Y;public uint Orientation,Fixed;public short Color,Duplex,YRes,TT,Collate;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Form;
            public short Pixels;public uint Bits,Width,Height,Flags,Frequency,ICMMethod,ICMIntent,Media,Dither,Reserved1,Reserved2,PanningWidth,PanningHeight;
        }
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool EnumDisplaySettings(string device,int mode,ref Mode data);
        public static int RefreshRate(string device) { var m=new Mode();m.Size=(short)Marshal.SizeOf(typeof(Mode));return EnumDisplaySettings(device,-1,ref m)?(int)m.Frequency:60; }
        public static Screen[] Screens() { return Screen.AllScreens.OrderBy(s=>s.Primary?0:1).ThenBy(s=>s.Bounds.X).ThenBy(s=>s.Bounds.Y).ToArray(); }
        internal static int ScreenIndex(Screen[] screens,string device){
            int selected=Array.FindIndex(screens,s=>s.DeviceName==device);
            if(selected<0)selected=Array.FindIndex(screens,s=>!s.Primary);
            return Math.Max(0,selected);
        }
        public static string Model(Screen screen) {
            if(DateTime.UtcNow>=modelsUntil){modelsUntil=DateTime.UtcNow.AddMinutes(2);models=Query.Wmi("SELECT InstanceName,UserFriendlyName FROM WmiMonitorID",@"root\wmi");}
            var device=new DisplayDevice();device.Size=Marshal.SizeOf(typeof(DisplayDevice));
            if(EnumDisplayDevices(screen.DeviceName,0,ref device,0)){
                string identity=device.Id??"";var segments=identity.Split('\\');
                var model=models.FirstOrDefault(m=>segments.Length>1&&m.Text("InstanceName").IndexOf("\\"+segments[1]+"\\",StringComparison.OrdinalIgnoreCase)>=0);
                var codes=model==null?null:model.Get("UserFriendlyName") as ushort[];
                if(codes!=null)return new string(codes.Where(c=>c>0).Select(c=>(char)c).ToArray());
                if(device.Description!="Generic PnP Monitor")return device.Description;
            }
            return "";
        }
        public static string Label(Screen s,int i) { string model=Model(s);return "Monitor "+(i+1)+(s.Primary?" · Primary":"")+(model.Length>0?" · "+model:"")+" · "+s.Bounds.Width+"×"+s.Bounds.Height+" · "+RefreshRate(s.DeviceName)+" Hz · "+(s.Bounds.Height>s.Bounds.Width?"Portrait":"Landscape"); }
    }
    internal sealed class Dashboard : Form {
        Options options;
        readonly Telemetry telemetry=new Telemetry();
        readonly Services services;
        readonly System.Windows.Forms.Timer clock=new System.Windows.Forms.Timer();
        readonly System.Threading.Timer collector;
        readonly Canvas canvas;
        readonly FlowLayoutPanel toolbar=new FlowLayoutPanel();
        readonly NotifyIcon tray;
        readonly WindowsUpdates windowsUpdates=new WindowsUpdates();
        Data latest=new Data(), power=new Data();
        int collecting, refreshRequested=1, pollingChanged, shiftIndex, telemetryReleased, backgroundPolling;
        long nextSample, lastShift;
        long nextMemoryMaintenance;
        readonly Stopwatch uptime=Stopwatch.StartNew();
        string update="";
        volatile bool closing;
        long sampleCount,firstSampleAt,lastSampleAt,maxCollectionMs;
        string measurePath;int measureSeconds;long peakWorking,peakPrivate;TimeSpan initialCpu;
        bool measuredSettings;
        readonly bool reviewMode;
        bool settingsOpen;
        public Dashboard(string[] args) {
            options=Options.Load();reviewMode=args.Contains("--review");
            if(args.Length>=3&&args[0]=="--measure") { measurePath=Path.GetFullPath(args[1]);Int32.TryParse(args[2],out measureSeconds); options.Startup=false;if(args.Length>3&&Theme.All.Any(t=>t.Id==args[3]))options.Theme=args[3]; }
            Text="MonitorStats Native";ShowInTaskbar=measurePath!=null||reviewMode;FormBorderStyle=FormBorderStyle.None;MinimumSize=new Size(380,400);
            Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            canvas=new Canvas(options);canvas.Dock=DockStyle.Fill;Controls.Add(canvas);
            toolbar.AutoSize=true;toolbar.AutoSizeMode=AutoSizeMode.GrowAndShrink;toolbar.WrapContents=false;toolbar.FlowDirection=FlowDirection.LeftToRight;toolbar.Padding=new Padding(0);
            ButtonButton("Refresh",RequestRefresh);
            ButtonButton("Settings",OpenSettings);
            ButtonButton("—",()=>WindowState=FormWindowState.Minimized);
            ButtonButton("□",()=>{options.Fill=!options.Fill;ApplyLayout();});
            Controls.Add(toolbar);
            canvas.HideIpsChanged+=()=>canvas.Invalidate();
            canvas.WindowsUpdateClicked+=()=>{try{Process.Start("ms-settings:windowsupdate");}catch(Exception error){MessageBox.Show(this,error.Message,"Could not open Windows Update");}};
            services=new Services(options);
            services.Updated+=()=> { if(!closing&&IsHandleCreated)try{BeginInvoke((Action)(()=>{canvas.Extras=services.Snapshot();canvas.Invalidate();}));}catch(InvalidOperationException){} };
            var menu=new ContextMenuStrip();
            menu.Items.Add("Show",null,(s,e)=>{Show();WindowState=FormWindowState.Normal;});
            menu.Items.Add("Hide",null,(s,e)=>Hide());
            menu.Items.Add("Refresh",null,(s,e)=>RequestRefresh());
            menu.Items.Add("Settings",null,(s,e)=>OpenSettings());
            menu.Items.Add("Exit",null,(s,e)=>Close());
            tray=new NotifyIcon { Icon=Icon,Text="MonitorStats Native",ContextMenuStrip=menu,Visible=true };
            tray.DoubleClick+=(s,e)=>{Show();WindowState=FormWindowState.Normal;};
            SystemEvents.DisplaySettingsChanged+=DisplaysChanged;
            clock.Interval=1000;clock.Tick+=Clock;clock.Start();
            using(var process=Process.GetCurrentProcess())initialCpu=process.TotalProcessorTime;
            collector=new System.Threading.Timer(Collect,null,Timeout.Infinite,Timeout.Infinite);
            Shown+=(s,e)=> {
                ApplyLayout();collector.Change(0,Timeout.Infinite);
                    System.Threading.Tasks.Task.Run(()=>{string value=Services.CheckUpdate();if(!closing&&IsHandleCreated)try{BeginInvoke((Action)(()=>{
                        update=value;
                        
                        if(Services.IsNewer(value))tray.ShowBalloonTip(5000,"MonitorStats update","A newer public release is available. Open Settings to view it.",ToolTipIcon.Info);
                    }));}catch(InvalidOperationException){}});
            };
            Resize+=(s,e)=>{toolbar.Location=new Point(Math.Max(0,ClientSize.Width-toolbar.Width-12),6);toolbar.BringToFront();UpdatePollingState();};
            VisibleChanged+=(s,e)=>UpdatePollingState();
        }
        internal static int PollInterval(int selected,bool saving){return saving?Math.Max(10000,selected):selected;}
        void UpdatePollingState(){
            int saving=options.LowPowerWhenHidden&&(!Visible||WindowState==FormWindowState.Minimized)?1:0;
            clock.Interval=saving==1?10000:1000;
            if(Interlocked.Exchange(ref backgroundPolling,saving)!=saving){Interlocked.Exchange(ref pollingChanged,1);WakeCollector();}
        }
        protected override CreateParams CreateParams { get { var cp=base.CreateParams;if(measurePath==null&&!reviewMode)cp.ExStyle|=0x80;return cp; } }
        void ButtonButton(string label,Action clicked) {
            var button=new Button { Text=label,AutoSize=true,FlatStyle=FlatStyle.Flat,Margin=new Padding(2),TabStop=false,Height=25 };
            button.Click+=(s,e)=>clicked();toolbar.Controls.Add(button);
        }
        void DisplaysChanged(object s,EventArgs e) { if(!closing&&IsHandleCreated)try{BeginInvoke((Action)(()=>{if(!closing)ApplyLayout();}));}catch(InvalidOperationException){} }
        void ApplyLayout() {
            var screens=NativeDisplay.Screens();var screen=screens[NativeDisplay.ScreenIndex(screens,options.Display)];
            options.GameMonitor=Math.Max(1,Math.Min(screens.Length,options.GameMonitor));
            Rectangle area=screen.WorkingArea;
            MinimumSize=new Size(Math.Min(380,area.Width),Math.Min(400,area.Height));
            Bounds=WidgetBounds(area,options);
            canvas.Options=options;canvas.Invalidate();
            var theme=Theme.Find(options.Theme);BackColor=theme.Background;toolbar.BackColor=theme.Background;
            foreach(Control c in toolbar.Controls){c.BackColor=theme.Panel;c.ForeColor=theme.Text;}
            toolbar.Location=new Point(ClientSize.Width-toolbar.Width-12,6);
        }
        internal static Rectangle WidgetBounds(Rectangle area,Options value){
            int width=value.Fill?area.Width:Math.Min(area.Width,Math.Max(Math.Min(380,area.Width),area.Width*value.Width/100));
            int height=value.Fill?area.Height:Math.Min(area.Height,Math.Max(Math.Min(400,area.Height),area.Height*value.Height/100));
            int x=value.Position=="left"?area.Left:value.Position=="right"?area.Right-width:area.Left+(area.Width-width)/2;
            return new Rectangle(x,area.Top+(area.Height-height)/2,width,height);
        }
        void OpenSettings() {
            if(settingsOpen||closing)return;
            settingsOpen=true;try{ShowSettings();}finally{settingsOpen=false;}
        }
        void ShowSettings() {
            using(var dialog=new Settings(options,latest,power,update,()=>SensorStatus.Describe(latest,power,services.Snapshot(),windowsUpdates.Snapshot,
                PollInterval(options.Poll,Volatile.Read(ref backgroundPolling)!=0),Volatile.Read(ref backgroundPolling)!=0,
                options.Endpoint.Length>0&&options.Username.Length>0&&options.Password.Length>0,options.Fps,canvas.Fps!=null))) {
                System.Windows.Forms.Timer preview=null;
                if(measurePath!=null){preview=new System.Windows.Forms.Timer{Interval=15000};preview.Tick+=(s,e)=>{dialog.DialogResult=DialogResult.Cancel;dialog.Close();};preview.Start();}
                DialogResult result;
                try {result=dialog.ShowDialog(this);} finally {
                    if(preview!=null)preview.Dispose();
                    if(!closing)BeginInvoke((Action)(()=>{if(GC.GetTotalMemory(false)>4*1048576)GC.Collect(2,GCCollectionMode.Forced,false);}));
                }
                if(result!=DialogResult.OK)return;
                try{dialog.Value.Save();}catch(Exception error){MessageBox.Show(this,error.Message,"Settings could not be saved");return;}
                bool changed=options.Poll!=dialog.Value.Poll||options.LowPowerWhenHidden!=dialog.Value.LowPowerWhenHidden;
                options=dialog.Value;
                services.Configure(options);ApplyLayout();UpdatePollingState();
                if(changed){Interlocked.Exchange(ref pollingChanged,1);WakeCollector();}
            }
        }
        void WakeCollector(){if(closing)return;try{collector.Change(0,Timeout.Infinite);}catch(ObjectDisposedException){}}
        void RequestRefresh(){Interlocked.Exchange(ref refreshRequested,1);WakeCollector();}
        internal static long NextDeadline(long deadline,long now,int interval){if(interval<=0)throw new ArgumentOutOfRangeException("interval");return deadline>now?deadline:deadline+((now-deadline)/interval+1)*interval;}
        void ReleaseTelemetry(){if(Interlocked.Exchange(ref telemetryReleased,1)==0)telemetry.Dispose();}
        void Collect(object state) {
            if(closing||Interlocked.Exchange(ref collecting,1)!=0)return;
            try {
                bool refresh=Interlocked.Exchange(ref refreshRequested,0)!=0;
                if(refresh) {telemetry.Refresh();services.Refresh();nextSample=0;}
                windowsUpdates.Tick(refresh);
                if(Interlocked.Exchange(ref pollingChanged,0)!=0)nextSample=0;
                if(uptime.ElapsedMilliseconds>=nextSample) {
                    int interval=PollInterval(options.Poll,Volatile.Read(ref backgroundPolling)!=0);
                    nextSample=NextDeadline(nextSample,uptime.ElapsedMilliseconds,interval);
                    long started=uptime.ElapsedMilliseconds;
                    var sample=telemetry.Read();
                    if(measurePath!=null){long completed=uptime.ElapsedMilliseconds;if(sampleCount==0)firstSampleAt=completed;lastSampleAt=completed;sampleCount++;maxCollectionMs=Math.Max(maxCollectionMs,completed-started);}
                    services.Tick(sample.Section("network"));
                    var estimate=Power.Read(sample,NativeDisplay.Screens());
                    if(!closing)BeginInvoke((Action)(()=>{latest=sample;power=estimate;canvas.Stats=sample;canvas.Power=estimate;canvas.WindowsUpdate=windowsUpdates.Snapshot;canvas.Invalidate();}));
                }
            } catch(Exception error) { Program.Log(error.ToString()); }
            finally {
                Interlocked.Exchange(ref collecting,0);
                if(closing)ReleaseTelemetry();
                if(!closing)try{int delay=Volatile.Read(ref refreshRequested)!=0||Volatile.Read(ref pollingChanged)!=0?1:(int)Math.Max(1,Math.Min(Int32.MaxValue,nextSample-uptime.ElapsedMilliseconds));collector.Change(delay,Timeout.Infinite);}catch(ObjectDisposedException){}
            }
        }
        void Clock(object sender,EventArgs args) {
            if(uptime.ElapsedMilliseconds>=nextMemoryMaintenance){
                nextMemoryMaintenance=uptime.ElapsedMilliseconds+30000;
                if(GC.GetTotalMemory(false)>8*1048576)GC.Collect(2,GCCollectionMode.Optimized,false);
            }
            if(Visible&&WindowState!=FormWindowState.Minimized){
                double? fps=null,ms=null;string game=options.Fps?ForegroundMonitor.Sample(options.GameMonitor,out fps,out ms):null;
                canvas.Game=game;canvas.Fps=fps;canvas.RecordFrame(ms);canvas.RefreshClock();
            }
            if(uptime.ElapsedMilliseconds-lastShift>=options.Shift){lastShift=uptime.ElapsedMilliseconds;canvas.Shift=++shiftIndex%9;canvas.Invalidate();}
            if(measurePath!=null) {
                if(!measuredSettings&&uptime.Elapsed.TotalSeconds>=10){measuredSettings=true;BeginInvoke((Action)OpenSettings);}
                using(var process=Process.GetCurrentProcess()){
                    process.Refresh();peakWorking=Math.Max(peakWorking,process.PeakWorkingSet64);peakPrivate=Math.Max(peakPrivate,process.PeakPagedMemorySize64);
                    if(uptime.Elapsed.TotalSeconds>=measureSeconds){
                        var report=Data.Of("version",Program.Version,"architecture",Environment.Is64BitProcess?"x64":"x86","seconds",uptime.Elapsed.TotalSeconds,"processes",1,"peakWorkingSetMB",peakWorking/1048576.0,"peakPrivateMB",peakPrivate/1048576.0,
                            "currentWorkingSetMB",process.WorkingSet64/1048576.0,"currentPrivateMB",process.PrivateMemorySize64/1048576.0,
                            "managedHeapMB",GC.GetTotalMemory(false)/1048576.0,
                            "hardwareSamples",Interlocked.Read(ref sampleCount),"averageHardwareIntervalMs",sampleCount>1?(double?)(lastSampleAt-firstSampleAt)/(sampleCount-1):null,"maxCollectionMs",Interlocked.Read(ref maxCollectionMs),
                            "averageCpuPercent",(process.TotalProcessorTime-initialCpu).TotalSeconds/uptime.Elapsed.TotalSeconds/Environment.ProcessorCount*100,
                            "cpuDetected",latest.Section("cpu").Text("model").Length>0,"gpuDetected",latest.Section("gpu").Text("model").Length>0,
                            "driveCount",latest.Rows("drives").Count(),"batteryCount",latest.Rows("batteries").Count(),"networkDetected",latest.Section("network").Text("adapter").Length>0);
                        var extra=services.Snapshot();
                        report["publicIPv4Detected"]=extra.Text("publicIPv4").Length>0;
                        report["publicIPv6Detected"]=extra.Text("publicIPv6").Length>0;
                        report["routerDetected"]=extra.Text("routerModel").Length>0;
                        report["dnsDetected"]=extra.Section("dnsResolver").Text("name").Length>0;
                        report["gpuLoadValid"]=latest.Section("gpu").Number("coreLoadPercent")>=0&&latest.Section("gpu").Number("coreLoadPercent")<=100;
                        report["vramTotalGB"]=latest.Section("gpu").Number("vramTotalGB");
                        report["fanCount"]=latest.Rows("fans").Count();
                        report["theme"]=options.Theme;
                        File.WriteAllText(measurePath,report.Json());Close();
                    }
                }
            }
        }
        protected override void OnFormClosing(FormClosingEventArgs e) {
            closing=true;clock.Stop();collector.Dispose();services.Dispose();windowsUpdates.Dispose();
            SystemEvents.DisplaySettingsChanged-=DisplaysChanged;tray.Visible=false;tray.Dispose();clock.Dispose();
            // The in-flight collector owns its native handles until it exits.
            if(Volatile.Read(ref collecting)==0)ReleaseTelemetry();
            base.OnFormClosing(e);
        }
    }
}
