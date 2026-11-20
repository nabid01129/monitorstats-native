# Native edition changes

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
