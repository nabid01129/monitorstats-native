using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MonitorStatsNative {
    internal partial class Theme {
        public string Id,Family;
        public float Radius;public string BorderStyle;
        public Color Background,Panel,Border,Accent,Text,Muted;
        public Theme(string id,string bg,string panel,string border,string accent,string text,string muted,string font,float radius,string borderStyle) {Id=id;Background=ColorTranslator.FromHtml(bg);Panel=ColorTranslator.FromHtml(panel);Border=ColorTranslator.FromHtml(border);Accent=ColorTranslator.FromHtml(accent);Text=ColorTranslator.FromHtml(text);Muted=ColorTranslator.FromHtml(muted);Family=font;Radius=radius;BorderStyle=borderStyle;}
        public static Theme Find(string id) {return All.FirstOrDefault(t=>t.Id==id)??All[0];}
    }
    internal sealed class Line {
        public string Caption,Text,Icon,Badge;public bool Strong;public Color? Color;
        public Line(string caption,string text,bool strong=false,Color? color=null){Caption=caption;Text=text;Strong=strong;Color=color;}
    }
    internal sealed class Card {
        public Line ClockText,DateText,GameText,FpsText;
        public string Title;public List<Line[]> Rows=new List<Line[]>();public double? Bar;public double[] Cores;public double[] CoreTemps;public bool Graph,Clock;
        public Card(string title){Title=title;}
        public Card Add(params Line[] lines){var present=lines.Where(l=>!String.IsNullOrEmpty(l.Text)).ToArray();if(present.Length>0)Rows.Add(present);return this;}
        public int Height {get{return 42+Rows.Sum(r=>r.Any(l=>l.Caption.Length>0)?44:25)+(Bar.HasValue?16:0)+(Cores!=null?(int)Math.Ceiling(Cores.Length/6.0)*28:0)+(Graph?64:0);}}
    }
    internal sealed class Canvas : Control {
        public Options Options;
        public Data Stats=new Data(),Extras=new Data(),Power=new Data();
        public Data WindowsUpdate=new Data();
        public string Game; public double? Fps;
        public int Shift;
        public event Action HideIpsChanged;
        public event Action WindowsUpdateClicked;
        bool showIps=true;
        readonly Queue<double?> frames=new Queue<double?>();
        readonly Dictionary<int,Font> fonts=new Dictionary<int,Font>();
        readonly Dictionary<int,SolidBrush> brushes=new Dictionary<int,SolidBrush>();
        readonly StringFormat textFormat=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap};
        Theme selectedTheme;string selectedThemeId;
        string fontFamily;
        float textZoom=1f;
        readonly string windowsVersion;
        Rectangle hideButton;
        Rectangle windowsUpdateButton;
        Rectangle windowsUpdateSummaryButton;
        Rectangle clockRegion;
        List<Card>[] cachedColumns;
        Data cachedStats,cachedExtras,cachedPower;
        Data cachedWindowsUpdate;
        Options cachedOptions;
        bool cachedShowIps;
        public Canvas(Options options){
            Options=options;DoubleBuffered=true;ResizeRedraw=true;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
            using(var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))
            using(var key=machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")) {
                string product=Convert.ToString(key==null?null:key.GetValue("ProductName")),edition=Convert.ToString(key==null?null:key.GetValue("EditionID")),build=Convert.ToString(key==null?null:key.GetValue("CurrentBuildNumber")),release=Convert.ToString(key==null?null:key.GetValue("DisplayVersion"));
                windowsVersion=WindowsLabel(product,edition,build,release);
            }
        }
        internal static string WindowsLabel(string product,string edition,string build,string release){
            int number;
            string system=Int32.TryParse(build,out number)&&number>=22000?"Windows 11":"Windows 10";
            string label=edition.StartsWith("Professional",StringComparison.OrdinalIgnoreCase)?edition.Replace("Professional","Pro"):
                edition.StartsWith("Core",StringComparison.OrdinalIgnoreCase)?edition.Replace("Core","Home"):edition;
            if(String.IsNullOrWhiteSpace(label))return product+(release.Length>0?" · "+release:"");
            return system+" "+label+(release.Length>0?" · "+release:"");
        }
        public void RecordFrame(double? ms){frames.Enqueue(ms);while(frames.Count>90)frames.Dequeue();}
        public void RefreshClock(){if(clockRegion.IsEmpty)Invalidate();else Invalidate(clockRegion);}
        void ClearFonts(){foreach(var font in fonts.Values)font.Dispose();fonts.Clear();}
        Theme CurrentTheme(){
            if(selectedTheme==null||selectedThemeId!=Options.Theme){
                selectedTheme=Theme.Find(Options.Theme);selectedThemeId=Options.Theme;
                foreach(var brush in brushes.Values)brush.Dispose();brushes.Clear();
            }
            return selectedTheme;
        }
        SolidBrush BrushFor(Color color){SolidBrush brush;int key=color.ToArgb();if(!brushes.TryGetValue(key,out brush)){brush=new SolidBrush(color);brushes[key]=brush;}return brush;}
        Font FontFor(float size,bool bold=false,bool applyZoom=true){
            var theme=CurrentTheme();
            if(fontFamily!=theme.Family){ClearFonts();fontFamily=theme.Family;}
            if(applyZoom)size*=textZoom;
            int quarter=Math.Max(4,(int)Math.Round(size*4)),key=quarter*2+(bold?1:0);Font fnt;
            if(!fonts.TryGetValue(key,out fnt)){fnt=new Font(theme.Family,quarter/4f,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);fonts[key]=fnt;}
            return fnt;
        }
        new static Line Text(string text,bool bold=false){return new Line("",text,bold);}
        static Line Metric(string caption,Data data,string field,string unit="",int digits=1){return new Line(caption,Query.Format(data.Number(field),unit,digits),true);}
        string IP(string value){return value.Length==0?"":showIps?value:"Hidden";}
        List<Card>[] Build() {
            var left=new List<Card>();var right=new List<Card>();var cpu=Stats.Section("cpu");var gpu=Stats.Section("gpu");var ram=Stats.Section("ram");var net=Stats.Section("network").Copy();
            foreach(var pair in Extras)net[pair.Key]=pair.Value;
            var system=new Card("System"){Clock=true};
            system.ClockText=Text(DateTime.Now.ToString("h:mm:ss tt"),true);
            system.DateText=Text(DateTime.Now.ToString("dddd, MMM d, yyyy"));
            system.Add(system.ClockText,Text(windowsVersion));
            system.Add(system.DateText,Text(WindowsUpdate.Text("heading"),true));
            if(WindowsUpdate.Text("heading").Length>0)system.Add(Text(WindowsUpdate.Text("summary")));
            var batteries=Stats.Rows("batteries").OrderBy(b=>b.Text("category"),StringComparer.OrdinalIgnoreCase).ThenBy(b=>b.Text("name"),StringComparer.OrdinalIgnoreCase).Select(b=>new Line("",b.Number("batteryPercent").HasValue?Query.Format(b.Number("batteryPercent"),"%",0):b.Text("batteryLevel")=="Connected"?"—":b.Text("batteryLevel"),true){Icon=b.Text("category")}).ToArray();
            for(int i=0;i<batteries.Length;i+=4)system.Add(batteries.Skip(i).Take(4).ToArray());
            left.Add(system);
            if(Options.Fps){
                var fps=new Card("Game frame rate · Monitor "+Options.GameMonitor){Graph=true};
                fps.GameText=Text(GameLabel());
                fps.FpsText=Text(Fps.HasValue?Query.Format(Fps," FPS",0):"— FPS",true);
                fps.Add(fps.GameText);fps.Add(fps.FpsText);left.Add(fps);
            }
            var processor=new Card("Processor"){Bar=cpu.Number("loadPercent")};
            processor.Add(Text(Options.Name("cpu",cpu.Text("model")),true));
            processor.Add(Metric("Usage",cpu,"loadPercent","%"),Metric("Clock",cpu,"clockMHz"," MHz",0),Metric("Temp",cpu,"tempC","°C"));
            var temps=cpu.Get("coreTempsC") as double[];
            var cpuFans=Stats.Rows("fans").Where(f=>Query.Match(f.Text("name"),"^CPU$|CPU.*fan|fan.*CPU"));
            var cooling=new List<Line>{Metric("Power",cpu,"powerW"," W")};
            if(temps!=null&&temps.Length>0)cooling.Add(new Line("Max core",Query.Format(temps.Max(),"°C"),true));
            cooling.AddRange(cpuFans.Select(fan=>Metric(fan.Text("name"),fan,"value"," "+fan.Text("units"),0)));
            for(int i=0;i<cooling.Count;i+=3)processor.Add(cooling.Skip(i).Take(3).ToArray());
            if(Options.Cores) {processor.Cores=cpu.Get("cores") as double[];processor.CoreTemps=temps;}
            left.Add(processor);
            var graphics=new Card("Graphics"){Bar=gpu.Number("coreLoadPercent")};graphics.Add(Text(Options.Name("gpu",gpu.Text("model")),true));
            graphics.Add(Metric("GPU load",gpu,"coreLoadPercent","%"),Metric("Temperature",gpu,"tempC","°C"),Metric("Power",gpu,"watt"," W"));
            graphics.Add(Metric("Core clock",gpu,"coreMHz"," MHz",0),Metric("Memory clock",gpu,"vramMHz"," MHz",0),Metric("Fan",gpu,"fanPercent","%"));
            graphics.Add(Metric("Fan RPM",gpu,"fanRPM"," RPM",0));
            if(gpu.Number("vramUsedGB").HasValue||gpu.Number("vramTotalGB").HasValue)graphics.Add(Text("VRAM "+Query.Format(gpu.Number("vramUsedGB")," GB",2)+" / "+Query.Format(gpu.Number("vramTotalGB")," GB",2)));
            left.Add(graphics);
            var process=new Card("Processes");process.Add(Metric("Running processes",ram,"processCount","",0));
            int rank=0;foreach(var p in ram.Rows("topProcesses"))process.Add(Text((++rank)+". "+p.Text("name")),Text(p.Number("workingSetMB")>=1024?Query.Format(p.Number("workingSetMB")/1024," GB",2):Query.Format(p.Number("workingSetMB")," MB",0)));
            left.Add(process);
            if(Power.Count>0){var p=new Card("Total system power");p.Add(Text((Power.Text("mode")=="Measured"?"":"≈ ")+Query.Format(Power.Number("watts")," W",0),true));p.Add(Text("Range "+Query.Format(Power.Number("low"),"",0)+"–"+Query.Format(Power.Number("high")," W",0)),Text(Power.Text("mode")));left.Add(p);}
            var memory=new Card("Memory"){Bar=ram.Number("loadPercent")};memory.Add(Text(Options.Name("ram",ram.Text("spec")),true));
            var sizes=ram.Get("stickCapacitiesGB") as double[];
            if(sizes!=null)memory.Add(Text(sizes.Length+" sticks · "+String.Join(" + ",sizes.Select(s=>s.ToString("0.#")+" GB"))+" · "+Query.Format(ram.Number("speedMTs")," MT/s",0)));
            memory.Add(Metric("Used",ram,"usedGB"," GB",2),Metric("Free",ram,"freeGB"," GB",2),Metric("Total",ram,"totalGB"," GB",2));right.Add(memory);
            var drives=Stats.Rows("drives").ToArray();
            foreach(var drive in drives){
                var d=new Card("Storage · "+drive.Text("letter")+" · "+drive.Text("type")){Bar=drive.Number("loadPercent")};d.Add(Text(Options.Name("drive:"+drive.Text("id"),drive.Text("name")),true));
                d.Add(Metric("Used",drive,"usedGB"," GB",0),Metric("Free",drive,"freeGB"," GB",0),Metric("Total",drive,"totalGB"," GB",0));
                d.Add(Metric("Read",drive,"readKBps"," KB/s"),Metric("Write",drive,"writeKBps"," KB/s"));
                d.Add(Text(drive.Text("health")),Metric("Temp",drive,"tempC","°C"));
                d.Add(Metric("Wear used",drive,"wearPercent","%"),Metric("Power-on",drive,"powerOnHours"," h",0),Metric("Host writes",drive,"hostWritesTB"," TB",2));right.Add(d);
            }
            var network=new Card("Network");
            network.Add(Text(net.Text("name"),true));network.Add(Text(String.Join(" · ",new[]{net.Text("adapter"),net.Text("profile"),net.Text("linkSpeed")}.Where(s=>s.Length>0))));
            if(net.Text("routerModel").Length>0)network.Add(Text("Router · "+net.Text("routerManufacturer")+" "+net.Text("routerModel")));
            double factor=Options.Unit=="Mbps"?0.008192:0.001024;
            network.Add(new Line("Received",Query.Format(net.Number("inKBps")*factor," "+Options.Unit,2),true),new Line("Sent",Query.Format(net.Number("outKBps")*factor," "+Options.Unit,2),true));
            network.Add(new Line("Local IP",IP(net.Text("localIP")),true),new Line("Public IPv4",IP(net.Text("publicIPv4")),true){Badge=net.Text("ipv4Type")});
            network.Add(new Line("Public IPv6",IP(net.Text("publicIPv6")),true));
            network.Add(new Line("ISP",net.Text("provider"),true),Metric("RX optical power",net,"rxOpticalPowerDbm"," dBm",2));
            if(net.Get("rxOpticalPowerDbm")!=null)network.Add(Text((net.Text("opticalStale")=="True"?"Last reading · ":"Updated ")+net.Text("opticalUpdatedAt")));
            var dns=net.Section("dnsResolver");
            if(dns.Count>0){
                System.Net.IPAddress address;
                string name=dns.Text("name");
                if(!showIps&&System.Net.IPAddress.TryParse(name,out address))name="Hidden";
                network.Add(Text("Active DNS "+name+(dns.Text("country").Length>0?" · "+dns.Text("country"):"")),Metric("Ping",dns,"pingMs"," ms",0));network.Add(Text("Server "+dns.Text("location")));
            }
            right.Add(network);
            return new[]{left,right};
        }
        string GameLabel(){return !String.IsNullOrWhiteSpace(Game)?Game:Fps.HasValue?"Game process unavailable":"Waiting for game frames from RTSS";}
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            var g=e.Graphics;var theme=CurrentTheme();g.FillRectangle(BrushFor(theme.Background),e.ClipRectangle);g.SmoothingMode=SmoothingMode.AntiAlias;
            if(fonts.Count>128)ClearFonts();
            if(cachedColumns==null||cachedStats!=Stats||cachedExtras!=Extras||cachedPower!=Power||cachedWindowsUpdate!=WindowsUpdate||cachedOptions!=Options||cachedShowIps!=showIps){
                cachedColumns=Build();cachedStats=Stats;cachedExtras=Extras;cachedPower=Power;cachedWindowsUpdate=WindowsUpdate;cachedOptions=Options;cachedShowIps=showIps;
            }else{
                var system=cachedColumns[0][0];
                system.ClockText.Text=DateTime.Now.ToString("h:mm:ss tt");system.DateText.Text=DateTime.Now.ToString("dddd, MMM d, yyyy");
                var fps=cachedColumns[0].FirstOrDefault(card=>card.Graph);
                if(fps!=null){fps.GameText.Text=GameLabel();fps.FpsText.Text=Fps.HasValue?Query.Format(Fps," FPS",0):"— FPS";}
            }
            var columns=cachedColumns;
            float user=Options.Scale/100f;
            textZoom=user;
            float monitorScale=Options.AutoScale?Math.Min(ClientSize.Width/768f,ClientSize.Height/1366f):1f;
            monitorScale=Math.Max(0.45f,Math.Min(2f,monitorScale));
            float scale=monitorScale;
            float width=ClientSize.Width/scale,height=ClientSize.Height/scale;
            g.ScaleTransform(scale,scale);
            int dx=(Shift%3-1)*2,dy=(Shift/3-1)*2;g.TranslateTransform(dx,dy);
            float columnWidth=(width-36)/2;
            clockRegion=Rectangle.Empty;hideButton=Rectangle.Empty;windowsUpdateButton=Rectangle.Empty;windowsUpdateSummaryButton=Rectangle.Empty;
            for(int c=0;c<2;c++){
                float total=columns[c].Sum(card=>card.Height);
                // Fit cards within the monitor; the user scale adjusts text independently.
                float fit=Math.Max(0.001f,Math.Min(1,(height-50-columns[c].Count*10)/total));
                float x=12+c*(columnWidth+12),y=40;
                foreach(var card in columns[c]){
                    float h=card.Height*fit;
                    var pixels=Rectangle.Ceiling(new RectangleF((x+dx)*scale,(y+dy)*scale,columnWidth*scale,h*scale));
                    if(c==0&&card.Clock)clockRegion=pixels;
                    if(card.Clock&&WindowsUpdate.Text("heading").Length>0)windowsUpdateButton=Rectangle.Intersect(ClientRectangle,Rectangle.Ceiling(new RectangleF((x+columnWidth/2+dx)*scale,(y+37*fit+dy)*scale,(columnWidth/2-12)*scale,(h-37*fit)*scale)));
                    if(card.Clock&&WindowsUpdate.Text("summary").Length>0)windowsUpdateSummaryButton=Rectangle.Intersect(ClientRectangle,Rectangle.Ceiling(new RectangleF((x+12+dx)*scale,(y+87*fit+dy)*scale,(columnWidth-24)*scale,25*fit*scale)));
                    if(c==0&&card.Graph)clockRegion=Rectangle.Union(clockRegion,pixels);
                    if(card.Title=="Network"){
                        PointF[] points={new PointF(x+columnWidth-75,y+6*fit),new PointF(x+columnWidth-10,y+28*fit)};
                        using(var transform=g.Transform)transform.TransformPoints(points);
                        hideButton=Rectangle.Intersect(ClientRectangle,Rectangle.Ceiling(RectangleF.FromLTRB(points[0].X,points[0].Y,points[1].X,points[1].Y)));
                    }
                    if(y+h>0&&y<height&&pixels.IntersectsWith(e.ClipRectangle))DrawCard(g,card,new RectangleF(x,y,columnWidth,h),fit,theme);
                    y+=h+10;
                }
            }
        }
        void DrawCard(Graphics g,Card card,RectangleF box,float fit,Theme theme){
            using(var outline=new GraphicsPath()){
                float radius=theme.Radius*fit*2;
                if(radius>0){outline.AddArc(box.Left,box.Top,radius,radius,180,90);outline.AddArc(box.Right-radius,box.Top,radius,radius,270,90);outline.AddArc(box.Right-radius,box.Bottom-radius,radius,radius,0,90);outline.AddArc(box.Left,box.Bottom-radius,radius,radius,90,90);outline.CloseFigure();}
                else outline.AddRectangle(box);
                g.FillPath(BrushFor(theme.Panel),outline);
                using(var pen=new Pen(theme.Border)){if(theme.BorderStyle=="dashed")pen.DashStyle=DashStyle.Dash;g.DrawPath(pen,outline);if(theme.BorderStyle=="double")g.DrawRectangle(pen,box.X+3,box.Y+3,box.Width-6,box.Height-6);}
            }
            float y=box.Y+12*fit;DrawText(g,card.Title.ToUpperInvariant(),FontFor(12*fit,true),theme.Accent,new RectangleF(box.X+12,y,box.Width-24,20*fit));y+=25*fit;
            if(card.Title=="Network"){
                var button=new RectangleF(box.Right-75,box.Y+6*fit,65,22*fit);
                DrawText(g,showIps?"Hide IPs":"Show IPs",FontFor(10*fit),theme.Text,button);
            }
            foreach(var row in card.Rows){
                float w=(box.Width-24)/row.Length;
                for(int i=0;i<row.Length;i++){
                    var line=row[i];float x=box.X+12+i*w;
                    if(line.Caption.Length>0){
                        DrawText(g,line.Caption.ToUpperInvariant(),FontFor(10*fit),theme.Muted,new RectangleF(x,y,w-4,14*fit));
                        if(line.Badge=="Real IP"||line.Badge=="Shared IP"){
                            Font badgeFont=FontFor(9*fit,true);
                            float captionWidth=g.MeasureString(line.Caption.ToUpperInvariant(),FontFor(10*fit)).Width;
                            float badgeWidth=g.MeasureString(line.Badge,badgeFont).Width+6*fit;
                            var badge=new RectangleF(x+captionWidth+4*fit,y-1*fit,badgeWidth,14*fit);
                            bool shared=line.Badge=="Shared IP";
                            g.FillRectangle(BrushFor(shared?Color.FromArgb(69,10,10):Color.FromArgb(6,78,59)),badge);
                            DrawText(g,line.Badge,badgeFont,shared?Color.FromArgb(252,165,165):Color.FromArgb(110,231,183),new RectangleF(badge.X+3*fit,badge.Y+1*fit,badge.Width-4*fit,badge.Height));
                        }
                        DrawText(g,line.Text,FontFor(14*fit,line.Strong),line.Color??theme.Text,new RectangleF(x,y+15*fit,w-4,24*fit));
                    }else{
                        float size=card.Clock&&card.Rows.IndexOf(row)==0&&i==0?24*fit:line.Strong?14*fit:12*fit;
                        if(line.Icon!=null){DrawIcon(g,line.Icon,x,y,13*fit,theme);x+=19*fit;}
                        float iconOffset=line.Icon!=null?19*fit:0;
                        DrawText(g,line.Text,FontFor(size,line.Strong),line.Color??(line.Strong?theme.Text:theme.Muted),new RectangleF(x,y,w-4-iconOffset,25*fit));
                    }
                }
                y+=(row.Any(l=>l.Caption.Length>0)?44:25)*fit;
            }
            if(card.Bar.HasValue){DrawBar(g,new RectangleF(box.X+12,y,box.Width-24,5*fit),card.Bar.Value,theme);y+=16*fit;}
            if(card.Cores!=null)for(int i=0;i<card.Cores.Length;i++){
                float w=(box.Width-24)/6,x=box.X+12+(i%6)*w,cy=y+(i/6)*28*fit;
                DrawText(g,(i+1).ToString(),FontFor(8*fit),theme.Muted,new RectangleF(x,cy,w,12*fit));
                DrawBar(g,new RectangleF(x,cy+13*fit,w-8,4*fit),card.Cores[i],theme);
                if(card.CoreTemps!=null&&i<card.CoreTemps.Length)DrawText(g,card.CoreTemps[i].ToString("0")+"°",FontFor(7*fit),theme.Muted,new RectangleF(x,cy+18*fit,w,10*fit));
            }
            if(card.Graph){
                var graph=new RectangleF(box.X+12,y,box.Width-24,50*fit);using(var pen=new Pen(theme.Border))g.DrawRectangle(pen,graph.X,graph.Y,graph.Width,graph.Height);
                var samples=frames.ToArray();using(var pen=new Pen(theme.Accent,1.2f)){
                    for(int i=1;i<samples.Length;i++)if(samples[i-1].HasValue&&samples[i].HasValue){
                        float x0=graph.X+(i-1)*graph.Width/89,x1=graph.X+i*graph.Width/89;
                        float y0=graph.Bottom-(float)Math.Min(100,samples[i-1].Value)/100*graph.Height,y1=graph.Bottom-(float)Math.Min(100,samples[i].Value)/100*graph.Height;
                        g.DrawLine(pen,x0,y0,x1,y1);
                    }
                }
            }
        }
        static void DrawIcon(Graphics g,string category,float x,float y,float size,Theme theme){
            using(var pen=new Pen(theme.Muted,Math.Max(1,size/12))){
                if(category=="Mouse"){g.DrawEllipse(pen,x+size/4,y,size/2,size);g.DrawLine(pen,x+size/2,y+2,x+size/2,y+size/3);}
                else if(category=="Earbuds"){g.DrawEllipse(pen,x,y,size/3,size/3);g.DrawLine(pen,x+size/6,y+size/3,x+size/6,y+size);g.DrawEllipse(pen,x+size*2/3,y,size/3,size/3);g.DrawLine(pen,x+size*5/6,y+size/3,x+size*5/6,y+size);}
                else if(category=="Audio"){g.DrawArc(pen,x,y,size,size,180,180);g.DrawRectangle(pen,x,y+size/2,size/5,size/2);g.DrawRectangle(pen,x+size*4/5,y+size/2,size/5,size/2);}
                else if(category=="Keyboard"){g.DrawRectangle(pen,x,y+size/4,size,size/2);g.DrawLine(pen,x+2,y+size/2,x+size-2,y+size/2);}
                else if(category=="Controller"){g.DrawEllipse(pen,x,y+size/4,size,size*2/3);g.DrawLine(pen,x+size/4,y+size/3,x+size/4,y+size*2/3);g.DrawLine(pen,x+size/8,y+size/2,x+size*3/8,y+size/2);}
                else g.DrawRectangle(pen,x+size/4,y,size/2,size);
            }
        }
        void DrawText(Graphics g,string value,Font font,Color color,RectangleF box){
            if(String.IsNullOrEmpty(value)||box.Width<=0||box.Height<=0)return;
            {
                // IPv6 scales down within its full-width row.
                SizeF measured=g.MeasureString(value,font);
                float shrink=Math.Min(1,Math.Min(box.Width/Math.Max(1,measured.Width)*0.98f,box.Height/Math.Max(1,font.GetHeight(g))));
                g.DrawString(value,shrink<1?FontFor(font.Size*shrink,font.Bold,false):font,BrushFor(color),box,textFormat);
            }
        }
        void DrawBar(Graphics g,RectangleF box,double percent,Theme theme){
            g.FillRectangle(BrushFor(theme.Border),box);
            g.FillRectangle(BrushFor(theme.Accent),box.X,box.Y,box.Width*(float)Math.Max(0,Math.Min(100,percent))/100,box.Height);
        }
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;if(windowsUpdateButton.Contains(e.Location)||windowsUpdateSummaryButton.Contains(e.Location)){var update=WindowsUpdateClicked;if(update!=null)update();return;}if(hideButton.Contains(e.Location)){showIps=!showIps;var h=HideIpsChanged;if(h!=null)h();Invalidate();}}
        protected override void Dispose(bool disposing){if(disposing){ClearFonts();foreach(var brush in brushes.Values)brush.Dispose();brushes.Clear();textFormat.Dispose();}base.Dispose(disposing);}
    }
}
