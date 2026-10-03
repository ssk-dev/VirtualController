using SharpGen.Runtime;
using System.Runtime.InteropServices;
using Vortice.DirectInput;

namespace VirtualController.Core.Devices;

/// <summary>
/// Reads a physical controller through DirectInput. Used for devices unavailable through XInput, such as
/// DirectInput-only gamepads, many PlayStation controllers in generic HID mode, and older joysticks.
/// </summary>
public sealed class DirectInputDeviceReader : IDeviceReader
{
    private readonly Vortice.DirectInput.IDirectInputDevice8 _device;
    private readonly int _buttonCount;

    public PhysicalDeviceInfo Info { get; }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    public DirectInputDeviceReader(Vortice.DirectInput.IDirectInputDevice8 device, PhysicalDeviceInfo info, int buttonCount)
    {
        _device = device;
        Info = info;
        _buttonCount = buttonCount;

        // Normalize axis ranges so the conversion below can always assume 0..65535.
        foreach (var objectInfo in _device.GetObjects(DeviceObjectTypeFlags.Axis))
        {
            var properties = _device.GetObjectPropertiesById(objectInfo.ObjectId);
            properties.Range = new InputRange(0, 65535);
        }

        // DirectInput requires a valid top-level window handle; IntPtr.Zero returns E_INVALIDARG. This app
        // does not have a window handle available here and only needs background read access, so use the
        // desktop window as a valid handle.
        // IMPORTANT: SetDataFormat must be called before Acquire(). Without it, the COM device does not know
        // the requested state layout, and GetCurrentJoystickState() returns undefined/incorrect raw values for
        // buttons, axes, sliders, and POV (causing incorrect display and missing live highlights).
        _device.SetDataFormat<RawJoystickState>();
        _device.SetCooperativeLevel(GetDesktopWindow(), CooperativeLevel.NonExclusive | CooperativeLevel.Background);
        _device.Acquire();
    }

    public bool Poll(out DeviceState state)
    {
        try
        {
            _device.Poll();
            var joyState = _device.GetCurrentJoystickState();

            var buttons = new bool[_buttonCount];
            for (int i = 0; i < _buttonCount && i < joyState.Buttons.Length; i++)
            {
                buttons[i] = joyState.Buttons[i];
            }

            int pov = (joyState.PointOfViewControllers is { Length: > 0 })
                ? joyState.PointOfViewControllers[0]
                : -1;

            var axes = new float[DeviceState.AxisSlotCount];
            foreach (var axisId in Info.AvailableAxes)
            {
                axes[(int)axisId] = axisId switch
                {
                    PhysicalAxisId.X => NormalizeBidirectional(joyState.X),
                    // DirectInput reports positive raw Y when pulling the stick down, opposite to XInput
                    // (positive = forward/up), which virtual controllers (Xbox360VirtualPad/DualShock4VirtualPad)
                    // and mapping evaluation follow. Negate Y here at the source so the live preview and
                    // mapping engine do not each need a separate conversion.
                    PhysicalAxisId.Y => -NormalizeBidirectional(joyState.Y),
                    PhysicalAxisId.Z => NormalizeBidirectional(joyState.Z),
                    PhysicalAxisId.RotationX => NormalizeBidirectional(joyState.RotationX),
                    PhysicalAxisId.RotationY => NormalizeBidirectional(joyState.RotationY),
                    PhysicalAxisId.RotationZ => NormalizeBidirectional(joyState.RotationZ),
                    PhysicalAxisId.Slider0 => NormalizeUnidirectional(GetSlider(joyState, 0)),
                    PhysicalAxisId.Slider1 => NormalizeUnidirectional(GetSlider(joyState, 1)),
                    _ => 0f
                };
            }

            state = new DeviceState
            {
                Buttons = buttons,
                Axes = axes,
                PovDirectionDegrees = pov
            };
            return true;
        }
        catch (SharpGenException)
        {
            // The device disconnected or access/focus was lost; try to acquire it again.
            try { _device.Acquire(); } catch { /* Device is still unavailable. */ }
            state = DeviceState.Empty(_buttonCount);
            return false;
        }
    }

    private static int GetSlider(Vortice.DirectInput.JoystickState joyState, int index)
        => joyState.Sliders is { } sliders && index < sliders.Length ? sliders[index] : 0;

    /// <summary>Normalizes stick-like axes (X, Y, Z, rotations) from 0..65535 to -1.0 .. 1.0.</summary>
    private static float NormalizeBidirectional(int raw) => (raw - 32767) / 32767f;

    /// <summary>Normalizes sliders (typically physically unidirectional, e.g. a throttle) from 0..65535 to 0.0 .. 1.0.</summary>
    private static float NormalizeUnidirectional(int raw) => Math.Clamp(raw / 65535f, 0f, 1f);

    public void Dispose()
    {
        try
        {
            _device.Unacquire();
        }
        finally
        {
            _device.Dispose();
        }
    }
}
