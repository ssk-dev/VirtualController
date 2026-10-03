using Vortice.DirectInput;

namespace VirtualController.Core.Devices;

/// <summary>
/// Centralizes creation of the DirectInput COM root object. Verified against Vortice.DirectInput 3.2.0:
/// <see cref="IDirectInput8"/> has no public parameterless constructor (only an internal one for COM marshaling).
/// Create instances through the static factory method <see cref="DInput.DirectInput8Create()"/>.
/// </summary>
internal static class DirectInputFactory
{
    public static Vortice.DirectInput.IDirectInput8 Create() => DInput.DirectInput8Create();
}
