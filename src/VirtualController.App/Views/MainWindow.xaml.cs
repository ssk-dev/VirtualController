using System.Windows;
using System.Windows.Controls.Primitives;
using VirtualController.App.Diagnostics;
using VirtualController.App.ViewModels;

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

        // Fix fuer den Bug "Ziel-Typ/Ziel-Wert wird nicht uebernommen": faengt jede Auswahl-Aenderung
        // einer beliebigen ComboBox innerhalb der (nun nach Ziel-Typ gruppierten, also auf mehrere
        // DataGrids verteilten) Mapping-Tabelle ab, um dort per UpdateSource() (siehe
        // OnAnyComboBoxSelectionChanged) den Wert zuverlaessig ins ViewModel zu uebernehmen. Die
        // Registrierung erfolgt bewusst am gemeinsamen aeusseren Vorfahren "MappingsScrollViewer"
        // statt an einer einzelnen DataGrid, da SelectionChangedEvent durch die dazwischenliegende
        // ItemsControl/DataTemplate-Verschachtelung unveraendert bis hierher durchbubbelt.
        MappingsScrollViewer.AddHandler(Selector.SelectionChangedEvent, new System.Windows.Controls.SelectionChangedEventHandler(OnAnyComboBoxSelectionChanged), true);
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

