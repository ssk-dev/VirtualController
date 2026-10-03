namespace VirtualController.Core.Devices;

/// <summary>
/// Describes a physical input device connected to the system, regardless of whether it is accessed through
/// XInput or DirectInput.
/// </summary>
public sealed record PhysicalDeviceInfo(
    string DeviceId,
    string DisplayName,
    InputApi Api,
    int ApiSlot,
    int ButtonCount,
    bool HasPov,
    IReadOnlyList<PhysicalAxisId> AvailableAxes = null!,
    ushort? VendorId = null,
    ushort? ProductId = null)
{
    /// <summary>Generic axis slots actually provided by this device (see <see cref="PhysicalAxisId"/>).</summary>
    public IReadOnlyList<PhysicalAxisId> AvailableAxes { get; init; } = AvailableAxes ?? Array.Empty<PhysicalAxisId>();

    /// <summary>USB vendor ID, extracted from DirectInput's <c>ProductGuid</c> (see
    /// <see cref="DeviceEnumerator"/>). Null if unavailable, e.g. when no matching DirectInput counterpart
    /// can be found for an XInput device (see <see cref="ProductId"/>). Used only for HidHide device blocking
    /// by resolving the PnP instance ID from the vendor/product IDs.</summary>
    public ushort? VendorId { get; init; } = VendorId;

    /// <summary>USB product ID; see <see cref="VendorId"/> for details about its source.</summary>
    public ushort? ProductId { get; init; } = ProductId;

    public override string ToString() => $"{DisplayName} ({Api}, Slot {ApiSlot})";
}
