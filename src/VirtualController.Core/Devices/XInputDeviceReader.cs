using Vortice.XInput;

namespace VirtualController.Core.Devices;

/// <summary>
/// Reads a physical controller through XInput (user index 0-3). XInput is used by Xbox-compatible controllers
/// and provides the lowest polling latency.
/// </summary>
public sealed class XInputDeviceReader : IDeviceReader
{
    private const int ButtonCount = 14; // 10 XInput digital buttons + 4 D-pad bits are handled separately.

    private readonly int _userIndex;

    public PhysicalDeviceInfo Info { get; }

    /// <summary>
    /// XInput always uses slots 0-5 with fixed meanings (left/right stick axes and triggers), independent of
    /// the generic PhysicalAxisId names, which are used here only as slot indices. Slots 3-5 intentionally use
    /// RotationX/RotationY/RotationZ (instead of the earlier incorrect RotationZ/Slider0/Slider1 mapping) to
    /// match polling order and distinguish them from DirectInput's actual X/Y/Z rotation slots. See
    /// <see cref="Mapping.MappingEngine"/> for API-specific trigger detection based on these slots.
    /// </summary>
    private static readonly PhysicalAxisId[] FixedAvailableAxes =
    {
        PhysicalAxisId.X, PhysicalAxisId.Y, PhysicalAxisId.Z,
        PhysicalAxisId.RotationX, PhysicalAxisId.RotationY, PhysicalAxisId.RotationZ
    };

    public XInputDeviceReader(int userIndex)
    {
        _userIndex = userIndex;
        Info = new PhysicalDeviceInfo(
            DeviceId: $"xinput:{userIndex}",
            DisplayName: $"XInput Controller {userIndex + 1}",
            Api: InputApi.XInput,
            ApiSlot: userIndex,
            ButtonCount: ButtonCount,
            HasPov: false,
            AvailableAxes: FixedAvailableAxes);
    }

    /// <summary>Checks whether a device is connected at this slot without reading the full state.</summary>
    public static bool IsConnected(int userIndex)
        => XInput.GetState(userIndex, out _);

    public bool Poll(out DeviceState state)
    {
        if (!XInput.GetState(_userIndex, out State raw))
        {
            state = DeviceState.Empty(ButtonCount);
            return false;
        }

        var gp = raw.Gamepad;
        var buttons = new bool[ButtonCount];
        buttons[0] = (gp.Buttons & GamepadButtons.A) != 0;
        buttons[1] = (gp.Buttons & GamepadButtons.B) != 0;
        buttons[2] = (gp.Buttons & GamepadButtons.X) != 0;
        buttons[3] = (gp.Buttons & GamepadButtons.Y) != 0;
        buttons[4] = (gp.Buttons & GamepadButtons.LeftShoulder) != 0;
        buttons[5] = (gp.Buttons & GamepadButtons.RightShoulder) != 0;
        buttons[6] = (gp.Buttons & GamepadButtons.LeftThumb) != 0;
        buttons[7] = (gp.Buttons & GamepadButtons.RightThumb) != 0;
        buttons[8] = (gp.Buttons & GamepadButtons.Back) != 0;
        buttons[9] = (gp.Buttons & GamepadButtons.Start) != 0;
        buttons[10] = (gp.Buttons & GamepadButtons.DPadUp) != 0;
        buttons[11] = (gp.Buttons & GamepadButtons.DPadDown) != 0;
        buttons[12] = (gp.Buttons & GamepadButtons.DPadLeft) != 0;
        buttons[13] = (gp.Buttons & GamepadButtons.DPadRight) != 0;

        state = new DeviceState
        {
            Buttons = buttons,
            Axes = new[]
            {
                Normalize(gp.LeftThumbX),
                Normalize(gp.LeftThumbY),
                Normalize(gp.RightThumbX),
                Normalize(gp.RightThumbY),
                gp.LeftTrigger / 255f,
                gp.RightTrigger / 255f,
                0f,
                0f
            },
            PovDirectionDegrees = -1
        };
        return true;
    }

    private static float Normalize(short raw) => raw < 0 ? raw / 32768f : raw / 32767f;

    public void Dispose()
    {
        // XInput has no handles to clean up.
    }
}
