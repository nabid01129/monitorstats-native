using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MonitorStatsNative {
    // One in-flight provider read, with immutable snapshots for the fast poller.
    internal sealed class AsyncSnapshot<T> : IDisposable where T : class {
        readonly object gate=new object();
        readonly Func<T> read;
        readonly Action release;
        T value;
        bool busy,disposed,released;
        public AsyncSnapshot(T initial,Func<T> read,Action release){value=initial;this.read=read;this.release=release;}
        public T Value {get{lock(gate)return value;}}
        public void Request(){
            lock(gate){if(disposed||busy)return;busy=true;}
            ThreadPool.QueueUserWorkItem(_=>{
                try{T fresh=read();lock(gate){if(!disposed&&fresh!=null)value=fresh;}}
                catch(Exception error){Program.Log("Background provider: "+error);}
                finally{Action cleanup=null;lock(gate){busy=false;if(disposed&&!released){released=true;cleanup=release;}}Release(cleanup);}
            });
        }
        static void Release(Action cleanup){if(cleanup!=null)try{cleanup();}catch(Exception error){Program.Log("Provider cleanup: "+error);}}
        public void Dispose(){Action cleanup=null;lock(gate){disposed=true;if(!busy&&!released){released=true;cleanup=release;}}Release(cleanup);}
    }
    internal sealed class BatteryCollector : IDisposable {
        readonly Dictionary<string,DateTime> due=new Dictionary<string,DateTime>();
        readonly AsyncSnapshot<Data[]> worker;
        List<Data> batteries=new List<Data>();
        MonitorStatsPeripheralBatteryReading[] windowsBatteries=new MonitorStatsPeripheralBatteryReading[0];
        int refresh;
        public BatteryCollector(){MonitorStatsPeripheralBattery.RegisterForDeviceNotifications();worker=new AsyncSnapshot<Data[]>(new Data[0],Read,MonitorStatsPeripheralBattery.UnregisterDeviceNotifications);}
        public Data[] Snapshot {get{return worker.Value;}}
        public void Request(){worker.Request();}
        public void Refresh(){Interlocked.Exchange(ref refresh,1);}
        bool Due(string key,int seconds){DateTime deadline;if(due.TryGetValue(key,out deadline)&&DateTime.UtcNow<deadline)return false;due[key]=DateTime.UtcNow.AddSeconds(seconds);return true;}
        Data[] Read(){
            if(Interlocked.Exchange(ref refresh,0)!=0)due.Clear();
            bool changed = MonitorStatsPeripheralBattery.ConsumeDeviceChange();

            bool presence = Due("presence", 5) || changed, batteryRefresh = Due("battery", 300);
            bool discover=batteryRefresh||changed||Due("batteryInventory",30);
            var fresh = MonitorStatsPeripheralBattery.Read(discover);
            if(discover)windowsBatteries=fresh.Where(b=>!DynamicBattery(b)).Concat(BluetoothState.Controllers()).ToArray();
            fresh=fresh.Where(DynamicBattery).Concat(windowsBatteries).GroupBy(b=>String.IsNullOrEmpty(b.ContainerId)?b.Name:b.ContainerId,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToArray();
            if (presence || batteryRefresh) {
                var connected = BluetoothState.Read();
                var updated = new List<Data>();
                foreach (var item in fresh) {
                    if (!BluetoothState.IsConnected(item,connected)) continue;
                    var old = batteries.FirstOrDefault(b => b.Text("name") == item.Name);
                    updated.Add(!batteryRefresh && old != null && item.Detail != "MCHOSE G3 2.4 GHz receiver" ? old : Battery(item));
                }
                batteries = updated;
            } else {
                batteries.RemoveAll(b => b.Text("detail") == "MCHOSE G3 2.4 GHz receiver" || b.Text("detail").StartsWith("Windows controller level"));
                batteries.AddRange(fresh.Where(DynamicBattery).Select(Battery));
            }
            return batteries.ToArray();
        }
        static bool DynamicBattery(MonitorStatsPeripheralBatteryReading b){return b.Detail=="MCHOSE G3 2.4 GHz receiver"||(b.Detail??"").StartsWith("Windows controller level");}
        static Data Battery(MonitorStatsPeripheralBatteryReading b) {
            return Data.Of("name", b.Name, "category", b.Category, "batteryPercent", b.BatteryPercent, "batteryLevel", b.BatteryLevel, "detail", b.Detail);
        }
        public void Dispose(){worker.Dispose();}
    }
}
