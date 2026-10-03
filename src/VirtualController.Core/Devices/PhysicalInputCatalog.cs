namespace VirtualController.Core.Devices;

/// <summary>
/// Builds the complete list of a device's physical inputs (buttons, axis directions, D-pad) with sensible
/// default display names. Used by the UI to show all assignable inputs for each selected device.
/// </summary>
public static class PhysicalInputCatalog
{
    /// <summary>Button-Reihenfolge exakt wie in <see cref="XInputDeviceReader.Poll"/> befuellt (Index 0-13).</summary>
    private static readonly string[] XInputButtonNames =
    {
        "A", "B", "X", "Y",
        "LB", "RB",
        "Left stick (click)", "Right stick (click)",
        "Back", "Start",
        "D-Pad Hoch", "D-Pad Runter", "D-Pad Links", "D-Pad Rechts"
    };

    public static IReadOnlyList<PhysicalInputRef> BuildInputs(PhysicalDeviceInfo device)
    {
        var inputs = new List<PhysicalInputRef>();

        for (int i = 0; i < device.ButtonCount; i++)
        {
            string name = device.Api == InputApi.XInput && i < XInputButtonNames.Length
                ? XInputButtonNames[i]
                : $"Button {i + 1}";
            inputs.Add(new PhysicalInputRef(device.DeviceId, PhysicalInputKind.Button, i, name));
        }

        if (device.Api == InputApi.XInput)
        {
            // XInput has a fixed axis layout (see XInputDeviceReader), so use the established names.
            AddAxisPair(inputs, device, PhysicalAxisId.X, "Left stick X");
            AddAxisPair(inputs, device, PhysicalAxisId.Y, "Left stick Y");
            AddAxisPair(inputs, device, PhysicalAxisId.Z, "Right stick X");
            AddAxisPair(inputs, device, PhysicalAxisId.RotationX, "Right stick Y");
            inputs.Add(new PhysicalInputRef(device.DeviceId, PhysicalInputKind.AxisPositive, (int)PhysicalAxisId.RotationY, "Left trigger"));
            inputs.Add(new PhysicalInputRef(device.DeviceId, PhysicalInputKind.AxisPositive, (int)PhysicalAxisId.RotationZ, "Right trigger"));
        }
        else
        {
            // DirectInput: list only axes actually present on this device according to enumeration.
            foreach (var axisId in device.AvailableAxes)
            {
                if (axisId is PhysicalAxisId.Slider0 or PhysicalAxisId.Slider1)
                {
                    // Sliders are typically physically unidirectional (e.g. a throttle), so create one 0..1 entry.
                    string sliderName = axisId == PhysicalAxisId.Slider0 ? "Slider 1" : "Slider 2";
                    inputs.Add(new PhysicalInputRef(device.DeviceId, PhysicalInputKind.AxisPositive, (int)axisId, sliderName));
                }
                else
                {
                    AddAxisPair(inputs, device, axisId, DirectInputAxisName(axisId));
                }
            }
        }

        if (device.HasPov)
        {
            inputs.Add(new PhysicalInputRef(device.DeviceId, PhysicalInputKind.DPadUp, 0, "D-pad Up"));
            inputs.Add(new PhysicalInputRef(device.DeviceId, PhysicalInputKind.DPadDown, 1, "D-pad Down"));
            inputs.Add(new PhysicalInputRef(device.DeviceId, PhysicalInputKind.DPadLeft, 2, "D-pad Left"));
            inputs.Add(new PhysicalInputRef(device.DeviceId, PhysicalInputKind.DPadRight, 3, "D-pad Right"));
        }

        return inputs;
    }

    private static string DirectInputAxisName(PhysicalAxisId axisId) => axisId switch
    {
        PhysicalAxisId.X => "X axis",
        PhysicalAxisId.Y => "Y axis",
        PhysicalAxisId.Z => "Z axis",
        PhysicalAxisId.RotationX => "X rotation",
        PhysicalAxisId.RotationY => "Y rotation",
        PhysicalAxisId.RotationZ => "Z rotation",
        _ => axisId.ToString()
    };

    private static void AddAxisPair(List<PhysicalInputRef> inputs, PhysicalDeviceInfo device, PhysicalAxisId axis, string baseName)
    {
        inputs.Add(new PhysicalInputRef(device.DeviceId, PhysicalInputKind.AxisPositive, (int)axis, $"{baseName} +"));
        inputs.Add(new PhysicalInputRef(device.DeviceId, PhysicalInputKind.AxisNegative, (int)axis, $"{baseName} -"));
    }

    /// <summary>Unique persistence key for custom display names, independent of virtual controller assignment.</summary>
    public static string BuildStorageKey(string deviceId, PhysicalInputKind kind, int index) => $"{deviceId}|{kind}|{index}";

    /// <summary>Unique persistence key for custom names of combined 2D sticks (see
    /// <see cref="DeviceSettings.StickNames"/>), identified by the stick's X-axis index. Unique within a device
    /// because each axis can belong to only one stick.</summary>
    public static string BuildStickStorageKey(int xAxisIndex) => $"stick:{xAxisIndex}";

    /// <summary>Parses a storage key in the format "{DeviceId}|{PhysicalInputKind}|{Index}" (see
    /// <see cref="BuildStorageKey"/>) into its parts. The last two pipe-separated segments must be the kind
    /// and index; preceding segments are rejoined as the device ID in case it contains a pipe. Defined here
    /// rather than in <see cref="Profiles.ProfileStore"/> so <see cref="InputSettingsDictionaryConverter"/> can
    /// determine during serialization whether an input supports analog settings (calibration/deadzone/curve).</summary>
    public static bool TryParseStorageKey(string key, out string deviceId, out PhysicalInputKind kind, out int index)
    {
        deviceId = string.Empty;
        kind = default;
        index = 0;

        var segments = key.Split('|');
        if (segments.Length < 3)
        {
            return false;
        }

        if (!Enum.TryParse(segments[^2], out kind) || !int.TryParse(segments[^1], out index))
        {
            return false;
        }

        deviceId = string.Join('|', segments[..^2]);
        return true;
    }
}
