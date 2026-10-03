using System.Windows.Controls;

namespace VirtualController.App.Views.Controls;

/// <summary>
/// Code-behind for <see cref="Axis2DPadControl"/>; intentionally contains no logic. Marker/deadzone positions
/// and highlight animation are calculated declaratively through bindings to the
/// <see cref="ViewModels.Axis2DVisualizationViewModel"/> set as the DataContext.
/// </summary>
public partial class Axis2DPadControl : System.Windows.Controls.UserControl
{
    public Axis2DPadControl()
    {
        InitializeComponent();
    }
}
