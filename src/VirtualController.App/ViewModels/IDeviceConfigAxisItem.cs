using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Gemeinsame Schnittstelle fuer die beiden Arten von Elementen innerhalb der "Achsen"-Gruppe
/// (<see cref="DeviceConfigAxisGroupViewModel.Items"/>): eine eigenstaendige physische Achse
/// (<see cref="DeviceConfigAxisPairViewModel"/>, z.B. Trigger/Schieberegler) oder ein kompletter
/// Stick aus zwei zusammengehoerigen Achsen (<see cref="DeviceConfigStickGroupViewModel"/>, z.B.
/// "Linker Stick" = X+Y). Analog zum <see cref="IAxisVisualizationItem"/>-Muster der Live-Vorschau:
/// die View waehlt die passende Darstellung allein anhand des Laufzeittyps (implizite DataTemplates),
/// waehrend <see cref="DeviceConfigDeviceViewModel"/> beide Arten generisch fuer Live-Update/Reset
/// behandeln kann (<see cref="AllRows"/>, <see cref="UpdateVisualization"/>, <see cref="ResetVisualization"/>).
/// </summary>
public interface IDeviceConfigAxisItem
{
    /// <summary>Alle darin enthaltenen Eingabezeilen (Positiv-/Negativ-Richtungen), unabhaengig von
    /// der internen Verschachtelung - fuer generisches Live-Update/Reset.</summary>
    IEnumerable<DeviceConfigInputRowViewModel> AllRows { get; }

    /// <summary>Aktualisiert die direkt in dieser Karte eingebettete Live-Visualisierung (siehe
    /// <see cref="DeviceConfigAxisPairViewModel.Visualization"/> bzw.
    /// <see cref="DeviceConfigStickGroupViewModel.Visualization"/>) anhand des zuletzt gepollten
    /// Geraetezustands.</summary>
    void UpdateVisualization(DeviceState state);

    /// <summary>Setzt die eingebettete Live-Visualisierung auf den Ruhezustand zurueck (z.B. beim
    /// Zuklappen des Geraets im Konfigurationsdialog).</summary>
    void ResetVisualization();
}
