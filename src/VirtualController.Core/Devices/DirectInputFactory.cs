using Vortice.DirectInput;

namespace VirtualController.Core.Devices;

/// <summary>
/// Isoliert die Erzeugung des DirectInput-COM-Wurzelobjekts an einer einzigen Stelle.
/// Verifiziert gegen Vortice.DirectInput 3.2.0: <see cref="IDirectInput8"/> besitzt keinen
/// oeffentlichen parameterlosen Konstruktor (nur einen internen fuer das COM-Marshalling).
/// Die korrekte Erzeugung erfolgt ueber die statische Factory-Methode
/// <see cref="DInput.DirectInput8Create()"/>.
/// </summary>
internal static class DirectInputFactory
{
    public static Vortice.DirectInput.IDirectInput8 Create() => DInput.DirectInput8Create();
}
