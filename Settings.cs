using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MonitorStatsNative {
    internal sealed class Settings : Form {
        public Options Value;
        readonly Dictionary<string,Control> inputs=new Dictionary<string,Control>();
        readonly Dictionary<string,TextBox> names=new Dictionary<string,TextBox>();
        readonly Label release=new Label();
        static readonly string Releases=Services.Releases;
        public Settings(Options value,Data stats,Data power,string latest){
            Value=value.Clone();Text="MonitorStats Native settings · "+Program.Version;Size=new Size(720,760);MinimumSize=new Size(560,520);
            StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;Font=new Font("Segoe UI",10);
            var tabs=new TabControl {Dock=DockStyle.Fill};Controls.Add(tabs);
            FlowLayoutPanel general=Page(tabs,"General"),display=Page(tabs,"Display"),network=Page(tabs,"Network"),rename=Page(tabs,"Device names"),wattage=Page(tabs,"Power estimate");
            Add(general,"Native version",new Label{Text=Program.Version+(Environment.Is64BitProcess?" · 64-bit":" · 32-bit"),AutoSize=true});
            release.Text=String.IsNullOrEmpty(latest)?"GitHub update check unavailable":"Latest GitHub release "+latest;release.AutoSize=true;
            Add(general,"Updates",release);
            var update=new Button{Text="Check GitHub updates",AutoSize=true};
            update.Click+=async(s,e)=>{update.Enabled=false;release.Text="Checking…";string tag=await System.Threading.Tasks.Task.Run(()=>Services.CheckUpdate());if(!IsDisposed){release.Text=tag.Length>0?"Latest GitHub release "+tag:"Update check unavailable";update.Enabled=true;Process.Start(Releases);}};
            Add(general,"",update);
            Select(general,"Theme","theme",Theme.All.Select(t=>t.Id).ToArray(),Value.Theme);
            Select(general,"Hardware polling","poll",new[]{"1000","2000","3000","5000","10000"},Value.Poll.ToString());
            Select(general,"OLED shift interval (minutes)","shift",new[]{"1","2","5","10","15","30","60"},(Value.Shift/60000).ToString());
            Check(general,"Start with Windows","startup",Value.Startup);Check(general,"Show game frame rate","fps",Value.Fps);Check(general,"Show CPU thread bars","cores",Value.Cores);
            var screens=NativeDisplay.Screens();
            var target=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=480};
            foreach(var pair in screens.Select((s,i)=>new{Screen=s,Index=i}))target.Items.Add(new Choice{Id=pair.Screen.DeviceName,Text=NativeDisplay.Label(pair.Screen,pair.Index)});
            target.SelectedIndex=Math.Max(0,Array.FindIndex(screens,s=>s.DeviceName==Value.Display));inputs["display"]=target;Add(display,"Widget monitor",target);
            var game=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=480};
            for(int i=0;i<screens.Length;i++)game.Items.Add(new Choice{Id=(i+1).ToString(),Text=NativeDisplay.Label(screens[i],i)});
            game.SelectedIndex=Math.Max(0,Math.Min(screens.Length-1,Value.GameMonitor-1));inputs["game"]=game;Add(display,"Game monitor",game);
            Check(display,"Fill monitor working area","fill",Value.Fill);
            Select(display,"Window position","position",new[]{"left","center","right"},Value.Position);
            Number(display,"Window width (%)","width",25,100,Value.Width);Number(display,"Window height (%)","height",25,100,Value.Height);
            Check(display,"Automatic monitor scaling","auto",Value.AutoScale);Number(display,"UI scale (%)","scale",70,130,Value.Scale);
            Select(network,"Network speed units","unit",new[]{"Mbps","MB/s"},Value.Unit);
            Add(network,"ONU optical power",new Label{Text="Optical readings refresh every 30 minutes. Credentials are encrypted for this Windows account.",AutoSize=true,MaximumSize=new Size(500,0)});
            Input(network,"Local ONU HTTP address","endpoint",Value.Endpoint,false);
            Input(network,"ONU username","username",Value.Username,false);
            Input(network,"ONU password","password",Value.Password,true);
            Add(network,"",new Label{Text="Configure the ONU separately for this native edition.",AutoSize=true});
            var devices=new List<KeyValuePair<string,string>>{new KeyValuePair<string,string>("cpu",stats.Section("cpu").Text("model")),new KeyValuePair<string,string>("gpu",stats.Section("gpu").Text("model")),new KeyValuePair<string,string>("ram",stats.Section("ram").Text("spec"))};
            devices.AddRange(stats.Rows("drives").Select(d=>new KeyValuePair<string,string>("drive:"+d.Text("id"),d.Text("name"))));
            foreach(var device in devices){string custom;Value.Names.TryGetValue(device.Key,out custom);var box=new TextBox{Text=custom??"",Width=510};names[device.Key]=box;Add(rename,device.Value,box);}
            var reset=new Button{Text="Reset names",AutoSize=true};reset.Click+=(s,e)=>{foreach(var box in names.Values)box.Clear();};Add(rename,"",reset);
            var parts=new TextBox{Name="PowerComponents",AutoSize=false,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Width=600,Height=420};
            parts.Text=String.Join(Environment.NewLine+Environment.NewLine,power.Rows("parts").Select(item=>item.Text("name")+"   "+Query.Format(item.Number("watts")," W")+"   ("+item.Text("source")+")"+Environment.NewLine+"Range "+Query.Format(item.Number("low"))+"–"+Query.Format(item.Number("high")," W")+Environment.NewLine+item.Text("detail")));
            Add(wattage,"System power",new Label{AutoSize=true,Text=Query.Format(power.Number("watts")," W")+" · "+power.Text("mode")});Add(wattage,"Components",parts);
            Add(wattage,"",new Label{AutoSize=true,MaximumSize=new Size(550,0),Text="Uninstrumented parts, external displays, and PSU loss use estimates. Exact total power requires a wall meter."});
            var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(6)};
            var save=new Button{Text="Save",AutoSize=true};save.Click+=(s,e)=>Save();footer.Controls.Add(save);
            var cancel=new Button{Text="Cancel",AutoSize=true,DialogResult=DialogResult.Cancel};footer.Controls.Add(cancel);Controls.Add(footer);
            AcceptButton=save;CancelButton=cancel;
            Shown+=(s,e)=>{
                Rectangle area=Screen.FromControl(Owner??this).WorkingArea;
                MinimumSize=new Size(Math.Min(560,area.Width),Math.Min(520,area.Height));
                Size=new Size(Math.Min(Width,area.Width),Math.Min(Height,area.Height));
                Location=new Point(Math.Max(area.Left,Math.Min(Left,area.Right-Width)),Math.Max(area.Top,Math.Min(Top,area.Bottom-Height)));
            };
        }
        sealed class Choice{public string Id,Text;public override string ToString(){return Text;}}
        static FlowLayoutPanel Page(TabControl tabs,string title){
            var tab=new TabPage(title);tabs.TabPages.Add(tab);
            var flow=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(12)};
            flow.ClientSizeChanged+=(s,e)=>{
                int available=Math.Max(80,flow.ClientSize.Width-flow.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth-8);
                foreach(Control child in flow.Controls){
                    if(child.AutoSize){if(child.MaximumSize.Width!=available)child.MaximumSize=new Size(available,0);}
                    else{int desired=child.Tag is int?(int)child.Tag:child.Width;child.Tag=desired;int width=Math.Min(desired,available);if(child.Width!=width)child.Width=width;}
                }
            };
            tab.Controls.Add(flow);return flow;
        }
        static void Add(FlowLayoutPanel flow,string title,Control control){
            if(title.Length>0)flow.Controls.Add(new Label{Text=title,AutoSize=true,Margin=new Padding(3,12,3,3)});
            control.Margin=new Padding(3,3,3,6);flow.Controls.Add(control);
        }
        void Check(FlowLayoutPanel page,string label,string key,bool value){var c=new CheckBox{Text=label,Checked=value,AutoSize=true};inputs[key]=c;Add(page,"",c);}
        void Select(FlowLayoutPanel page,string label,string key,string[] values,string value){var c=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=330};c.Items.AddRange(values);c.SelectedItem=value;if(c.SelectedIndex<0)c.SelectedIndex=0;if(key=="theme"){c.FormattingEnabled=true;c.Format+=(s,e)=>{if(Convert.ToString(e.ListItem)=="fullblack")e.Value="Full Black";};}inputs[key]=c;Add(page,label,c);}
        void Number(FlowLayoutPanel page,string label,string key,int min,int max,int value){var c=new NumericUpDown{Minimum=min,Maximum=max,Value=Math.Max(min,Math.Min(max,value)),Width=130};inputs[key]=c;Add(page,label,c);}
        void Input(FlowLayoutPanel page,string label,string key,string value,bool secret){var c=new TextBox{Text=value,Width=480,UseSystemPasswordChar=secret};inputs[key]=c;Add(page,label,c);}
        string Str(string key){return inputs[key].Text;}
        bool Bool(string key){return ((CheckBox)inputs[key]).Checked;}
        int Num(string key){return (int)((NumericUpDown)inputs[key]).Value;}
        void Save(){
            string endpoint=Str("endpoint").Trim();Uri address;
            if(endpoint.Length>0&&(!Uri.TryCreate(endpoint,UriKind.Absolute,out address)||address.Scheme!="http"||!Services.Private(address.Host))){MessageBox.Show(this,"Enter the ONU’s private IPv4 HTTP address.");return;}
            Value.Theme=Convert.ToString(((ComboBox)inputs["theme"]).SelectedItem);Value.Poll=Int32.Parse(Str("poll"));Value.Shift=Int32.Parse(Str("shift"))*60000;
            Value.Startup=Bool("startup");Value.Fps=Bool("fps");Value.Cores=Bool("cores");Value.Fill=Bool("fill");Value.AutoScale=Bool("auto");
            Value.Display=((Choice)((ComboBox)inputs["display"]).SelectedItem).Id;Value.GameMonitor=Int32.Parse(((Choice)((ComboBox)inputs["game"]).SelectedItem).Id);
            Value.Position=Str("position");Value.Width=Num("width");Value.Height=Num("height");Value.Scale=Num("scale");Value.Unit=Str("unit");
            Value.Endpoint=endpoint;Value.Username=Str("username").Trim();Value.Password=Str("password");
            Value.Names.Clear();foreach(var field in names){string name=field.Value.Text.Trim();if(name.Length>0)Value.Names[field.Key]=name;}
            Value.Normalize();DialogResult=DialogResult.OK;Close();
        }
    }
}
