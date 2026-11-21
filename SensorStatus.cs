using System;
using System.Collections.Generic;
using System.Linq;

namespace MonitorStatsNative {
    // Formatting runs only when Settings opens this tab or requests a refresh.
    // It reads snapshots and never calls a sensor, network or device provider.
    internal static class SensorStatus {
        static void Metric(List<string> lines,string label,Data data,string field,string fallback=""){
            string source=data.Text(field+"Source");if(source.Length==0)source=fallback;
            lines.Add(label+": "+(data.Number(field).HasValue?"Available"+(source.Length>0?" — "+source:""):"Unavailable — no current reading"));
        }
        public static string Describe(Data stats,Data power,Data extras,Data updates,int interval,bool saving,bool opticalConfigured,bool fpsEnabled,bool fpsAvailable){
            var lines=new List<string>();
            lines.Add("Hardware interval: "+interval+" ms"+(saving?" — reduced background polling":""));
            if(stats.Get("sampledAtUtc") is DateTime){var stamp=(DateTime)stats.Get("sampledAtUtc");lines.Add("Last hardware sample: "+stamp.ToLocalTime().ToString("h:mm:ss tt")+" ("+Math.Max(0,(int)(DateTime.UtcNow-stamp).TotalSeconds)+" seconds ago)");}
            else lines.Add("Last hardware sample: waiting for first reading");
            lines.Add("Status reflects the latest snapshot; values may change after collection.");
            lines.Add("");
            int count=(int)(stats.Number("afterburnerSensorCount")??0);
            lines.Add("MSI Afterburner: "+(count>0?count+" valid sensor readings":"No valid shared-memory readings — check Afterburner monitoring settings"));
            lines.Add("RTSS: "+(!fpsEnabled?"FPS display disabled":fpsAvailable?"Frames available for the selected foreground game":"No current frames for the selected monitor; RTSS must be running and detecting the game"));
            lines.Add("");
            var cpu=stats.Section("cpu");var gpu=stats.Section("gpu");var ram=stats.Section("ram");
            Metric(lines,"CPU usage",cpu,"loadPercent");Metric(lines,"CPU clock",cpu,"clockMHz");Metric(lines,"CPU temperature",cpu,"tempC");Metric(lines,"CPU power",cpu,"powerW");
            var temperatures=cpu.Get("coreTempsC") as double[];
            lines.Add("CPU core temperatures: "+(temperatures!=null&&temperatures.Length>0?temperatures.Length+" readings — MSI Afterburner":"Unavailable — no current readings"));
            lines.Add("");
            Metric(lines,"GPU usage",gpu,"coreLoadPercent");Metric(lines,"GPU temperature",gpu,"tempC");Metric(lines,"GPU core clock",gpu,"coreMHz");Metric(lines,"GPU memory clock",gpu,"vramMHz");Metric(lines,"GPU power",gpu,"watt");Metric(lines,"GPU fan speed",gpu,"fanRPM");Metric(lines,"GPU fan percentage",gpu,"fanPercent");Metric(lines,"VRAM usage",gpu,"vramUsedGB");Metric(lines,"VRAM capacity",gpu,"vramTotalGB","Windows / graphics driver");
            lines.Add("");Metric(lines,"RAM usage",ram,"usedGB","Windows");Metric(lines,"Running processes",ram,"processCount","Windows");
            lines.Add("Motherboard: "+(stats.Section("motherboard").Text("product").Length>0?"Detected — Windows BIOS information":"Not reported by Windows BIOS information"));
            foreach(var drive in stats.Rows("drives"))lines.Add("Drive "+drive.Text("letter").TrimEnd(':')+": Windows volume information"+(drive.Number("wearPercent").HasValue?"; NVMe SMART health available":"; extended SMART health unavailable"));
            foreach(var fan in stats.Rows("fans"))lines.Add("Fan "+fan.Text("name")+": "+(fan.Text("source").Length>0?fan.Text("source"):"Source not recorded"));
            lines.Add("");
            lines.Add("Network traffic: "+(stats.Section("network").Number("inKBps").HasValue?"Available — Windows adapter counters":"Waiting for adapter counters"));
            lines.Add("Public IPv4: "+(extras.Text("publicIPv4").Length>0?"Available — public IP lookup":"Unavailable / lookup pending"));
            lines.Add("Public IPv6: "+(extras.Text("publicIPv6").Length>0?"Available — public IP lookup":"Unavailable / lookup pending; IPv6 may not be supported"));
            lines.Add("Router: "+(extras.Text("routerModel").Length>0?"Detected — gateway UPnP":"Not detected; gateway UPnP may be unavailable"));
            lines.Add("Active DNS: "+(extras.Section("dnsResolver").Text("name").Length>0?"Detected — resolver diagnostic lookup":"Unavailable / lookup pending"));
            lines.Add("ONU optical power: "+(!opticalConfigured?"Not configured":extras.Number("rxOpticalPowerDbm").HasValue?extras.Text("opticalStale")=="True"?"Last successful reading; latest ONU request failed":"Available — configured ONU":"Unavailable / request pending; check ONU access and credentials"));
            lines.Add("Windows Update: "+(updates.Text("status")=="cached"?"Cached Windows Update Agent results available":updates.Text("status")=="unavailable"?"Cached query unavailable":"Cached query pending"));
            lines.Add("");
            var batteries=stats.Rows("batteries").ToArray();
            if(batteries.Length==0)lines.Add("Wireless batteries: no connected device reported a supported reading");
            foreach(var device in batteries)lines.Add("Battery · "+device.Text("name")+": "+device.Text("detail")+(device.Number("batteryPercent").HasValue?" (percentage reported)":" (coarse level / connection only)"));
            lines.Add("System power: "+(power.Count>0?power.Text("mode"):"Waiting for readings")+". Estimated components are listed in Power estimate.");
            return String.Join(Environment.NewLine,lines);
        }
    }
}
