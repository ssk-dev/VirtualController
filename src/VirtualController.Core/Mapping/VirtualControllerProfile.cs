using System.Text.Json.Serialization;
using VirtualController.Core.Devices;
using VirtualController.Core.Virtual;

namespace VirtualController.Core.Mapping;

/// <summary>Target of one mapping entry: which virtual controller element the physical input affects.</summary>
public enum MappingTargetKind
{
    Button,
    Axis,
    Trigger,
    DPad
}

/// <summary>
/// Defines how to switch between a virtual controller's modes (see
/// <see cref="VirtualControllerProfile.ModeSwitchMechanism"/>). The mechanisms are mutually exclusive per
/// controller, but both are supported and selectable in the UI.
/// </summary>
public enum ModeSwitchMechanism
{
    /// <summary>A single controller-wide trigger (<see cref="VirtualControllerProfile.ToggleTrigger"/>) advances
    /// to the next enabled mode on each rising edge, cycling back to the beginning.</summary>
    Toggle,

    /// <summary>Each mode can have its own trigger (<see cref="ControllerMode.SwitchTrigger"/>); a rising edge
    /// activates that mode directly. A trigger can be used by only one mode per controller.</summary>
    Switch
}

/// <summary>
/// Reference to a physical input used to switch modes, either by cycling/toggling or by activating a specific
/// mode directly. Separate from <see cref="MappingEntry"/> because this evaluates an event (rising edge) and
/// has no virtual controller target.
/// </summary>
public sealed class PhysicalInputTrigger
{
    public required string DeviceId { get; set; }
    public required PhysicalInputKind Kind { get; set; }
    public required int Index { get; set; }
}

/// <summary>
/// One mapping entry: a physical input (button/axis/D-pad on a connected controller) controls a virtual
/// controller element. Multiple physical inputs, including those from different devices, can map to the same
/// virtual target (e.g. two controllers sharing Start).
/// </summary>
public sealed class MappingEntry
{
    /// <summary>Source: physical device and button/axis/D-pad index.</summary>
    public required string SourceDeviceId { get; set; }
    public required PhysicalInputKind SourceKind { get; set; }
    public required int SourceIndex { get; set; }

    /// <summary>Target: virtual controller element to activate.</summary>
    public required MappingTargetKind TargetKind { get; set; }
    public VirtualButton? TargetButton { get; set; }
    public VirtualAxis? TargetAxis { get; set; }
    public VirtualTrigger? TargetTrigger { get; set; }
    public DPadDirection? TargetDPadDirection { get; set; }

    /// <summary>For axes: invert the deflection direction.</summary>
    public bool Invert { get; set; }

    /// <summary>
    /// For axis targets: when true, uses only the half of the physical source axis specified by
    /// <see cref="SourceKind"/> (e.g. Y+) and normalizes it to 0..1 as the target axis value; sign remains
    /// independently controlled by <see cref="Invert"/>. This allows physical halves to map to the same or
    /// different virtual axes. When false (the default, including older profiles without this field), passes
    /// through the full bidirectional source range.
    /// </summary>
    public bool DirectionalOnly { get; set; }

    /// <summary>Display name for the UI table, e.g. "Controller 1 - Button A" -> "South".</summary>
    public string? Description { get; set; }
}

/// <summary>
/// One user-named mode of a virtual controller (e.g. "Flight", "Racing") with its own independent mapping
/// table. A virtual controller can have several modes, but only one is active at a time
/// (<see cref="VirtualControllerProfile.ActiveModeId"/>). A mode must be created before mappings can be added.
/// </summary>
public sealed class ControllerMode
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Only enabled modes can become active (see the toggle/switch mechanism in
    /// <see cref="VirtualControllerProfile"/>); disabled modes are skipped when switching.</summary>
    public bool Enabled { get; set; } = true;

    [JsonConverter(typeof(MappingEntryListConverter))]
    public List<MappingEntry> Mappings { get; set; } = new();

    /// <summary>Used only when <see cref="VirtualControllerProfile.ModeSwitchMechanism"/> is
    /// <see cref="Mapping.ModeSwitchMechanism.Switch"/>: physical input whose rising edge activates this mode.
    /// Must be unique within the controller (checked against other modes during assignment).</summary>
    public PhysicalInputTrigger? SwitchTrigger { get; set; }
}

/// <summary>
/// One virtual controller and its complete mapping table (physical controllers/inputs that affect it).
/// </summary>
public sealed class VirtualControllerProfile
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public required ControllerLayout Layout { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Target polling/submission loop rate in Hz (e.g. 1000).</summary>
    public int PollingRateHz { get; set; } = 1000;

    /// <summary>All modes for this virtual controller (see <see cref="ControllerMode"/>). A newly created
    /// controller starts without a mode; one must be created before mappings can be captured or assigned.</summary>
    public List<ControllerMode> Modes { get; set; } = new();

    /// <summary>ID of the active mode (see <see cref="ActiveMode"/>), or null when no mode has been created or activated.</summary>
    public Guid? ActiveModeId { get; set; }

    /// <summary>Defines the mechanism used to switch between modes; see <see cref="Mapping.ModeSwitchMechanism"/>.</summary>
    public ModeSwitchMechanism ModeSwitchMechanism { get; set; } = ModeSwitchMechanism.Toggle;

    /// <summary>Used only with <see cref="ModeSwitchMechanism.Toggle"/>: controller-wide trigger that advances
    /// to the next enabled mode on each rising edge, cycling back to the beginning.</summary>
    public PhysicalInputTrigger? ToggleTrigger { get; set; }

    /// <summary>Whether to show a brief on-screen notification with the controller/mode names whenever the
    /// active mode changes, either by tab click while stopped or by a toggle/switch trigger while running.</summary>
    public bool NotifyOnModeChange { get; set; }

    /// <summary>Whether to block this controller's assigned physical devices from other applications through
    /// HidHide while the virtual controller is running (see <see cref="Engine.ControllerSession.NeededDeviceIds"/>
    /// and <see cref="Devices.HidHideController"/>). This prevents a game from reacting to both the physical
    /// device and its virtual counterpart. Opt-in (disabled by default) because HidHide is a separate driver.
    /// Has no effect unless HidHide is installed and ready (see <see cref="Devices.HidHideController.IsAvailable"/>).</summary>
    public bool HidHideEnabled { get; set; }

    /// <summary>Whether to start this virtual controller automatically when the program at
    /// <see cref="AutoStartExecutablePath"/> starts, and stop it when the program exits (see the corresponding
    /// checkbox beside Start/Stop in <see cref="Views.MainWindow"/>). Evaluated through periodic polling in
    /// <c>MainViewModel</c>. Opt-in (disabled by default); has no effect until an executable path is set.</summary>
    public bool AutoStartEnabled { get; set; }

    /// <summary>Full path to the .exe monitored for automatic controller start/stop (see
    /// <see cref="AutoStartEnabled"/>). Stores the full path rather than the filename to distinguish programs
    /// with the same name in different locations. Set by the user through a file picker; null/empty until selected.</summary>
    public string? AutoStartExecutablePath { get; set; }

    /// <summary>The active mode, or null if none has been created/activated or <see cref="ActiveModeId"/> no
    /// longer refers to an existing mode.</summary>
    public ControllerMode? ActiveMode => ActiveModeId is { } id ? Modes.FirstOrDefault(m => m.Id == id) : null;

    /// <summary>
    /// Physical devices assigned to this virtual controller, identified by
    /// <see cref="Devices.PhysicalDeviceInfo.DeviceId"/>. Restricts Capture and active mappings to these devices.
    /// Empty means all connected devices are included (default).
    /// </summary>
    public List<string> AssignedDeviceIds { get; set; } = new();

    /// <summary>Actual ViGEmBus backend, derived from the layout.</summary>
    public VirtualBackend Backend => LayoutBackendMap.Resolve(Layout);
}

/// <summary>Complete saved configuration: all defined virtual controllers.</summary>
public sealed class AppProfile
{
    public List<VirtualControllerProfile> Controllers { get; set; } = new();

    /// <summary>
    /// User-defined display names for physical inputs (e.g. "Sniper button" instead of "Button 4"), keyed
    /// by "{DeviceId}|{PhysicalInputKind}|{Index}". Valid across virtual controllers. Deprecated: used only
    /// to load older profiles and automatically migrated to <see cref="DeviceSettings"/> during loading
    /// (see <see cref="Profiles.ProfileStore.Load"/>).
    /// </summary>
    public Dictionary<string, string> CustomInputNames { get; set; } = new();

    /// <summary>
    /// Settings per physical device (device enabled state and per-input custom name, enabled state, calibration,
    /// deadzone, and response curve), keyed by <see cref="PhysicalDeviceInfo.DeviceId"/>. Shared across virtual
    /// controllers.
    /// </summary>
    public Dictionary<string, DeviceSettings> DeviceSettings { get; set; } = new();

    /// <summary>Whether newly connected/disconnected physical devices should be detected automatically through
    /// background polling (see MainViewModel), without restarting the app or manually clicking Refresh devices.
    /// Can be disabled on the Settings tab to avoid periodic device scans and their background work. Enabled by default.</summary>
    public bool AutoDeviceDetectionEnabled { get; set; } = true;

    /// <summary>Whether the application starts automatically when signing in to Windows.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Whether the main window starts minimized when launched with Windows.</summary>
    public bool StartMinimized { get; set; }

    /// <summary>Whether the main window stays on top. Enabled by default.</summary>
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>UI language selection. Supported values are "system", "en", and "de". "system" uses the
    /// current OS language when the app starts.</summary>
    public string UiLanguage { get; set; } = "system";
}
