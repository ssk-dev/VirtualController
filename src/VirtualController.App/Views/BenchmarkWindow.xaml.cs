using System.Collections.Generic;
using System.Windows;
using VirtualController.App.ViewModels;

namespace VirtualController.App.Views;

/// <summary>
/// Non-modal real-time display window for one device's hardware benchmark (see
/// <see cref="BenchmarkWindowViewModel"/>). Business logic lives in
/// <see cref="BenchmarkWindowViewModel"/> and <see cref="DeviceConfigDeviceViewModel"/>, which owns session start/stop.
///
/// At most one instance exists per device (see <see cref="ShowFor"/>, like the singleton pattern in
/// <see cref="ModeChangeToast"/>). Reopening it activates the existing window. Closing the window, either
/// through X or the device's benchmark button, does not stop the session; it continues in the background
/// until the user explicitly stops it with the Start/Stop button.
/// </summary>
public partial class BenchmarkWindow : Window
{
    /// <summary>Open windows keyed by their device view model; see <see cref="ShowFor"/>.</summary>
    private static readonly Dictionary<DeviceConfigDeviceViewModel, BenchmarkWindow> OpenWindows = new();

    private readonly BenchmarkWindowViewModel _viewModel;

    private BenchmarkWindow(BenchmarkWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    /// <summary>Opens the real-time display for <paramref name="device"/>, or activates an existing window for
    /// that device and brings it to the foreground.</summary>
    public static void ShowFor(DeviceConfigDeviceViewModel device, Window? owner)
    {
        if (OpenWindows.TryGetValue(device, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            existing.Activate();
            return;
        }

        var window = new BenchmarkWindow(new BenchmarkWindowViewModel(device)) { Owner = owner };
        OpenWindows[device] = window;

        window.Closed += (_, _) =>
        {
            OpenWindows.Remove(device);
            window._viewModel.Dispose();
        };

        window.Show();
    }
}
