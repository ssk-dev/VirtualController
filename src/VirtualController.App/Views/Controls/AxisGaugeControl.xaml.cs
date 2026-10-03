using System.Windows.Controls;

namespace VirtualController.App.Views.Controls;

/// <summary>
/// Code-behind for <see cref="AxisGaugeControl"/>; intentionally contains no logic. Marker/deadzone positions
/// and highlight animation are calculated declaratively through bindings to the
/// <see cref="ViewModels.AxisVisualizationViewModel"/> set as the DataContext.
/// </summary>
public partial class AxisGaugeControl : System.Windows.Controls.UserControl
{
    public AxisGaugeControl()
    {
        InitializeComponent();
    }
}
