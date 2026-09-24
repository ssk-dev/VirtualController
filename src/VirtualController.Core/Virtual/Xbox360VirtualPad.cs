using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using VirtualController.Core.Mapping;

namespace VirtualController.Core.Virtual;

/// <summary>
/// Virtueller Xbox 360 Controller (XInput). Wird von Windows und praktisch allen Spielen
/// automatisch als vollwertiger Xbox-Controller erkannt, ohne dass das Spiel etwas von
/// ViGEmBus wissen muss.
/// </summary>
public sealed class Xbox360VirtualPad : IVirtualPad
{
    private readonly IXbox360Controller _controller;
    private bool _connected;

    public VirtualBackend Backend => VirtualBackend.Xbox360;

    public Xbox360VirtualPad(ViGEmClient client)
    {
        _controller = client.CreateXbox360Controller();
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

        _controller.SetButtonState(Xbox360Button.A, state.PressedButtons.Contains(VirtualButton.South));
        _controller.SetButtonState(Xbox360Button.B, state.PressedButtons.Contains(VirtualButton.East));
        _controller.SetButtonState(Xbox360Button.X, state.PressedButtons.Contains(VirtualButton.West));
        _controller.SetButtonState(Xbox360Button.Y, state.PressedButtons.Contains(VirtualButton.North));
        _controller.SetButtonState(Xbox360Button.LeftShoulder, state.PressedButtons.Contains(VirtualButton.LeftShoulder));
        _controller.SetButtonState(Xbox360Button.RightShoulder, state.PressedButtons.Contains(VirtualButton.RightShoulder));
        _controller.SetButtonState(Xbox360Button.LeftThumb, state.PressedButtons.Contains(VirtualButton.LeftThumbClick));
        _controller.SetButtonState(Xbox360Button.RightThumb, state.PressedButtons.Contains(VirtualButton.RightThumbClick));
        _controller.SetButtonState(Xbox360Button.Back, state.PressedButtons.Contains(VirtualButton.Back));
        _controller.SetButtonState(Xbox360Button.Start, state.PressedButtons.Contains(VirtualButton.Start));
        _controller.SetButtonState(Xbox360Button.Guide, state.PressedButtons.Contains(VirtualButton.Guide));

        _controller.SetButtonState(Xbox360Button.Up, state.DPad.HasUp());
        _controller.SetButtonState(Xbox360Button.Down, state.DPad.HasDown());
        _controller.SetButtonState(Xbox360Button.Left, state.DPad.HasLeft());
        _controller.SetButtonState(Xbox360Button.Right, state.DPad.HasRight());

        _controller.SetAxisValue(Xbox360Axis.LeftThumbX, ToShort(state.LeftStickX));
        _controller.SetAxisValue(Xbox360Axis.LeftThumbY, ToShort(state.LeftStickY));
        _controller.SetAxisValue(Xbox360Axis.RightThumbX, ToShort(state.RightStickX));
        _controller.SetAxisValue(Xbox360Axis.RightThumbY, ToShort(state.RightStickY));

        _controller.SetSliderValue(Xbox360Slider.LeftTrigger, ToByte(state.LeftTrigger));
        _controller.SetSliderValue(Xbox360Slider.RightTrigger, ToByte(state.RightTrigger));

        _controller.SubmitReport();
    }

    private static short ToShort(float normalized)
    {
        float clamped = Math.Clamp(normalized, -1f, 1f);
        return (short)(clamped * (clamped < 0 ? 32768 : 32767));
    }

    private static byte ToByte(float normalized01) => (byte)(Math.Clamp(normalized01, 0f, 1f) * 255);

    public void Dispose() => Disconnect();
}
