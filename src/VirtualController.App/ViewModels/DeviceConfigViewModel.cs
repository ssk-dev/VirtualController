using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Wurzel-ViewModel des "Geraetekonfiguration"-Tabs: zeigt alle bekannten physischen Geraete (auch
/// bereits deaktivierte, damit sie wieder aktiviert werden koennen, sowie aktuell getrennte, aber
/// zuvor schon konfigurierte Geraete - siehe <see cref="MainViewModel.GetAllKnownDevices"/>) mit
/// der Moeglichkeit, das gesamte Geraet oder einzelne Eingaben zu deaktivieren und Eingaben
/// umzubenennen. Aenderungen wirken sofort auf die im <see cref="MainViewModel"/> gehaltenen
/// <see cref="DeviceSettings"/> und werden per <see cref="MainViewModel.NotifyDeviceSettingsChanged"/>
/// an die Geraeteauswahl und alle laufenden Sessions weitergereicht. Die Geraete werden als Liste mit
/// Detailbereich dargestellt (<see cref="SelectedDevice"/>, siehe MainWindow.xaml), analog zum
/// Mapping-Tab. <see cref="UpdateDevices"/> gleicht die Liste bei jedem Geraete-Scan (inkl. dem
/// periodischen Hotplug-Polling in <see cref="MainViewModel"/>) inkrementell ab, statt sie zu
/// verwerfen und neu aufzubauen, damit laufende Bearbeitungen und die Live-Ueberwachung des
/// ausgewaehlten Geraets dabei nicht unterbrochen werden.
/// </summary>
public sealed partial class DeviceConfigViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _mainViewModel;

    /// <summary>Sichtbare (nicht ausgeblendete) Geraete, dargestellt in der Hauptliste dieses Tabs.</summary>
    public ObservableCollection<DeviceConfigDeviceViewModel> Devices { get; } = new();

    /// <summary>Vom Nutzer manuell ausgeblendete Geraete (siehe <see cref="DeviceSettings.Hidden"/>), dargestellt
    /// in einer separaten, einklappbaren Liste unterhalb der Hauptliste, mit der Moeglichkeit, sie jederzeit
    /// wieder einzublenden (siehe MainWindow.xaml). Gedacht u.a. fuer die eigenen, per ViGEmBus emulierten
    /// virtuellen Controller dieser Anwendung, die von XInput/DirectInput nicht von echter Hardware
    /// unterschieden werden koennen und daher nicht automatisch gefiltert werden.</summary>
    public ObservableCollection<DeviceConfigDeviceViewModel> HiddenDevices { get; } = new();

    [ObservableProperty]
    private DeviceConfigDeviceViewModel? _selectedDevice;

    /// <summary>Ob der "Gerätekonfiguration"-Tab des Hauptfensters aktuell tatsaechlich sichtbar ist UND
    /// das Fenster nicht minimiert ist (siehe <see cref="SetScreenActive"/>, gesetzt durch
    /// <see cref="MainViewModel"/>). Wird an jedes <see cref="DeviceConfigDeviceViewModel"/> weitergereicht,
    /// damit dessen Live-Polling (siehe <see cref="DeviceConfigDeviceViewModel.SetScreenActive"/>) nur
    /// laeuft, waehrend dieser Tab tatsaechlich sichtbar ist - unabhaengig von <see cref="IsSelected"/>
    /// (welches Geraet im Detailbereich angezeigt wird).</summary>
    private bool _isScreenActive;

    /// <summary>Legt fest, ob dieser Tab aktuell tatsaechlich sichtbar ist UND das Fenster nicht minimiert
    /// ist - wird von <see cref="MainViewModel"/> bei jedem Tab-Wechsel bzw. Minimieren/Wiederherstellen
    /// aufgerufen und an alle bekannten Geraete (sichtbare und ausgeblendete) weitergereicht, da
    /// <see cref="SelectedDevice"/> theoretisch auf ein beliebiges davon zeigen kann.</summary>
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

    /// <summary>Gleicht <see cref="Devices"/> und <see cref="HiddenDevices"/> anhand der aktuell bekannten
    /// Geraete (verbunden + bereits zuvor bekannte, aber aktuell getrennte - siehe
    /// <see cref="MainViewModel.GetAllKnownDevices"/>) ab, statt die Listen bei jedem Aufruf komplett zu
    /// verwerfen und neu aufzubauen. Wird nicht nur beim erstmaligen Aufbau, sondern auch bei jedem
    /// nachfolgenden Geraete-Scan aufgerufen (inkl. dem periodischen Hotplug-Polling in MainViewModel, ca.
    /// alle paar Sekunden) - ein destruktiver Neuaufbau wuerde dabei staendig genau das Steuerelement
    /// zerstoeren, das der Nutzer gerade bearbeitet (z.B. bei jedem Tastendruck in einem
    /// Umbenennungs-Feld), sowie die laufende Live-Ueberwachung des ausgewaehlten Geraets unterbrechen.</summary>
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
                known.Device, settings, _mainViewModel.NotifyDeviceSettingsChanged, known.IsConnected);
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

    /// <summary>Reagiert auf <see cref="DeviceConfigDeviceViewModel.Hidden"/>-Aenderungen (ausgeloest ueber
    /// <see cref="DeviceConfigDeviceViewModel.ToggleHiddenCommand"/>) und verschiebt das betroffene Geraet
    /// sofort zwischen <see cref="Devices"/> und <see cref="HiddenDevices"/>, ohne auf den naechsten
    /// periodischen Geraete-Scan warten zu muessen.</summary>
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
