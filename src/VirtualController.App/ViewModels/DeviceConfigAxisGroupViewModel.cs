using System.Collections.ObjectModel;
using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Gruppiert alle Achsen eines Geraets im Konfigurationsdialog als Liste von <see cref="IDeviceConfigAxisItem"/>:
/// entweder ein einzelnes Positiv-/Negativ-Paar (<see cref="DeviceConfigAxisPairViewModel"/>, fuer
/// Trigger/Schieberegler/unpartnerte Rotationsachsen) oder ein kompletter Stick aus zwei gepaarten Achsen
/// (<see cref="DeviceConfigStickGroupViewModel"/>, z.B. "Linker Stick" = X+Y), damit jede physische Achse
/// bzw. jeder Stick in der View als eigener, optisch klar abgegrenzter Block (Name + Enable, darunter die
/// Kalibrierungs-Einstellmoeglichkeiten) dargestellt werden kann - statt, wie zuvor, als lose
/// Aneinanderreihung einzelner Zeilen ohne erkennbare Zusammengehoerigkeit.
/// </summary>
public sealed class DeviceConfigAxisGroupViewModel : IDeviceConfigGroup
{
    public string GroupName => "Achsen";

    public ObservableCollection<IDeviceConfigAxisItem> Items { get; } = new();

    public IEnumerable<DeviceConfigInputRowViewModel> AllRows => Items.SelectMany(i => i.AllRows);

    /// <summary>Aktualisiert die in allen Karten eingebetteten Live-Visualisierungen anhand des zuletzt
    /// gepollten Geraetezustands. Ersetzt die frueher separate, flache <c>AxisVisualizations</c>-Liste:
    /// jede Karte (Einzelachse oder Stick) verwaltet ihre eigene Visualisierung nun direkt selbst.</summary>
    public void UpdateFromState(DeviceState state)
    {
        foreach (var item in Items)
        {
            item.UpdateVisualization(state);
        }
    }

    /// <summary>Setzt alle eingebetteten Live-Visualisierungen auf den Ruhezustand zurueck (z.B. beim
    /// Zuklappen des Geraets im Konfigurationsdialog).</summary>
    public void Reset()
    {
        foreach (var item in Items)
        {
            item.ResetVisualization();
        }
    }
}

