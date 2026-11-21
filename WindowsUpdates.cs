using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace MonitorStatsNative {
    internal sealed class WindowsUpdates : IDisposable {
        readonly AsyncSnapshot<Data> worker=new AsyncSnapshot<Data>(new Data(),Read,null);
        DateTime next;
        public Data Snapshot {get{return worker.Value;}}
        public void Tick(bool refresh){if(!refresh&&DateTime.UtcNow<next)return;next=DateTime.UtcNow.AddMinutes(30);worker.Request();}
        static object Property(object value,string name,params object[] args){return value.GetType().InvokeMember(name,BindingFlags.GetProperty,null,value,args,CultureInfo.InvariantCulture);}
        static object Call(object value,string name,params object[] args){return value.GetType().InvokeMember(name,BindingFlags.InvokeMethod,null,value,args,CultureInfo.InvariantCulture);}
        static void Release(object value){if(value!=null&&Marshal.IsComObject(value))Marshal.FinalReleaseComObject(value);}
        internal static Data Notice(int count,bool restart,IEnumerable<string> titles){
            var names=titles.Where(t=>!String.IsNullOrWhiteSpace(t)).Select(t=>Regex.Replace(t,@"\s+"," ").Trim()).Take(1).ToArray();
            string heading=restart?"Windows restart required":count>0?count+" Windows update"+(count==1?"":"s")+" available":"";
            string summary=String.Join(" · ",names.Select(t=>t.Length>72?t.Substring(0,69)+"…":t));
            if(count>names.Length)summary+=(summary.Length>0?" · ":"")+"+"+(count-names.Length)+" more";
            if(restart&&summary.Length==0)summary="Restart to finish installing Windows updates";
            return Data.Of("count",count,"restart",restart,"heading",heading,"summary",summary);
        }
        internal const string SearchCriteria="IsInstalled=0 and IsHidden=0 and DeploymentAction='Installation' or IsInstalled=0 and IsHidden=0 and DeploymentAction='OptionalInstallation'";
        static Data Read(){
            object info=null,session=null,searcher=null,result=null,updates=null;
            bool restart=false;
            try{
                info=Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.SystemInfo",true));
                restart=Convert.ToBoolean(Property(info,"RebootRequired"));
                session=Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session",true));
                searcher=Call(session,"CreateUpdateSearcher");
                // Read only Windows' cached catalog; do not trigger an online scan.
                searcher.GetType().InvokeMember("Online",BindingFlags.SetProperty,null,searcher,new object[]{false});
                result=Call(searcher,"Search",SearchCriteria);
                updates=Property(result,"Updates");
                int count=Convert.ToInt32(Property(updates,"Count"));
                var titles=new List<string>();var drivers=new List<string>();
                for(int i=0;i<count;i++){object update=null;try{update=Property(updates,"Item",i);(Convert.ToInt32(Property(update,"Type"))==2?drivers:titles).Add(Convert.ToString(Property(update,"Title")));}finally{Release(update);}}
                var notice=Notice(count,restart,titles.Concat(drivers));notice["status"]="cached";return notice;
            }catch(Exception error){var notice=Notice(0,restart,new string[0]);notice["status"]="unavailable";notice["errorCode"]=error.HResult;return notice;}
            finally{Release(updates);Release(result);Release(searcher);Release(session);Release(info);}
        }
        public void Dispose(){worker.Dispose();}
    }
}
