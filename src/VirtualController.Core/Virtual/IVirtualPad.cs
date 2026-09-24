using VirtualController.Core.Mapping;

namespace VirtualController.Core.Virtual;

/// <summary>
/// Einheitliche Schreib-Schnittstelle fuer einen beim ViGEmBus-Treiber angemeldeten virtuellen
/// Controller. Implementierungen uebersetzen den generischen <see cref="VirtualPadState"/> in
/// das native Report-Format des jeweiligen Backends (Xbox360 = XInput, DualShock4 = HID).
/// </summary>
public interface IVirtualPad : IDisposable
{
    VirtualBackend Backend { get; }

    /// <summary>Meldet den virtuellen Controller beim ViGEmBus-Treiber an. Wirft, falls der Treiber nicht installiert ist.</summary>
    void Connect();

    /// <summary>Meldet den virtuellen Controller wieder ab (Windows entfernt das Geraet sofort).</summary>
    void Disconnect();

    /// <summary>Uebertraegt den aktuellen Zustand als einzelnen HID/XInput-Report an den Treiber.</summary>
    void Submit(VirtualPadState state);
}
