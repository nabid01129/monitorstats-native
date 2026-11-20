using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MonitorStatsNative {
    internal static class Power {
        static double Clamp(double n,double low,double high) { return Math.Max(low,Math.Min(high,n)); }
        static Data Part(string name,double watts,double low,double high,string detail,bool live=false) { return Data.Of("name",name,"watts",watts,"low",Math.Max(0,low),"high",high,"detail",detail,"source",live ? "Live sensor" : "Estimate"); }
        static Data Estimate(string name,double watts,string detail,double low=0.7,double high=1.35) { return Part(name,watts,watts*low,watts*high,detail); }
        static Data Sensor(string name,double watts,string detail) { return Part(name,watts,watts*0.96,watts*1.04,detail,true); }
        static double CpuRating(string name) {
            var m=Regex.Match(name,@"(?i)i([3579])[- ](\d{4,5})");
            if(m.Success) { int tier=Int32.Parse(m.Groups[1].Value),gen=Int32.Parse(m.Groups[2].Value.Substring(0,m.Groups[2].Value.Length-3)); return tier==3?55:tier==5?65:tier==7?(gen>=12?125:95):125; }
            return Query.Match(name,"ryzen.*9")?170:Query.Match(name,"ryzen.*7")?105:Query.Match(name,@"ryzen.*5.*x\b")?95:65;
        }
        static double GpuRating(string name) {
            if(Query.Match(name,"uhd|iris|integrated|apu|radeon graphics"))return 12;
            var m=Regex.Match(name,@"(?i)rtx\s*(\d{4})\s*(ti)?");
            if(m.Success) {
                bool ti=m.Groups[2].Success; int tier=Int32.Parse(m.Groups[1].Value);
                var ratings=new Dictionary<int,double>{{3050,130},{3060,ti?200:170},{3070,ti?290:220},{3080,ti?350:320},{3090,ti?450:350},{4060,ti?160:115},{4070,ti?285:200},{4080,320},{4090,450},{5060,ti?180:145},{5070,ti?300:250},{5080,360},{5090,575}};
                if(ratings.ContainsKey(tier))return ratings[tier];
            }
            var rx=Regex.Match(name,@"(?i)rx\s*(\d{4})");
            if(rx.Success){int t=Int32.Parse(rx.Groups[1].Value);return t>=7900?300:t>=7800?260:t>=7700?245:t>=7600?165:t>=6800?250:t>=6700?220:85;}
            return Query.Match(name,"gtx.*16")?125:Query.Match(name,"gtx.*10")?150:85;
        }
        public static Data Read(Data stats,Screen[] screens) {
            if(stats.Count==0)return new Data();
            var parts=new List<Data>();var cpu=stats.Section("cpu");var gpu=stats.Section("gpu");var ram=stats.Section("ram");var net=stats.Section("network");
            double cp=cpu.Number("powerW")??Math.Max(4,CpuRating(cpu.Text("model"))*(0.12+0.88*Clamp(cpu.Number("loadPercent")??18,0,100)/100));
            double gp=gpu.Number("watt")??Math.Max(4,GpuRating(gpu.Text("model"))*(0.06+0.94*Clamp(gpu.Number("coreLoadPercent")??12,0,100)/100));
            parts.Add(cpu.Get("powerW")!=null?Sensor("CPU package",cp,cpu.Text("model")):Estimate("CPU package",cp,cpu.Text("model"),0.62,1.38));
            parts.Add(gpu.Get("watt")!=null?Sensor("Graphics card",gp,gpu.Text("model")):Estimate("Graphics card",gp,gpu.Text("model"),0.65,1.35));
            double capacity=ram.Number("totalGB")??0,sticks=ram.Number("stickCount")??Math.Ceiling(capacity/16);bool ddr5=ram.Text("generation").Contains("DDR5");
            double memory=Math.Max(0.5,sticks*(ddr5?1.9:1.45)+capacity*(ddr5?0.065:0.05)+Clamp(((ram.Number("speedMTs")??2666)-2666)/1000,-0.25,0.75)*0.25*sticks);
            parts.Add(Estimate("Memory modules",memory,ram.Text("spec"),0.65,1.45));
            foreach(var drive in stats.Rows("drives").GroupBy(d=>d.Text("deviceId")).Select(g=>g.First())) {
                bool active=(drive.Number("readKBps")??0)+(drive.Number("writeKBps")??0)>128;string type=drive.Text("type");
                parts.Add(Estimate("Storage · "+drive.Text("name"),type=="HDD"?(active?6:4.5):type=="NVMe"?(active?3.5:0.65):type=="SSD"?(active?1.5:0.25):(active?3:1.2),type+" · "+(active?"active sample":"idle sample"),0.5,active?1.7:2.8));
            }
            var board=stats.Section("motherboard");string identity=board.Text("manufacturer")+" "+board.Text("product");
            parts.Add(Estimate("Motherboard",Query.Match(identity,@"\b(z\d{3}|x\d{3}|trx\d+|wrx\d+)\b")?34:Query.Match(identity,@"\b(b\d{3}|h\d{3}|a\d{3})\b")?25:27,identity+" · board and chipset",0.7,1.4));
            int fans=Math.Max(3,stats.Rows("fans").Count(f=>!Query.Match(f.Text("name"),"gpu|graphics")));
            parts.Add(Estimate("Cooling fans",fans*1.25,fans+" modeled fans, tachometer sensors do not expose watts",0.45,2));
            parts.Add(Estimate("Network adapter",1.1+Clamp(((net.Number("inKBps")??0)+(net.Number("outKBps")??0))/1024*0.25,0,1.8),net.Text("adapter"),0.5,2));
            foreach(var p in stats.Rows("peripherals")){
                string category=p.Text("category");double w=category=="Keyboard"?(Query.Match(p.Text("name"),"gaming|mechanical|rgb|corsair|razer|steelseries")?1.5:0.4):category=="Mouse"?0.25:category=="Camera"?2.2:category=="USB audio"?1.4:category=="Controller"?1.1:0.8;
                parts.Add(Estimate("USB · "+p.Text("name"),w*(p.Number("quantity")??1),category+" · modeled USB bus draw",0.4,2.2));
            }
            var displays=new List<Data>();
            foreach(var screen in screens) {
                double refresh=NativeDisplay.RefreshRate(screen.DeviceName);
                double watts=Clamp(18*Math.Sqrt(Math.Max(384000,screen.Bounds.Width*(double)screen.Bounds.Height)/2073600)*(1+(Clamp(refresh,60,240)-60)/1200),10,65);
                string model=NativeDisplay.Model(screen);
                displays.Add(Estimate("Display · "+(model.Length>0?model:screen.DeviceName),watts,screen.Bounds.Width+"×"+screen.Bounds.Height+" · "+refresh+" Hz · estimated backlight power",0.55,1.65));
            }
            double dc=parts.Sum(p=>p.Number("watts")??0),low=parts.Sum(p=>p.Number("low")??0),high=parts.Sum(p=>p.Number("high")??0);
            bool measured=stats.Text("systemPowerKind")=="whole-system"&&stats.Number("systemPowerW").HasValue;
            if(stats.Number("systemPowerW").HasValue){
                dc=stats.Number("systemPowerW").Value;parts.Clear();parts.Add(Sensor("PC power sensor",dc,stats.Text("systemPowerKind")));low=dc*0.96;high=dc*1.04;
            }
            if(!measured&&stats.Text("systemPowerKind")!="pc-input")parts.Add(Part("PSU conversion loss",dc*(1/0.88-1),low*(1/0.93-1),high*(1/0.78-1),"Estimated 88% efficiency, range 78–93%"));
            if(!measured)parts.AddRange(displays);
            return Data.Of("watts",parts.Sum(p=>p.Number("watts")??0),"low",parts.Sum(p=>p.Number("low")??0),"high",parts.Sum(p=>p.Number("high")??0),"mode",measured?"Measured":cpu.Get("powerW")!=null||gpu.Get("watt")!=null?"Mixed estimate":"Estimated","parts",parts.ToArray());
        }
    }
}
