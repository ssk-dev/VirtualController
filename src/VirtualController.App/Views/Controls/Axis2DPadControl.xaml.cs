using System.Windows.Controls;

namespace VirtualController.App.Views.Controls;

/// <summary>
/// Code-Behind fuer <see cref="Axis2DPadControl"/> - enthaelt bewusst keine Logik. Die gesamte
/// Darstellung (Marker-/Deadzone-Position, Highlight-Animation) wird deklarativ per Bindings gegen
/// das per DataContext gesetzte <see cref="ViewModels.Axis2DVisualizationViewModel"/> berechnet.
/// </summary>
public partial class Axis2DPadControl : System.Windows.Controls.UserControl
{
    public Axis2DPadControl()
    {
        InitializeComponent();
    }
}
