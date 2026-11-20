using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MonitorStatsNative {
    internal static class VideoMemory {
        [DllImport("dxgi.dll")] static extern int CreateDXGIFactory(ref Guid iid,out IntPtr factory);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Enumerate(IntPtr factory,uint index,out IntPtr adapter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Describe(IntPtr adapter,out Description description);
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        struct Description {
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Name;
            public uint Vendor,Device,Subsystem,Revision;
            public UIntPtr DedicatedVideo,DedicatedSystem,SharedSystem;
            public uint LuidLow;public int LuidHigh;
        }
        public static double? Read(string model) {
            // DXGI's SIZE_T values can be capped by a 32-bit process's address
            // space. Prefer the driver's 64-bit installed-memory record.
            try {
                using(var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))
                using(var video=machine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video")) {
                    if(video!=null)foreach(string id in video.GetSubKeyNames())
                    using(var adapter=video.OpenSubKey(id+@"\0000")) {
                        if(adapter==null||!String.Equals(Convert.ToString(adapter.GetValue("DriverDesc")),model,StringComparison.OrdinalIgnoreCase))continue;
                        object memory=adapter.GetValue("HardwareInformation.qwMemorySize");
                        if(memory==null)continue;
                        byte[] bytes=memory as byte[];
                        ulong count=bytes!=null&&bytes.Length>=8?BitConverter.ToUInt64(bytes,0):Convert.ToUInt64(memory);
                        if(count>0&&count<1099511627776UL)return count/1073741824.0;
                    }
                }
            }catch{}
            IntPtr factory=IntPtr.Zero;
            try {
                Guid iid=new Guid("7b7166ec-21c7-44ae-b21a-c9ae321ae369");
                if(CreateDXGIFactory(ref iid,out factory)!=0)return null;
                var enumerate=(Enumerate)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory),7*IntPtr.Size),typeof(Enumerate));
                for(uint i=0;i<16;i++){
                    IntPtr adapter;
                    if(enumerate(factory,i,out adapter)!=0)break;
                    try{
                        var describe=(Describe)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(adapter),8*IntPtr.Size),typeof(Describe));
                        Description desc;
                        if(describe(adapter,out desc)==0&&String.Equals(desc.Name.Trim(),model.Trim(),StringComparison.OrdinalIgnoreCase)){
                            ulong bytes=desc.DedicatedVideo.ToUInt64();
                            return IntPtr.Size==4&&bytes>=2147483648UL?null:(double?)(bytes/1073741824.0);
                        }
                    }finally{Marshal.Release(adapter);}
                }
            }catch{}finally{if(factory!=IntPtr.Zero)Marshal.Release(factory);}
            return null;
        }
    }
    internal static class BluetoothState {
        [StructLayout(LayoutKind.Sequential)]struct DeviceInterface {public int Size;public Guid Class;public int Flags;public IntPtr Reserved;}
        [DllImport("setupapi.dll",EntryPoint="SetupDiGetClassDevsW",CharSet=CharSet.Unicode)]static extern IntPtr Devices(ref Guid guid,string enumerator,IntPtr parent,uint flags);
        [DllImport("setupapi.dll")]static extern bool SetupDiEnumDeviceInterfaces(IntPtr set,IntPtr device,ref Guid guid,uint index,ref DeviceInterface data);
        [DllImport("setupapi.dll")]static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        static bool HasBatteryInterface(){
            // Windows battery miniclass drivers register this interface.
            Guid guid=new Guid("72631e54-78a4-11d0-bcf7-00aa00b7b32a");
            IntPtr set=Devices(ref guid,null,IntPtr.Zero,0x12);
            if(set==new IntPtr(-1))return false;
            try{var data=new DeviceInterface();data.Size=Marshal.SizeOf(typeof(DeviceInterface));return SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref guid,0,ref data);}
            finally{SetupDiDestroyDeviceInfoList(set);}
        }
        static object Wait(object operation,Type resultType) {
            var extensions=Assembly.LoadFrom(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"System.Runtime.WindowsRuntime.dll")).GetType("System.WindowsRuntimeSystemExtensions");
            var method=extensions.GetMethods().First(m=>m.Name=="AsTask"&&m.IsGenericMethod&&m.GetParameters().Length==1);
            var task=(Task)method.MakeGenericMethod(resultType).Invoke(null,new[]{operation});
            if(!task.Wait(2000))throw new TimeoutException("Windows battery query timed out");
            return task.GetType().GetProperty("Result").GetValue(task,null);
        }
        public static MonitorStatsPeripheralBatteryReading[] Controllers() {
            var readings=new List<MonitorStatsPeripheralBatteryReading>();
            if(!HasBatteryInterface())return readings.ToArray();
            try{
                var battery=Type.GetType("Windows.Devices.Power.Battery, Windows, ContentType=WindowsRuntime");
                var info=Type.GetType("Windows.Devices.Enumeration.DeviceInformation, Windows, ContentType=WindowsRuntime");
                if(battery==null||info==null)return readings.ToArray();
                var selector=battery.GetMethods().First(m=>m.Name=="GetDeviceSelector"&&m.GetParameters().Length==0).Invoke(null,null);
                var find=info.GetMethods().First(m=>m.Name=="FindAllAsync"&&m.GetParameters().Length==2&&m.GetParameters()[0].ParameterType==typeof(string));
                var fromId=battery.GetMethods().First(m=>m.Name=="FromIdAsync"&&m.GetParameters().Length==1);
                var devices=(IEnumerable)Wait(find.Invoke(null,new object[]{selector,new List<string>{"System.Devices.ContainerId"}}),find.ReturnType.GetGenericArguments()[0]);
                foreach(object device in devices)try{
                    string id=(string)info.GetProperty("Id").GetValue(device,null);
                    string name=(string)info.GetProperty("Name").GetValue(device,null);
                    if(Query.Match(id,@"ACPI\\PNP0C0A")||Query.Match(name,"ACPI.?Compliant.*Battery|internal battery"))continue;
                    object instance=Wait(fromId.Invoke(null,new object[]{id}),fromId.ReturnType.GetGenericArguments()[0]);
                    if(instance==null)continue;
                    object report=battery.GetMethod("GetReport").Invoke(instance,null);
                    object remaining=report.GetType().GetProperty("RemainingCapacityInMilliwattHours").GetValue(report,null),full=report.GetType().GetProperty("FullChargeCapacityInMilliwattHours").GetValue(report,null);
                    if(remaining==null||full==null||Convert.ToDouble(full)<=0)continue;
                    int percent=(int)Math.Round(100*Convert.ToDouble(remaining)/Convert.ToDouble(full));if(percent<0||percent>100)continue;
                    var properties=info.GetProperty("Properties").GetValue(device,null) as IDictionary<string,object>;
                    object container=null;if(properties!=null)properties.TryGetValue("System.Devices.ContainerId",out container);
                    string category=Query.Match(name,"tws|earbud|earphone|haylou|airpods")?"Earbuds":Query.Match(name,"headset|headphone|speaker|audio")?"Audio":Query.Match(name,"mouse")?"Mouse":Query.Match(name,"keyboard")?"Keyboard":Query.Match(name,"gamepad|controller|xbox|joystick")?"Controller":"Wireless device";
                    readings.Add(new MonitorStatsPeripheralBatteryReading{Name=name,ContainerId=container==null?null:Convert.ToString(container).Trim('{','}'),IsBluetoothDevice=Query.Match(id,"BTHENUM|BTHLEDEVICE|BTHLE"),Category=category,BatteryPercent=percent,Detail="Windows battery controller report"});
                }catch{}
            }catch{}
            return readings.ToArray();
        }
        public static HashSet<string> Read() {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try {
                var info = Type.GetType("Windows.Devices.Enumeration.DeviceInformation, Windows, ContentType=WindowsRuntime");
                var kind = Type.GetType("Windows.Devices.Enumeration.DeviceInformationKind, Windows, ContentType=WindowsRuntime");
                if (info == null || kind == null) return result;
                var find = info.GetMethods().First(m => m.Name == "FindAllAsync" && m.GetParameters().Length == 3 && m.GetParameters()[2].ParameterType == kind);
                var extensions = Assembly.LoadFrom(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "System.Runtime.WindowsRuntime.dll")).GetType("System.WindowsRuntimeSystemExtensions");
                var asTask = extensions.GetMethods().First(m => m.Name == "AsTask" && m.IsGenericMethod && m.GetParameters().Length == 1);
                foreach (string name in new[] { "BluetoothDevice", "BluetoothLEDevice" }) {
                    var bt = Type.GetType("Windows.Devices.Bluetooth." + name + ", Windows, ContentType=WindowsRuntime");
                    if (bt == null) continue;
                    string selector = (string)bt.GetMethods().First(m => m.Name == "GetDeviceSelector" && m.GetParameters().Length == 0).Invoke(null, null);
                    var props = new List<string> { "System.Devices.Aep.IsConnected", "System.Devices.Aep.ContainerId" };
                    object op = find.Invoke(null, new object[] { selector, props, Enum.Parse(kind, "AssociationEndpoint") });
                    var task = (Task)asTask.MakeGenericMethod(find.ReturnType.GetGenericArguments()[0]).Invoke(null, new[] { op });
                    if (!task.Wait(2000)) continue;
                    var rows = (IEnumerable)task.GetType().GetProperty("Result").GetValue(task, null);
                    foreach (object row in rows) {
                        var values = row.GetType().GetProperty("Properties").GetValue(row, null) as IDictionary<string, object>;
                        if (values == null) continue;
                        object online, id;
                        if (values.TryGetValue("System.Devices.Aep.IsConnected", out online) && Convert.ToBoolean(online) && values.TryGetValue("System.Devices.Aep.ContainerId", out id)) result.Add(Convert.ToString(id).Trim('{','}'));
                    }
                }
            } catch { }
            return result;
        }
    }
    internal static class Nvml {
        static IntPtr library, device;
        static bool attempted;
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32.dll")] static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Init();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetHandle(uint index, out IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetValue(IntPtr handle, out uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetValueAt(IntPtr handle, uint kind, out uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetUsage(IntPtr handle, out Usage value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetMemory(IntPtr handle, out Memory value);
        [StructLayout(LayoutKind.Sequential)] struct Usage { public uint Gpu, Mem; }
        [StructLayout(LayoutKind.Sequential)] struct Memory { public ulong Total, Free, Used; }
        static T Function<T>(string name) where T : class {
            IntPtr ptr = GetProcAddress(library, name);
            return ptr == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer(ptr, typeof(T)) as T;
        }
        public static Data Read() {
            var data = new Data();
            try {
                if (!attempted) {
                    attempted = true;
                    library = LoadLibrary(Path.Combine(Environment.SystemDirectory, "nvml.dll"));
                    if (library == IntPtr.Zero) library = LoadLibrary(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\NVSMI\nvml.dll"));
                    if (library == IntPtr.Zero) return data;
                    var init = Function<Init>("nvmlInit_v2"); var handle = Function<GetHandle>("nvmlDeviceGetHandleByIndex_v2");
                    if (init == null || handle == null || init() != 0 || handle(0, out device) != 0) { Close(); return data; }
                }
                if (device == IntPtr.Zero) return data;
                uint value; Usage usage; Memory mem;
                var getUse = Function<GetUsage>("nvmlDeviceGetUtilizationRates");
                if (getUse != null && getUse(device, out usage) == 0) data["coreLoadPercent"] = usage.Gpu;
                var getMem = Function<GetMemory>("nvmlDeviceGetMemoryInfo");
                if (getMem != null && getMem(device, out mem) == 0) { data["vramUsedGB"] = mem.Used / 1073741824.0; data["vramTotalGB"] = mem.Total / 1073741824.0; }
                var temp = Function<GetValueAt>("nvmlDeviceGetTemperature");
                if (temp != null && temp(device, 0, out value) == 0) data["tempC"] = value;
                var clock = Function<GetValueAt>("nvmlDeviceGetClockInfo");
                if (clock != null) { if (clock(device, 0, out value) == 0) data["coreMHz"] = value; if (clock(device, 2, out value) == 0) data["vramMHz"] = value; }
                var watts = Function<GetValue>("nvmlDeviceGetPowerUsage");
                if (watts != null && watts(device, out value) == 0) data["watt"] = value / 1000.0;
                var fan = Function<GetValue>("nvmlDeviceGetFanSpeed");
                if (fan != null && fan(device, out value) == 0) data["fanPercent"] = value;
            } catch { }
            return data;
        }
        public static void Close() {
            device = IntPtr.Zero;
            if (library != IntPtr.Zero) { var stop = Function<Init>("nvmlShutdown"); if (stop != null) stop(); FreeLibrary(library); library = IntPtr.Zero; }
        }
    }
}
