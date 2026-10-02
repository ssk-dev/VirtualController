# Virtual Controller

Windows tool for creating multiple virtual game controllers (Xbox 360 / DualShock 4 as the
Windows backend, the "Nintendo" layout is purely cosmetic), onto which any connected
physical controller can be freely mapped - inspired by x360ce, but as standalone, system-wide
visible devices (no per-game DLL hijacking).

## Features

- **Multiple independent virtual controllers at the same time**: each with its own name,
  layout, polling rate and completely independent mapping table.
- **Free mapping**: any buttons/axes/D-Pad directions of any physical source controller
  (even across devices, e.g. two controllers sharing "Start") can be mapped to any
  buttons/axes/triggers/D-Pad directions of the virtual target controller. Axes can be
  inverted or split into two independent halves.
- **Two input APIs**: XInput (Xbox-compatible controllers, up to 4 devices, very low
  latency) and DirectInput (generic HID joysticks/gamepads, PlayStation controllers,
  older devices) - both usable at the same time.
- **Two target backends via ViGEmBus**: Xbox 360 (XInput) and DualShock 4 (HID). The
  display layout ("Xbox", "PlayStation", "Nintendo") is purely cosmetic and independent of
  this, see [Known limitation: layouts](#known-limitation-layouts).
- **Modes per virtual controller**: any number of freely named modes (e.g. "Flight mode",
  "Racing") each with its own, independent mapping table. Switching either via a single
  toggle trigger (cycles through modes) or via a dedicated switch trigger per mode
  (activates that mode directly). Optionally with a short on-screen notification on every
  switch.
- **Precise axis processing**: calibration wizard for value range, center point and
  deadzone (including automatic stick-drift detection), as well as a selectable response
  curve (linear, exponential, S-curve) per physical axis.
- **1000 Hz precision loop**: dedicated high-resolution timer per virtual controller
  (sub-millisecond accuracy on Windows 10 1809+, otherwise automatic spin-wait fallback),
  purely in user mode, without additional admin rights.
- **Automatic hotplug detection**: newly connected or disconnected physical devices are
  detected via background polling, without having to restart the app or refresh manually
  (can be disabled).
- **Device/input management**: rename, disable or completely hide individual devices or
  individual inputs from all selection lists (e.g. for irrelevant devices) - independent of
  which virtual controller they are currently assigned to.
- **Optional HidHide integration**: locks the physical devices assigned to a virtual
  controller for all other applications while it is running, so that e.g. a game does not
  react to both the physical AND the derived virtual device at the same time. Requires the
  separately installed [HidHide](https://github.com/nefarius/HidHide) driver; the
  corresponding checkbox is greyed out as long as it is not installed.
- **Tray icon**: keeps running in the background if needed (closing the window minimizes it
  to the tray, always reachable again via the tray icon).
- **Persistence**: all virtual controllers, mapping tables and device settings are
  automatically saved as JSON under `%AppData%\VirtualController\` - split into one file per
  virtual controller (`Controllers\controller-{name}.json`), one file per physical device
  (`Devices\device-{brand}-{name}.json`, e.g. `device-logitech-x56.json`) and a small
  `settings.json` for general settings. An old, combined `profiles.json` still present from
  an earlier version is automatically split into this format on first startup and renamed to
  `profiles.json.migrated`.

## Prerequisites (one-time, on the target machine)

1. **Windows 10, version 1809 or newer** (x64). On older versions, the app automatically
   falls back to a spin-wait fallback for the precision loop; ViGEmBus itself also supports
   Windows 7/8.1.
2. **ViGEmBus driver** (kernel-mode virtual USB bus, digitally signed, ~300 KB,
   no restart required): https://github.com/nefarius/ViGEmBus/releases
   (available in the repo as a local copy under `driver/ViGEmBus_1.22.0_x64_x86_arm64.exe`).
   - Without this driver, the app reports "ViGEmBus not available" on startup and cannot
     create virtual controllers (the app itself does not need admin rights for this, only
     the one-time driver installation does).
3. **Optional: HidHide driver**, only needed if the "lock physical devices while running"
   feature is to be used: https://github.com/nefarius/HidHide/releases.
   Without this driver the app works completely normally, the corresponding checkbox is
   simply greyed out.
4. **.NET 8 SDK/Runtime only needed when building from source yourself** (see
   [Building](#building-from-source)). The ready-made release download (see below) is
   "self-contained" and already includes the .NET runtime - nothing additional needs to be
   installed.

## Installation (recommended: ready-made download)

1. Download the latest release: [Releases page](../../releases) - get the ZIP archive
   `VirtualController-win-x64.zip` there (contains the ready-to-use `VirtualController.exe`
   as well as all native WPF DLLs required for it - **all files belong together in one
   folder**, the main EXE alone is not enough to start it).
2. Extract the ZIP to any location.
3. Install the [prerequisites](#prerequisites-one-time-on-the-target-machine) (at least
   ViGEmBus), if not already present.
4. Start `VirtualController.exe`.

## Building from source

```
dotnet restore
dotnet build -c Release
```

## Running (from source)

```
dotnet run --project src/VirtualController.App -c Release
```

Or start the generated `VirtualController.exe` directly under
`src/VirtualController.App/bin/Release/net8.0-windows/`.

## Creating a release (for maintainers)

Pushing a Git tag in the format `v<Major>.<Minor>.<Patch>` (e.g. `v1.0.0`) automatically
triggers the GitHub Actions workflow `.github/workflows/release.yml`: this builds the
self-contained `win-x64` EXE of the main application, packages it together with the required
native WPF DLLs into a ZIP, and publishes it as a new GitHub release.

```
git tag v1.0.0
git push origin v1.0.0
```

Alternatively, the workflow can also be triggered manually via the "Actions" tab on GitHub
("Run workflow"), e.g. for testing without creating a tag for it.

## Architecture overview

- **VirtualController.Core** - pure logic, no UI:
  - `Virtual/` ViGEmBus wrapper (Xbox360/DualShock4), layout definitions
  - `Devices/` XInput and DirectInput reading of physical controllers, device detection,
    input capture, calibration, optional HidHide integration
  - `Mapping/` profile model (virtual controller + modes + mapping table) and the mapping engine
  - `Timing/` high-resolution loop (up to 1000 Hz+), purely user mode, no driver needed
  - `Engine/` connects everything into running sessions per virtual controller
  - `Profiles/` JSON persistence under `%AppData%\VirtualController\` - split into
    `Controllers\controller-{name}.json` (per virtual controller), `Devices\device-{brand}-{name}.json`
    (per physical device) and `settings.json` (general settings); automatically migrates an old,
    combined `profiles.json` still present on first startup into this format
- **VirtualController.App** - WPF UI (MVVM via CommunityToolkit.Mvvm), tray icon. Performs
  in-app updates via a hidden, generated PowerShell process (see
  `VirtualController.Core.Updates.UpdateInstaller`): this waits for the application process to
  end after it closes, copies the new files, and then restarts the application.

## Known limitation: layouts

Windows/ViGEmBus technically only knows two virtual target devices: **Xbox 360** (XInput) and
**DualShock 4** (HID). A native "Nintendo Switch Pro Controller" target does not exist. The
"Nintendo" layout in the app is therefore purely cosmetic (button labeling) and internally
uses the Xbox360 backend.

