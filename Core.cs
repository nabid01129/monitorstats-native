using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace MonitorStatsNative {
    internal sealed class Data : Dictionary<string, object> {
        public Data() : base(StringComparer.OrdinalIgnoreCase) { }
        public static Data Of(params object[] pairs) {
            var data = new Data();
            for (int i = 0; i + 1 < pairs.Length; i += 2) data[(string)pairs[i]] = pairs[i + 1];
            return data;
        }
        public object Get(string key) { object value; return TryGetValue(key, out value) ? value : null; }
        public string Text(string key) { return Convert.ToString(Get(key), CultureInfo.InvariantCulture); }
        public double? Number(string key) {
            double value;
            return Get(key) != null && Double.TryParse(Text(key), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !Double.IsInfinity(value) && !Double.IsNaN(value) ? (double?)value : null;
        }
        public Data Section(string key) { return Get(key) as Data ?? new Data(); }
        public Data Copy() { var copy = new Data(); foreach (var pair in this) copy[pair.Key] = pair.Value; return copy; }
        public static Data Json(string json) { return ConvertTree(new JavaScriptSerializer().DeserializeObject(json)) as Data ?? new Data(); }
        public static object ConvertTree(object value) {
            var map = value as IDictionary<string, object>;
            if (map != null) { var result = new Data(); foreach (var item in map) result[item.Key] = ConvertTree(item.Value); return result; }
            var array = value as object[];
            return array == null ? value : array.Select(ConvertTree).ToArray();
        }
        public IEnumerable<Data> Rows(string key) {
            var rows = Get(key) as IEnumerable;
            return rows == null ? Enumerable.Empty<Data>() : rows.Cast<object>().OfType<Data>();
        }
        public string Json() { return new JavaScriptSerializer().Serialize(this); }
    }
    internal sealed class Options {
        public string Theme = "classic", Display = "", Position = "center", Unit = "Mbps";
        public int Poll = 3000, Width = 75, Height = 75, Scale = 100, Shift = 300000, GameMonitor = 1;
        public bool Fill = true, AutoScale = true, Startup = false, Fps = true, Cores = true;
        public Dictionary<string, string> Names = new Dictionary<string, string>();
        public string Endpoint = "", Username = "", Password = "";
        public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitorStats Native");
        public static Options Load() {
            try {
                var result = new JavaScriptSerializer().Deserialize<Options>(File.ReadAllText(Path.Combine(Folder, "settings.json")));
                if (result == null) return new Options();
                result.Normalize();
                if (result.Password.Length > 0) {
                    try { result.Password = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(result.Password), null, DataProtectionScope.CurrentUser)); }
                    catch { result.Password = ""; }
                }
                return result;
            } catch { return new Options(); }
        }
        public void Normalize() {
            Theme=MonitorStatsNative.Theme.Find(Theme??"classic").Id;
            Display=Display??"";Endpoint=Endpoint??"";Username=Username??"";Password=Password??"";
            if(!new[]{"left","center","right"}.Contains(Position))Position="center";
            if(Unit!="Mbps"&&Unit!="MB/s")Unit="Mbps";
            if (!new[] { 1000, 2000, 3000, 5000, 10000 }.Contains(Poll)) Poll = 3000;
            Width = Math.Max(25, Math.Min(100, Width)); Height = Math.Max(25, Math.Min(100, Height));
            Scale = Math.Max(70, Math.Min(130, Scale)); Shift = Math.Max(60000, Math.Min(3600000, Shift));
            GameMonitor = Math.Max(1, GameMonitor); Names = Names ?? new Dictionary<string, string>();
        }
        public Options Clone() { return new JavaScriptSerializer().Deserialize<Options>(new JavaScriptSerializer().Serialize(this)); }
        public void Save() {
            Normalize();
            var stored = Clone();
            if (stored.Password.Length > 0) stored.Password = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(stored.Password), null, DataProtectionScope.CurrentUser));
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, "settings.json"), temp = path + ".tmp";
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(stored));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) {
                if (Startup) key.SetValue("MonitorStatsNative", "\"" + System.Windows.Forms.Application.ExecutablePath + "\"");
                else key.DeleteValue("MonitorStatsNative", false);
            }
        }
        public string Name(string key, string detected) { string value; return Names.TryGetValue(key, out value) && !String.IsNullOrWhiteSpace(value) ? value : detected; }
    }
    internal static class Query {
        public static List<Data> Wmi(string query, string scope = @"root\cimv2") {
            var result = new List<Data>();
            try {
                using (var search = new ManagementObjectSearcher(scope, query, new EnumerationOptions { ReturnImmediately = true, Rewindable = false, Timeout = TimeSpan.FromSeconds(3) }))
                using (var rows = search.Get()) {
                    foreach (ManagementBaseObject row in rows) using (row) {
                        var data = new Data();
                        foreach (PropertyData prop in row.Properties) data[prop.Name] = prop.Value;
                        result.Add(data);
                    }
                }
            } catch { }
            return result;
        }
        public static bool Match(string text, string expression) { return System.Text.RegularExpressions.Regex.IsMatch(text ?? "", expression, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
        public static string Format(double? value, string unit = "", int digits = 1) { return value.HasValue ? value.Value.ToString("N" + digits, CultureInfo.CurrentCulture) + unit : ""; }
    }
}
