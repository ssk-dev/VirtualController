namespace VirtualController.App.ViewModels;

/// <summary>
/// Gemeinsame Schnittstelle fuer die beiden Arten von Eingabe-Gruppen im Geraete-Konfigurationsdialog:
/// <see cref="DeviceConfigInputGroupViewModel"/> (flache Liste, fuer "Buttons"/"D-Pad") und
/// <see cref="DeviceConfigAxisGroupViewModel"/> (paarweise Positiv-/Negativ-Achsen mit gemeinsamem
/// Rahmen, fuer "Achsen"). Ermoeglicht es <see cref="DeviceConfigDeviceViewModel"/>, beide Gruppentypen
/// generisch in einer einzigen <c>InputGroups</c>-Collection zu verwalten (Live-Update, Reset), ohne den
/// konkreten Typ zu kennen; die View waehlt die passende Darstellung allein anhand des Laufzeittyps
/// (implizite DataTemplates).
/// </summary>
public interface IDeviceConfigGroup
{
    string GroupName { get; }

    /// <summary>Alle in dieser Gruppe enthaltenen Eingabezeilen, unabhaengig von der internen
    /// Verschachtelung (flach bzw. paarweise) - fuer generisches Live-Update/Reset.</summary>
    IEnumerable<DeviceConfigInputRowViewModel> AllRows { get; }
}
