namespace VirtualController.Core.Devices;

/// <summary>
/// Beschreibt ein am System angeschlossenes physisches Eingabegeraet, unabhaengig davon
/// ob es ueber XInput oder DirectInput angesprochen wird.
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
    /// <summary>Welche generischen Achsen-Slots (siehe <see cref="PhysicalAxisId"/>) dieses konkrete Geraet tatsaechlich liefert.</summary>
    public IReadOnlyList<PhysicalAxisId> AvailableAxes { get; init; } = AvailableAxes ?? Array.Empty<PhysicalAxisId>();

    /// <summary>USB-Hersteller-ID (Vendor ID), ermittelt aus DirectInputs <c>ProductGuid</c> (siehe
    /// <see cref="DeviceEnumerator"/>). Null, falls nicht ermittelbar - z.B. bei einem XInput-Geraet,
    /// zu dem sich kein passendes DirectInput-Gegenstueck finden liess (siehe <see cref="ProductId"/>).
    /// Wird ausschliesslich fuer die HidHide-Geraetesperre (Aufloesung der PnP-Instanz-ID ueber
    /// Vendor/Product-ID) benoetigt.</summary>
    public ushort? VendorId { get; init; } = VendorId;

    /// <summary>USB-Produkt-ID (Product ID), siehe <see cref="VendorId"/> fuer Details zur Herkunft.</summary>
    public ushort? ProductId { get; init; } = ProductId;

    public override string ToString() => $"{DisplayName} ({Api}, Slot {ApiSlot})";
}
