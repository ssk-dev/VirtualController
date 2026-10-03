using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Combines two physical axes (X and Y) of the same stick/joystick into a 2D position for the square
/// coordinate-field visualization (see <see cref="Views.Controls.Axis2DPadControl"/>). Display-only,
/// like <see cref="AxisVisualizationViewModel"/>; it does not generate or modify controller values.
/// </summary>
public sealed partial class Axis2DVisualizationViewModel : ObservableObject, IAxisVisualizationItem
{
    /// <summary>
    /// True when a positive Y value should appear upward (XInput convention: moving the stick forward/up
    /// produces positive values); false when it should appear downward (DirectInput convention: the raw Y
    /// value increases when pulling down and decreases when pushing up). Without this distinction, the live
    /// preview would show DirectInput stick movement vertically inverted.
    /// </summary>
    private readonly bool _invertYForDisplay;

    /// <summary>Display name of the combined stick (e.g. "Left stick").</summary>
    public string Name { get; }

    public AxisVisualizationViewModel X { get; }

    public AxisVisualizationViewModel Y { get; }

    /// <summary>True when either the X or Y value leaves its own deadzone.</summary>
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

    /// <summary>Height of the top grid row (above the marker) in the 2D pad, accounting for the API-specific
    /// Y sign convention (see <see cref="_invertYForDisplay"/>).</summary>
    public double YTopMarkerFraction => _invertYForDisplay ? Y.AfterMarkerFraction : Y.MarkerFraction;

    /// <summary>Height of the bottom grid row (below the marker) in the 2D pad.</summary>
    public double YBottomMarkerFraction => _invertYForDisplay ? Y.MarkerFraction : Y.AfterMarkerFraction;

    /// <summary>Height of the top grid row (above the deadzone band) in the 2D pad.</summary>
    public double YTopDeadzoneFraction => _invertYForDisplay ? Y.AfterDeadzoneFraction : Y.DeadzoneStartFraction;

    /// <summary>Height of the bottom grid row (below the deadzone band) in the 2D pad.</summary>
    public double YBottomDeadzoneFraction => _invertYForDisplay ? Y.DeadzoneStartFraction : Y.AfterDeadzoneFraction;

    /// <summary>Updates both axis values from the most recently polled device state.</summary>
    public void UpdateFromState(DeviceState state)
    {
        X.UpdateFromState(state);
        Y.UpdateFromState(state);
        IsOutsideDeadzone = X.IsOutsideDeadzone || Y.IsOutsideDeadzone;
    }

    /// <summary>Resets both axis values to their resting state.</summary>
    public void Reset()
    {
        X.Reset();
        Y.Reset();
        IsOutsideDeadzone = false;
    }
}
