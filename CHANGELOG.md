# Native edition changes

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
