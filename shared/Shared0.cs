using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
public static class MonitorStatsMemory {
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
  public struct StatusEx {
    public uint Length; public uint Load; public ulong TotalPhys; public ulong AvailPhys;
    public ulong TotalPageFile; public ulong AvailPageFile; public ulong TotalVirtual;
    public ulong AvailVirtual; public ulong AvailExtendedVirtual;
  }
  [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
  public static extern bool GlobalMemoryStatusEx(ref StatusEx status);
  public static StatusEx Read() {
    StatusEx status = new StatusEx(); status.Length = (uint)Marshal.SizeOf(typeof(StatusEx));
    GlobalMemoryStatusEx(ref status); return status;
  }
  public static ProcessSnapshot ReadProcesses() {
    Process[] processes;
    try { processes = Process.GetProcesses(); } catch { return new ProcessSnapshot(); }
    var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    foreach (Process process in processes) {
      try {
        string name = process.ProcessName;
        long bytes = process.WorkingSet64;
        long previous;
        totals.TryGetValue(name, out previous);
        totals[name] = previous + Math.Max(0, bytes);
      } catch { } finally { process.Dispose(); }
    }
    return new ProcessSnapshot {
      Count = processes.Length,
      Top = totals.OrderByDescending(item => item.Value).Take(5)
        .Select(item => new ProcessMemoryItem { Name = item.Key, WorkingSetBytes = item.Value }).ToArray()
    };
  }
}
public sealed class ProcessSnapshot {
  public int Count { get; set; }
  public ProcessMemoryItem[] Top { get; set; }
  public ProcessSnapshot() { Count = 0; Top = new ProcessMemoryItem[0]; }
}
public sealed class ProcessMemoryItem {
  public string Name { get; set; }
  public long WorkingSetBytes { get; set; }
}