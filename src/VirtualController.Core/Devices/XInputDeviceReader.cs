using Vortice.XInput;

namespace VirtualController.Core.Devices;

/// <summary>
/// Liest einen physischen Controller ueber XInput aus (User-Index 0-3). XInput ist die von
/// Xbox-kompatiblen Controllern genutzte API und bietet die geringste Abfrage-Latenz.
/// </summary>
public sealed class XInputDeviceReader : IDeviceReader
{
    private const int ButtonCount = 14; // 10 XInput-Digitalbuttons + 4 DPad-Bits werden separat behandelt

    private readonly int _userIndex;

    public PhysicalDeviceInfo Info { get; }

    /// <summary>
    /// XInput belegt immer genau die Slots 0-5 mit fester Bedeutung (0=LinkerStickX, 1=LinkerStickY,
    /// 2=RechterStickX, 3=RechterStickY, 4=LinkerTrigger, 5=RechterTrigger) - unabhaengig vom generischen
    /// PhysicalAxisId-Namen, der hier nur als Slot-Index verwendet wird. Die Slot-Indizes 3-5 nutzen
    /// bewusst RotationX/RotationY/RotationZ (statt der frueheren, fehlerhaften Belegung RotationZ/
    /// Slider0/Slider1), damit sie mit der tatsaechlichen Poll-Reihenfolge uebereinstimmen und sich
    /// klar von DirectInputs eigener Slot-3-5-Bedeutung (echte X/Y/Z-Rotation) unterscheiden lassen -
    /// siehe <see cref="Mapping.MappingEngine"/> fuer die API-abhaengige Trigger-Erkennung anhand dieser Slots.
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

    /// <summary>Prueft ohne vollen State-Read, ob an diesem Slot ueberhaupt ein Geraet angeschlossen ist.</summary>
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
        // XInput benoetigt kein Handle-Cleanup.
    }
}
