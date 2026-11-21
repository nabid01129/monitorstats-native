# MonitorStats Native

This separate Windows edition uses Windows Forms and in-process C# collectors. The Electron edition and its settings are separate.

## Install and use

Run **MonitorStats-Native-Setup-1.0.21-x64.exe** on 64-bit Windows. Start the dashboard from its Start menu or desktop shortcut. Its tray menu provides Show, Hide, Refresh, Settings, and Exit. Exit a running native edition from its tray menu before upgrading.

In Settings, choose the widget monitor, game monitor, placement, scaling, theme, polling interval, and OLED shifting interval. Configure your ONU address and credentials in the Network tab to enable optical power. Credentials are encrypted for your Windows account.

Choose **Full Black** in the Theme menu for black dashboard and card backgrounds. Settings always uses the standard light Windows appearance. OLED shifting applies to every dashboard theme.

## Readings and optional software

- Windows supplies CPU utilization, RAM details, drives, process memory, adapter traffic, and monitor information.
- MSI Afterburner supplies CPU/GPU temperatures, clocks, power, fan speeds, and other sensors when configured to expose those readings.
- RTSS supplies the selected foreground game's FPS and frame-time graph. It must be running and detecting the game.
- Fan Control is the fallback for fan readings unavailable from the primary sources. Its IPC component must be enabled and its fan sensors named.
- The 64-bit edition loads Fan Control's 64-bit IPC library directly. The optional 32-bit build can read its sensor service through the architecture-independent client.
- NVIDIA's management library can fill missing GPU readings when available. Afterburner is the primary source.
- Supported wireless devices display the battery reading Windows or their receiver exposes. The MCHOSE G3 has a dedicated receiver reader. Unsupported device battery values are omitted.
- Active resolver identification uses Akamai's DNS diagnostic record, reverse DNS, and IP geolocation. Router discovery uses the active gateway's UPnP service. Shared/Real IP classification requires both its WAN address and a public IPv4 reading.
- Uninstrumented system power is an estimate. Settings contains the component breakdown.

Windows 10/11 with .NET Framework 4.8 is required. Windows 11 normally includes this framework.

## Memory target

The target is under 100 MB of resident RAM for the native application. Memory depends on drivers, device inventory, desktop scale, and optional integrations. The application uses no hard memory cap or repeated working-set trimming.

Optional sensor applications have their own memory usage. The native collectors run inside the native process.

## Local settings

Settings are in `%LOCALAPPDATA%\MonitorStats Native`. The older Electron edition's settings are separate. Uninstalling either edition preserves the other edition's settings.

## Build from source

Run `powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1` from the repository. This builds x64 by default. Add `-Architecture x86` for a 32-bit build.

Add `-Installer -NsisPath "C:\Program Files (x86)\NSIS\makensis.exe"` to build an installer with NSIS 3. Run `powershell -NoProfile -ExecutionPolicy Bypass -File ./validate.ps1` to validate.

The shared directory contains the C# memory, battery, NVMe, RTSS, and Fan Control readers. Polling and rendering run directly in the native process.

## Open source and contributions

Licensed under MIT. Bug reports and pull requests are welcome. Include Windows version, app version, reproduction steps and optional redacted screenshots. Never attach settings, credentials, public IP addresses or private logs. Build and run validation before submitting changes.

## Updates and privacy

Native updates come only from [MonitorStats Native releases](https://github.com/nabid01129/monitorstats-native/releases). This is independent of MonitorStats Widget. Update checks contact GitHub; public IP and DNS identification contact external IP/geolocation services. ONU credentials stay encrypted in your Windows account. Do not include your local settings folder in a contribution.

## Wireless battery compatibility

Device detection is automatic across earbuds, headsets, speakers, keyboards, mice and controllers when Windows or a supported receiver exposes battery data. Bluetooth connection status is checked separately from stored battery values, so paired but disconnected devices are omitted. Battery values normally refresh every five minutes. XInput may supply a coarse level instead of a percentage. Proprietary receivers that do not expose a supported battery protocol cannot be measured universally; they require a device-specific reader. An attached dongle alone is not proof that its wireless device is connected.

## Update intervals

1000 ms means a one-second target for CPU, GPU, RAM and adapter traffic. A value need not change every sample, and the source software may update its own sensors less often. Processes refresh every five seconds, drive health every 30 seconds, battery values every five minutes and public IP/DNS/optical information normally every 30 minutes (with connection-change and retry handling). Slow wireless battery requests run separately from the fast hardware poller, inside the same process.

## Windows Update notice

A compact notice beside the Windows version shows pending software, optional preview and driver updates or a required restart, with a short update title. Click it to open Windows Update settings. The widget reads Windows Update Agent's cached catalog at startup and every 30 minutes in a background worker. It does not trigger an online update scan, download or installation. Availability reflects the most recent results detected by Windows.

## Sensor status and background polling

Settings > Sensors shows the sources of the latest readings, provider availability and sample time. Its Refresh status button reads existing snapshots only; it does not run another hardware or online scan. Missing readings remain omitted from the dashboard.

Settings > General offers **Reduce polling while minimized or hidden (10 seconds)**. It is off by default. When enabled, the hardware poller and clock timer run every 10 seconds while the widget is minimized or hidden. Showing the widget restores the selected hardware interval immediately. This reduces background work; it is not a RAM cap. Existing slower metadata schedules remain in effect.
