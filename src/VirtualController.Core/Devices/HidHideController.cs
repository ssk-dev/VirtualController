using System.Text.Json;
using Nefarius.Drivers.HidHide;
using Nefarius.Utilities.DeviceManagement.PnP;

namespace VirtualController.Core.Devices;

/// <summary>
/// Wraps control of the separately installed HidHide driver (https://github.com/nefarius/HidHide) through the
/// official <c>Nefarius.Drivers.HidHide</c> package (classic, non-DI usage). Blocks a virtual controller's
/// physical devices from other applications while it is running (see <see cref="Engine.ControllerManager"/>),
/// preventing a game from reacting to both the physical device and its virtual counterpart.
///
/// Reference-counted: multiple virtual controllers may share a physical device (e.g. two profiles reference
/// it in their mappings). A PnP instance ID is removed from HidHide's blocked list only when no active
/// controller references it (see <see cref="Lock"/>/<see cref="Unlock"/>).
///
/// This class enables <see cref="IHidHideControlService.IsActive"/> (the driver's global switch) only when it
/// has no existing locks of its own, and disables it after unlocking only when none of its locks remain.
/// Other applications already using IsActive/BlockedInstanceIds independently are not disturbed.
///
/// Crash recovery: persist this app's currently blocked instance IDs after each change (see
/// <see cref="PersistLockState"/>), allowing <see cref="CleanupOrphanedLocks"/> to remove orphaned locks on the
/// next startup if the app crashed. Normal shutdown calls <see cref="Unlock"/> for every lock and clears the file.
/// </summary>
public sealed class HidHideController
{
    private readonly IHidHideControlService _service = new HidHideControlService();

    /// <summary>Reference count per blocked PnP instance ID across all virtual controllers.</summary>
    private readonly Dictionary<string, int> _refCounts = new();

    /// <summary>Storage path for this app's currently blocked instance IDs; see <see cref="PersistLockState"/>
    /// and <see cref="CleanupOrphanedLocks"/>. Kept in a small separate file rather than
    /// <see cref="Profiles.ProfileStore"/> because this is runtime/crash diagnostic state, not a user-edited profile.</summary>
    private static string LockStateFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VirtualController", "hidhide-locks.json");

    /// <summary>Whether the HidHide driver is installed and operational. If false, the blocking feature must be
    /// disabled in the UI. Queries the driver every time rather than caching because installation status can
    /// change while the app is running.</summary>
    public bool IsAvailable
    {
        get
        {
            try
            {
                return _service.IsInstalled && _service.IsOperational;
            }
            catch
            {
                // Driver lookup can fail when HidHide is not installed (e.g. missing registry key/device path).
                // Treat the feature as unavailable instead of crashing the app.
                return false;
            }
        }
    }

    /// <summary>Finds all currently connected PnP instance IDs belonging to a physical device identified by
    /// vendor/product ID (see <see cref="PhysicalDeviceInfo"/>). Returns an empty list if the device has no
    /// VID/PID (e.g. an XInput device without a unique DirectInput counterpart; see <see cref="DeviceEnumerator"/>)
    /// or no matching instance is currently found.</summary>
    public IReadOnlyList<string> ResolveInstanceIds(PhysicalDeviceInfo device)
    {
        if (device.VendorId is not { } vendorId || device.ProductId is not { } productId)
        {
            return Array.Empty<string>();
        }

        string hardwareId = $"VID_{vendorId:X4}&PID_{productId:X4}";
        if (!Devcon.FindInDeviceClassByHardwareId(
                DeviceClassIds.HumanInterfaceDevices, hardwareId, out var instanceIds, presentOnly: true, allowPartial: true))
        {
            return Array.Empty<string>();
        }

        return instanceIds.ToList();
    }

    /// <summary>Increments the reference count for each PnP instance ID and adds newly referenced instances to
    /// HidHide's block list. Enables the global HidHide switch if needed (see class docs). No-op when
    /// <paramref name="instanceIds"/> is empty.</summary>
    public void Lock(IReadOnlyCollection<string> instanceIds)
    {
        if (instanceIds.Count == 0)
        {
            return;
        }

        bool hadAnyBefore = _refCounts.Count > 0;

        foreach (var instanceId in instanceIds)
        {
            if (_refCounts.TryGetValue(instanceId, out var count))
            {
                _refCounts[instanceId] = count + 1;
                continue;
            }

            _refCounts[instanceId] = 1;
            _service.AddBlockedInstanceId(instanceId);
        }

        if (!hadAnyBefore)
        {
            _service.IsActive = true;
        }

        PersistLockState();
    }

    /// <summary>Decrements the reference count for each PnP instance ID and unlocks instances whose count reaches
    /// zero. Disables the global HidHide switch when no locks owned by this app remain. No-op when
    /// <paramref name="instanceIds"/> is empty.</summary>
    public void Unlock(IReadOnlyCollection<string> instanceIds)
    {
        if (instanceIds.Count == 0)
        {
            return;
        }

        foreach (var instanceId in instanceIds)
        {
            if (!_refCounts.TryGetValue(instanceId, out var count))
            {
                continue;
            }

            if (count <= 1)
            {
                _refCounts.Remove(instanceId);
                _service.RemoveBlockedInstanceId(instanceId);
            }
            else
            {
                _refCounts[instanceId] = count - 1;
            }
        }

        if (_refCounts.Count == 0)
        {
            _service.IsActive = false;
        }

        PersistLockState();
    }

    /// <summary>Writes this app's currently blocked instance IDs (<see cref="_refCounts"/>) to
    /// <see cref="LockStateFilePath"/> so <see cref="CleanupOrphanedLocks"/> can detect locks left by a crash on
    /// the next startup. Write errors are swallowed; diagnostic persistence must never crash the app. Similar
    /// to <see cref="App.Diagnostics.DebugLog"/>, but implemented here without depending on the App project because
    /// <see cref="HidHideController"/> lives in Core.</summary>
    private void PersistLockState()
    {
        try
        {
            var directory = Path.GetDirectoryName(LockStateFilePath)!;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(LockStateFilePath, JsonSerializer.Serialize(_refCounts.Keys.ToList()));
        }
        catch
        {
            // Diagnostic persistence only; must never crash the app.
        }
    }

    /// <summary>
    /// Detects and removes HidHide locks orphaned by a previous unclean shutdown. Normal shutdown calls
    /// <see cref="Unlock"/> for every owned lock and leaves <see cref="LockStateFilePath"/> empty; entries still
    /// present at startup of a fresh instance indicate the previous run crashed before unlocking. Call once
    /// during app startup before any controller starts (see <see cref="Engine.ControllerManager"/>). Disable
    /// the global HidHide switch only if the driver's entire block list is empty afterward, so locks owned by
    /// other applications remain effective.
    /// </summary>
    /// <returns>Instance IDs actually identified as orphaned and removed (empty if no cleanup was needed), for
    /// an optional UI warning.</returns>
    public IReadOnlyList<string> CleanupOrphanedLocks()
    {
        List<string>? persistedInstanceIds;
        try
        {
            if (!File.Exists(LockStateFilePath))
            {
                return Array.Empty<string>();
            }

            persistedInstanceIds = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(LockStateFilePath));
        }
        catch
        {
            // A corrupted/unreadable file cannot reliably identify orphaned locks, so do nothing rather than
            // throwing or removing locks speculatively.
            return Array.Empty<string>();
        }

        if (persistedInstanceIds is null || persistedInstanceIds.Count == 0)
        {
            return Array.Empty<string>();
        }

        var removed = new List<string>();
        try
        {
            var currentlyBlocked = _service.BlockedInstanceIds.ToHashSet();
            foreach (var instanceId in persistedInstanceIds)
            {
                if (currentlyBlocked.Contains(instanceId))
                {
                    _service.RemoveBlockedInstanceId(instanceId);
                    removed.Add(instanceId);
                }
            }

            if (removed.Count > 0 && _service.BlockedInstanceIds.Count == 0)
            {
                _service.IsActive = false;
            }
        }
        catch
        {
            // Driver is unavailable; cleanup cannot be performed.
            return Array.Empty<string>();
        }

        PersistLockState(); // _refCounts is empty in a fresh instance, so the file is cleared.
        return removed;
    }
}
