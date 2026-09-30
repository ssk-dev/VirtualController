using System.Windows;
using System.Windows.Controls.Primitives;
using VirtualController.App.Diagnostics;
using VirtualController.App.ViewModels;
using VirtualController.Core.Updates;

namespace VirtualController.App.Views;

/// <summary>
/// Code-Behind des Hauptfensters. Enthaelt bewusst keine Geschaeftslogik - diese lebt komplett
/// im <see cref="MainViewModel"/>. Hier wird lediglich der DataContext gesetzt, ein initialer
/// Verbindungsversuch zum ViGEmBus-Treiber angestossen und beim Schliessen sauber aufgeraeumt.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += (_, _) => _viewModel.ConnectDriverCommand.Execute(null);

        // Automatische Update-Pruefung beim Start (nur falls "Automatisch auf Updates prüfen" aktiv ist,
        // siehe UpdateViewModel.RunStartupCheckAsync) - bewusst "fire-and-forget" (async void-artig ueber
        // den Lambda-Ausdruck) statt den Start des Fensters darauf warten zu lassen: die Pruefung selbst
        // blockiert dank interner Ausnahmebehandlung (UpdateCheckException wird dort verworfen) niemals
        // die Anwendung, auch nicht bei fehlender Internetverbindung.
        Loaded += (_, _) => _ = _viewModel.Update.RunStartupCheckAsync();

        // Zeigt das Update-Popup (siehe UpdateAvailableDialog), sobald eine neuere, noch nicht per
        // "Update ueberspringen" markierte Version gefunden wurde - ausgeloest sowohl von der
        // automatischen Start-Pruefung als auch vom manuellen "Auf Updates prüfen"-Button im
        // "Einstellungen"-Tab.
        _viewModel.Update.UpdateAvailable += OnUpdateAvailable;

        // Zeigt eine kurze Bildschirmbenachrichtigung (unten links, 2 Sekunden), wenn sich der aktive
        // Modus eines virtuellen Controllers tatsaechlich geaendert hat und dieser Controller
        // Benachrichtigungen aktiviert hat (siehe VirtualControllerViewModel.NotifyOnModeChange).
        _viewModel.ModeActivated += (vm, mode) => ModeChangeToast.Show(vm.Name, mode.Name);

        // Haelt MainViewModel.IsWindowMinimized aktuell: Window.WindowState laesst sich nicht direkt per
        // XAML-Binding an eine bool-Eigenschaft koppeln, daher hier ueber das StateChanged-Ereignis
        // manuell nachgezogen. Wird benoetigt, damit teures Live-Polling physischer Geraete (Mapping-
        // Tabellen-Hervorhebung, Achsen-Live-Vorschau der Geraetekonfiguration) waehrend der Minimierung
        // des Fensters komplett angehalten wird (siehe MainViewModel.RefreshScreenActiveStates) - eine
        // waehrend dieser Zeit ohnehin unsichtbare visuelle Rueckmeldung muss nicht berechnet werden.
        StateChanged += (_, _) => _viewModel.IsWindowMinimized = WindowState == WindowState.Minimized;

        // Aktualisiert zusaetzlich das Maximieren/Wiederherstellen-Glyph der eigenen Titelleiste (siehe
        // MainWindow.xaml, TitleBar-Border), da dessen Icon je nach WindowState zwischen "maximieren"
        // und "wiederherstellen" wechseln muss - ein reiner XAML-Trigger auf Window.WindowState kann
        // hierfuer nicht direkt am Button ansetzen, da WindowState keine mit einfachen DataTriggern
        // bindbare Eigenschaft dieses Buttons/TextBlocks ist.
        StateChanged += (_, _) => UpdateMaximizeRestoreGlyph();
        UpdateMaximizeRestoreGlyph();

        // Fix fuer den Bug "Ziel-Typ/Ziel-Wert wird nicht uebernommen": faengt jede Auswahl-Aenderung
        // einer beliebigen ComboBox innerhalb der (nun nach Ziel-Typ gruppierten, also auf mehrere
        // DataGrids verteilten) Mapping-Tabelle ab, um dort per UpdateSource() (siehe
        // OnAnyComboBoxSelectionChanged) den Wert zuverlaessig ins ViewModel zu uebernehmen. Die
        // Registrierung erfolgt bewusst am gemeinsamen aeusseren Vorfahren "MappingsScrollViewer"
        // statt an einer einzelnen DataGrid, da SelectionChangedEvent durch die dazwischenliegende
        // ItemsControl/DataTemplate-Verschachtelung unveraendert bis hierher durchbubbelt.
        MappingsScrollViewer.AddHandler(Selector.SelectionChangedEvent, new System.Windows.Controls.SelectionChangedEventHandler(OnAnyComboBoxSelectionChanged), true);
    }

    /// <summary>Minimiert das Fenster ueber den Minimieren-Button der eigenen Titelleiste (siehe
    /// MainWindow.xaml, TitleBar-Border) - Ersatz fuer die entfallene native Windows-Titelleiste.</summary>
    private void OnMinimizeButtonClicked(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>Wechselt zwischen maximiertem und normalem Fensterzustand ueber den Maximieren/
    /// Wiederherstellen-Button der eigenen Titelleiste - Ersatz fuer die entfallene native Windows-
    /// Titelleiste. Das Icon selbst wird ueber <see cref="UpdateMaximizeRestoreGlyph"/> aktuell gehalten.</summary>
    private void OnMaximizeRestoreButtonClicked(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>Schliesst das Fenster ueber den Schliessen-Button der eigenen Titelleiste - Ersatz fuer
    /// die entfallene native Windows-Titelleiste. Loest wie gewohnt <see cref="MainWindow_OnClosing"/> aus.</summary>
    private void OnCloseButtonClicked(object sender, RoutedEventArgs e) => Close();

    /// <summary>Aktualisiert das Icon des Maximieren/Wiederherstellen-Buttons der eigenen Titelleiste
    /// anhand des aktuellen <see cref="Window.WindowState"/> - zeigt das "Maximieren"-Glyph (Segoe MDL2
    /// Assets E922), solange das Fenster normal/minimiert ist, und das "Wiederherstellen"-Glyph (E923),
    /// solange es maximiert ist. Ein reiner XAML-Trigger auf WindowState kann hierfuer nicht direkt am
    /// Button ansetzen, da WindowState keine mit einfachen DataTriggern bindbare Eigenschaft dieses
    /// Buttons/TextBlocks ist - daher hier ueber das StateChanged-Ereignis manuell nachgezogen (siehe
    /// Konstruktor).</summary>
    private void UpdateMaximizeRestoreGlyph()
        => MaximizeRestoreGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";

    private void OnMappingsScrollViewerPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        // Fix fuer den Bug "Mapping-Tabelle laesst sich mit dem Mausrad nicht scrollen, wenn sich der
        // Mauszeiger ueber einer DataGrid-Zeile innerhalb der Tabelle befindet": WPFs DataGrid besitzt
        // (als Teil seines Standard-Steuerelement-Templates) einen eigenen internen ScrollViewer, der
        // das MouseWheel-Ereignis IMMER als Handled markiert, sobald sich der Mauszeiger ueber einer
        // Zeile befindet - unabhaengig davon, ob dieser innere DataGrid ueberhaupt scrollen muesste.
        // Dadurch bubbelt das Ereignis niemals bis zu diesem aeusseren ScrollViewer hoch. Da
        // PreviewMouseWheel jedoch ein Tunneling-Event ist, erreicht es diesen aeusseren ScrollViewer
        // bereits VOR dem inneren DataGrid-ScrollViewer. Hier wird daher manuell um das Mausrad-Delta
        // gescrollt und das Ereignis als Handled markiert, sodass das eigentliche (fehlerhafte)
        // Scroll-Verhalten des inneren DataGrid komplett uebersprungen wird und ausschliesslich dieser
        // aeussere ScrollViewer scrollt.
        if (sender is not System.Windows.Controls.ScrollViewer scrollViewer)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void OnAnyComboBoxSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Fix fuer den Bug "Ziel-Typ/Ziel-Wert wird nicht uebernommen bzw. faellt auf den alten
        // Wert zurueck": ComboBoxen in DataGridTemplateColumn-Zellen wechseln beim Auswaehlen eines
        // Eintrags nicht automatisch/zuverlaessig in den Zellen-Bearbeitungsmodus. Dadurch loest WPF
        // den Ruecktransport (UpdateSource) des Zwei-Wege-Bindings von SelectedItem/SelectedValue oft
        // gar nicht oder erst durch einen zufaelligen Nebeneffekt (z.B. Fokuswechsel Sekunden spaeter)
        // aus - das Binding selbst bleibt dabei die ganze Zeit "Active" und fehlerfrei, weshalb dies
        // durch reine Bindungspruefung nicht auffaellt. Durch das explizite Erzwingen von
        // UpdateSource() direkt hier (SelectionChanged bubbelt von jeder ComboBox im DataGrid bis zu
        // diesem Fenster-weiten Handler durch) wird der Wert garantiert sofort ins ViewModel
        // uebernommen, unabhaengig vom Zellen-Fokuszustand.
        if (e.OriginalSource is System.Windows.Controls.ComboBox comboBox)
        {
            System.Windows.Data.BindingOperations.GetBindingExpression(comboBox, Selector.SelectedItemProperty)?.UpdateSource();
            System.Windows.Data.BindingOperations.GetBindingExpression(comboBox, Selector.SelectedValueProperty)?.UpdateSource();
        }
    }

    private void MainWindow_OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _viewModel.Update.UpdateAvailable -= OnUpdateAvailable;
        _viewModel.Dispose();
    }

    private void OnAssignInputClicked(object sender, RoutedEventArgs e)
    {
        // Der "Zuweisen"-Button lebt innerhalb der Zeile der Mapping-DataGrid, deren DataContext das
        // zugehoerige MappingRowViewModel ist (siehe MainWindow.xaml, DataTemplate DataType="{x:Type
        // vm:MappingRowViewModel}") - so laesst sich ohne CommandParameter/Command-Bindung ermitteln,
        // fuer welche konkrete Zeile der Dialog geoeffnet werden soll.
        if (((FrameworkElement)sender).DataContext is not MappingRowViewModel row)
        {
            return;
        }

        var dialogViewModel = new AssignInputDialogViewModel(row.BuildAssignableInputs());
        var dialog = new AssignInputDialog(dialogViewModel) { Owner = this };

        if (dialog.ShowDialog() == true && dialogViewModel.SelectedInput is not null)
        {
            row.AssignInput(dialogViewModel.SelectedInput);
        }
    }

    private void OnAssignSwitchTriggerClicked(object sender, RoutedEventArgs e)
    {
        // Analog zu OnAssignInputClicked: der "Zuweisen"-Button fuer den Switch-Trigger lebt innerhalb
        // des Tab-Labels eines Modus, deren DataContext das zugehoerige ModeViewModel ist (siehe
        // MainWindow.xaml, DataTemplate DataType="{x:Type vm:ModeViewModel}" in der Modus-Tab-Leiste).
        if (((FrameworkElement)sender).DataContext is not ModeViewModel mode)
        {
            return;
        }

        var dialogViewModel = new AssignInputDialogViewModel(mode.BuildAssignableInputs());
        var dialog = new AssignInputDialog(dialogViewModel) { Owner = this };

        if (dialog.ShowDialog() == true && dialogViewModel.SelectedInput is not null)
        {
            mode.AssignSwitchTrigger(dialogViewModel.SelectedInput);
        }
    }

    private void OnAssignToggleTriggerClicked(object sender, RoutedEventArgs e)
    {
        // Analog zu OnAssignSwitchTriggerClicked: der "Zuweisen"-Button fuer den controller-weiten
        // Toggle-Trigger lebt im Abschnitt "Modi Switch / Toggle", deren DataContext der aktuell
        // ausgewaehlte Controller ist (SelectedController, also ein VirtualControllerViewModel).
        if (((FrameworkElement)sender).DataContext is not VirtualControllerViewModel controller)
        {
            return;
        }

        var dialogViewModel = new AssignInputDialogViewModel(controller.BuildAssignableToggleTriggerInputs());
        var dialog = new AssignInputDialog(dialogViewModel) { Owner = this };

        if (dialog.ShowDialog() == true && dialogViewModel.SelectedInput is not null)
        {
            controller.AssignToggleTrigger(dialogViewModel.SelectedInput);
        }
    }

    private void OnModeTabBorderPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Fix fuer die Bugs "beim Klick auf einen Modus-Tab wird der falsche Tab ausgewaehlt" und
        // "die Mapping-Tabelle zeigt beim Modus-Wechsel nicht die passenden Mappings an" (beide sind
        // tatsaechlich dieselbe Ursache: SelectedMode wurde beim Klick nicht zuverlaessig aktualisiert).
        // Fruehere Versuche haengten den Handler an das ListBoxItem bzw. die ListBox selbst und mussten
        // per Hit-Test/ItemContainerGenerator vom angeklickten Element auf das zugehoerige Datenelement
        // zurueckschliessen - das erwies sich als unzuverlaessig. Der Handler haengt daher jetzt direkt am
        // Wurzel-Border jedes Tabs innerhalb des DataTemplate (siehe MainWindow.xaml): dessen DataContext
        // ist durch den DataTemplate-Mechanismus GARANTIERT genau das ModeViewModel dieses Tabs - es ist
        // also kein Hit-Test/Container-Lookup mehr noetig, sondern nur ein direkter Zugriff auf
        // DataContext. Anschliessend wird lediglich noch die umschliessende ListBox gesucht, um deren
        // SelectedItem (zwei-Wege gebunden an VirtualControllerViewModel.SelectedMode) explizit zu setzen.
        // Da PreviewMouseLeftButtonDown ein Tunneling-Event ist und e.Handled bewusst NICHT gesetzt wird,
        // laeuft das eigentliche Klickverhalten verschachtelter Buttons/TextBox/CheckBox danach unveraendert
        // weiter.
        //
        // Temporaeres Debug-Logging (siehe VirtualController.App.Diagnostics.DebugLog) zur gezielten
        // Fehlersuche des Bugs "falscher Tab wird ausgewaehlt": protokolliert jeden Klick, welches Modus
        // als DataContext erkannt wurde, ob eine ListBox gefunden wurde und was tatsaechlich als
        // SelectedItem gesetzt wurde - damit sich anhand der Log-Datei nachvollziehen laesst, ob dieser
        // Handler ueberhaupt ausgefuehrt wird und mit welchem Ergebnis.
        var originalSourceInfo = (e.OriginalSource as System.Windows.FrameworkElement)?.GetType().Name ?? e.OriginalSource?.GetType().Name ?? "null";
        DebugLog.Write($"[ModeTab] PreviewMouseLeftButtonDown ausgeloest. sender={sender.GetType().Name} OriginalSource={originalSourceInfo}");

        if (sender is not System.Windows.FrameworkElement border || border.DataContext is not ModeViewModel mode)
        {
            DebugLog.Write($"[ModeTab] Abbruch: sender ist kein FrameworkElement mit ModeViewModel-DataContext (DataContext={(sender as System.Windows.FrameworkElement)?.DataContext?.GetType().Name ?? "null"}).");
            return;
        }

        DebugLog.Write($"[ModeTab] Geklicktes Modus-DataContext = '{mode.Name}' (Id={mode.Mode.Id}).");

        if (FindVisualAncestor<System.Windows.Controls.ListBox>(border) is not { } listBox)
        {
            DebugLog.Write("[ModeTab] Abbruch: keine umschliessende ListBox gefunden.");
            return;
        }

        DebugLog.Write($"[ModeTab] Vor SelectedItem-Zuweisung: listBox.SelectedItem war '{(listBox.SelectedItem as ModeViewModel)?.Name ?? "null"}'.");
        listBox.SelectedItem = mode;
        DebugLog.Write($"[ModeTab] Nach SelectedItem-Zuweisung: listBox.SelectedItem ist jetzt '{(listBox.SelectedItem as ModeViewModel)?.Name ?? "null"}'.");
    }

    private void OnMappingsDataGridTargetUpdated(object sender, System.Windows.Data.DataTransferEventArgs e)
    {
        // Diagnose-Logging fuer den Bug "Mapping-Tabelle aktualisiert sich beim Modus-Wechsel waehrend
        // der Controller laeuft nicht": TargetUpdated feuert genau dann, wenn das ItemsSource-Binding
        // einer der (nach Ziel-Typ gruppierten) Mapping-DataGrids tatsaechlich einen neuen Wert ins
        // UI-Steuerelement uebernommen hat - das ist die "Ground Truth" dessen, was wirklich angezeigt
        // wird, im Gegensatz zu dem, was das ViewModel ueber SelectedMode/ActiveModeId zu wissen glaubt
        // (siehe DebugLog-Aufrufe in VirtualControllerViewModel.OnSelectedModeChanged). Der DataContext
        // dieser DataGrid ist seit der Gruppierung nach Ziel-Typ keine VirtualControllerViewModel mehr,
        // sondern die MappingGroupViewModel der jeweiligen Gruppe (Button/Achse/Trigger/D-Pad) - daher
        // wird hier deren Header/Kind statt des Controller-Namens protokolliert. Wenn dieser Handler
        // beim Tab-Wechsel waehrend der Laufzeit NICHT feuert (oder mit falscher Mapping-Anzahl/falschem
        // Gruppen-Header), liegt der Fehler tatsaechlich im UI-Rendering/Binding und nicht in der
        // ViewModel-Logik.
        if (sender is not System.Windows.Controls.DataGrid grid)
        {
            return;
        }

        string groupInfo = grid.DataContext is MappingGroupViewModel group
            ? $"Gruppe='{group.Header}' (Kind={group.Kind})"
            : "Gruppe=<keine MappingGroupViewModel als DataContext>";

        int itemCount = grid.ItemsSource is System.Collections.ICollection collection
            ? collection.Count
            : grid.ItemsSource?.Cast<object>().Count() ?? -1;

        DebugLog.Write($"[MappingsGrid] TargetUpdated: {groupInfo} AngezeigteMappingAnzahl={itemCount}");
    }

    /// <summary>
    /// Zeigt das Update-Popup (siehe <see cref="UpdateAvailableDialog"/>) fuer <paramref name="details"/>
    /// an - aufgerufen sowohl von der automatischen Start-Pruefung als auch vom manuellen "Auf Updates
    /// prüfen"-Button (siehe <see cref="MainViewModel.Update"/>). Hat der Nutzer im Popup erfolgreich
    /// eine Installation gestartet (<see cref="UpdateAvailableDialog.InstallationStarted"/>), wird die
    /// Anwendung anschliessend beendet, damit der bereits gestartete, separate Updater-Prozess die
    /// aktuell durch die laufende EXE gesperrten Installationsdateien ueberschreiben und die neue Version
    /// starten kann (siehe VirtualController.Core.Updates.UpdateInstaller).
    /// </summary>
    private void OnUpdateAvailable(UpdateCheckResult details)
    {
        var dialogViewModel = _viewModel.Update.CreateAvailableDialogViewModel(details);
        var dialog = new UpdateAvailableDialog(dialogViewModel) { Owner = this };
        dialog.InstallationStarted += () => System.Windows.Application.Current.Shutdown();
        dialog.ShowDialog();
    }

    /// <summary>
    /// Oeffnet den "Version wechseln"-Dialog (<see cref="RollbackDialog"/>), ueber den der Nutzer gezielt
    /// zu einer beliebigen an der Update-Quelle verfuegbaren Version wechseln kann - einschliesslich
    /// aelterer Versionen (Rollback), was die reguläre Update-Pruefung bewusst nicht anbietet. Hat der
    /// Nutzer erfolgreich eine Installation gestartet (<see cref="RollbackDialog.InstallationStarted"/>),
    /// wird die Anwendung anschliessend beendet, analog zu <see cref="OnUpdateAvailable"/>.
    /// </summary>
    private void RollbackButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialogViewModel = _viewModel.Update.CreateRollbackDialogViewModel();
        var dialog = new RollbackDialog(dialogViewModel) { Owner = this };
        dialog.InstallationStarted += () => System.Windows.Application.Current.Shutdown();
        dialog.ShowDialog();
    }

    private static T? FindVisualAncestor<T>(System.Windows.DependencyObject start) where T : System.Windows.DependencyObject
    {
        var current = start;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}

