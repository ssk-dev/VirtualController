using System.Collections.Generic;
using System.Windows;
using VirtualController.App.ViewModels;

namespace VirtualController.App.Views;

/// <summary>
/// Nicht-modales Echtzeit-Anzeigefenster fuer den Hardware-Benchmark eines einzelnen Geraets (siehe
/// <see cref="BenchmarkWindowViewModel"/>). Enthaelt bewusst keine Geschaeftslogik - diese lebt komplett
/// im <see cref="BenchmarkWindowViewModel"/> bzw. im zugrunde liegenden
/// <see cref="DeviceConfigDeviceViewModel"/> (Start/Stop der Sitzung selbst).
///
/// Pro Geraet existiert hoechstens eine Instanz gleichzeitig (siehe <see cref="ShowFor"/>, analog zum
/// Singleton-Muster von <see cref="ModeChangeToast"/>): ein erneuter Aufruf fuer dasselbe Geraet aktiviert
/// lediglich das bereits offene Fenster, statt ein weiteres zu oeffnen. Schliessen des Fensters (Klick auf
/// X oder erneuter Klick auf den zugehoerigen "Benchmark-Anzeige"-Button) beendet NICHT die laufende
/// Benchmark-Sitzung - diese laeuft im Hintergrund weiter, bis der Nutzer sie explizit per Start/Stop-Button
/// (auch innerhalb dieses Fensters vorhanden) stoppt.
/// </summary>
public partial class BenchmarkWindow : Window
{
    /// <summary>Bereits offene Fenster, geschluesselt nach dem zugrunde liegenden Geraete-ViewModel -
    /// siehe <see cref="ShowFor"/>.</summary>
    private static readonly Dictionary<DeviceConfigDeviceViewModel, BenchmarkWindow> OpenWindows = new();

    private readonly BenchmarkWindowViewModel _viewModel;

    private BenchmarkWindow(BenchmarkWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    /// <summary>Oeffnet das Echtzeit-Anzeigefenster fuer <paramref name="device"/>, bzw. aktiviert (bringt
    /// in den Vordergrund) ein fuer dieses Geraet bereits offenes Fenster.</summary>
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
