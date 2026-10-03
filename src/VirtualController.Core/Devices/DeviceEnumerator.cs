using Vortice.DirectInput;

namespace VirtualController.Core.Devices;

/// <summary>
/// Finds all currently connected physical controllers through XInput (slots 0-3) and DirectInput (other
/// HID gamepads/joysticks, including most PlayStation controllers). Devices already found through XInput
/// are not listed again during DirectInput enumeration because XInput controllers also appear as DirectInput devices.
/// </summary>
public static class DeviceEnumerator
{
    /// <summary>Capabilities detected once for a DirectInput device and cached for the process lifetime
    /// (see <see cref="_capabilitiesCache"/> and its use in <see cref="EnumerateAll"/>).</summary>
    private sealed record DirectInputCapabilities(int ButtonCount, bool HasPov, List<PhysicalAxisId> AvailableAxes);

    /// <summary>
    /// Cache of capabilities (ButtonCount/HasPov/AvailableAxes) for detected DirectInput devices, keyed by
    /// <see cref="DeviceInstance.InstanceGuid"/>. <see cref="EnumerateAll"/> runs every two seconds from the
    /// hot-plug timer (see <c>MainViewModel.HotplugPollInterval</c>). Without this cache, every scan would create
    /// a new <see cref="Vortice.DirectInput.IDirectInputDevice8"/> for every connected device and query its
    /// capabilities/axes, even when nothing changed. This adds unnecessary COM overhead and creates a short-lived
    /// device object while a <see cref="DirectInputDeviceReader"/> may already be polling it. Device capabilities
    /// do not change while connected, so process-wide caching is safe.
    /// </summary>
    private static readonly Dictionary<Guid, DirectInputCapabilities> _capabilitiesCache = new();

    public static IReadOnlyList<PhysicalDeviceInfo> EnumerateAll()
    {
        var result = new List<PhysicalDeviceInfo>();
        var xinputSlots = new HashSet<int>();

        for (int i = 0; i < 4; i++)
        {
            if (!XInputDeviceReader.IsConnected(i))
            {
                continue;
            }

            xinputSlots.Add(i);
            result.Add(new PhysicalDeviceInfo(
                DeviceId: $"xinput:{i}",
                DisplayName: $"XInput Controller {i + 1}",
                Api: InputApi.XInput,
                ApiSlot: i,
                ButtonCount: 14,
                HasPov: false));
        }

        using var directInput = DirectInputFactory.Create();
        int diSlot = 0;
        // Collect vendor/product IDs for DirectInput devices identified as XInput-like (see LooksLikeXInputDevice).
        // XInput itself does not provide VID/PID, which is needed for HidHide device blocking (assigned to
        // XInput entries after this loop).
        var xinputVidPidCandidates = new HashSet<(ushort VendorId, ushort ProductId)>();
        foreach (var deviceInstance in directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
        {
            // Do not also list devices already exposed through XInput as DirectInput duplicates. XInput-capable
            // devices can be identified reliably only through their XInput slot, so when XInput slots are active,
            // limit matching DirectInput gamepads by name.
            if (xinputSlots.Count > 0 && LooksLikeXInputDevice(deviceInstance.InstanceName))
            {
                if (TryGetVendorProductId(deviceInstance.ProductGuid, out var xVendorId, out var xProductId))
                {
                    xinputVidPidCandidates.Add((xVendorId, xProductId));
                }
                diSlot++;
                continue;
            }

            // Use cached capabilities for already-known connected devices; their capabilities do not change
            // while connected. Repeating CreateDevice()/Capabilities/GetObjects(Axis) on each hot-plug scan
            // would add unnecessary COM overhead (see _capabilitiesCache documentation).
            if (!_capabilitiesCache.TryGetValue(deviceInstance.InstanceGuid, out var capabilities))
            {
                using var device = directInput.CreateDevice(deviceInstance.InstanceGuid);
                capabilities = new DirectInputCapabilities(
                    ButtonCount: Math.Max(device.Capabilities.ButtonCount, 1),
                    HasPov: device.Capabilities.PovCount > 0,
                    AvailableAxes: DetectAvailableAxes(device));
                _capabilitiesCache[deviceInstance.InstanceGuid] = capabilities;
            }

            bool hasVidPid = TryGetVendorProductId(deviceInstance.ProductGuid, out var vendorId, out var productId);
            result.Add(new PhysicalDeviceInfo(
                DeviceId: $"dinput:{deviceInstance.InstanceGuid}",
                DisplayName: deviceInstance.InstanceName,
                Api: InputApi.DirectInput,
                ApiSlot: diSlot++,
                ButtonCount: capabilities.ButtonCount,
                HasPov: capabilities.HasPov,
                AvailableAxes: capabilities.AvailableAxes,
                VendorId: hasVidPid ? vendorId : null,
                ProductId: hasVidPid ? productId : null));
        }

        // Assign only when there is exactly one unique VID/PID candidate. With multiple different controller
        // models connected, XInput provides no API to reliably map slots to physical devices; blocking the
        // wrong device is riskier than not blocking any device.
        if (xinputVidPidCandidates.Count == 1)
        {
            var (resolvedVendorId, resolvedProductId) = xinputVidPidCandidates.Single();
            for (int i = 0; i < result.Count; i++)
            {
                if (result[i].Api == InputApi.XInput)
                {
                    result[i] = result[i] with { VendorId = resolvedVendorId, ProductId = resolvedProductId };
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Extracts the vendor/product IDs from DirectInput's <see cref="DeviceInstance.ProductGuid"/>. DirectInput
    /// encodes USB HID devices using the GUID format "ppppvvvv-0000-0000-0000-504944564944"; the final six
    /// bytes spell "PIDVID" in ASCII and identify this layout. <see cref="Guid.ToByteArray"/> returns Data1 in
    /// little-endian order (bytes 0-3), so VendorId is bytes [0..1] and ProductId is bytes [2..3]. Kept local
    /// for HidHide device resolution because Vortice provides no direct VendorId/ProductId properties.
    /// </summary>
    private static bool TryGetVendorProductId(Guid productGuid, out ushort vendorId, out ushort productId)
    {
        var bytes = productGuid.ToByteArray();
        // Bytes 10-15 must spell "PIDVID" in ASCII; otherwise the device does not use the expected layout.
        ReadOnlySpan<byte> pidVidMarker = "PIDVID"u8;
        if (!bytes.AsSpan(10, 6).SequenceEqual(pidVidMarker))
        {
            vendorId = 0;
            productId = 0;
            return false;
        }

        vendorId = (ushort)(bytes[0] | (bytes[1] << 8));
        productId = (ushort)(bytes[2] | (bytes[3] << 8));
        return true;
    }

    /// <summary>
    /// Determines which generic axis slots (see <see cref="PhysicalAxisId"/>) a DirectInput device actually has.
    /// Uses the stable, vendor-independent HID <see cref="DeviceObjectInstance.Usage"/> ID (Generic Desktop
    /// page) reported for each axis object by <see cref="Vortice.DirectInput.IDirectInputDevice8.GetObjects"/>.
    /// IMPORTANT: <see cref="DeviceObjectInstance.Offset"/> is unsuitable because DirectInput assigns it
    /// nondeterministically before SetDataFormat(), packing objects from zero in enumeration order. Devices
    /// with gaps in their axis layout (e.g. RzAxis + Slider but no RxAxis/RyAxis) were therefore mapped
    /// incorrectly. Comparing raw values to zero also fails because absent axes can return misleading values.
    /// </summary>
    private static List<PhysicalAxisId> DetectAvailableAxes(Vortice.DirectInput.IDirectInputDevice8 device)
    {
        // Generic Desktop HID usage IDs are fixed and vendor-independent, unlike DirectInput offsets, which
        // are nondeterministically packed in enumeration order before SetDataFormat(): 0x30=X, 0x31=Y, 0x32=Z,
        // 0x33=RotationX, 0x34=RotationY, 0x35=RotationZ, 0x36=Slider, 0x37=Dial, 0x38=Wheel. Offset-only
        // matching fails for devices with gaps (e.g. RzAxis + Slider but no RxAxis/RyAxis), where DirectInput
        // packs existing objects from offset 0. This can misidentify RzAxis as RotationX, leaving the actual
        // input unread while the incorrectly mapped slot remains at raw zero (-1.0 normalized).
        var primaryAxisUsages = new Dictionary<int, PhysicalAxisId>
        {
            [0x30] = PhysicalAxisId.X,
            [0x31] = PhysicalAxisId.Y,
            [0x32] = PhysicalAxisId.Z,
            [0x33] = PhysicalAxisId.RotationX,
            [0x34] = PhysicalAxisId.RotationY,
            [0x35] = PhysicalAxisId.RotationZ,
        };

        // DirectInput maps each additional analog axis beyond the primary six (X/Y/Z/RotationX/Y/Z) by position
        // to one of two generic Slider slots in DIJOYSTATE2.lSlider[], regardless of HID usage (0x36=Slider,
        // 0x37=Dial, 0x38=Wheel). Some devices, such as the Saitek X-56 Rhino throttle, declare the second
        // extra axis as Dial rather than a second Slider. Matching only 0x36 would ignore it even though
        // DirectInput provides its raw value in Sliders[1] (see DirectInputDeviceReader.Poll).
        var extraAxisUsages = new HashSet<int> { 0x36, 0x37, 0x38 };

        var axes = new List<PhysicalAxisId>();
        bool slider0Assigned = false;
        foreach (var objectInfo in device.GetObjects(DeviceObjectTypeFlags.Axis))
        {
            PhysicalAxisId axisId;
            if (primaryAxisUsages.TryGetValue(objectInfo.Usage, out axisId))
            {
                // Primary axis; use directly without assigning a Slider0/1 slot.
            }
            else if (extraAxisUsages.Contains(objectInfo.Usage))
            {
                // First extra axis (any usage) maps to Slider0; second maps to Slider1.
                axisId = slider0Assigned ? PhysicalAxisId.Slider1 : PhysicalAxisId.Slider0;
                slider0Assigned = true;
            }
            else
            {
                continue;
            }

            if (!axes.Contains(axisId))
            {
                axes.Add(axisId);
            }
        }

        return axes;
    }

    /// <summary>Opens a reader for a device previously found by <see cref="EnumerateAll"/>.</summary>
    public static IDeviceReader OpenReader(PhysicalDeviceInfo info)
    {
        if (info.Api == InputApi.XInput)
        {
            return new XInputDeviceReader(info.ApiSlot);
        }

        var guid = Guid.Parse(info.DeviceId.Substring("dinput:".Length));
        using var directInput = DirectInputFactory.Create();
        var device = directInput.CreateDevice(guid);
        return new DirectInputDeviceReader(device, info, info.ButtonCount);
    }

    private static bool LooksLikeXInputDevice(string instanceName)
        => instanceName.Contains("XInput", StringComparison.OrdinalIgnoreCase)
           || instanceName.Contains("Xbox", StringComparison.OrdinalIgnoreCase);
}
