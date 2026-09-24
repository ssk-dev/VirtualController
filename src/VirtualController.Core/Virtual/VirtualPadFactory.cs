using Nefarius.ViGEm.Client;

namespace VirtualController.Core.Virtual;

/// <summary>Erzeugt die passende <see cref="IVirtualPad"/>-Implementierung fuer ein gewaehltes Backend.</summary>
public static class VirtualPadFactory
{
    public static IVirtualPad Create(ViGEmClient client, VirtualBackend backend) => backend switch
    {
        VirtualBackend.Xbox360 => new Xbox360VirtualPad(client),
        VirtualBackend.DualShock4 => new DualShock4VirtualPad(client),
        _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unbekanntes ViGEmBus-Backend.")
    };
}
