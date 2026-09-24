using System.Windows.Controls;

namespace VirtualController.App.Views.Controls;

/// <summary>
/// Code-Behind fuer <see cref="AxisGaugeControl"/> - enthaelt bewusst keine Logik. Die gesamte
/// Darstellung (Marker-/Deadzone-Position, Highlight-Animation) wird deklarativ per Bindings gegen
/// das per DataContext gesetzte <see cref="ViewModels.AxisVisualizationViewModel"/> berechnet.
/// </summary>
public partial class AxisGaugeControl : System.Windows.Controls.UserControl
{
    public AxisGaugeControl()
    {
        InitializeComponent();
    }
}
