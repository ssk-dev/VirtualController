using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Root view model for the Device Configuration tab. Shows all known physical devices, including disabled
/// devices that can be re-enabled and previously configured devices that are now disconnected (see
/// <see cref="MainViewModel.GetAllKnownDevices"/>). Users can disable a device or individual inputs and rename
/// inputs. Changes immediately update <see cref="DeviceSettings"/> held by <see cref="MainViewModel"/> and are
/// propagated to device selection and running sessions through
/// <see cref="MainViewModel.NotifyDeviceAvailabilityChanged"/> (for <see cref="DeviceSettings.Enabled"/> or
/// <see cref="DeviceSettings.Hidden"/> changes that require a full refresh) and
/// <see cref="MainViewModel.NotifyDeviceSettingsChanged"/> (for settings-only changes). Devices appear in a
/// list-and-details layout (see <see cref="SelectedDevice"/> and MainWindow.xaml), like the Mapping tab.
/// <see cref="UpdateDevices"/> incrementally reconciles the list on every device scan, including periodic
/// hot-plug polling in <see cref="MainViewModel"/>, rather than rebuilding it and interrupting edits or live
/// monitoring of the selected device.
/// </summary>
public sealed partial class DeviceConfigViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _mainViewModel;

    /// <summary>Visible (not hidden) devices shown in this tab's main list.</summary>
    public ObservableCollection<DeviceConfigDeviceViewModel> Devices { get; } = new();

    /// <summary>Devices manually hidden by the user (see <see cref="DeviceSettings.Hidden"/>), shown in a
    /// separate collapsible list below the main list and available to restore at any time (see MainWindow.xaml).
    /// This is useful for the app's ViGEmBus-emulated virtual controllers, which XInput/DirectInput cannot
    /// distinguish from physical hardware and therefore cannot filter automatically.</summary>
    public ObservableCollection<DeviceConfigDeviceViewModel> HiddenDevices { get; } = new();

    [ObservableProperty]
    private DeviceConfigDeviceViewModel? _selectedDevice;

    /// <summary>Whether the main window's Device Configuration tab is visible and the window is not minimized
    /// (see <see cref="SetScreenActive"/>, set by <see cref="MainViewModel"/>). Propagated to every
    /// <see cref="DeviceConfigDeviceViewModel"/> so its live polling (see
    /// <see cref="DeviceConfigDeviceViewModel.SetScreenActive"/>) runs only while the tab is visible,
    /// independently of <see cref="IsSelected"/> (the device shown in the details pane).</summary>
    private bool _isScreenActive;

    /// <summary>Sets whether this tab is visible and the window is not minimized. Called by
    /// <see cref="MainViewModel"/> whenever the tab changes or the window is minimized/restored, and
    /// propagated to all known devices (visible and hidden) because <see cref="SelectedDevice"/> can refer to any of them.</summary>
    public void SetScreenActive(bool value)
    {
        _isScreenActive = value;

        foreach (var device in Devices.Concat(HiddenDevices))
        {
            device.SetScreenActive(value);
        }
    }

    public DeviceConfigViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
        UpdateDevices();
    }

    /// <summary>Reconciles <see cref="Devices"/> and <see cref="HiddenDevices"/> with all known devices, both
    /// connected and previously seen but currently disconnected (see <see cref="MainViewModel.GetAllKnownDevices"/>),
    /// instead of rebuilding the lists on every call. Runs on initial setup and every later device scan,
    /// including periodic hot-plug polling every few seconds. Rebuilding would repeatedly destroy controls
    /// being edited (e.g. on each keystroke in a rename field) and interrupt live monitoring of the selected device.</summary>
    public void UpdateDevices()
    {
        var knownDevices = _mainViewModel.GetAllKnownDevices();
        var seenDeviceIds = new HashSet<string>();

        foreach (var known in knownDevices)
        {
            seenDeviceIds.Add(known.Device.DeviceId);

            var existing = Devices.FirstOrDefault(d => d.Device.DeviceId == known.Device.DeviceId)
                ?? HiddenDevices.FirstOrDefault(d => d.Device.DeviceId == known.Device.DeviceId);
            if (existing is not null)
            {
                existing.UpdateConnectionState(known.Device, known.IsConnected);
                continue;
            }

            var settings = _mainViewModel.GetOrCreateDeviceSettings(known.Device.DeviceId);
            var deviceViewModel = new DeviceConfigDeviceViewModel(
                known.Device, settings, _mainViewModel.NotifyDeviceAvailabilityChanged,
                _mainViewModel.NotifyDeviceSettingsChanged, known.IsConnected);
            deviceViewModel.PropertyChanged += OnDevicePropertyChanged;
            deviceViewModel.SetScreenActive(_isScreenActive);

            (deviceViewModel.Hidden ? HiddenDevices : Devices).Add(deviceViewModel);
        }

        foreach (var stale in Devices.Concat(HiddenDevices).Where(d => !seenDeviceIds.Contains(d.Device.DeviceId)).ToList())
        {
            RemoveDevice(stale);
        }

        if (SelectedDevice is null || !Devices.Contains(SelectedDevice))
        {
            SelectedDevice = Devices.FirstOrDefault();
        }
    }

    /// <summary>Handles changes to <see cref="DeviceConfigDeviceViewModel.Hidden"/> (raised through
    /// <see cref="DeviceConfigDeviceViewModel.ToggleHiddenCommand"/>) and immediately moves the device between
    /// <see cref="Devices"/> and <see cref="HiddenDevices"/> without waiting for the next periodic scan.</summary>
    private void OnDevicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DeviceConfigDeviceViewModel.Hidden) || sender is not DeviceConfigDeviceViewModel device)
        {
            return;
        }

        if (device.Hidden)
        {
            Devices.Remove(device);
            if (!HiddenDevices.Contains(device))
            {
                HiddenDevices.Add(device);
            }

            if (SelectedDevice == device)
            {
                SelectedDevice = Devices.FirstOrDefault();
            }
        }
        else
        {
            HiddenDevices.Remove(device);
            if (!Devices.Contains(device))
            {
                Devices.Add(device);
            }

            SelectedDevice ??= device;
        }
    }

    private void RemoveDevice(DeviceConfigDeviceViewModel device)
    {
        device.PropertyChanged -= OnDevicePropertyChanged;
        Devices.Remove(device);
        HiddenDevices.Remove(device);
        device.Dispose();
    }

    partial void OnSelectedDeviceChanged(DeviceConfigDeviceViewModel? oldValue, DeviceConfigDeviceViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }
    }

    public void Dispose()
    {
        foreach (var device in Devices.Concat(HiddenDevices))
        {
            device.PropertyChanged -= OnDevicePropertyChanged;
            device.Dispose();
        }
    }
}
