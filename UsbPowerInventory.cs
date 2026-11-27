using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace MonitorStatsNative {
    // A composite receiver can expose many HID interfaces. Model its USB bus
    // power once, using the physical USB ancestor instead of interface count.
    internal static class UsbPowerInventory {
        [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode,EntryPoint="CM_Locate_DevNodeW")]
        static extern uint Locate(out uint node,string id,uint flags);
        [DllImport("cfgmgr32.dll",EntryPoint="CM_Get_Parent")]
        static extern uint Parent(out uint parent,uint node,uint flags);
        [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode,EntryPoint="CM_Get_Device_IDW")]
        static extern uint DeviceId(uint node,StringBuilder id,uint length,uint flags);
        static bool Physical(string id){return Query.Match(id,@"^USB\\VID_[0-9A-F]{4}&PID_[0-9A-F]{4}[^\\]*\\")&&id.IndexOf("&MI_",StringComparison.OrdinalIgnoreCase)<0;}
        internal static List<Data> Read(IEnumerable<Data> rows){
            var cache=new Dictionary<uint,string>();
            Func<string,string> resolve=id=>{
                if(Physical(id))return id;
                uint node;if(Locate(out node,id,0)!=0)return null;
                var visited=new List<uint>();string result=null;
                for(int i=0;i<12;i++){
                    if(cache.TryGetValue(node,out result))break;
                    visited.Add(node);var name=new StringBuilder(512);
                    if(DeviceId(node,name,(uint)name.Capacity,0)!=0)break;
                    if(Physical(name.ToString())){result=name.ToString();break;}
                    uint parent;if(Parent(out parent,node,0)!=0||parent==node)break;node=parent;
                }
                foreach(uint item in visited)cache[item]=result;
                return result;
            };
            return Build(rows,resolve);
        }
        static string Category(Data row){
            string name=row.Text("Name"),kind=row.Text("PNPClass");
            return Query.Match(name,"gamepad|game controller|joystick|xbox")?"Controller":
                kind.Equals("Mouse",StringComparison.OrdinalIgnoreCase)||Query.Match(name,"mouse")?"Mouse":
                kind.Equals("Keyboard",StringComparison.OrdinalIgnoreCase)||Query.Match(name,"keyboard")?"Keyboard":
                Query.Match(name,"camera|webcam")?"Camera":Query.Match(name,"audio|headphone|microphone")?"USB audio":"Other USB device";
        }
        static int Priority(Data row){string category=Category(row);return category=="Controller"?0:category=="Mouse"?1:category=="Keyboard"?2:category=="Camera"?3:category=="USB audio"?4:5;}
        internal static List<Data> Build(IEnumerable<Data> rows,Func<string,string> resolve){
            return rows.Where(p=>Query.Match(p.Text("PNPDeviceID"),@"^(USB|HID)\\")&&!Query.Match(p.Text("Name"),"hub|host controller|composite|input device$|mass storage"))
                .Select(p=>new{Row=p,Root=resolve(p.Text("PNPDeviceID"))}).Where(p=>!String.IsNullOrEmpty(p.Root))
                .GroupBy(p=>p.Root,StringComparer.OrdinalIgnoreCase).Select(g=>g.OrderBy(p=>Priority(p.Row)).First().Row)
                .GroupBy(p=>Category(p)+"|"+p.Text("Name"),StringComparer.OrdinalIgnoreCase).Take(32)
                .Select(g=>Data.Of("name",g.First().Text("Name"),"quantity",g.Count(),"category",Category(g.First()))).ToList();
        }
    }
}
