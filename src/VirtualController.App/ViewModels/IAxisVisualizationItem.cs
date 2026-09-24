using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Gemeinsame Schnittstelle fuer die beiden Arten von Achsenvisualisierungs-Items
/// (<see cref="AxisVisualizationViewModel"/> fuer Einzelachsen, <see cref="Axis2DVisualizationViewModel"/>
/// fuer kombinierte X/Y-Achsen), damit <see cref="DeviceConfigDeviceViewModel"/> beliebig viele Achsen
/// eines Geraets generisch, ohne Typ-Unterscheidung, per Live-Polling aktualisieren kann. Welche
/// konkrete Darstellung (horizontaler Slider vs. quadratisches Koordinatenfeld) verwendet wird,
/// entscheidet die View allein anhand des Laufzeittyps (implizite DataTemplates).
/// </summary>
public interface IAxisVisualizationItem
{
    /// <summary>Aktualisiert Wert(e) und Deadzone-Zustand anhand des zuletzt gepollten Geraetezustands.</summary>
    void UpdateFromState(DeviceState state);

    /// <summary>Setzt die Anzeige auf den Ruhezustand zurueck (z.B. beim Zuklappen des Geraets im Konfigurationsdialog).</summary>
    void Reset();
}
