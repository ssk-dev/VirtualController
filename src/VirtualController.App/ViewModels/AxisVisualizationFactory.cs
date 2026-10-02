using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Baut aus der Achsen-Ausstattung eines physischen Geraets (<see cref="PhysicalDeviceInfo.AvailableAxes"/>
/// bzw. der festen XInput-Belegung) die Liste generischer Visualisierungs-Items fuer den Geraete-
/// Konfigurationsdialog auf: zusammengehoerige Stick-Achsenpaare (z.B. Linker/Rechter Stick) werden zu
/// einer <see cref="Axis2DVisualizationViewModel"/> (quadratisches Koordinatenfeld) zusammengefasst,
/// alle uebrigen Achsen (Rotationsachsen, Trigger, Schieberegler) bleiben als einzelne
/// <see cref="AxisVisualizationViewModel"/> (horizontaler Slider) bestehen. Rein deklarativ/lesend -
/// erzeugt keine eigenen Controllerwerte, sondern nur die Anzeige-Struktur darueber.
/// </summary>
public static class AxisVisualizationFactory
{
    public static IReadOnlyList<IAxisVisualizationItem> BuildItems(PhysicalDeviceInfo device, DeviceSettings settings)
    {
        var items = new List<IAxisVisualizationItem>();

        if (device.Api == InputApi.XInput)
        {
            // Feste XInput-Belegung (siehe PhysicalInputCatalog/XInputDeviceReader): zwei vollwertige
            // Analog-Sticks als 2D-Pad, zwei Trigger als einzelne, einseitige Slider. XInput liefert fuer
            // beide Sticks positive Y-Werte bei Vorwaerts-/Aufwaertsbewegung -> invertYForDisplay: true.
            items.Add(Build2DAxis(device, settings, "Linker Stick", PhysicalAxisId.X, PhysicalAxisId.Y, invertYForDisplay: true));
            items.Add(Build2DAxis(device, settings, "Rechter Stick", PhysicalAxisId.Z, PhysicalAxisId.RotationX, invertYForDisplay: true));
            items.Add(BuildSingleAxis(device, settings, "Linker Trigger", PhysicalAxisId.RotationY, bidirectional: false));
            items.Add(BuildSingleAxis(device, settings, "Rechter Trigger", PhysicalAxisId.RotationZ, bidirectional: false));
            return items;
        }

        // DirectInput: nur tatsaechlich vorhandene Achsen beruecksichtigen. X/Y werden - falls beide
        // vorhanden - als ein gemeinsamer 2D-Stick dargestellt (typische Joystick-Grundachse); alle
        // uebrigen Achsen (Rotationen, Z, Slider) bleiben einzelne Slider, gemaess Anforderung
        // ("X-Rotation, Y-Rotation, Z-Rotation als horizontaler Slider").
        var available = new HashSet<PhysicalAxisId>(device.AvailableAxes);

        if (available.Contains(PhysicalAxisId.X) && available.Contains(PhysicalAxisId.Y))
        {
            // DirectInputDeviceReader negiert die Y-Achse bereits an der Quelle, sodass sie wie bei
            // XInput positive Rohwerte bei Vorwaerts-/Aufwaertsbewegung liefert -> invertYForDisplay: true,
            // identisch zu den XInput-Sticks oben.
            items.Add(Build2DAxis(device, settings, "Stick (X/Y)", PhysicalAxisId.X, PhysicalAxisId.Y, invertYForDisplay: true));
            available.Remove(PhysicalAxisId.X);
            available.Remove(PhysicalAxisId.Y);
        }

        foreach (var axisId in device.AvailableAxes)
        {
            if (!available.Contains(axisId))
            {
                continue; // Bereits als Teil des X/Y-2D-Pads oben verarbeitet.
            }

            bool isSlider = axisId is PhysicalAxisId.Slider0 or PhysicalAxisId.Slider1;
            string name = DirectInputAxisName(axisId);
            items.Add(BuildSingleAxis(device, settings, name, axisId, bidirectional: !isSlider));
        }

        return items;
    }

    private static AxisVisualizationViewModel BuildSingleAxis(
        PhysicalDeviceInfo device, DeviceSettings settings, string name, PhysicalAxisId axisId, bool bidirectional)
    {
        var inputSettings = settings.GetOrCreateInputSettings(device.DeviceId, PhysicalInputKind.AxisPositive, (int)axisId);
        return new AxisVisualizationViewModel(name, inputSettings, (int)axisId, bidirectional);
    }

    private static Axis2DVisualizationViewModel Build2DAxis(
        PhysicalDeviceInfo device, DeviceSettings settings, string name, PhysicalAxisId xAxisId, PhysicalAxisId yAxisId, bool invertYForDisplay)
    {
        var x = BuildSingleAxis(device, settings, $"{name} X", xAxisId, bidirectional: true);
        var y = BuildSingleAxis(device, settings, $"{name} Y", yAxisId, bidirectional: true);
        return new Axis2DVisualizationViewModel(name, x, y, invertYForDisplay);
    }

    private static string DirectInputAxisName(PhysicalAxisId axisId) => axisId switch
    {
        PhysicalAxisId.X => "X-Achse",
        PhysicalAxisId.Y => "Y-Achse",
        PhysicalAxisId.Z => "Z-Achse",
        PhysicalAxisId.RotationX => "X-Rotation",
        PhysicalAxisId.RotationY => "Y-Rotation",
        PhysicalAxisId.RotationZ => "Z-Rotation",
        PhysicalAxisId.Slider0 => "Schieberegler 1",
        PhysicalAxisId.Slider1 => "Schieberegler 2",
        _ => axisId.ToString()
    };
}
