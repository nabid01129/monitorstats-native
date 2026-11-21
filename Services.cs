using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace MonitorStatsNative {
    internal sealed class Services : IDisposable {
        readonly object gate = new object();
        Data details = new Data();
        Options options;
        int generation;
        bool busy, disposed, pending;
        DateTime next = DateTime.MinValue, opticalNext = DateTime.MinValue, publicNext=DateTime.MinValue, routerNext=DateTime.MinValue, dnsNext=DateTime.MinValue;
        string gateway;
        public event Action Updated;
        public Services(Options options) { this.options = options.Clone(); NetworkChange.NetworkAddressChanged += Changed; }
        public Data Snapshot() { lock (gate) return details.Copy(); }
        public void Configure(Options value) {
            lock (gate) {
                if(options.Endpoint==value.Endpoint&&options.Username==value.Username&&options.Password==value.Password)return;
                generation++;
                details.Remove("rxOpticalPowerDbm");details.Remove("opticalUpdatedAt");details.Remove("opticalStale");
                options = value.Clone(); opticalNext = DateTime.MinValue; next = DateTime.MinValue;
            }
        }
        public void Refresh() { lock (gate) { generation++; pending=true; next = publicNext = routerNext = dnsNext = opticalNext = DateTime.MinValue; } }
        void Changed(object sender, EventArgs args) {
            lock (gate) { generation++; details = new Data(); next = publicNext = routerNext = dnsNext = opticalNext = DateTime.MinValue; pending = true; }
            Notify();
        }
        void Notify() { var handler = Updated; if (handler != null) handler(); }
        public void Tick(Data network) {
            Options config; int token; string router;bool publicDue,routerDue,dnsDue;
            DateTime publicDeadline,routerDeadline,dnsDeadline,opticalDeadline;
            lock (gate) {
                gateway = network.Text("gateway");
                if (disposed || busy || DateTime.UtcNow < next) return;
                busy = true; pending = false; config = options.Clone(); token = generation; router = gateway;
                publicDue=DateTime.UtcNow>=publicNext;routerDue=DateTime.UtcNow>=routerNext;dnsDue=DateTime.UtcNow>=dnsNext;
                publicDeadline=publicNext;routerDeadline=routerNext;dnsDeadline=dnsNext;opticalDeadline=opticalNext;
            }
            Task.Run(() => {
                Data result;
                lock (gate) result = details.Copy();
                bool publicSuccess = false;
                try {
                    if(publicDue){
                        var v4Task = Task.Run(() => Public("https://api4.ipify.org?format=json"));
                        var v6Task = Task.Run(() => Public("https://api6.ipify.org?format=json"));
                        Task.WaitAll(v4Task, v6Task);
                        string v4 = v4Task.Result, v6 = v6Task.Result;
                        MergePublicAddresses(result,v4,v6);
                        string ip = String.IsNullOrEmpty(v4) ? v6 : v4;
                        if (!String.IsNullOrEmpty(ip)) {
                            Data geo = Geo(ip);
                            string provider=geo.Section("connection").Text("isp");
                            if(provider.Length>0)result["provider"] = provider;
                            publicSuccess = !String.IsNullOrEmpty(v4);
                        }
                        publicDeadline=DateTime.UtcNow.AddSeconds(publicSuccess?1800:30);
                    }
                    if(routerDue){
                        var routerData = Router(router);
                        foreach (var pair in routerData) result[pair.Key] = pair.Value;
                        routerDeadline=DateTime.UtcNow.AddSeconds(routerData.Text("routerModel").Length>0?1800:60);
                    }
                    string wan = result.Text("routerWanIPv4");
                    string publicIp=result.Text("publicIPv4");
                    result["ipv4Type"] = wan.Length > 0 && publicIp.Length>0 ? wan == publicIp ? "Real IP" : "Shared IP" : null;
                    if(dnsDue){
                        try{Data dns = Resolver();if (dns.Count > 0) result["dnsResolver"] = dns;else result.Remove("dnsResolver");}catch{result.Remove("dnsResolver");}
                        dnsDeadline=DateTime.UtcNow.AddMinutes(30);
                    }
                    bool opticalDue=DateTime.UtcNow>=opticalDeadline;
                    if (opticalDue && config.Endpoint.Length > 0 && config.Username.Length > 0 && config.Password.Length > 0) {
                        try {
                            double? rx = Optical(config);
                            if (rx.HasValue) { result["rxOpticalPowerDbm"] = rx; result["opticalUpdatedAt"] = DateTime.Now.ToString("t"); }
                            result["opticalStale"]=!rx.HasValue;
                            opticalDeadline = DateTime.UtcNow.AddSeconds(rx.HasValue ? 1800 : 30);
                        } catch { result["opticalStale"]=true;opticalDeadline = DateTime.UtcNow.AddSeconds(30); }
                    }else if(opticalDue){opticalDeadline=DateTime.MaxValue;}
                } catch {
                    // Failed optional work must back off rather than restart on every poll.
                    DateTime retry=DateTime.UtcNow.AddSeconds(30);
                    if(publicDeadline<=DateTime.UtcNow)publicDeadline=retry;
                    if(routerDeadline<=DateTime.UtcNow)routerDeadline=retry;
                    if(dnsDeadline<=DateTime.UtcNow)dnsDeadline=retry;
                }
                finally {
                    lock (gate) {
                        if (!disposed && token == generation) {
                            details = result;
                            publicNext=publicDeadline;routerNext=routerDeadline;dnsNext=dnsDeadline;opticalNext=opticalDeadline;
                        }
                        busy = false;
                        next = token != generation || pending ? DateTime.MinValue : new[]{publicNext,routerNext,dnsNext,opticalNext}.Min();
                    }
                    Notify();
                }
            });
        }
        internal static void MergePublicAddresses(Data result,string v4,string v6){
            // Keep the last successful value on a transient request failure.
            // NetworkAddressChanged clears the entire snapshot on a new network.
            if(!String.IsNullOrEmpty(v4))result["publicIPv4"]=v4;
            if(!String.IsNullOrEmpty(v6))result["publicIPv6"]=v6;
        }
        static string Public(string url) {
            try { string value = Data.Json(Get(url)).Text("ip"); IPAddress ip; return IPAddress.TryParse(value, out ip) ? value : null; } catch { return null; }
        }
        static Data Geo(string ip) { try { return Data.Json(Get("https://ipwho.is/" + Uri.EscapeDataString(ip))); } catch { return new Data(); } }
        internal static string Get(string url, string method = "GET", string body = null, CookieContainer cookies = null, string referer = null, Dictionary<string,string> headers = null) {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "MonitorStatsNative/"+Program.Version;
            request.Timeout = 6000; request.ReadWriteTimeout = 6000; request.Method = method;
            request.Proxy = null; request.AllowAutoRedirect = false;
            if (cookies != null) request.CookieContainer = cookies;
            if (referer != null) request.Referer = referer;
            if (headers != null) foreach (var pair in headers) request.Headers[pair.Key] = pair.Value;
            if (body != null) {
                request.ContentType = headers != null && headers.ContainsKey("SOAPAction") ? "text/xml; charset=utf-8" : "application/x-www-form-urlencoded";
                byte[] bytes = Encoding.UTF8.GetBytes(body); request.ContentLength = bytes.Length;
                using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            }
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8)) {
                var result = new StringBuilder(); var buffer = new char[4096];
                int count;
                while ((count = reader.Read(buffer, 0, buffer.Length)) > 0) {
                    if (result.Length + count > 512 * 1024) throw new IOException("Response too large");
                    result.Append(buffer, 0, count);
                }
                return result.ToString();
            }
        }
        static Data Router(string gateway) {
            var result = new Data();
            if (!Private(gateway)) return result;
            var locations = new List<string> { "http://" + gateway + ":5000/rootDesc.xml" };
            try {
                using (var udp = new UdpClient(AddressFamily.InterNetwork)) {
                    udp.Client.ReceiveTimeout = 1600;
                    foreach (string target in new[] { "urn:schemas-upnp-org:device:InternetGatewayDevice:1", "upnp:rootdevice" }) {
                        var packet = Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 1\r\nST: " + target + "\r\n\r\n");
                        udp.Send(packet, packet.Length, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900));
                    }
                    DateTime until = DateTime.UtcNow.AddSeconds(2);
                    while (DateTime.UtcNow < until) {
                        IPEndPoint sender = new IPEndPoint(IPAddress.Any,0);
                        string reply = Encoding.ASCII.GetString(udp.Receive(ref sender));
                        var match = Regex.Match(reply, @"(?im)^location\s*:\s*(http://[^\r\n]+)");
                        Uri uri;
                        if (match.Success && Uri.TryCreate(match.Groups[1].Value.Trim(), UriKind.Absolute, out uri) && uri.Host == gateway) locations.Add(uri.AbsoluteUri);
                    }
                }
            } catch { }
            foreach (string location in locations.Distinct()) try {
                XDocument doc;
                using (var reader = XmlReader.Create(new StringReader(Get(location)), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) doc = XDocument.Load(reader);
                Func<XContainer,string,string> tag = (node,name) => node.Descendants().Where(e => e.Name.LocalName == name).Select(e => e.Value.Trim()).FirstOrDefault() ?? "";
                string maker = tag(doc,"manufacturer"), model = tag(doc,"modelName"), number = tag(doc,"modelNumber");
                result["routerManufacturer"] = maker; result["routerModel"] = number.StartsWith(model, StringComparison.OrdinalIgnoreCase) ? number : (model + " " + number).Trim();
                var service = doc.Descendants().Where(e => e.Name.LocalName == "service").FirstOrDefault(e => Query.Match(tag(e,"serviceType"), "WANIPConnection:|WANPPPConnection:"));
                if (service != null) {
                    string type = tag(service,"serviceType"); var control = new Uri(new Uri(location),tag(service,"controlURL"));
                    if (control.Host == gateway && control.Scheme == "http") {
                        string body = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:GetExternalIPAddress xmlns:u=\"" + type + "\"/></s:Body></s:Envelope>";
                        string response = Get(control.AbsoluteUri,"POST",body,null,null,new Dictionary<string,string> { { "SOAPAction", "\"" + type + "#GetExternalIPAddress\"" } });
                        var match = Regex.Match(response, @"<NewExternalIPAddress>([^<]+)");
                        IPAddress wan;
                        if (match.Success && IPAddress.TryParse(match.Groups[1].Value, out wan)) result["routerWanIPv4"] = wan.ToString();
                    }
                }
                if (maker.Length > 0 || model.Length > 0) break;
            } catch { }
            return result;
        }
        internal static bool Private(string address) {
            IPAddress ip;
            if (!IPAddress.TryParse(address, out ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
            byte[] b = ip.GetAddressBytes();
            return b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] >= 16 && b[1] <= 31 || b[0] == 127;
        }
        static double? Optical(Options options) {
            Uri host;
            if (!Uri.TryCreate(options.Endpoint,UriKind.Absolute,out host) || host.Scheme != "http" || !Private(host.Host)) return null;
            string root = host.GetLeftPart(UriPartial.Authority);
            var cookies = new CookieContainer();
            Get(root + "/");
            string challenge = Get(root + "/asp/GetRandCount.asp","POST","",cookies);
            var token = Regex.Match(challenge.Trim(), @"(?i)[a-f0-9]{32}$");
            if (!token.Success) return null;
            cookies.Add(new Uri(root),new Cookie("Cookie","UserName:" + options.Username + ":PassWord:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(options.Password)) + ":Language:english:id=-1","/"));
            Get(root + "/login.cgi","POST","x.X_HW_Token=" + token.Value,cookies,root+"/");
            Get(root + "/index.asp","GET",null,cookies,root+"/");
            Get(root + "/frame.asp","GET",null,cookies,root+"/index.asp");
            try {
                foreach (string path in new[] { "/html/status/opticinfo.asp", "/html/amp/opticinfo/opticinfo.asp" }) try {
                    string html = Get(root+path,"GET",null,cookies,root+"/frame.asp");
                    var row = Regex.Match(html,@"(?i)new\s+stOpticInfo\s*\(([^)]*)\)");
                    if (!row.Success) continue;
                    var values = Regex.Matches(row.Groups[1].Value,"\"([^\"]*)\"");
                    double value;
                    if (values.Count > 2 && Double.TryParse(values[2].Groups[1].Value.Replace(',','.'),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value) && value >= -100 && value <= 20) return value;
                } catch { }
            } finally { try { Get(root+"/logout.cgi?RequestFile=html/logout.html","POST","",cookies,root+"/frame.asp"); } catch { } }
            return null;
        }
        [DllImport("dnsapi.dll", EntryPoint="DnsQuery_W",CharSet=CharSet.Unicode)] static extern int DnsQuery(string name, ushort type, uint flags, IntPtr extra, out IntPtr results, IntPtr reserved);
        [DllImport("dnsapi.dll")] static extern void DnsRecordListFree(IntPtr records, int type);
        static Data Resolver() {
            var result = new Data(); IntPtr list;
            if (DnsQuery("whoami.ds.akahelp.net",16,0x8,IntPtr.Zero,out list,IntPtr.Zero) != 0) return result;
            var fields = new List<string>();
            try {
                for (IntPtr p=list; p!=IntPtr.Zero; p=Marshal.ReadIntPtr(p)) {
                    if (Marshal.ReadInt16(p,IntPtr.Size*2) != 16) continue;
                    int dataOffset=IntPtr.Size==8?32:24;
                    int count = Marshal.ReadInt32(p,dataOffset);
                    int pointerOffset=dataOffset+(IntPtr.Size==8?8:4);
                    for(int i=0;i<Math.Min(count,16);i++) fields.Add(Marshal.PtrToStringUni(Marshal.ReadIntPtr(p,pointerOffset + i*IntPtr.Size)));
                }
            } finally { DnsRecordListFree(list,1); }
            string ip = null;
            for(int i=0;i<fields.Count;i++) {
                string value = fields[i] ?? "";
                if (value.Equals("ns",StringComparison.OrdinalIgnoreCase) && i+1<fields.Count) value = fields[i+1];
                else { var match = Regex.Match(value,@"(?i)^ns\s+(.+)"); if (!match.Success) continue; value = match.Groups[1].Value; }
                IPAddress address;
                if (IPAddress.TryParse(value.Trim(),out address)) { ip=address.ToString(); break; }
            }
            if (ip == null) return result;
            var geo = Geo(ip);
            string hostname = "";
            try { hostname = Dns.GetHostEntry(ip).HostName; } catch { }
            string identity = (hostname + " " + geo.Section("connection").Text("org") + " " + geo.Section("connection").Text("isp")).ToLowerInvariant();
            string name = Query.Match(identity,"adguard") || ip.StartsWith("94.140.") ? "AdGuard DNS" : Query.Match(identity,"cloudflare") || ip=="1.1.1.1" || ip=="1.0.0.1" ? "Cloudflare DNS" : Query.Match(identity,"google") || ip=="8.8.8.8" || ip=="8.8.4.4" ? "Google DNS" : Query.Match(identity,"quad9") ? "Quad9" : Query.Match(identity,"opendns|cisco") ? "OpenDNS" : Query.Match(identity,"nextdns") ? "NextDNS" : ip;
            result = Data.Of("name",name,"ip",ip,"hostname",hostname,"country",geo.Text("country"),"location",geo.Text("city") + ", " + geo.Text("region"));
            try { using(var ping=new Ping()) { var reply=ping.Send(ip,1500); if(reply.Status==IPStatus.Success)result["pingMs"]=reply.RoundtripTime; } } catch { }
            return result;
        }
        public const string Releases="https://github.com/nabid01129/monitorstats-native/releases/latest";
        public static bool IsNewer(string tag) { Version latest,current; return Version.TryParse((tag??"").Trim().TrimStart('v','V'),out latest)&&Version.TryParse(Program.Version,out current)&&latest>current; }
        public static string CheckUpdate() {
            try { return Data.Json(Get("https://api.github.com/repos/nabid01129/monitorstats-native/releases/latest")).Text("tag_name"); } catch { return ""; }
        }
        public void Dispose() { lock(gate) { disposed=true; generation++; } NetworkChange.NetworkAddressChanged -= Changed; }
    }
}
