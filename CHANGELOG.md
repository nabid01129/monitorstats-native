# Native edition changes

## 1.0.24 (2026-11-22)

- Treat the G3 radio-off notification as sleep/power-off ambiguity rather than deleting the measured battery entry. Show a dimmed ~ percentage for last-known readings; fresh reports restore the normal display.
- Retain actual receiver readings across app/PC restarts in a local per-receiver cache. Expire cached measurements after seven days, reject malformed/future values, and clear the visible entry when the receiver is removed.
- Stop repeated configuration requests while the radio explicitly reports offline; the existing asynchronous input read listens for wake notifications.
- Prefer a connected G3 Bluetooth battery over the sleeping 2.4 GHz cache, avoiding duplicate entries.
- Add regression checks for sleep, long idle periods, cached/live display distinction, cache expiry, invalid data, and receiver removal.

## 1.0.23 (local build)

- Verify G3 identity once for each open receiver connection; subsequent status requests no longer depend on repeating the model exchange.
- Preserve the last real battery measurement across radio-off/sleep notifications, while continuing to hide offline devices. Restore a recent percentage on a wake notification without waiting for another percentage packet.
- Expire reused percentages by the battery measurement timestamp; connection notifications cannot keep old battery data alive indefinitely.
- Add regression checks for hidden offline readings, wake before a fresh battery packet, and stale battery expiration.
## 1.0.22 (local build)

- Clear the old five-minute request deadline when the G3 receiver reports radio offline, including sleep transitions.
- Retry missing battery responses after five seconds instead of treating a receiver acknowledgement as a fresh reading.
- Drain pending receiver messages before scheduling requests and accept fresh battery reports as wake evidence without requiring a separate reconnect event.
- Keep normal battery refresh at five minutes and hide offline readings. Add sleep/wake regression coverage.
## 1.0.21 (2026-11-21)

- Read primitive numeric telemetry directly instead of formatting and parsing it on every access, preserving native numeric precision and invariant parsing for text-based values.
- Reuse palette brushes during drawing and dispose them on theme changes or canvas close; reuse one text-formatting object for the canvas lifetime.
- Use numeric font-cache keys and cache the selected theme to reduce repeated string allocations and catalog lookups.
- Remove an unused text-fitting flag; keep the existing text fitting, layout, themes and polling behavior.
- Add tests for numeric precision, zero values, invalid/non-finite inputs, regional settings and bounded drawing resources; verify RTSS frame calculations, timestamp wrap and dead-feed handling.
- Verify identical rendered fixture pixels outside the wall-clock area and measure the numeric/rendering paths against version 1.0.20.

## 1.0.20 (local build)

- Keep custom names for temporarily disconnected drives when other settings are saved. Reset names still clears all custom names, and Cancel preserves the original configuration.
- Use the same automatic monitor fallback in Settings and the dashboard, preventing an unrelated settings save from switching monitors.
- Isolate network-adapter query failures so a changing or removed adapter cannot discard the rest of a hardware sample.
- Prevent nested Settings dialogs from overwriting each other's changes.
- Clean up drive labels in sensor diagnostics.
- Add regression checks for monitor fallback, disconnected-drive names, reset/cancel behavior, and failed/recovered network providers.

## 1.0.19 (local build)

- Add a light Sensors settings tab showing per-reading sources, missing-reading explanations, provider status and sample age from existing snapshots.
- Refresh diagnostics only when the tab is opened or its refresh button is clicked; do not add sensor scans or network requests.
- Add an opt-in background polling option: use a 10-second minimum interval when minimized or hidden, and restore the selected interval immediately when shown.
- Reduce clock timer wakeups while background polling is active; preserve normal foreground behavior and slower metadata schedules.
- Record source labels when Afterburner, Windows, NVIDIA or Fan Control actually supplies a value.
- Validate diagnostic source labels, unavailable values, settings persistence and polling policies alongside the existing regression suite.

## 1.0.18 (local build)

- Keep successful IPv4 and IPv6 readings independently across transient lookup failures; retry when IPv4 is missing rather than treating IPv6 alone as a complete lookup.
- Clear previous network details on connection changes as before.
- Make the full Windows Update summary clickable, and ignore right clicks on dashboard actions.
- Ensure in-flight telemetry collectors release their handles once on shutdown; guard display-change callbacks during exit.
- Correct Afterburner CPU-core and RAM names that were incorrectly prefixed as GPU sensors; use valid per-core clock/temperature readings when aggregate readings are unavailable.
- Cache frequently reused sensor-matching expressions and NVIDIA function bindings; skip RTSS reads when FPS display is disabled.
- Remove unused console polling loops and obsolete scroll state.
- Add network-failure and update-click regression checks, and verify rendered content stays within monitor height.
- Add an optional --review launch flag for inspecting the dashboard with desktop automation; normal window behavior is unchanged.

## 1.0.17 (local build)

- Include pending driver updates and optional preview updates in the cached Windows Update notice.
- Prioritize Windows/software update titles over optional driver titles in the compact summary.
- Keep update checks in the existing bounded background worker, without online scans or installation.

## 1.0.16 (local build)

- Show a compact pending Windows Update or restart notice beside the Windows version, with a short update title and remaining-update count.
- Open Windows Update settings when the notice is clicked.
- Read Windows' cached software-update catalog at startup and every 30 minutes in a bounded background worker; do not trigger online scans, downloads or installation.
- Hide the notice when no pending update or restart is reported.
- Add tests for update summaries, restart notices, all themes and click targets across OLED shifts.
- Host managed validation checks in Windows PowerShell, with architecture-matched test hosts.

## 1.0.15

- Release Afterburner shared-memory mappings marked DEAD or otherwise invalid instead of holding them indefinitely.
- Retry the sensor connection with a five-second backoff after a publisher restart or feed failure.
- Use the same disconnect path for failures and shutdown.
- Add an isolated dead-feed and recovered-feed integration test, alongside existing rendering, polling and battery checks.
- Preserve existing sensor priorities and avoid introducing additional sensor providers.

## 1.0.14

- Isolate slow wireless battery I/O from CPU, GPU, RAM and network collection using one bounded in-process worker.
- Preserve immutable battery snapshots and defer device-handle cleanup until the active read finishes.
- Replace the 250 ms polling wakeup loop with deadline-based, one-shot scheduling; skip missed deadlines without overlapping reads.
- Apply polling interval changes and manual refresh immediately.
- Label polling choices in seconds and milliseconds and explain the fixed refresh rates for slower data.
- Add tests for nonblocking reads, single in-flight work, safe shutdown, polling deadlines and formatted settings.
- Retain the rendering crash fix from 1.0.13.

## 1.0.13

- Fix the repeated rendering crash when FPS data exists but the game process name is unavailable.
- Keep explicit references to dynamic text instead of indexing optional rows.
- Show an unavailable-process label while retaining valid FPS data.
- Add regression coverage reproducing the original exception and 200 render transitions with missing names, game exits and telemetry changes.
- Preserve existing sensor-source priorities and behavior.

## 1.0.12

- Keep battery icons in a stable order by device type and name, independent of discovery timing.
- Add a regression check using reversed mouse and earbuds discovery results.

## 1.0.11 (local build)

- Use native Windows device-container connection state for Bluetooth LE devices, including mice and keyboards.
- Remove timed-out endpoint discovery from the periodic connection check.
- Preserve Bluetooth Classic audio matching and supported 2.4 GHz receiver readers.
- Validate native structure alignment for device connection properties.

## 1.0.10 (local build)

- Restore connected Bluetooth audio battery display using Windows' native connection state.
- Correct Bluetooth endpoint selectors and read-only Windows property dictionaries.
- Automatically match battery reports for all device categories rather than a model allowlist.
- Recheck classic Bluetooth connections every five seconds and on device events; cache slower endpoint queries.
- Keep battery-value refresh at five minutes, with discovery on device changes and periodic fallback.
- Preserve the dedicated G3 receiver reader and standard XInput battery levels.
- Add generic category connection and disconnection regression checks.

## 1.0.9 (local build)

- Restore mouse connection state from a fresh, verified receiver battery report when a separate reconnect event is missed.
- Retry initial receiver identification every five seconds until a valid battery reading arrives; normal battery refresh remains five minutes.
- Reset battery timestamps when the receiver handle is closed.
- Add tests for disconnect, battery-based reconnect and invalid percentages.

## 1.0.8 (local build)

- Keep the complete dashboard within the selected monitor without scrolling.
- Apply UI scale to text independently of card layout and monitor fitting.
- Fit long text within its allotted width and height to avoid overlap.
- Verify zero scrolling at 70%, 100% and 130% across portrait and landscape layouts.

## 1.0.7 (local build)

- Fix automatic monitor fitting cancelling the selected UI scale.
- Initially apply user zoom after monitor fitting; this intermediate scrolling behavior was superseded by the no-scroll layout in 1.0.8.
- Add regression coverage proving 120% zoom enlarges the layout with automatic scaling enabled.

## 1.0.6 (local review build)

- Preserve ONU refresh deadlines when saving unrelated appearance settings.
- Clear old optical readings when ONU credentials change or are removed.
- Avoid a full hardware inventory rescan after appearance-only settings changes.
- Do not apply new in-memory settings after a save error.
- Handle failure to open the release page without an unhandled UI exception.
- Add regression coverage for ONU refresh isolation and credential changes.
- Visually inspect the live dashboard and all five Settings tabs.

## 1.0.5

- Fix the collapsed component breakdown in Power estimate settings.
- Keep Settings light under every dashboard theme.
- Point update checks and release links to the separate MonitorStats Native project.
- Compare native release versions numerically against the installed native version.
- Add settings-layout and update-comparison regression checks.
- Package standalone build sources, without requiring the Electron project.

## 1.0.4

- Restore the standard light Windows appearance for Settings, independently of the dashboard theme.

## 1.0.3

- Add the x64 edition and make x64 the default build architecture.
- Load 64-bit sensor libraries directly in the x64 edition.
- Show the application architecture beside its version in settings.
- Retain an optional x86 build and architecture-specific validation.

## 1.0.2

- Add Full Black with black dashboard, card, toolbar, and settings backgrounds.
- Correct large GPU VRAM totals in the 32-bit app using the driver's 64-bit memory record.
- Restore CPU and case fan readings from 64-bit Fan Control through its sensor service.
- Fix card spacing, text fitting, scroll limits, settings sizing, and IP visibility button coordinates during OLED shifts.
- Reuse dashboard card models between sensor samples and bound the font cache during resizing.
- Prevent stale network requests from overwriting refresh deadlines after a connection change.
- Preserve other settings if a saved ONU password cannot be decrypted.
- Restore the existing window when the application is launched again.

## 1.0.1

- Restore green Real IP and red Shared IP badges.
- Read the Windows edition from the 64-bit registry view.

## 1.0.0

- Introduce the separate native Windows edition.
