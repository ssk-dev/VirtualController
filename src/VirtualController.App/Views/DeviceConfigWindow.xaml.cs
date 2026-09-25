using System.Windows;
using VirtualController.App.ViewModels;

namespace VirtualController.App.Views;

/// <summary>
/// Code-Behind des "Geraete konfigurieren"-Dialogs. Enthaelt bewusst keine Geschaeftslogik - diese
/// lebt komplett im <see cref="DeviceConfigViewModel"/>. Beim Schliessen wird der aufrufenden
/// <see cref="MainViewModel"/>-Instanz als Sicherheitsnetz mitgeteilt, dass sich die Verfuegbarkeit von
/// Geraeten geaendert haben koennte (siehe <see cref="MainViewModel.NotifyDeviceAvailabilityChanged"/>),
/// damit die Geraeteauswahl neu gefiltert und laufende Sessions aktualisiert werden. Reine
/// Einstellungsaenderungen (Umbenennung, Kalibrierung etc.) werden dagegen bereits waehrend der
/// Bearbeitung ueber den leichtgewichtigen <see cref="MainViewModel.NotifyDeviceSettingsChanged"/>-Pfad
/// verteilt, siehe <see cref="DeviceConfigViewModel"/>.
/// </summary>
public partial class DeviceConfigWindow : Window
{
    private readonly MainViewModel _mainViewModel;
    private readonly DeviceConfigViewModel _viewModel;

    public DeviceConfigWindow(MainViewModel mainViewModel)
    {
        InitializeComponent();
        _mainViewModel = mainViewModel;
        _viewModel = new DeviceConfigViewModel(mainViewModel);
        DataContext = _viewModel;
        Closed += (_, _) => _viewModel.Dispose();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        _mainViewModel.NotifyDeviceAvailabilityChanged();
        Close();
    }
}
