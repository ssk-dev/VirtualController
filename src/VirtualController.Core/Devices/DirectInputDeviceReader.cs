using SharpGen.Runtime;
using System.Runtime.InteropServices;
using Vortice.DirectInput;

namespace VirtualController.Core.Devices;

/// <summary>
/// Liest einen physischen Controller ueber DirectInput aus. Wird fuer alle Geraete genutzt,
/// die nicht ueber XInput erreichbar sind (z.B. reine DirectInput-Gamepads, viele PlayStation-
/// Controller im generischen HID-Modus, alte Joysticks).
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

        // Achsen-Bereich vereinheitlichen, damit Normalisierung unten immer 0..65535 annehmen kann.
        foreach (var objectInfo in _device.GetObjects(DeviceObjectTypeFlags.Axis))
        {
            var properties = _device.GetObjectPropertiesById(objectInfo.ObjectId);
            properties.Range = new InputRange(0, 65535);
        }

        // DirectInput verlangt ein gueltiges Top-Level-Fensterhandle (IntPtr.Zero fuehrt zu
        // E_INVALIDARG und wirft eine Exception). Da diese App keine eigenes Fenster-Handle an
        // dieser Stelle zur Verfuegung hat und ausschliesslich lesenden Hintergrundzugriff
        // benoetigt, wird das Desktop-Fenster als gueltiges Handle verwendet.
        // WICHTIG: SetDataFormat MUSS vor Acquire() gesetzt werden - ohne dieses Datenformat
        // kennt das COM-Geraet das gewuenschte Zustands-Layout nicht, wodurch
        // GetCurrentJoystickState() undefinierte/falsche Rohwerte fuer Buttons, Achsen,
        // Slider und POV liefert (Ursache fuer falsche Anzeige und fehlendes Live-Highlight).
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
                    // DirectInput liefert fuer die Y-Achse einen positiven Rohwert beim Zurueckziehen/
                    // Abwaertsbewegen des Sticks - das Gegenteil der XInput-Konvention (positiv =
                    // vorwaerts/oben), der die virtuellen Controller (Xbox360VirtualPad/DualShock4VirtualPad)
                    // sowie die gesamte Mapping-Auswertung (MappingEngine) folgen. Durch die Negierung hier,
                    // direkt an der Quelle, muss diese Umrechnung nicht mehr an jeder einzelnen Verwendungsstelle
                    // (Live-Vorschau, Mapping-Engine) separat beruecksichtigt werden.
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
            // Geraet wurde getrennt oder Fokus/Zugriff verloren -> erneutes Acquire versuchen.
            try { _device.Acquire(); } catch { /* Geraet weiterhin nicht verfuegbar */ }
            state = DeviceState.Empty(_buttonCount);
            return false;
        }
    }

    private static int GetSlider(Vortice.DirectInput.JoystickState joyState, int index)
        => joyState.Sliders is { } sliders && index < sliders.Length ? sliders[index] : 0;

    /// <summary>Fuer Stick-artige Achsen (X, Y, Z, Rotationen): Rohbereich 0..65535 auf -1.0 .. 1.0 normalisieren.</summary>
    private static float NormalizeBidirectional(int raw) => (raw - 32767) / 32767f;

    /// <summary>Fuer Slider (typischerweise physisch einseitig, z.B. Schubregler): Rohbereich 0..65535 auf 0.0 .. 1.0 normalisieren.</summary>
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
