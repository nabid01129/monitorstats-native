using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MonitorStatsNative {
    // GetProcessIoCounters is write I/O across ALL devices, not per-drive disk
    // traffic. Keep this ranking separate from the volume write measurements.
    internal sealed class AppWriteHistory : IDisposable {
        const long BucketTicks=TimeSpan.TicksPerMinute*15;
        const int Day=96,Month=2880,Signature=0x31495741;
        struct Bucket { public int Time,App; public ulong Bytes; }
        readonly string path;
        readonly List<string> names=new List<string>();
        readonly Dictionary<string,int> ids=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        readonly List<Bucket> records=new List<Bucket>();
        readonly Dictionary<int,int> current=new Dictionary<int,int>();
        Dictionary<int,ProcessWriteItem> previous=new Dictionary<int,ProcessWriteItem>();
        readonly Dictionary<int,double> day=new Dictionary<int,double>(),month=new Dictionary<int,double>();
        int epoch=-1;
        DateTime lastSample,nextSave;
        bool dirty;
        Data snapshot=new Data();
        public AppWriteHistory():this(Path.Combine(Options.Folder,"app-write-io.bin")){}
        internal AppWriteHistory(string path):this(path,DateTime.UtcNow){}
        internal AppWriteHistory(string path,DateTime now){this.path=path;Load(now);}
        internal Data Snapshot {get{return snapshot;}}
        void Load(DateTime now){
            try{
                if(!File.Exists(path))return;
                using(var reader=new BinaryReader(File.OpenRead(path))){
                    if(reader.BaseStream.Length>8*1024*1024||reader.ReadInt32()!=Signature)throw new InvalidDataException();
                    int count=reader.ReadInt32();if(count<0||count>4096)throw new InvalidDataException();
                    for(int i=0;i<count;i++){string name=reader.ReadString();if(String.IsNullOrWhiteSpace(name)||name.Length>128||ids.ContainsKey(name))throw new InvalidDataException();ids.Add(name,names.Count);names.Add(name);}
                    int length=reader.ReadInt32();if(length<0||length>400000||reader.BaseStream.Length-reader.BaseStream.Position!=length*16L)throw new InvalidDataException();
                    int nowBucket=(int)(now.Ticks/BucketTicks);
                    for(int i=0;i<length;i++){
                        var item=new Bucket{Time=reader.ReadInt32(),App=reader.ReadInt32(),Bytes=reader.ReadUInt64()};
                        if(item.App<0||item.App>=names.Count||item.Time>nowBucket)throw new InvalidDataException();
                        if(item.Time>nowBucket-Month)records.Add(item);
                    }
                }
                Window((int)(now.Ticks/BucketTicks));Publish();
            }catch{names.Clear();ids.Clear();records.Clear();current.Clear();day.Clear();month.Clear();epoch=-1;}
        }
        static void Add(Dictionary<int,double> totals,int id,ulong bytes){double old;totals.TryGetValue(id,out old);totals[id]=old+bytes;}
        void Window(int now){
            if(epoch==now)return;
            epoch=now;records.RemoveAll(b=>b.Time<=now-Month);current.Clear();day.Clear();month.Clear();
            if(records.Count==0&&names.Count>0){names.Clear();ids.Clear();dirty=true;}
            for(int i=0;i<records.Count;i++){
                var item=records[i];if(item.Time>now)continue;
                Add(month,item.App,item.Bytes);if(item.Time>now-Day)Add(day,item.App,item.Bytes);
                if(item.Time==now)current[item.App]=i;
            }
        }
        Data[] Top(Dictionary<int,double> totals){return totals.Where(p=>p.Value>0).OrderByDescending(p=>p.Value).ThenBy(p=>names[p.Key],StringComparer.OrdinalIgnoreCase).Take(3).Select(p=>Data.Of("name",names[p.Key],"bytes",p.Value)).ToArray();}
        void Publish(){snapshot=Data.Of("last24h",Top(day),"last30d",Top(month),"source","Windows process write I/O: file, network and device writes; 30-second samples; 15-minute buckets");}
        internal void Observe(ProcessWriteItem[] samples,DateTime now){
            bool uncertain=now<lastSample||(lastSample!=DateTime.MinValue&&now-lastSample>TimeSpan.FromMinutes(15));
            Window((int)(now.Ticks/BucketTicks));
            var next=new Dictionary<int,ProcessWriteItem>();
            foreach(var item in samples??new ProcessWriteItem[0]){
                if(item==null||item.Id<=0||item.Created==0||String.IsNullOrWhiteSpace(item.Name)||item.Name.Length>128)continue;
                next[item.Id]=item;ProcessWriteItem old;
                if(uncertain||!previous.TryGetValue(item.Id,out old)||old.Created!=item.Created||item.Bytes<old.Bytes)continue;
                ulong delta=item.Bytes-old.Bytes;if(delta==0)continue;
                int id;
                if(!ids.TryGetValue(item.Name,out id)){if(names.Count>=4096)continue;id=names.Count;ids.Add(item.Name,id);names.Add(item.Name);}
                int index;
                if(current.TryGetValue(id,out index)){
                    var bucket=records[index];if(UInt64.MaxValue-bucket.Bytes<delta)continue;bucket.Bytes+=delta;records[index]=bucket;
                }else{
                    if(records.Count>=400000)continue;
                    current[id]=records.Count;records.Add(new Bucket{Time=epoch,App=id,Bytes=delta});
                }
                Add(day,id,delta);Add(month,id,delta);dirty=true;
            }
            previous=next;lastSample=now;Publish();
            if(now>=nextSave){nextSave=now.AddMinutes(5);Save();}
        }
        void Save(){
            if(!dirty)return;
            try{
                Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".tmp";
                using(var writer=new BinaryWriter(File.Create(temp))){
                    writer.Write(Signature);writer.Write(names.Count);foreach(string name in names)writer.Write(name);
                    writer.Write(records.Count);foreach(var item in records){writer.Write(item.Time);writer.Write(item.App);writer.Write(item.Bytes);}
                }
                if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);dirty=false;
            }catch{/* Read-only or locked history must not interrupt the process scan. */}
        }
        public void Dispose(){Save();}
    }
}
