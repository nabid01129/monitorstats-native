using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MonitorStatsNative {
    internal sealed class Telemetry : IDisposable {
        readonly Dictionary<string, DateTime> due = new Dictionary<string, DateTime>();
        readonly Dictionary<string, Data> identities = new Dictionary<string, Data>();
        Data memory = new Data(), cpu = new Data(), gpu = new Data(), board = new Data();
        List<Data> volumes = new List<Data>(), peripherals = new List<Data>();
        readonly BatteryCollector battery = new BatteryCollector();
        ProcessSnapshot processes = new ProcessSnapshot();
        NetworkInterface adapter;
        long received, sent, sampled;
        MemoryMappedFile mahMap;
        MemoryMappedViewAccessor mah;
        readonly byte[] text = new byte[260];
        CpuTime[] previous;
        bool closed;
        [StructLayout(LayoutKind.Sequential)] struct CpuTime { public long Idle, Kernel, User, Dpc, Interrupt; public uint Count; }
        [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int info, [Out] CpuTime[] data, int size, out int length);
        [DllImport("iphlpapi.dll")] static extern int GetBestInterface(uint destination, out uint index);

        bool Due(string key, int seconds) {
            DateTime value;
            if (due.TryGetValue(key, out value) && DateTime.UtcNow < value) return false;
            due[key] = DateTime.UtcNow.AddSeconds(seconds); return true;
        }
        public void Refresh() { due.Clear(); battery.Refresh(); }
        public Telemetry() { }
        public Data Read() {
            if (closed) return new Data();
            if (Due("identity", 120)) Inventory();
            if (Due("storage", 30)) Storage();
            if (Due("process", 5)) processes = MonitorStatsMemory.ReadProcesses();
            battery.Request();
            var sensors = Sensors();
            var cpuNow = cpu.Copy(); var gpuNow = gpu.Copy();
            var loads = CpuLoads();
            cpuNow["loadPercent"] = loads.Length == 0 ? (double?)null : loads.Average();
            cpuNow["cores"] = loads;
            cpuNow["coreTempsC"] = new double[0];
            Fill(cpuNow, sensors, "loadPercent", @"^CPU( total)? usage$");
            Fill(cpuNow, sensors, "clockMHz", @"^CPU clock$");
            Fill(cpuNow, sensors, "tempC", @"^CPU( package)? temperature$");
            Fill(cpuNow, sensors, "powerW", @"^CPU power$");
            var cores = sensors.Where(s => Query.Match(s.Text("name"), @"^CPU\d+ usage$")).OrderBy(SensorIndex).Select(s => s.Number("value").Value).ToArray();
            if (cores.Length > 0) cpuNow["cores"] = cores;
            cpuNow["coreTempsC"] = sensors.Where(s => Query.Match(s.Text("name"), @"^CPU\d+ temperature$")).OrderBy(SensorIndex).Select(s => s.Number("value").Value).ToArray();
            var gp = sensors.Where(s => Query.Match(s.Text("name"), @"^GPU1? ")).ToList();
            Fill(gpuNow, gp, "coreLoadPercent", @"^GPU\d* usage$");
            Fill(gpuNow, gp, "coreMHz", @"^GPU\d* (core )?clock$");
            Fill(gpuNow, gp, "vramMHz", @"memory clock$");
            Fill(gpuNow, gp, "tempC", @"temperature$");
            Fill(gpuNow, gp, "fanPercent", @"fan speed$");
            Fill(gpuNow, gp, "fanRPM", @"fan tachometer$");
            Fill(gpuNow, gp, "watt", @"power$");
            var gpuMemory = gp.FirstOrDefault(s => Query.Match(s.Text("name"), @"memory usage$"));
            if (gpuMemory != null) gpuNow["vramUsedGB"] = gpuMemory.Number("value") / (gpuMemory.Text("units") == "GB" ? 1 : 1024);
            if (new[] {"coreLoadPercent","coreMHz","vramMHz","tempC","fanPercent","watt","vramUsedGB"}.Any(key=>gpuNow.Get(key)==null)) {
                foreach (var item in Nvml.Read()) if (gpuNow.Get(item.Key) == null) gpuNow[item.Key] = item.Value;
            }
            var fans = sensors.Where(s => Query.Match(s.Text("name"), "fan") && Query.Match(s.Text("units"), "RPM|%")).ToList();
            if (!fans.Any(s => Query.Match(s.Text("name"), "CPU")) || gpuNow.Get("fanRPM") == null) {
                if (Due("fallback", 3)) {
                    try { identities["fans"] = Data.Json(FanControlReader.Snapshot()); } catch { }
                }
                Data fanData;
                if (identities.TryGetValue("fans", out fanData)) foreach (var fan in fanData.Rows("fans")) {
                    bool isGpu=Query.Match(fan.Text("name"),"gpu|nvidia|geforce|radeon");
                    if(isGpu&&gpuNow.Get("fanRPM")!=null)continue;
                    if (!fans.Any(s => s.Text("name").Equals(fan.Text("name"), StringComparison.OrdinalIgnoreCase))) fans.Add(fan);
                    if (gpuNow.Get("fanRPM") == null && isGpu) gpuNow["fanRPM"] = fan.Number("value");
                }
            }
            var status = MonitorStatsMemory.Read();
            var ram = memory.Copy();
            ram["totalGB"] = status.TotalPhys / 1073741824.0;
            ram["freeGB"] = status.AvailPhys / 1073741824.0;
            ram["usedGB"] = (status.TotalPhys - status.AvailPhys) / 1073741824.0;
            ram["loadPercent"] = status.Load;
            ram["processCount"] = processes.Count;
            ram["topProcesses"] = processes.Top.Select(p => Data.Of("name", p.Name, "workingSetMB", p.WorkingSetBytes / 1048576.0)).ToArray();
            var counters = Query.Wmi("SELECT Name,DiskReadBytesPersec,DiskWriteBytesPersec FROM Win32_PerfFormattedData_PerfDisk_LogicalDisk");
            var drives = volumes.Select(v => {
                var copy = v.Copy();
                var counter = counters.FirstOrDefault(c => c.Text("Name") == v.Text("letter"));
                if (counter != null) { copy["readKBps"] = counter.Number("DiskReadBytesPersec") / 1024; copy["writeKBps"] = counter.Number("DiskWriteBytesPersec") / 1024; }
                try { var di = new DriveInfo(v.Text("letter")); copy["freeGB"] = di.TotalFreeSpace / 1073741824.0; copy["usedGB"] = (di.TotalSize - di.TotalFreeSpace) / 1073741824.0; copy["loadPercent"] = 100.0 * (di.TotalSize - di.TotalFreeSpace) / di.TotalSize; } catch { }
                return copy;
            }).ToArray();
            var result = Data.Of("cpu", cpuNow, "gpu", gpuNow, "ram", ram, "drives", drives, "fans", fans.ToArray(), "motherboard", board, "peripherals", peripherals.ToArray(), "network", Network(), "batteries", battery.Snapshot, "status", "live");
            var power = sensors.FirstOrDefault(s => Query.Match(s.Text("units"), @"^(W|watts?)$") && Query.Match(s.Text("name"), @"^(whole system power|total system power|system total power|total (pc|computer) power|(psu|power supply) (output )?power|ac input power|wall power)$"));
            if (power != null) { result["systemPowerW"] = power.Number("value"); result["systemPowerKind"] = Query.Match(power.Text("name"), "whole|system total|total system") ? "whole-system" : Query.Match(power.Text("name"), "ac input|wall") ? "pc-input" : "pc-output"; }
            return result;
        }
        static int SensorIndex(Data s) { var m = Regex.Match(s.Text("name"), @"\d+"); return m.Success ? Int32.Parse(m.Value) : 0; }
        static void Fill(Data target, IEnumerable<Data> sensors, string field, string pattern) {
            var value = sensors.FirstOrDefault(s => Query.Match(s.Text("name"), pattern));
            if (value != null) target[field] = value.Number("value");
        }
        double[] CpuLoads() {
            var data = new CpuTime[Environment.ProcessorCount];
            int length;
            int size = Marshal.SizeOf(typeof(CpuTime));
            if (NtQuerySystemInformation(8, data, data.Length * size, out length) != 0) {
                if(length<=data.Length*size||length>size*4096)return new double[0];
                data=new CpuTime[(length+size-1)/size];
                if(NtQuerySystemInformation(8,data,data.Length*size,out length)!=0)return new double[0];
            }
            var result = new double[data.Length];
            if (previous != null && previous.Length == data.Length) for (int i = 0; i < data.Length; i++) {
                double total = data[i].Kernel + data[i].User - previous[i].Kernel - previous[i].User;
                result[i] = total <= 0 ? 0 : Math.Max(0, Math.Min(100, 100 * (1 - (data[i].Idle - previous[i].Idle) / total)));
            }
            previous = data; return result;
        }
        void Inventory() {
            cpu = Query.Wmi("SELECT Name FROM Win32_Processor").Select(p => Data.Of("model", p.Text("Name"))).FirstOrDefault() ?? new Data();
            gpu = Query.Wmi("SELECT Name FROM Win32_VideoController").Where(p => !Query.Match(p.Text("Name"), "Basic Display|Remote Display|Virtual")).OrderBy(p => Query.Match(p.Text("Name"), "NVIDIA") ? 0 : 1).Select(p => Data.Of("model", p.Text("Name"))).FirstOrDefault() ?? new Data();
            gpu["vramTotalGB"] = VideoMemory.Read(gpu.Text("model"));
            board = Query.Wmi("SELECT Manufacturer,Product,Model FROM Win32_BaseBoard").Select(p => Data.Of("manufacturer", p.Text("Manufacturer"), "product", p.Text("Product"), "model", p.Text("Model"))).FirstOrDefault() ?? new Data();
            var modules = Query.Wmi("SELECT Capacity,SMBIOSMemoryType,ConfiguredClockSpeed,Speed,DeviceLocator,InterleavePosition,InterleaveDataDepth FROM Win32_PhysicalMemory");
            var gens = new Dictionary<int, string> { {20,"DDR"}, {21,"DDR2"}, {24,"DDR3"}, {26,"DDR4"}, {27,"LPDDR"}, {28,"LPDDR2"}, {29,"LPDDR3"}, {30,"LPDDR4"}, {34,"DDR5"}, {35,"LPDDR5"} };
            var generations = modules.Select(m => gens.ContainsKey((int)(m.Number("SMBIOSMemoryType") ?? 0)) ? gens[(int)m.Number("SMBIOSMemoryType")] : "").Where(s => s.Length > 0).Distinct().ToArray();
            var channels = modules.Select(m => Regex.Match(m.Text("DeviceLocator"), @"(?i)\bchannel[\s_-]*([a-z0-9]+)")).Where(m => m.Success).Select(m => m.Groups[1].Value.ToUpperInvariant()).Distinct().Count();
            if (channels == 0 && modules.Any(m => m.Number("InterleaveDataDepth") > 0)) channels = modules.Where(m => m.Number("InterleavePosition") > 0).Select(m => m.Number("InterleavePosition")).Distinct().Count();
            string channel = channels == 1 ? "Single channel" : channels == 2 ? "Dual channel" : channels == 4 ? "Quad channel" : channels > 0 ? channels + "-channel" : "Channel not reported";
            var speeds = modules.Select(m => m.Number("ConfiguredClockSpeed") > 0 ? m.Number("ConfiguredClockSpeed") : m.Number("Speed")).Where(n => n > 0).Distinct().ToArray();
            memory = Data.Of("generation", String.Join(" / ", generations), "channelMode", channel, "stickCount", modules.Count,
                "stickCapacitiesGB", modules.Select(m => (m.Number("Capacity") ?? 0) / 1073741824.0).ToArray(),
                "speedMTs", speeds.Length == 1 ? speeds[0] : null, "spec", String.Join(" / ", generations) + " · " + channel);
            peripherals = Query.Wmi("SELECT Name,PNPDeviceID,PNPClass,Manufacturer,ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode=0").Where(p => Query.Match(p.Text("PNPDeviceID"), @"^(USB|HID)\\") && !Query.Match(p.Text("Name"), "hub|host controller|composite|input device$|mass storage"))
                .GroupBy(p => p.Text("Name")).Take(32).Select(g => Data.Of("name", g.Key, "manufacturer", g.First().Text("Manufacturer"), "quantity", g.Count(), "category",
                    Query.Match(g.Key,"keyboard") ? "Keyboard" : Query.Match(g.Key,"mouse") ? "Mouse" : Query.Match(g.Key,"audio|headphone|microphone") ? "USB audio" : Query.Match(g.Key,"controller|gamepad") ? "Controller" : Query.Match(g.Key,"camera") ? "Camera" : "Other USB device")).ToList();
        }
        void Storage() {
            var disks = Query.Wmi("SELECT Index,Model,MediaType,InterfaceType FROM Win32_DiskDrive");
            var physical = Query.Wmi("SELECT DeviceId,MediaType,BusType,HealthStatus FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage");
            var diskByLetter = new Dictionary<string, Data>();
            foreach (var disk in disks) {
                int index = (int)(disk.Number("Index") ?? 0);
                var partitions = Query.Wmi("ASSOCIATORS OF {Win32_DiskDrive.DeviceID='\\\\.\\PHYSICALDRIVE" + index + "'} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
                foreach (var partition in partitions) foreach (var logical in Query.Wmi("ASSOCIATORS OF {Win32_DiskPartition.DeviceID='" + partition.Text("DeviceID").Replace("'", "\\'") + "'} WHERE AssocClass=Win32_LogicalDiskToPartition")) diskByLetter[logical.Text("DeviceID")] = disk;
            }
            var next = new List<Data>();
            foreach (var volume in Query.Wmi("SELECT DeviceID,Size,FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3")) {
                string letter = volume.Text("DeviceID");
                Data disk;
                diskByLetter.TryGetValue(letter, out disk);
                int index = disk == null ? -1 : (int)(disk.Number("Index") ?? -1);
                var ph = physical.FirstOrDefault(p => p.Text("DeviceId") == index.ToString());
                string type = ph != null && ph.Number("BusType") == 17 ? "NVMe" : ph != null && ph.Number("MediaType") == 4 ? "SSD" : ph != null && ph.Number("MediaType") == 3 ? "HDD" : "Drive";
                var item = Data.Of("id", letter, "letter", letter, "deviceId", index.ToString(), "name", disk == null ? letter : disk.Text("Model") + " (" + letter + ")", "type", type,
                    "totalGB", volume.Number("Size") / 1073741824, "health", ph == null ? null : ph.Number("HealthStatus") == 0 ? "Healthy" : "Attention");
                if (type == "NVMe" && index >= 0) {
                    double[] smart = MonitorStatsNvmeSmart.Read(index);
                    if (smart != null) { if (smart[0] >= 0) item["tempC"] = smart[0]; item["wearPercent"] = smart[1]; item["hostWritesTB"] = smart[2] * 512000 / 1e12; item["powerOnHours"] = smart[3]; item["health"] = smart[4] == 0 ? "Healthy (" + Math.Max(0,100-smart[1]) + "% life remaining)" : "SMART warning"; }
                }
                next.Add(item);
            }
            volumes = next;
        }
        List<Data> Sensors() {
            var result = new List<Data>();
            try {
                if (mah == null && Due("afterburner", 5)) {
                    mahMap = MemoryMappedFile.OpenExisting("MAHMSharedMemory", MemoryMappedFileRights.Read);
                    mah = mahMap.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                }
                if (mah != null) {
                    // MSI marks an unloaded publisher as DEAD. Holding that
                    // mapping prevents recovery when Afterburner restarts.
                    if(mah.Capacity<28||mah.ReadUInt32(0)!=0x4D41484D){DisconnectAfterburner();due["afterburner"]=DateTime.UtcNow.AddSeconds(5);return result;}
                    int header = (int)mah.ReadUInt32(8), count = Math.Min(512, (int)mah.ReadUInt32(12)), size = (int)mah.ReadUInt32(16);
                    if (header < 28 || size < 1324) throw new IOException();
                    for (int i = 0; i < count; i++) {
                        long offset = (long)header + (long)i * size;
                        if (offset + 1324 > mah.Capacity) break;
                        mah.ReadArray(offset, text, 0, 260); string name = Encoding.ASCII.GetString(text).Trim('\0', ' ');
                        mah.ReadArray(offset + 260, text, 0, 260); string unit = Encoding.ASCII.GetString(text).Trim('\0', ' ');
                        double value = mah.ReadSingle(offset + 1300); uint gpuIndex = mah.ReadUInt32(offset + 1316);
                        if (Double.IsNaN(value) || Double.IsInfinity(value) || Math.Abs(value) > 3e38 || name.Length == 0) continue;
                        if (gpuIndex < 16 && !Query.Match(name, @"^GPU\d+")) name = "GPU" + (gpuIndex + 1) + " " + name;
                        result.Add(Data.Of("name", name, "units", unit, "value", value));
                    }
                }
            } catch { DisconnectAfterburner();due["afterburner"]=DateTime.UtcNow.AddSeconds(5); }
            return result;
        }
        void DisconnectAfterburner(){var view=mah;var mapping=mahMap;mah=null;mahMap=null;if(view!=null)view.Dispose();if(mapping!=null)mapping.Dispose();}
        Data Network() {
            uint best; GetBestInterface(0x08080808, out best);
            var interfaces=NetworkInterface.GetAllNetworkInterfaces();
            var current = interfaces.Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback).OrderBy(n => {
                try { return n.GetIPProperties().GetIPv4Properties().Index == best ? 0 : n.GetIPProperties().GatewayAddresses.Count > 0 ? 1 : 2; } catch { return 3; }
            }).FirstOrDefault();
            if (current == null) { adapter = null; return new Data(); }
            var properties = current.GetIPProperties(); var counts = current.GetIPv4Statistics();
            var ip = properties.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
            double now = Stopwatch.GetTimestamp(), elapsed = sampled == 0 ? 0 : (now - sampled) / Stopwatch.Frequency;
            bool same = adapter != null && adapter.Id == current.Id;
            var gateway=properties.GatewayAddresses.Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork&&!a.Address.Equals(System.Net.IPAddress.Any)).Select(a=>a.Address.ToString()).FirstOrDefault();
            if(gateway==null||current.NetworkInterfaceType==NetworkInterfaceType.Tunnel||Query.Match(current.Description,"VPN|TAP|WARP|WireGuard|Tunnel")){
                var lanGateway=interfaces.Where(n=>n.OperationalStatus==OperationalStatus.Up&&(n.NetworkInterfaceType==NetworkInterfaceType.Ethernet||n.NetworkInterfaceType==NetworkInterfaceType.Wireless80211)&&!Query.Match(n.Description,"VPN|TAP|WARP|WireGuard|Tunnel"))
                    .SelectMany(n=>n.GetIPProperties().GatewayAddresses).Select(a=>a.Address.ToString()).FirstOrDefault(Services.Private);
                if(lanGateway!=null)gateway=lanGateway;
            }
            var result = Data.Of("name", current.Name, "adapter", current.Description, "gateway", gateway,
                "localIP", ip == null ? null : ip.Address.ToString(), "dns", properties.DnsAddresses.Select(a => a.ToString()).ToArray(), "linkSpeed", current.Speed >= 1000000000 ? (current.Speed / 1e9).ToString("0.#") + " Gbps" : (current.Speed / 1e6).ToString("0") + " Mbps",
                "inKBps", same && elapsed > 0 && counts.BytesReceived >= received ? (double?)((counts.BytesReceived - received) / elapsed / 1024) : null,
                "outKBps", same && elapsed > 0 && counts.BytesSent >= sent ? (double?)((counts.BytesSent - sent) / elapsed / 1024) : null);
            if(!same||Due("networkProfile",30)){
                int index=0;try{index=properties.GetIPv4Properties().Index;}catch{}
                var profile=Query.Wmi("SELECT Name,NetworkCategory,InterfaceIndex FROM MSFT_NetConnectionProfile",@"root\StandardCimv2").FirstOrDefault(p=>p.Number("InterfaceIndex")==index);
                identities["networkProfile"]=profile??new Data();
            }
            Data savedProfile;
            if(identities.TryGetValue("networkProfile",out savedProfile)&&savedProfile.Count>0){
                result["name"]=savedProfile.Text("Name");
                result["profile"]=savedProfile.Number("NetworkCategory")==1?"Private":savedProfile.Number("NetworkCategory")==2?"Domain":"Public";
            }
            adapter = current; received = counts.BytesReceived; sent = counts.BytesSent; sampled = (long)now;
            return result;
        }
        public void Dispose() {
            closed = true;
            DisconnectAfterburner();
            battery.Dispose();
            ForegroundMonitor.CloseRtss(); Nvml.Close();
        }
    }
}
