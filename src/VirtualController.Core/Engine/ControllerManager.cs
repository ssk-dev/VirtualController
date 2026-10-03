using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using VirtualController.Core.Devices;
using VirtualController.Core.Mapping;
using VirtualController.Core.Virtual;

namespace VirtualController.Core.Engine;

/// <summary>
/// Central manager for all virtual controllers in the application. Holds one <see cref="ViGEmClient"/>
/// connection to the ViGEmBus driver (one per process is sufficient and recommended) and one running
/// <see cref="ControllerSession"/> per configured virtual controller.
/// </summary>
public sealed class ControllerManager : IDisposable
{
    private readonly Dictionary<Guid, ControllerSession> _sessions = new();
    private readonly HidHideController _hidHide = new();

    /// <summary>Tracks the PnP instance IDs currently blocked through HidHide for each profile (see
    /// <see cref="ResolveHidHideInstanceIds"/>), so the same IDs can be unlocked when a controller is removed
    /// or updated. <see cref="HidHideController.Unlock"/> uses reference counting; see its documentation.</summary>
    private readonly Dictionary<Guid, HashSet<string>> _hidHideLockedInstanceIds = new();

    public event Action<Guid, Exception>? SessionFaulted;

    /// <summary>Raised with a user-facing warning when a HidHide situation should be shown but is not fatal,
    /// e.g. orphaned locks from a previous crash were cleaned up or HidHide is enabled in a profile but the
    /// driver is unavailable. Provides UI feedback for automatic cleanup (see <see cref="MainViewModel"/>).</summary>
    public event Action<string>? HidHideWarning;

    /// <summary>Whether the HidHide driver is installed and ready; used to disable the UI checkbox for blocking
    /// physical devices while this controller runs.</summary>
    public bool IsHidHideAvailable => _hidHide.IsAvailable;

    /// <summary>
    /// Initializes the connection to the ViGEmBus driver. Must succeed before virtual controllers can be created.
    /// </summary>
    /// <exception cref="VigemBusNotFoundException">ViGEmBus is not installed on this machine.</exception>
    public void Initialize()
    {
        _client ??= new ViGEmClient();

        // Once at startup, check for HidHide locks left by a previous crashed run (normal shutdown removes
        // them through Unlock()) and clean them up so physical devices are not incorrectly blocked from
        // other applications indefinitely.
        var orphaned = _hidHide.CleanupOrphanedLocks();
        if (orphaned.Count > 0)
        {
            HidHideWarning?.Invoke(
                $"HidHide: automatically removed {orphaned.Count} orphaned device lock(s) left by a previous unclean shutdown.");
        }
    }

    public IReadOnlyList<PhysicalDeviceInfo> GetAvailablePhysicalDevices() => DeviceEnumerator.EnumerateAll();

    public ControllerSession AddController(VirtualControllerProfile profile, IReadOnlyDictionary<string, DeviceSettings>? deviceSettings = null)
    {
        if (_client is null)
        {
            throw new InvalidOperationException($"{nameof(ControllerManager)}.{nameof(Initialize)}() must be called first.");
        }

        // Prevent a device leak: if this profile already has a session (e.g. Start was invoked again without
        // Stop after a layout change), end the old session and disconnect it from ViGEmBus before replacing
        // the dictionary entry. Otherwise the old virtual controller would remain registered in Windows
        // until the process exits.
        RemoveController(profile.Id);

        var pad = VirtualPadFactory.Create(_client, profile.Backend);
        var session = new ControllerSession(profile, pad, GetAvailablePhysicalDevices(), deviceSettings);
        session.Faulted += ex => SessionFaulted?.Invoke(profile.Id, ex);

        _sessions[profile.Id] = session;
        session.Start();

        var instanceIds = ResolveHidHideInstanceIds(profile, session);
        _hidHide.Lock(instanceIds);
        _hidHideLockedInstanceIds[profile.Id] = instanceIds;

        return session;
    }

    public void RemoveController(Guid profileId)
    {
        if (_hidHideLockedInstanceIds.Remove(profileId, out var lockedInstanceIds))
        {
            _hidHide.Unlock(lockedInstanceIds);
        }

        if (_sessions.Remove(profileId, out var session))
        {
            session.Dispose();
        }
    }

    public void UpdateController(VirtualControllerProfile profile, IReadOnlyDictionary<string, DeviceSettings>? deviceSettings = null)
    {
        if (_sessions.TryGetValue(profile.Id, out var session))
        {
            session.UpdateProfile(profile, GetAvailablePhysicalDevices());
            if (deviceSettings is not null)
            {
                session.UpdateDeviceSettings(deviceSettings);
            }

            // Update HidHide locks by diff so profile changes (e.g. assigned devices or HidHideEnabled) take
            // effect immediately without restarting the controller.
            var newInstanceIds = ResolveHidHideInstanceIds(profile, session);
            _hidHideLockedInstanceIds.TryGetValue(profile.Id, out var previousInstanceIds);
            previousInstanceIds ??= new HashSet<string>();

            var noLongerNeeded = previousInstanceIds.Where(id => !newInstanceIds.Contains(id)).ToList();
            var newlyNeeded = newInstanceIds.Where(id => !previousInstanceIds.Contains(id)).ToList();

            _hidHide.Unlock(noLongerNeeded);
            _hidHide.Lock(newlyNeeded);
            _hidHideLockedInstanceIds[profile.Id] = newInstanceIds;
        }
    }

    /// <summary>Resolves the PnP instance IDs to block through HidHide based on
    /// <see cref="VirtualControllerProfile.HidHideEnabled"/> and the physical devices actually needed by
    /// <paramref name="session"/> (<see cref="ControllerSession.NeededDeviceIds"/>). Returns an empty set when
    /// the option is disabled or HidHide is unavailable.</summary>
    private HashSet<string> ResolveHidHideInstanceIds(VirtualControllerProfile profile, ControllerSession session)
    {
        if (!profile.HidHideEnabled)
        {
            return new HashSet<string>();
        }

        if (!_hidHide.IsAvailable)
        {
            HidHideWarning?.Invoke(
                $"HidHide is enabled for \"{profile.Name}\", but the HidHide driver is unavailable. Physical devices will NOT be blocked.");
            return new HashSet<string>();
        }

        var availableDevices = GetAvailablePhysicalDevices().ToDictionary(d => d.DeviceId);
        var result = new HashSet<string>();

        foreach (var deviceId in session.NeededDeviceIds)
        {
            if (!availableDevices.TryGetValue(deviceId, out var device))
            {
                continue;
            }

            foreach (var instanceId in _hidHide.ResolveInstanceIds(device))
            {
                result.Add(instanceId);
            }
        }

        return result;
    }

    /// <summary>Immediately broadcasts changed device-wide settings (e.g. an input disabled in the configuration
    /// dialog) to all running sessions without requiring a full mapping profile update.</summary>
    public void BroadcastDeviceSettings(IReadOnlyDictionary<string, DeviceSettings> deviceSettings)
    {
        foreach (var session in _sessions.Values)
        {
            session.UpdateDeviceSettings(deviceSettings);
        }
    }

    public IReadOnlyDictionary<Guid, ControllerSession> Sessions => _sessions;

    private ViGEmClient? _client;

    public void Dispose()
    {
        foreach (var lockedInstanceIds in _hidHideLockedInstanceIds.Values)
        {
            _hidHide.Unlock(lockedInstanceIds);
        }
        _hidHideLockedInstanceIds.Clear();

        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }
        _sessions.Clear();
        _client?.Dispose();
        _client = null;
    }
}
