using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using VirtualController.Core.Mapping;

namespace VirtualController.Core.Virtual;

/// <summary>
/// Virtueller Sony DualShock 4 Controller (HID-Gamepad). Wird von Windows als
/// "Wireless Controller" erkannt und von PlayStation-optimierten Spielen (z.B. mit
/// entsprechenden Button-Prompts) automatisch bevorzugt.
/// </summary>
public sealed class DualShock4VirtualPad : IVirtualPad
{
    private readonly IDualShock4Controller _controller;
    private bool _connected;

    public VirtualBackend Backend => VirtualBackend.DualShock4;

    public DualShock4VirtualPad(ViGEmClient client)
    {
        _controller = client.CreateDualShock4Controller();
    }

    public void Connect()
    {
        if (_connected) return;
        _controller.Connect();
        _connected = true;
    }

    public void Disconnect()
    {
        if (!_connected) return;
        _controller.Disconnect();
        _connected = false;
    }

    public void Submit(VirtualPadState state)
    {
        if (!_connected) return;

        _controller.SetButtonState(DualShock4Button.Cross, state.PressedButtons.Contains(VirtualButton.South));
        _controller.SetButtonState(DualShock4Button.Circle, state.PressedButtons.Contains(VirtualButton.East));
        _controller.SetButtonState(DualShock4Button.Square, state.PressedButtons.Contains(VirtualButton.West));
        _controller.SetButtonState(DualShock4Button.Triangle, state.PressedButtons.Contains(VirtualButton.North));
        _controller.SetButtonState(DualShock4Button.ShoulderLeft, state.PressedButtons.Contains(VirtualButton.LeftShoulder));
        _controller.SetButtonState(DualShock4Button.ShoulderRight, state.PressedButtons.Contains(VirtualButton.RightShoulder));
        _controller.SetButtonState(DualShock4Button.ThumbLeft, state.PressedButtons.Contains(VirtualButton.LeftThumbClick));
        _controller.SetButtonState(DualShock4Button.ThumbRight, state.PressedButtons.Contains(VirtualButton.RightThumbClick));
        _controller.SetButtonState(DualShock4Button.Share, state.PressedButtons.Contains(VirtualButton.Back));
        _controller.SetButtonState(DualShock4Button.Options, state.PressedButtons.Contains(VirtualButton.Start));
        _controller.SetButtonState(DualShock4SpecialButton.Ps, state.PressedButtons.Contains(VirtualButton.Guide));
        _controller.SetButtonState(DualShock4SpecialButton.Touchpad, state.PressedButtons.Contains(VirtualButton.Share));

        _controller.SetDPadDirection(ToDPad(state.DPad));

        _controller.SetAxisValue(DualShock4Axis.LeftThumbX, ToByte(state.LeftStickX));
        _controller.SetAxisValue(DualShock4Axis.LeftThumbY, ToByte(-state.LeftStickY)); // DS4: Y-Achse invertiert (0 = oben)
        _controller.SetAxisValue(DualShock4Axis.RightThumbX, ToByte(state.RightStickX));
        _controller.SetAxisValue(DualShock4Axis.RightThumbY, ToByte(-state.RightStickY));

        _controller.SetSliderValue(DualShock4Slider.LeftTrigger, ToByte01(state.LeftTrigger));
        _controller.SetSliderValue(DualShock4Slider.RightTrigger, ToByte01(state.RightTrigger));

        _controller.SubmitReport();
    }

    private static DualShock4DPadDirection ToDPad(DPadDirection direction) => direction switch
    {
        DPadDirection.Up => DualShock4DPadDirection.North,
        DPadDirection.UpRight => DualShock4DPadDirection.Northeast,
        DPadDirection.Right => DualShock4DPadDirection.East,
        DPadDirection.DownRight => DualShock4DPadDirection.Southeast,
        DPadDirection.Down => DualShock4DPadDirection.South,
        DPadDirection.DownLeft => DualShock4DPadDirection.Southwest,
        DPadDirection.Left => DualShock4DPadDirection.West,
        DPadDirection.UpLeft => DualShock4DPadDirection.Northwest,
        _ => DualShock4DPadDirection.None
    };

    /// <summary>DS4-Sticks nutzen den vollen 0-255 Bereich mit 128 als Mitte (nicht symmetrisch wie XInput).</summary>
    private static byte ToByte(float normalized) => (byte)Math.Clamp((normalized * 127.5f) + 127.5f, 0, 255);

    private static byte ToByte01(float normalized01) => (byte)(Math.Clamp(normalized01, 0f, 1f) * 255);

    public void Dispose() => Disconnect();
}
