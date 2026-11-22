using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

// Only a last-known reading, never evidence of a live radio or current charge.
internal static class G3BatteryCache {
    const string RegistryPath = @"Software\MonitorStatsNative\G3Battery";
    internal static string ReceiverKey(string path) {
        using (var hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(path.ToUpperInvariant()))).Replace("-", "");
    }
    internal static bool Parse(string value, DateTime now, out int percent, out DateTime measured) {
        percent = 0; measured = DateTime.MinValue;
        var parts = (value ?? "").Split('|'); long ticks;
        if (parts.Length != 2 || !Int32.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out percent) ||
            percent < 0 || percent > 100 || !Int64.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ticks) ||
            ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) return false;
        measured = new DateTime(ticks, DateTimeKind.Utc);
        return measured <= now && now - measured <= TimeSpan.FromDays(7);
    }
    internal static bool Load(string receiver, DateTime now, out int percent, out DateTime measured) {
        percent = 0; measured = DateTime.MinValue;
        try {
            using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                return key != null && Parse(key.GetValue(receiver) as string, now, out percent, out measured);
        } catch { return false; }
    }
    internal static void Save(string receiver, int percent, DateTime measured) {
        if (String.IsNullOrEmpty(receiver)) return;
        try {
            using (var key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                key.SetValue(receiver, percent.ToString(CultureInfo.InvariantCulture) + "|" + measured.Ticks.ToString(CultureInfo.InvariantCulture), RegistryValueKind.String);
        } catch { /* A read-only profile must not prevent live battery reporting. */ }
    }
}
