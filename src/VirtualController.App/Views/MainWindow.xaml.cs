using System.Windows;
using System.Windows.Controls.Primitives;
using VirtualController.App.Diagnostics;
using VirtualController.App.ViewModels;
using VirtualController.Core.Updates;

namespace VirtualController.App.Views;

/// <summary>
/// Main window code-behind. Business logic lives in <see cref="MainViewModel"/>; this class sets the
/// DataContext, starts the initial ViGEmBus connection attempt, and performs cleanup when closing.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += (_, _) => _viewModel.ConnectDriverCommand.Execute(null);

        // Run the startup update check only when "Check for updates automatically" is enabled (see
        // UpdateViewModel.RunStartupCheckAsync). Fire and forget rather than delaying window startup;
        // internal exception handling ensures an update check never blocks the app, even without internet.
        Loaded += (_, _) => _ = _viewModel.Update.RunStartupCheckAsync();

        // Show the update dialog (see UpdateAvailableDialog) when a newer, non-skipped version is found,
        // either by the automatic startup check or the manual "Check for updates" button on the Settings tab.
        _viewModel.Update.UpdateAvailable += OnUpdateAvailable;

        // Show a brief on-screen notification (bottom left, two seconds) when a virtual controller's active
        // mode changes and notifications are enabled (see VirtualControllerViewModel.NotifyOnModeChange).
        _viewModel.ModeActivated += (vm, mode) => ModeChangeToast.Show(vm.Name, mode.Name);

        // Keep MainViewModel.IsWindowMinimized up to date. Window.WindowState cannot be bound directly to a
        // bool property in XAML, so update it through StateChanged. This lets us pause expensive physical
        // device polling while the window is minimized, when mapping highlights and the device configuration
        // axis preview cannot be seen (see MainViewModel.RefreshScreenActiveStates).
        StateChanged += (_, _) => _viewModel.IsWindowMinimized = WindowState == WindowState.Minimized;

        // Also update the custom title bar's maximize/restore glyph (see the TitleBar border in
        // MainWindow.xaml) for the current WindowState. A plain XAML trigger cannot bind Window.WindowState
        // directly to this button or TextBlock.
        StateChanged += (_, _) => UpdateMaximizeRestoreGlyph();
        UpdateMaximizeRestoreGlyph();

        // Work around target type/value selections not reaching the view model. Handle selection changes
        // from every ComboBox in the target-type-grouped mapping table and explicitly call UpdateSource()
        // (see OnAnyComboBoxSelectionChanged). Register on the shared MappingsScrollViewer ancestor rather
        // than on one DataGrid because SelectionChangedEvent bubbles through the ItemsControl/DataTemplate nesting.
        MappingsScrollViewer.AddHandler(Selector.SelectionChangedEvent, new System.Windows.Controls.SelectionChangedEventHandler(OnAnyComboBoxSelectionChanged), true);

        // Apply the same workaround to CheckBoxes ("Invert" and "This direction only"). The cell may not enter
        // edit mode on click, so UpdateSource() is not called for the two-way IsChecked binding. The CheckBox
        // still visibly toggles because ToggleButton updates its local IsChecked dependency property, hiding
        // the missing view-model update. CheckedEvent/UncheckedEvent bubble to this shared ancestor just like
        // Selector.SelectionChangedEvent, regardless of ItemsControl/DataTemplate nesting.
        MappingsScrollViewer.AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent, new RoutedEventHandler(OnAnyCheckBoxCheckedChanged), true);
        MappingsScrollViewer.AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent, new RoutedEventHandler(OnAnyCheckBoxCheckedChanged), true);
    }

    /// <summary>Minimizes the window through the custom title bar button (see the TitleBar border in
    /// MainWindow.xaml), replacing the removed native title bar.</summary>
    private void OnMinimizeButtonClicked(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>Toggles between maximized and normal through the custom title bar button, replacing the
    /// removed native title bar. The icon is updated by <see cref="UpdateMaximizeRestoreGlyph"/>.</summary>
    private void OnMaximizeRestoreButtonClicked(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>Closes the window through the custom title bar button, replacing the removed native title bar.
    /// Raises <see cref="MainWindow_OnClosing"/> as usual.</summary>
    private void OnCloseButtonClicked(object sender, RoutedEventArgs e) => Close();

    /// <summary>Updates the custom title bar's maximize/restore icon for the current
    /// <see cref="Window.WindowState"/>. Shows the maximize glyph (Segoe MDL2 Assets E922) when normal or
    /// minimized and the restore glyph (E923) when maximized. A plain XAML trigger cannot bind WindowState
    /// to this button or TextBlock, so the constructor updates it through StateChanged.</summary>
    private void UpdateMaximizeRestoreGlyph()
        => MaximizeRestoreGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";

    private void OnMappingsScrollViewerPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        // Work around the mapping table not scrolling with the mouse wheel over a DataGrid row. WPF's default
        // DataGrid template contains an inner ScrollViewer that marks MouseWheel handled whenever the pointer
        // is over a row, even if the grid itself cannot scroll, so the event never bubbles to this outer
        // ScrollViewer. PreviewMouseWheel tunnels through the outer viewer before reaching the inner one, so
        // scroll the outer viewer manually and mark the event handled to bypass the inner viewer.
        if (sender is not System.Windows.Controls.ScrollViewer scrollViewer)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void OnAnyComboBoxSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Work around target type/value selections not reaching the view model or reverting. ComboBoxes in
        // DataGridTemplateColumn cells do not reliably enter edit mode when an item is selected, so WPF may
        // not call UpdateSource() for the SelectedItem/SelectedValue two-way binding until an unrelated event
        // such as a later focus change. The binding remains active and error-free, so normal binding checks
        // do not reveal the problem. Explicitly call UpdateSource() here; SelectionChanged bubbles from every
        // ComboBox in the DataGrid to this window-level handler, guaranteeing an immediate update regardless
        // of cell focus.
        if (e.OriginalSource is System.Windows.Controls.ComboBox comboBox)
        {
            System.Windows.Data.BindingOperations.GetBindingExpression(comboBox, Selector.SelectedItemProperty)?.UpdateSource();
            System.Windows.Data.BindingOperations.GetBindingExpression(comboBox, Selector.SelectedValueProperty)?.UpdateSource();
        }
    }

    private void OnAnyCheckBoxCheckedChanged(object sender, RoutedEventArgs e)
    {
        // Apply the same workaround as OnAnyComboBoxSelectionChanged to CheckBoxes ("Invert" and "This
        // direction only"). ToggleButton immediately updates its own IsChecked dependency property, so the
        // CheckBox visibly toggles even if the DataGridTemplateColumn cell never enters edit mode. In that
        // case WPF does not update the view model, though the binding remains active and error-free. Explicitly
        // call UpdateSource() here; Checked/Unchecked bubbles from every CheckBox in the DataGrid to this
        // window-level handler, guaranteeing an immediate update regardless of cell focus.
        if (e.OriginalSource is System.Windows.Controls.CheckBox checkBox)
        {
            System.Windows.Data.BindingOperations.GetBindingExpression(checkBox, System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty)?.UpdateSource();
        }
    }

    private void MainWindow_OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _viewModel.Update.UpdateAvailable -= OnUpdateAvailable;
        _viewModel.Dispose();
    }

    private void OnAssignInputClicked(object sender, RoutedEventArgs e)
    {
        // The Assign button is inside a mapping DataGrid row whose DataContext is its MappingRowViewModel
        // (see the MappingRowViewModel DataTemplate in MainWindow.xaml). This identifies the target row
        // without a CommandParameter or command binding.
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
        // Like OnAssignInputClicked, the switch trigger's Assign button is inside a mode tab whose DataContext
        // is its ModeViewModel (see the ModeViewModel DataTemplate in MainWindow.xaml).
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
        // Like OnAssignSwitchTriggerClicked, the controller-wide toggle trigger's Assign button is in the
        // Mode switch/toggle section, whose DataContext is the selected VirtualControllerViewModel.
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
        // Work around incorrect mode tab selection and the mapping table showing the wrong rows; both came
        // from SelectedMode not being updated reliably on click. Earlier attempts attached this handler to
        // the ListBoxItem/ListBox and used hit testing or ItemContainerGenerator to find the data item, which
        // was unreliable. Attach it to each tab's root Border instead (see MainWindow.xaml): its DataContext
        // is guaranteed to be that tab's ModeViewModel, so no container lookup is needed. Find the enclosing
        // ListBox and explicitly set its SelectedItem (two-way bound to VirtualControllerViewModel.SelectedMode).
        // PreviewMouseLeftButtonDown tunnels, and e.Handled remains false so nested buttons, text boxes, and
        // checkboxes retain their normal click behavior.
        //
        // Temporary diagnostics (see VirtualController.App.Diagnostics.DebugLog) for incorrect tab selection:
        // log each click, the recognized ModeViewModel, whether a ListBox was found, and the SelectedItem set.
        var originalSourceInfo = (e.OriginalSource as System.Windows.FrameworkElement)?.GetType().Name ?? e.OriginalSource?.GetType().Name ?? "null";
        DebugLog.Write($"[ModeTab] PreviewMouseLeftButtonDown raised. sender={sender.GetType().Name} OriginalSource={originalSourceInfo}");

        if (sender is not System.Windows.FrameworkElement border || border.DataContext is not ModeViewModel mode)
        {
            DebugLog.Write($"[ModeTab] Aborted: sender is not a FrameworkElement with a ModeViewModel DataContext (DataContext={(sender as System.Windows.FrameworkElement)?.DataContext?.GetType().Name ?? "null"}).");
            return;
        }

        DebugLog.Write($"[ModeTab] Geklicktes Modus-DataContext = '{mode.Name}' (Id={mode.Mode.Id}).");

        if (FindVisualAncestor<System.Windows.Controls.ListBox>(border) is not { } listBox)
        {
            DebugLog.Write("[ModeTab] Aborted: no enclosing ListBox found.");
            return;
        }

        DebugLog.Write($"[ModeTab] Before SelectedItem assignment: listBox.SelectedItem was '{(listBox.SelectedItem as ModeViewModel)?.Name ?? "null"}'.");
        listBox.SelectedItem = mode;
        DebugLog.Write($"[ModeTab] After SelectedItem assignment: listBox.SelectedItem is now '{(listBox.SelectedItem as ModeViewModel)?.Name ?? "null"}'.");
    }

    private void OnMappingsDataGridTargetUpdated(object sender, System.Windows.Data.DataTransferEventArgs e)
    {
        // Diagnostics for the mapping table not updating when switching modes while the controller runs.
        // TargetUpdated fires when a grouped DataGrid's ItemsSource binding applies a new value to the UI,
        // the ground truth of what is displayed versus what the view model reports (see debug logs in
        // VirtualControllerViewModel.OnSelectedModeChanged). Each DataGrid's DataContext is now its
        // MappingGroupViewModel (button/axis/trigger/D-pad), not VirtualControllerViewModel, so log the
        // group header/kind instead of the controller name. If this handler does not fire on a mode switch,
        // or reports the wrong row count/group header, the problem is in UI rendering/binding rather than view-model logic.
        if (sender is not System.Windows.Controls.DataGrid grid)
        {
            return;
        }

        string groupInfo = grid.DataContext is MappingGroupViewModel group
            ? $"Gruppe='{group.Header}' (Kind={group.Kind})"
            : "Group=<no MappingGroupViewModel DataContext>";

        int itemCount = grid.ItemsSource is System.Collections.ICollection collection
            ? collection.Count
            : grid.ItemsSource?.Cast<object>().Count() ?? -1;

        DebugLog.Write($"[MappingsGrid] TargetUpdated: {groupInfo} DisplayedMappingCount={itemCount}");
    }

    /// <summary>
    /// Shows the update dialog (see <see cref="UpdateAvailableDialog"/>) for <paramref name="details"/>,
    /// called by both the automatic startup check and the manual "Check for updates" button (see
    /// <see cref="MainViewModel.Update"/>). After installation starts successfully
    /// (<see cref="UpdateAvailableDialog.InstallationStarted"/>), shuts down the app so the separate updater
    /// process can replace files locked by the running executable and launch the new version (see
    /// VirtualController.Core.Updates.UpdateInstaller).
    /// </summary>
    private void OnUpdateAvailable(UpdateCheckResult details)
    {
        // Blur and dim the main window while the modal update dialog is open (like a modal overlay).
        var blurEffect = new System.Windows.Media.Effects.BlurEffect { Radius = 8 };
        Effect = blurEffect;
        Opacity = 0.6;

        var dialogViewModel = _viewModel.Update.CreateAvailableDialogViewModel(details);
        var dialog = new UpdateAvailableDialog(dialogViewModel) { Owner = this };
        dialog.InstallationStarted += () => System.Windows.Application.Current.Shutdown();
        dialog.ShowDialog();

        // Remove the blur when the dialog closes (whether by skip, install, or cancel).
        Effect = null;
        Opacity = 1.0;
    }

    /// <summary>
    /// Opens the "Change version" dialog (<see cref="RollbackDialog"/>), which lets the user switch to any
    /// version available from the update source, including older versions (rollback), which regular update
    /// checks intentionally do not offer. Shuts down the app after installation starts successfully
    /// (<see cref="RollbackDialog.InstallationStarted"/>), as in <see cref="OnUpdateAvailable"/>.
    /// </summary>
    private void RollbackButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // Blur and dim the main window while the modal rollback dialog is open.
        var blurEffect = new System.Windows.Media.Effects.BlurEffect { Radius = 8 };
        Effect = blurEffect;
        Opacity = 0.6;

        var dialogViewModel = _viewModel.Update.CreateRollbackDialogViewModel();
        var dialog = new RollbackDialog(dialogViewModel) { Owner = this };
        dialog.InstallationStarted += () => System.Windows.Application.Current.Shutdown();
        dialog.ShowDialog();

        // Remove the blur when the dialog closes.
        Effect = null;
        Opacity = 1.0;
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

