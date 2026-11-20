using System;
using System.Runtime.InteropServices;
public static class MonitorStatsNvmeSmart {
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool DeviceIoControl(IntPtr handle, uint code, byte[] input, uint inputLength, byte[] output, uint outputLength, out uint returned, IntPtr overlapped);
  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool CloseHandle(IntPtr handle);

  // Returns temperature C, percentage used, data units written, power-on hours, critical warnings.
  public static double[] Read(int diskNumber) {
    IntPtr handle = CreateFile("\\\\.\\PhysicalDrive" + diskNumber, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
    if (handle == new IntPtr(-1)) return null;
    try {
      byte[] buffer = new byte[560];
      BitConverter.GetBytes(50).CopyTo(buffer, 0); // StorageDeviceProtocolSpecificProperty
      BitConverter.GetBytes(0).CopyTo(buffer, 4); // PropertyStandardQuery
      BitConverter.GetBytes(3).CopyTo(buffer, 8); // ProtocolTypeNvme
      BitConverter.GetBytes(2).CopyTo(buffer, 12); // NVMeDataTypeLogPage
      BitConverter.GetBytes(2).CopyTo(buffer, 16); // SMART/health information log page
      BitConverter.GetBytes(40).CopyTo(buffer, 24); // data offset within protocol descriptor
      BitConverter.GetBytes(512).CopyTo(buffer, 28);
      uint returned;
      if (!DeviceIoControl(handle, 0x002D1400, buffer, (uint)buffer.Length, buffer, (uint)buffer.Length, out returned, IntPtr.Zero)) return null;
      int data = 48;
      if (returned < data + 192) return null;
      int kelvin = BitConverter.ToUInt16(buffer, data + 1);
      double temperature = kelvin >= 273 && kelvin < 700 ? kelvin - 273.15 : -1;
      double used = buffer[data + 5]; // NVMe Percentage Used can legitimately exceed 100.
      ulong low = BitConverter.ToUInt64(buffer, data + 48);
      ulong high = BitConverter.ToUInt64(buffer, data + 56);
      double dataUnitsWritten = low + high * 18446744073709551616.0;
      ulong powerOnHours = BitConverter.ToUInt64(buffer, data + 128);
      double criticalWarnings = buffer[data];
      return new double[] { temperature, used, dataUnitsWritten, powerOnHours, criticalWarnings };
    } finally { CloseHandle(handle); }
  }

}