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
    return ReadProcesses(false);
  }
  [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
  [StructLayout(LayoutKind.Sequential)] private struct ProcessTime { public uint Low, High; }
  [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr OpenProcess(uint access,bool inherit,int id);
  [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
  [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetProcessIoCounters(IntPtr handle,out IoCounters counters);
  [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetProcessTimes(IntPtr handle,out ProcessTime created,out ProcessTime exited,out ProcessTime kernel,out ProcessTime user);
  public static ProcessSnapshot ReadProcesses(bool includeWrites) {
    Process[] processes;
    try { processes = Process.GetProcesses(); } catch { return new ProcessSnapshot(); }
    var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    var writes = new List<ProcessWriteItem>();
    foreach (Process process in processes) {
      try {
        string name = process.ProcessName;
        if(includeWrites){
          IntPtr handle=OpenProcess(0x1000,false,process.Id);
          if(handle!=IntPtr.Zero)try{
            IoCounters counters;ProcessTime created,exited,kernel,user;
            if(GetProcessIoCounters(handle,out counters)&&GetProcessTimes(handle,out created,out exited,out kernel,out user))
              writes.Add(new ProcessWriteItem{Id=process.Id,Created=((ulong)created.High<<32)|created.Low,Name=name,Bytes=counters.WriteBytes});
          }finally{CloseHandle(handle);}
        }
        long bytes = process.WorkingSet64;
        long previous;
        totals.TryGetValue(name, out previous);
        totals[name] = previous + Math.Max(0, bytes);
      } catch { } finally { process.Dispose(); }
    }
    return new ProcessSnapshot {
      Count = processes.Length,
      Writes = writes.ToArray(),
      Top = totals.OrderByDescending(item => item.Value).Take(5)
        .Select(item => new ProcessMemoryItem { Name = item.Key, WorkingSetBytes = item.Value }).ToArray()
    };
  }
}
public sealed class ProcessSnapshot {
  public int Count { get; set; }
  public ProcessMemoryItem[] Top { get; set; }
  public ProcessWriteItem[] Writes { get; set; }
  public ProcessSnapshot() { Count = 0; Top = new ProcessMemoryItem[0]; Writes=new ProcessWriteItem[0]; }
}
public sealed class ProcessWriteItem { public int Id; public ulong Created,Bytes; public string Name; }
public sealed class ProcessMemoryItem {
  public string Name { get; set; }
  public long WorkingSetBytes { get; set; }
}
