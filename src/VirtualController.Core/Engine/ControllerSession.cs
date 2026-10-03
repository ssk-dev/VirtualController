using VirtualController.Core.Devices;
using VirtualController.Core.Mapping;
using VirtualController.Core.Timing;
using VirtualController.Core.Virtual;

namespace VirtualController.Core.Engine;

/// <summary>
/// Coordinates one virtual controller: keeps required physical device readers open, polls them in a dedicated
/// high-frequency loop, applies the profile's mapping table, and sends the result to the virtual controller.
/// One instance corresponds to one entry in the UI controller list.
/// </summary>
public sealed class ControllerSession : IDisposable
{
    private readonly Dictionary<string, IDeviceReader> _readers = new();
    private readonly Dictionary<string, DeviceState> _latestStates = new();
    private readonly VirtualPadState _padState = new();
    private readonly IVirtualPad _pad;
    private readonly PrecisionLoop _loop;
    private readonly object _profileLock = new();
    private IReadOnlyDictionary<string, DeviceSettings> _deviceSettings;

    /// <summary>Last evaluated state for each switch trigger (controller toggle trigger and per-mode switch
    /// triggers), used to detect a rising edge (false -> true) between ticks. The stateless
    /// <see cref="Mapping.MappingEngine.IsPhysicalInputActive"/> provides only the current value. Key = (DeviceId, Kind, Index).</summary>
    private readonly Dictionary<(string DeviceId, PhysicalInputKind Kind, int Index), bool> _triggerWasActive = new();

    public VirtualControllerProfile Profile { get; private set; }

    /// <summary>Most recent loop-thread error (e.g. driver not installed), shown in the UI.</summary>
    public Exception? LastError { get; private set; }

    public event Action<Exception>? Faulted;

    public ControllerSession(
        VirtualControllerProfile profile,
        IVirtualPad pad,
        IReadOnlyList<PhysicalDeviceInfo> availableDevices,
        IReadOnlyDictionary<string, DeviceSettings>? deviceSettings = null)
    {
        Profile = profile;
        _pad = pad;
        _deviceSettings = deviceSettings ?? new Dictionary<string, DeviceSettings>();

        OpenRequiredReaders(availableDevices);
        _loop = new PrecisionLoop($"VCtrl-{profile.Name}", profile.PollingRateHz, Tick);
    }

    public void Start()
    {
        _pad.Connect();
        _loop.Start();
    }

    public void Stop()
    {
        _loop.Stop();
        _pad.Disconnect();
    }

    /// <summary>Thread-safely replaces the profile, e.g. after a mapping table change in the UI.</summary>
    public void UpdateProfile(VirtualControllerProfile profile, IReadOnlyList<PhysicalDeviceInfo> availableDevices)
    {
        lock (_profileLock)
        {
            Profile = profile;
            OpenRequiredReaders(availableDevices);
        }
    }

    /// <summary>Updates device-wide settings (including individually disabled inputs) without requiring a
    /// mapping profile change, e.g. after a change in the configuration dialog.</summary>
    public void UpdateDeviceSettings(IReadOnlyDictionary<string, DeviceSettings> deviceSettings)
    {
        lock (_profileLock)
        {
            _deviceSettings = deviceSettings;
        }
    }

    /// <summary>Physical devices (by <see cref="PhysicalDeviceInfo.DeviceId"/>) actually needed by this session,
    /// matching the set used internally by <see cref="OpenRequiredReaders"/> to determine which readers stay
    /// open. <see cref="ControllerManager"/> uses this to block those devices through HidHide while the session
    /// runs (see <see cref="Devices.HidHideController"/>).</summary>
    public IReadOnlyCollection<string> NeededDeviceIds => ComputeNeededDeviceIds();

    /// <summary>Finds physical devices referenced as mapping sources in any mode, not only the active one, plus
    /// devices used by toggle/switch triggers. Keeping readers open independently of the active mode avoids
    /// reopening readers and changing HidHide locks on every mode switch.</summary>
    private HashSet<string> ComputeNeededDeviceIds()
    {
        return Profile.Modes
            .SelectMany(mode => mode.Mappings)
            .Select(m => m.SourceDeviceId)
            .Concat(Profile.Modes
                .Where(mode => mode.SwitchTrigger is not null)
                .Select(mode => mode.SwitchTrigger!.DeviceId))
            .Concat(Profile.ToggleTrigger is not null
                ? new[] { Profile.ToggleTrigger.DeviceId }
                : Array.Empty<string>())
            .Distinct()
            .ToHashSet();
    }

    private void OpenRequiredReaders(IReadOnlyList<PhysicalDeviceInfo> availableDevices)
    {
        // Keep readers open for devices needed by any mode, not just the active one, to avoid reopening them
        // on each runtime mode switch. Also keep devices used by toggle/switch triggers open so edge detection
        // works independently of the active mode.
        var neededDeviceIds = ComputeNeededDeviceIds();
        var byId = availableDevices.ToDictionary(d => d.DeviceId);

        // Close readers that are no longer needed.
        foreach (var existingId in _readers.Keys.Where(id => !neededDeviceIds.Contains(id)).ToList())
        {
            _readers[existingId].Dispose();
            _readers.Remove(existingId);
            _latestStates.Remove(existingId);
        }

        // Open readers for newly mapped devices.
        foreach (var deviceId in neededDeviceIds)
        {
            if (_readers.ContainsKey(deviceId) || !byId.TryGetValue(deviceId, out var info))
            {
                continue;
            }

            _readers[deviceId] = DeviceEnumerator.OpenReader(info);
            _latestStates[deviceId] = DeviceState.Empty(info.ButtonCount);
        }
    }

    /// <summary>
    /// Evaluates the configured mode switch mechanism (<see cref="VirtualControllerProfile.ModeSwitchMechanism"/>)
    /// using the latest device states and updates <see cref="VirtualControllerProfile.ActiveModeId"/> as needed.
    /// Uses rising-edge detection through <see cref="_triggerWasActive"/> because
    /// <see cref="MappingEngine.IsPhysicalInputActive"/> is stateless; otherwise a held input would switch modes
    /// on every tick (or continuously advance in toggle mode).
    /// </summary>
    private void EvaluateModeSwitching()
    {
        switch (Profile.ModeSwitchMechanism)
        {
            case ModeSwitchMechanism.Toggle:
                if (Profile.ToggleTrigger is { } toggleTrigger && UpdateTriggerEdgeAndCheckRising(toggleTrigger))
                {
                    AdvanceToNextEnabledMode();
                }
                break;

            case ModeSwitchMechanism.Switch:
                foreach (var mode in Profile.Modes)
                {
                    if (mode.Enabled && mode.SwitchTrigger is { } switchTrigger && UpdateTriggerEdgeAndCheckRising(switchTrigger))
                    {
                        Profile.ActiveModeId = mode.Id;
                    }
                }
                break;
        }
    }

    /// <summary>Advances <see cref="VirtualControllerProfile.ActiveModeId"/> cyclically to the next enabled mode
    /// in <see cref="VirtualControllerProfile.Modes"/> order. Does nothing if no mode is enabled.</summary>
    private void AdvanceToNextEnabledMode()
    {
        var enabledModes = Profile.Modes.Where(m => m.Enabled).ToList();
        if (enabledModes.Count == 0)
        {
            return;
        }

        int currentIndex = enabledModes.FindIndex(m => m.Id == Profile.ActiveModeId);
        int nextIndex = (currentIndex + 1) % enabledModes.Count;
        Profile.ActiveModeId = enabledModes[nextIndex].Id;
    }

    private bool UpdateTriggerEdgeAndCheckRising(PhysicalInputTrigger trigger)
    {
        var key = (trigger.DeviceId, trigger.Kind, trigger.Index);
        bool wasActive = _triggerWasActive.TryGetValue(key, out var previous) && previous;
        bool isActive = MappingEngine.IsPhysicalInputActive(trigger, _latestStates, _deviceSettings);
        _triggerWasActive[key] = isActive;
        return isActive && !wasActive;
    }

    private void Tick()
    {
        try
        {
            lock (_profileLock)
            {
                foreach (var (deviceId, reader) in _readers)
                {
                    if (reader.Poll(out var state))
                    {
                        _latestStates[deviceId] = state;
                    }
                }

                EvaluateModeSwitching();

                MappingEngine.Apply(Profile, _latestStates, _padState, _deviceSettings);
            }

            _pad.Submit(_padState);
        }
        catch (Exception ex)
        {
            LastError = ex;
            Faulted?.Invoke(ex);
        }
    }

    public void Dispose()
    {
        Stop();
        _loop.Dispose();
        foreach (var reader in _readers.Values)
        {
            reader.Dispose();
        }
        _pad.Dispose();
    }
}
