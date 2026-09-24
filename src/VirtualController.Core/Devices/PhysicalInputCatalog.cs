namespace VirtualController.Core.Devices;

/// <summary>
/// Erzeugt die vollstaendige Liste aller physischen Eingabeelemente (Buttons, Achsen-Richtungen,
/// D-Pad) eines Geraets mit sinnvollen Standard-Anzeigenamen. Wird von der UI genutzt, um pro
/// ausgewaehltem Geraet eine aufklappbare Liste aller belegbaren Eingaben anzuzeigen.
/// </summary>
public static class PhysicalInputCatalog
{
    /// <summary>Button-Reihenfolge exakt wie in <see cref="XInputDeviceReader.Poll"/> befuellt (Index 0-13).</summary>
    private static readonly string[] XInputButtonNames =
    {
        "A", "B", "X", "Y",
        "LB", "RB",
        "Linker Stick (Klick)", "Rechter Stick (Klick)",
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
            // XInput hat immer eine feste Achsenbelegung (siehe XInputDeviceReader) -> bewaehrte Namen behalten.
            AddAxisPair(inputs, device, PhysicalAxisId.X, "Linker Stick X");
            AddAxisPair(inputs, device, PhysicalAxisId.Y, "Linker Stick Y");
            AddAxisPair(inputs, device, PhysicalAxisId.Z, "Rechter Stick X");
            AddAxisPair(inputs, device, PhysicalAxisId.RotationX, "Rechter Stick Y");
            inputs.Add(new PhysicalInputRef(device.DeviceId, PhysicalInputKind.AxisPositive, (int)PhysicalAxisId.RotationY, "Linker Trigger"));
            inputs.Add(new PhysicalInputRef(device.DeviceId, PhysicalInputKind.AxisPositive, (int)PhysicalAxisId.RotationZ, "Rechter Trigger"));
        }
        else
        {
            // DirectInput: nur die Achsen auflisten, die dieses konkrete Geraet laut Enumeration tatsaechlich besitzt.
            foreach (var axisId in device.AvailableAxes)
            {
                if (axisId is PhysicalAxisId.Slider0 or PhysicalAxisId.Slider1)
                {
                    // Slider sind typischerweise physisch einseitig (z.B. Schubregler) -> nur ein Eintrag, 0..1.
                    string sliderName = axisId == PhysicalAxisId.Slider0 ? "Schieberegler 1" : "Schieberegler 2";
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
            inputs.Add(new PhysicalInputRef(device.DeviceId, PhysicalInputKind.DPadUp, 0, "D-Pad Hoch"));
            inputs.Add(new PhysicalInputRef(device.DeviceId, PhysicalInputKind.DPadDown, 1, "D-Pad Runter"));
            inputs.Add(new PhysicalInputRef(device.DeviceId, PhysicalInputKind.DPadLeft, 2, "D-Pad Links"));
            inputs.Add(new PhysicalInputRef(device.DeviceId, PhysicalInputKind.DPadRight, 3, "D-Pad Rechts"));
        }

        return inputs;
    }

    private static string DirectInputAxisName(PhysicalAxisId axisId) => axisId switch
    {
        PhysicalAxisId.X => "X-Achse",
        PhysicalAxisId.Y => "Y-Achse",
        PhysicalAxisId.Z => "Z-Achse",
        PhysicalAxisId.RotationX => "X-Rotation",
        PhysicalAxisId.RotationY => "Y-Rotation",
        PhysicalAxisId.RotationZ => "Z-Rotation",
        _ => axisId.ToString()
    };

    private static void AddAxisPair(List<PhysicalInputRef> inputs, PhysicalDeviceInfo device, PhysicalAxisId axis, string baseName)
    {
        inputs.Add(new PhysicalInputRef(device.DeviceId, PhysicalInputKind.AxisPositive, (int)axis, $"{baseName} +"));
        inputs.Add(new PhysicalInputRef(device.DeviceId, PhysicalInputKind.AxisNegative, (int)axis, $"{baseName} -"));
    }

    /// <summary>Eindeutiger Persistenz-Schluessel fuer benutzerdefinierte Anzeigenamen, unabhaengig vom virtuellen Controller.</summary>
    public static string BuildStorageKey(string deviceId, PhysicalInputKind kind, int index) => $"{deviceId}|{kind}|{index}";

    /// <summary>Eindeutiger Persistenz-Schluessel fuer benutzerdefinierte Anzeigenamen kombinierter 2D-Sticks
    /// (siehe <see cref="DeviceSettings.StickNames"/>), identifiziert ueber den Achsen-Index der X-Achse
    /// dieses Sticks - eindeutig innerhalb eines Geraets, da jede Achse nur einem Stick zugeordnet sein kann.</summary>
    public static string BuildStickStorageKey(int xAxisIndex) => $"stick:{xAxisIndex}";
}
