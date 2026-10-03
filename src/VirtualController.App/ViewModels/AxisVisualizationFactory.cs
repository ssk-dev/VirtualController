using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Builds generic visualization items for the device configuration dialog from a physical device's available
/// axes (<see cref="PhysicalDeviceInfo.AvailableAxes"/> or the fixed XInput layout). Related stick axis pairs
/// (e.g. left/right sticks) become an <see cref="Axis2DVisualizationViewModel"/> (square coordinate field);
/// other axes (rotation axes, triggers, sliders) remain individual <see cref="AxisVisualizationViewModel"/>
/// instances (horizontal sliders). This is a read-only display structure and does not generate controller values.
/// </summary>
public static class AxisVisualizationFactory
{
    public static IReadOnlyList<IAxisVisualizationItem> BuildItems(PhysicalDeviceInfo device, DeviceSettings settings)
    {
        var items = new List<IAxisVisualizationItem>();

        if (device.Api == InputApi.XInput)
        {
            // Fixed XInput layout (see PhysicalInputCatalog/XInputDeviceReader): two analog sticks as 2D pads
            // and two triggers as unidirectional sliders. XInput reports positive Y when moving either stick
            // forward/up, so invertYForDisplay is true.
            items.Add(Build2DAxis(device, settings, "Left stick", PhysicalAxisId.X, PhysicalAxisId.Y, invertYForDisplay: true));
            items.Add(Build2DAxis(device, settings, "Right stick", PhysicalAxisId.Z, PhysicalAxisId.RotationX, invertYForDisplay: true));
            items.Add(BuildSingleAxis(device, settings, "Left trigger", PhysicalAxisId.RotationY, bidirectional: false));
            items.Add(BuildSingleAxis(device, settings, "Right trigger", PhysicalAxisId.RotationZ, bidirectional: false));
            return items;
        }

        // DirectInput: include only axes that are actually present. If both X and Y exist, display them as one
        // 2D stick (the typical joystick primary axes); keep all other axes (rotation, Z, sliders) separate.
        var available = new HashSet<PhysicalAxisId>(device.AvailableAxes);

        if (available.Contains(PhysicalAxisId.X) && available.Contains(PhysicalAxisId.Y))
        {
            // DirectInputDeviceReader already negates Y at the source, so forward/up movement produces
            // positive raw values like XInput. Use the same display inversion as for XInput sticks.
            items.Add(Build2DAxis(device, settings, "Stick (X/Y)", PhysicalAxisId.X, PhysicalAxisId.Y, invertYForDisplay: true));
            available.Remove(PhysicalAxisId.X);
            available.Remove(PhysicalAxisId.Y);
        }

        foreach (var axisId in device.AvailableAxes)
        {
            if (!available.Contains(axisId))
            {
                continue; // Already processed above as part of the X/Y 2D pad.
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
        PhysicalAxisId.X => "X axis",
        PhysicalAxisId.Y => "Y axis",
        PhysicalAxisId.Z => "Z axis",
        PhysicalAxisId.RotationX => "X rotation",
        PhysicalAxisId.RotationY => "Y rotation",
        PhysicalAxisId.RotationZ => "Z rotation",
        PhysicalAxisId.Slider0 => "Slider 1",
        PhysicalAxisId.Slider1 => "Slider 2",
        _ => axisId.ToString()
    };
}
