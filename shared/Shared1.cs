using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

public sealed class MonitorStatsPeripheralBatteryReading {
  public string Name { get; set; }
  public string Category { get; set; }
  public string ContainerId { get; set; }
  public bool IsBluetoothDevice { get; set; }
  public int? BatteryPercent { get; set; }
  public string BatteryLevel { get; set; }
  public string Detail { get; set; }
}

public static class MonitorStatsPeripheralBattery {
  [UnmanagedFunctionPointer(CallingConvention.Winapi)]
  private delegate uint CmNotifyCallback(IntPtr notify, IntPtr context, uint action, IntPtr eventData, uint eventDataSize);

  [StructLayout(LayoutKind.Sequential)]
  private struct DeviceInfoData {
    public int Size;
    public Guid ClassGuid;
    public uint DevInst;
    public IntPtr Reserved;
  }
  [StructLayout(LayoutKind.Sequential)]
  private struct DeviceInterfaceData {
    public int Size;
    public Guid ClassGuid;
    public int Flags;
    public IntPtr Reserved;
  }
  [StructLayout(LayoutKind.Sequential)]
  private struct HidCaps {
    public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] Reserved;
    public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
    public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
    public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
  }
  [StructLayout(LayoutKind.Sequential)]
  private struct PropertyKey {
    public Guid FormatId;
    public uint PropertyId;
    public PropertyKey(string formatId, uint propertyId) { FormatId = new Guid(formatId); PropertyId = propertyId; }
  }
  [StructLayout(LayoutKind.Sequential)]
  private struct XInputBatteryInfo { public byte Type; public byte Level; }

  [DllImport("setupapi.dll", EntryPoint="SetupDiGetClassDevsW", CharSet=CharSet.Unicode, SetLastError=true)]
  private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string enumerator, IntPtr hwndParent, uint flags);
  [DllImport("setupapi.dll", EntryPoint="SetupDiEnumDeviceInfo", SetLastError=true)]
  private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceInfoData deviceInfo);
  [DllImport("setupapi.dll", EntryPoint="SetupDiGetDevicePropertyW", CharSet=CharSet.Unicode, SetLastError=true)]
  private static extern bool SetupDiGetDeviceProperty(IntPtr set, ref DeviceInfoData deviceInfo, ref PropertyKey key, out uint propertyType, byte[] buffer, uint bufferSize, out uint requiredSize, uint flags);
  [DllImport("setupapi.dll", EntryPoint="SetupDiGetDeviceInstanceIdW", CharSet=CharSet.Unicode, SetLastError=true)]
  private static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref DeviceInfoData deviceInfo, StringBuilder instanceId, int instanceIdSize, out int requiredSize);
  [DllImport("setupapi.dll", EntryPoint="SetupDiGetClassDevsW", CharSet=CharSet.Unicode, SetLastError=true)]
  private static extern IntPtr SetupDiGetHidInterfaces(ref Guid classGuid, string enumerator, IntPtr hwndParent, uint flags);
  [DllImport("setupapi.dll", EntryPoint="SetupDiEnumDeviceInterfaces", SetLastError=true)]
  private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr deviceInfo, ref Guid classGuid, uint index, ref DeviceInterfaceData interfaceData);
  [DllImport("setupapi.dll", EntryPoint="SetupDiGetDeviceInterfaceDetailW", CharSet=CharSet.Unicode, SetLastError=true)]
  private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref DeviceInterfaceData interfaceData, IntPtr detail, uint detailSize, out uint requiredSize, IntPtr deviceInfo);
  [DllImport("hid.dll", EntryPoint="HidD_GetHidGuid")]
  private static extern void HidD_GetHidGuid(out Guid classGuid);
  [DllImport("hid.dll", EntryPoint="HidD_GetPreparsedData")]
  private static extern bool HidD_GetPreparsedData(IntPtr handle, out IntPtr preparsedData);
  [DllImport("hid.dll", EntryPoint="HidD_FreePreparsedData")]
  private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);
  [DllImport("hid.dll", EntryPoint="HidP_GetCaps")]
  private static extern int HidP_GetCaps(IntPtr preparsedData, out HidCaps caps);
  [DllImport("hid.dll", SetLastError=true)]
  [return: MarshalAs(UnmanagedType.U1)]
  private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] report, int reportLength);
  [DllImport("hid.dll", SetLastError=true)]
  [return: MarshalAs(UnmanagedType.U1)]
  private static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] report, int reportLength);
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
  [DllImport("setupapi.dll", EntryPoint="SetupDiDestroyDeviceInfoList", SetLastError=true)]
  private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
  [DllImport("xinput1_4.dll", EntryPoint="XInputGetBatteryInformation", SetLastError=true)]
  private static extern uint XInputGetBatteryInformation(uint userIndex, byte deviceType, out XInputBatteryInfo batteryInfo);
  [DllImport("cfgmgr32.dll", EntryPoint="CM_Register_Notification", CallingConvention=CallingConvention.Winapi)]
  private static extern uint CmRegisterNotification(byte[] filter, IntPtr context, CmNotifyCallback callback, out IntPtr notification);
  [DllImport("cfgmgr32.dll", EntryPoint="CM_Unregister_Notification", CallingConvention=CallingConvention.Winapi)]
  private static extern uint CmUnregisterNotification(IntPtr notification);

  // CM_NOTIFY_FILTER has a 400-byte device-instance union, even when the
  // all-interface-classes flag is used. Keep the callback rooted for the life
  // of the collector process and let it only set a lightweight dirty flag.
  private const int NotifyFilterSize = 16 + (200 * 2);
  private static readonly CmNotifyCallback NotificationCallback = OnDeviceNotification;
  private static int NotificationAttempted;
  private static int DeviceChangePending;
  private static IntPtr NotificationHandle = IntPtr.Zero;

  private static uint OnDeviceNotification(IntPtr notify, IntPtr context, uint action, IntPtr eventData, uint eventDataSize) {
    if (action == 0 || action == 1) Interlocked.Exchange(ref DeviceChangePending, 1);
    return 0; // CR_SUCCESS; do no device I/O on the PnP callback thread.
  }

  public static bool RegisterForDeviceNotifications() {
    if (Interlocked.CompareExchange(ref NotificationAttempted, 1, 0) != 0) return NotificationHandle != IntPtr.Zero;
    try {
      byte[] filter = new byte[NotifyFilterSize];
      BitConverter.GetBytes((uint)filter.Length).CopyTo(filter, 0); // cbSize
      BitConverter.GetBytes(1U).CopyTo(filter, 4); // ALL_INTERFACE_CLASSES
      BitConverter.GetBytes(0U).CopyTo(filter, 8); // DEVICEINTERFACE filter
      IntPtr notification;
      if (CmRegisterNotification(filter, IntPtr.Zero, NotificationCallback, out notification) != 0 || notification == IntPtr.Zero) {
        Interlocked.Exchange(ref NotificationAttempted, 0);
        return false;
      }
      NotificationHandle = notification;
      return true;
    } catch {
      Interlocked.Exchange(ref NotificationAttempted, 0);
      return false;
    }
  }

  public static bool ConsumeDeviceChange() {
    return Interlocked.Exchange(ref DeviceChangePending, 0) != 0;
  }

  public static void UnregisterDeviceNotifications() {
    CloseG3();
    IntPtr notification = NotificationHandle;
    NotificationHandle = IntPtr.Zero;
    if (notification != IntPtr.Zero) {
      try { CmUnregisterNotification(notification); } catch { }
    }
  }

  private static byte[] ReadProperty(IntPtr set, ref DeviceInfoData device, string formatId, uint propertyId, out uint type) {
    PropertyKey key = new PropertyKey(formatId, propertyId);
    byte[] buffer = new byte[1024];
    uint required;
    type = 0;
    if (!SetupDiGetDeviceProperty(set, ref device, ref key, out type, buffer, (uint)buffer.Length, out required, 0) || required == 0 || required > buffer.Length) return null;
    byte[] value = new byte[(int)required];
    Array.Copy(buffer, value, (int)required);
    return value;
  }
  private static string ReadName(IntPtr set, ref DeviceInfoData device) {
    uint type;
    byte[] value = ReadProperty(set, ref device, "A45C254E-DF1C-4EFD-8020-67D146A850E0", 14, out type);
    if (value == null || type != 0x12 || value.Length < 2) return null;
    return Encoding.Unicode.GetString(value).TrimEnd('\0').Trim();
  }
  private static string ReadContainerId(IntPtr set, ref DeviceInfoData device) {
    uint type;
    byte[] value = ReadProperty(set, ref device, "8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C", 2, out type);
    if (value == null || type != 0xD || value.Length != 16) return null;
    try { return new Guid(value).ToString("D"); } catch { return null; }
  }
  private static string ReadInstanceId(IntPtr set, ref DeviceInfoData device) {
    try {
      StringBuilder value = new StringBuilder(1024);
      int required;
      if (!SetupDiGetDeviceInstanceId(set, ref device, value, value.Capacity, out required)) return null;
      return value.ToString();
    } catch { return null; }
  }
  private static string GetCategory(string name) {
    if (Regex.IsMatch(name, @"(?i)\btws\b|true.?wireless|ear.?buds?|ear.?phones?|airpods|freebuds|galaxy buds|redmi buds|haylou")) return "Earbuds";
    if (Regex.IsMatch(name, @"(?i)headset|headphone|hands.?free|stereo|speaker|soundbar|microphone|\baudio\b")) return "Audio";
    if (Regex.IsMatch(name, "(?i)keyboard")) return "Keyboard";
    if (Regex.IsMatch(name, "(?i)game.?pad|game.?controller|joystick|xbox|playstation|dual.?sense|dual.?shock|nintendo|switch pro|controller")) return "Controller";
    if (Regex.IsMatch(name, "(?i)mouse|pointing device|touchpad|trackball")) return "Mouse";
    return "Wireless device";
  }
  private static string CleanName(string name) {
    return Regex.Replace(name, @"(?i)\s+(Hands.?Free(?:\s+(?:AG|HF))?|AVRCP Transport|Stereo|Consumer Control|Bluetooth LE Device)$", "").Trim();
  }

  // The G3 V1 sends radio status on its vendor collection (not the pointer input).
  // Keep one overlapped read pending so a status packet between samples is retained.
  private const int G3InputLength = 8;
  private const int G3FeatureLength = 520;
  private static FileStream g3Stream;
  private static byte[] g3Packet;
  private static Task<int> g3Read;
  private static bool g3Identified;
  private static bool? g3Online;
  private static int? g3Percent;
  private static int? g3DisplayedPercent;
  private static DateTime g3RefreshAt;
  private static DateTime g3NextQuery;
  private static DateTime g3LastStatus;

  private static void CloseG3() {
    if (g3Stream != null) g3Stream.Dispose();
    g3Stream = null;
    g3Packet = null;
    g3Read = null;
    g3Identified = false;
    g3Online = null;
    g3Percent = null;
    g3DisplayedPercent = null;
    g3LastStatus = DateTime.MinValue;
    g3RefreshAt = DateTime.MinValue;
    g3NextQuery = DateTime.MinValue;
  }

  private static bool RequestG3Status(SafeFileHandle handle) {
    // The G3 sends its battery input report in response to a configuration read.
    // Verify the model first because several mice share the receiver ID.
    byte[] request = new byte[G3FeatureLength];
    byte[] prefix = { 0x09, 0x0D, 0x05, 0x01, 0x01, 0x00, 0x06 };
    prefix.CopyTo(request, 0);
    try {
      if (!HidD_SetFeature(handle, request, request.Length)) return false;
      Thread.Sleep(100);
      byte[] reply = new byte[G3FeatureLength];
      reply[0] = 0x09;
      if (!HidD_GetFeature(handle, reply, reply.Length)) return false;
      for (int i = 0; i < prefix.Length; i++) {
        if (reply[i] != prefix[i]) return false;
      }
      byte[] productCode = { 0, 4, 0, 0, 0, 0, 0x19 };
      for (int i = 0; i < productCode.Length; i++) {
        if (reply[prefix.Length + i] != productCode[i]) return false;
      }

      // Read configuration without changing it. Only the separate input
      // report supplies the battery percentage.
      Array.Clear(request, 0, request.Length);
      byte[] readConfig = { 0x09, 0x00, 0x44, 0x00, 0x01, 0x00, 0x90, 0x01 };
      readConfig.CopyTo(request, 0);
      if (HidD_SetFeature(handle, request, request.Length)) {
        Thread.Sleep(120);
        Array.Clear(reply, 0, reply.Length);
        reply[0] = 0x09;
        HidD_GetFeature(handle, reply, reply.Length);
      }
      return true;
    } catch (ObjectDisposedException) { return false; }
  }

  private static FileStream OpenG3() {
    IntPtr set = IntPtr.Zero;
    try {
      Guid hidGuid;
      HidD_GetHidGuid(out hidGuid);
      set = SetupDiGetHidInterfaces(ref hidGuid, null, IntPtr.Zero, 0x2 | 0x10); // present HID interfaces
      if (set == new IntPtr(-1)) return null;
      for (uint index = 0; ; index++) {
        DeviceInterfaceData interfaceData = new DeviceInterfaceData();
        interfaceData.Size = Marshal.SizeOf(typeof(DeviceInterfaceData));
        if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref interfaceData)) break;
        uint requiredSize;
        SetupDiGetDeviceInterfaceDetail(set, ref interfaceData, IntPtr.Zero, 0, out requiredSize, IntPtr.Zero);
        if (requiredSize == 0) continue;
        IntPtr detail = Marshal.AllocHGlobal((int)requiredSize);
        try {
          Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
          if (!SetupDiGetDeviceInterfaceDetail(set, ref interfaceData, detail, requiredSize, out requiredSize, IntPtr.Zero)) continue;
          string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
          if (String.IsNullOrEmpty(path) || path.IndexOf("vid_258a&pid_0138&mi_01", StringComparison.OrdinalIgnoreCase) < 0) continue;
          SafeFileHandle handle = CreateFile(path, 0xC0000000u, 3, IntPtr.Zero, 3, 0x40000000u, IntPtr.Zero);
          try {
            if (handle.IsInvalid) continue;
            IntPtr preparsedData;
            if (!HidD_GetPreparsedData(handle.DangerousGetHandle(), out preparsedData)) continue;
            try {
              HidCaps caps;
              if (HidP_GetCaps(preparsedData, out caps) != 0x00110000 || caps.UsagePage != 0xFF02 ||
                  caps.Usage != 2 || caps.InputReportByteLength != G3InputLength ||
                  caps.FeatureReportByteLength != G3FeatureLength) continue;
              FileStream stream = new FileStream(handle, FileAccess.Read, 1, true);
              handle = null; // FileStream now owns the handle.
              return stream;
            } finally { HidD_FreePreparsedData(preparsedData); }
          } finally { if (handle != null) handle.Dispose(); }
        } finally { Marshal.FreeHGlobal(detail); }
      }
    } catch { }
    finally { if (set != IntPtr.Zero && set != new IntPtr(-1)) SetupDiDestroyDeviceInfoList(set); }
    return null;
  }

  private static MonitorStatsPeripheralBatteryReading ReadG3(bool discover) {
    try {
      if (g3Stream == null && discover) g3Stream = OpenG3();
      if (g3Stream == null) return null;
      if (g3Packet == null) g3Packet = new byte[G3InputLength];
      if (g3Read == null) g3Read = g3Stream.ReadAsync(g3Packet, 0, g3Packet.Length);
      var now = DateTime.UtcNow;
      if (now >= g3NextQuery) {
        // Keep the feature exchange in order on the collector thread. The
        // pending input read captures the response while the request completes.
        bool confirmed = RequestG3Status(g3Stream.SafeFileHandle);
        if (confirmed) g3Identified = true;
        g3NextQuery = now.AddSeconds(confirmed && g3Percent.HasValue ? 300 : 5);
      }
      for (int i = 0; i < 32 && g3Read.IsCompleted; i++) {
        int count = g3Read.GetAwaiter().GetResult();
        if (count == 0) { CloseG3(); return null; }
        ApplyG3Report(g3Packet, count, now);
        g3Read = g3Stream.ReadAsync(g3Packet, 0, g3Packet.Length);
      }
      if (g3Online != true) return null;
      if (now - g3LastStatus > TimeSpan.FromMinutes(11)) {
        g3Online = null;
        g3Percent = null;
        g3DisplayedPercent = null;
        return null;
      }
      if (g3Percent.HasValue && (!g3DisplayedPercent.HasValue || now >= g3RefreshAt)) {
        g3DisplayedPercent = g3Percent;
        g3RefreshAt = now.AddMinutes(5);
      }
      return new MonitorStatsPeripheralBatteryReading { Name = "MCHOSE G3", Category = "Mouse",
        BatteryPercent = g3DisplayedPercent, BatteryLevel = g3DisplayedPercent.HasValue ? null : "Connected",
        Detail = "MCHOSE G3 2.4 GHz receiver" };
    } catch (IOException) { CloseG3(); }
      catch (UnauthorizedAccessException) { CloseG3(); }
    return null;
  }

  private static void ApplyG3Report(byte[] report, int count, DateTime now) {
        if (g3Identified && count == G3InputLength && report != null && report.Length >= count && report[0] == 9 && report[1] == 0x0A) {
          int value = report[3];
          if (report[2] == 2 && value <= 1) {
            g3LastStatus = now;
            g3Online = value == 1;
            if (value == 0) { g3Percent = null; g3DisplayedPercent = null; }
            else if (!g3Percent.HasValue) g3NextQuery = now;
          } else if (report[2] == 5 && value <= 100) {
            // A fresh battery response proves the radio is online even if
            // the separate reconnect notification was missed.
            g3LastStatus = now;
            g3Online = true;
            g3Percent = value;
            g3NextQuery = now.AddMinutes(5);
          }
        }
  }

  public static MonitorStatsPeripheralBatteryReading[] Read(bool scanWindowsDevices) {
    var results = new List<MonitorStatsPeripheralBatteryReading>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (scanWindowsDevices) {
      IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, 0x2 | 0x4); // present devices, all classes
      if (set != new IntPtr(-1)) {
        try {
          for (uint index = 0; ; index++) {
            DeviceInfoData device = new DeviceInfoData();
            device.Size = Marshal.SizeOf(typeof(DeviceInfoData));
            if (!SetupDiEnumDeviceInfo(set, index, ref device)) break;
            uint type;
            byte[] value = ReadProperty(set, ref device, "104EA319-6EE2-4701-BD47-8DDBF425BBE5", 2, out type);
            // The Bluetooth battery property is a byte percentage. Fall back to
            // the documented shell BatteryLife property if the driver uses it.
            if (value == null || type != 0x3 || value.Length < 1) value = ReadProperty(set, ref device, "49CD1F76-5626-4B17-A4E8-18B4AA1A2213", 10, out type);
            if (value == null || type != 0x3 || value.Length < 1 || value[0] > 100) continue;
            string rawName = ReadName(set, ref device);
            string instanceId = ReadInstanceId(set, ref device);
            string containerId = ReadContainerId(set, ref device);
            bool isBluetoothDevice = !String.IsNullOrWhiteSpace(instanceId) && Regex.IsMatch(instanceId, @"(?i)^(?:BTHENUM|BTHLEDEVICE|BTHLE)");
            if (String.IsNullOrWhiteSpace(rawName)) rawName = isBluetoothDevice ? "Bluetooth device" : "Wireless device";
            string name = CleanName(rawName);
            if (String.IsNullOrWhiteSpace(name)) continue;
            if (Regex.IsMatch(instanceId ?? String.Empty, @"(?i)^ACPI\\PNP0C0A")) continue; // Skip the computer's internal battery.
            string category = GetCategory(name);
            string key = category + "|" + (containerId ?? (name + "|" + (instanceId ?? String.Empty)));
            if (!seen.Add(key)) continue;
            results.Add(new MonitorStatsPeripheralBatteryReading { Name = name, Category = category, ContainerId = containerId, IsBluetoothDevice = isBluetoothDevice, BatteryPercent = (int)value[0], BatteryLevel = null, Detail = "Windows-reported battery percentage" });
          }
        } catch { }
        finally { SetupDiDestroyDeviceInfoList(set); }
      }
    }
    var mouse = ReadG3(scanWindowsDevices);
    if (mouse != null) results.Add(mouse);

    // Check XInput connection state on every hardware sample so a wireless
    // controller can disappear even when its USB receiver stays connected.
    try {
      for (uint index = 0; index < 4; index++) {
        XInputBatteryInfo battery;
        if (XInputGetBatteryInformation(index, 0, out battery) != 0 || battery.Type == 0 || battery.Type == 1 || battery.Type == 0xFF) continue;
        string level = battery.Level == 0 ? "Empty" : battery.Level == 1 ? "Low" : battery.Level == 2 ? "Medium" : battery.Level == 3 ? "Full" : null;
        if (level == null) continue;
        bool alreadyReported = results.Exists(item => item.Category == "Controller" && Regex.IsMatch(item.Name, "(?i)xbox|controller"));
        if (alreadyReported) continue;
        results.Add(new MonitorStatsPeripheralBatteryReading { Name = "Xbox controller", Category = "Controller", ContainerId = null, IsBluetoothDevice = false, BatteryPercent = null, BatteryLevel = level, Detail = "Windows controller level; percentage unavailable" });
      }
    } catch { /* XInput is optional and may not be present on older systems. */ }
    return results.ToArray();
  }
}
