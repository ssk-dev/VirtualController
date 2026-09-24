using System.Windows;
using VirtualController.App.ViewModels;

namespace VirtualController.App.Views;

/// <summary>
/// Code-Behind des "Geraete konfigurieren"-Dialogs. Enthaelt bewusst keine Geschaeftslogik - diese
/// lebt komplett im <see cref="DeviceConfigViewModel"/>. Beim Schliessen wird der aufrufenden
/// <see cref="MainViewModel"/>-Instanz mitgeteilt, dass sich Geraeteeinstellungen geaendert haben
/// koennten, damit die Geraeteauswahl neu gefiltert und laufende Sessions aktualisiert werden.
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
        _mainViewModel.NotifyDeviceSettingsChanged();
        Close();
    }
}
