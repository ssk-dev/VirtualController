using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Kombiniert zwei physische Achsen (X und Y) desselben Sticks/Joysticks zu einer gemeinsamen
/// 2D-Position fuer die quadratische Koordinatenfeld-Visualisierung (siehe
/// <see cref="Views.Controls.Axis2DPadControl"/>). Rein darstellend, analog zu
/// <see cref="AxisVisualizationViewModel"/> - enthaelt keine eigene Logik zur Erzeugung oder
/// Veraenderung von Controllerwerten.
/// </summary>
public sealed partial class Axis2DVisualizationViewModel : ObservableObject, IAxisVisualizationItem
{
    /// <summary>
    /// true, wenn ein positiver Y-Wert auf dem Anzeigefeld nach oben dargestellt werden soll (XInput-
    /// Konvention: Vorwaerts-/Aufwaertsbewegung des Sticks liefert positive Werte), false, wenn ein
    /// positiver Y-Wert nach unten dargestellt werden soll (DirectInput-Konvention: der rohe Y-Wert
    /// waechst beim Zurueckziehen/Abwaertsbewegen des Sticks, sinkt beim Vorwaertsdruecken). Ohne diese
    /// Unterscheidung wuerde die Live-Vorschau bei DirectInput-Joysticks die Stickbewegung vertikal
    /// invertiert darstellen.
    /// </summary>
    private readonly bool _invertYForDisplay;

    /// <summary>Anzeigename des kombinierten Sticks (z.B. "Linker Stick").</summary>
    public string Name { get; }

    public AxisVisualizationViewModel X { get; }

    public AxisVisualizationViewModel Y { get; }

    /// <summary>true, sobald X- oder Y-Anteil die jeweils eigene Deadzone verlaesst.</summary>
    [ObservableProperty]
    private bool _isOutsideDeadzone;

    public Axis2DVisualizationViewModel(string name, AxisVisualizationViewModel x, AxisVisualizationViewModel y, bool invertYForDisplay = true)
    {
        Name = name;
        X = x;
        Y = y;
        _invertYForDisplay = invertYForDisplay;
        Y.PropertyChanged += OnYPropertyChanged;
    }

    private void OnYPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AxisVisualizationViewModel.MarkerFraction):
            case nameof(AxisVisualizationViewModel.AfterMarkerFraction):
                OnPropertyChanged(nameof(YTopMarkerFraction));
                OnPropertyChanged(nameof(YBottomMarkerFraction));
                break;
            case nameof(AxisVisualizationViewModel.DeadzoneStartFraction):
            case nameof(AxisVisualizationViewModel.DeadzoneWidthFraction):
            case nameof(AxisVisualizationViewModel.AfterDeadzoneFraction):
                OnPropertyChanged(nameof(YTopDeadzoneFraction));
                OnPropertyChanged(nameof(YBottomDeadzoneFraction));
                break;
        }
    }

    /// <summary>Hoehe der obersten Grid-Zeile (oberhalb des Markers) im 2D-Pad, unter Beruecksichtigung
    /// der API-abhaengigen Y-Vorzeichenkonvention (siehe <see cref="_invertYForDisplay"/>).</summary>
    public double YTopMarkerFraction => _invertYForDisplay ? Y.AfterMarkerFraction : Y.MarkerFraction;

    /// <summary>Hoehe der untersten Grid-Zeile (unterhalb des Markers) im 2D-Pad.</summary>
    public double YBottomMarkerFraction => _invertYForDisplay ? Y.MarkerFraction : Y.AfterMarkerFraction;

    /// <summary>Hoehe der obersten Grid-Zeile (oberhalb des Deadzone-Bands) im 2D-Pad.</summary>
    public double YTopDeadzoneFraction => _invertYForDisplay ? Y.AfterDeadzoneFraction : Y.DeadzoneStartFraction;

    /// <summary>Hoehe der untersten Grid-Zeile (unterhalb des Deadzone-Bands) im 2D-Pad.</summary>
    public double YBottomDeadzoneFraction => _invertYForDisplay ? Y.DeadzoneStartFraction : Y.AfterDeadzoneFraction;

    /// <summary>Aktualisiert beide Achsenanteile anhand des zuletzt gepollten Geraetezustands.</summary>
    public void UpdateFromState(DeviceState state)
    {
        X.UpdateFromState(state);
        Y.UpdateFromState(state);
        IsOutsideDeadzone = X.IsOutsideDeadzone || Y.IsOutsideDeadzone;
    }

    /// <summary>Setzt beide Achsenanteile auf den Ruhezustand zurueck.</summary>
    public void Reset()
    {
        X.Reset();
        Y.Reset();
        IsOutsideDeadzone = false;
    }
}
