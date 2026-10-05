using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// See LayoutInspectorOverlay.cs for why these aliases are needed: VirtualController.App has
// UseWindowsForms="true" (for the tray icon in App.xaml.cs), which brings System.Drawing/System.Windows.Forms
// into scope via implicit global usings, colliding with several WPF type names used below.
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Control = System.Windows.Controls.Control;
using Button = System.Windows.Controls.Button;

namespace VirtualController.App.Diagnostics;

/// <summary>
/// Standalone top-level window for the Ctrl+Shift+I layout inspector, in the spirit of a browser's separate
/// DevTools window. Hosts the Elements tree (<see cref="LayoutInspectorTreeView"/>) on the left and the
/// property editor (<see cref="LayoutInspectorPropertyEditor"/>) on the right, side by side in a Grid.
///
/// Deliberately a separate <see cref="Window"/> rather than panels embedded inside MainWindow's own Grid
/// (an earlier version of this inspector worked that way): MainWindow handles Ctrl+Shift+I and clicks for
/// the highlight overlay (see MainWindow.xaml.cs) via window-level PreviewKeyDown/PreviewMouseLeftButtonDown
/// handlers that tunnel through the entire window and mark the event handled, which made the embedded tree
/// panel's own click handlers unreachable - they never got a chance to run because the preview handler
/// higher up the tunneling path already consumed the click. A separate window has its own independent input
/// routing, so its buttons/text boxes/tree rows receive clicks normally.
///
/// Not owned by MainWindow and <see cref="Window.ShowInTaskbar"/> is left true, so it behaves like a normal
/// secondary window (can be moved to a second monitor, alt-tabbed to, etc.) instead of a transient tool
/// window tied to the main window's lifetime.
/// </summary>
public sealed class LayoutInspectorWindow : Window
{
    public LayoutInspectorTreeView Tree { get; }
    public LayoutInspectorPropertyEditor PropertyEditor { get; }

    /// <summary>Raised when the user closes this window themselves (title bar X button, Alt+F4, etc.), as
    /// opposed to MainWindow calling <see cref="Window.Hide"/> itself when toggling the inspector off via
    /// Ctrl+Shift+I/Escape. MainWindow uses this to also exit inspector mode (deactivate the overlay)
    /// whenever the user closes this window directly, per the user's request.</summary>
    public event System.Action? ClosedByUser;

    public LayoutInspectorWindow()
    {
        Title = "Layout Inspector";
        Width = 760;
        Height = 820;
        Background = new SolidColorBrush(Color.FromArgb(246, 0, 14, 28));

        Tree = new LayoutInspectorTreeView();
        PropertyEditor = new LayoutInspectorPropertyEditor();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(Tree, 0);
        Grid.SetColumn(PropertyEditor, 1);
        grid.Children.Add(Tree);
        grid.Children.Add(PropertyEditor);

        // Top toolbar: "Export tree to file..." dumps every FrameworkElement in the full real visual tree
        // (not just whichever rows happen to be lazily expanded in the Elements panel right now - see
        // LayoutInspectorTreeView.Root) with its size/alignment/margin/padding to a plain-text file. Added
        // because manually clicking through dozens of nested elements one at a time to diagnose a layout bug
        // (e.g. content not stretching on window resize) is slow; a single text dump can instead be read in
        // bulk and grepped/searched for the first element whose ActualWidth/Height stops matching its parent's.
        var exportButton = new Button
        {
            Content = "Export tree to file...",
            Margin = new Thickness(8),
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromRgb(0x09, 0x34, 0x55)),
            Foreground = Brushes.White,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        exportButton.Click += (_, _) => ExportTreeToFile();
        DockPanel.SetDock(exportButton, Dock.Top);

        var dockPanel = new DockPanel();
        dockPanel.Children.Add(exportButton);
        dockPanel.Children.Add(grid);
        Content = dockPanel;

        // Selecting a row in the tree also loads that element into the property editor, so both panels
        // always agree on which element is "current" without MainWindow needing to wire them together itself.
        Tree.ElementSelected += element => PropertyEditor.Inspect(element);

        // Closing this window (the X button) should only hide it, not destroy it: MainWindow keeps a single
        // instance alive for the whole app lifetime (see MainWindow.xaml.cs) and just calls Show()/Hide()
        // on Ctrl+Shift+I, so the tree/property editor state and event subscriptions persist across toggles.
        // Window.Closing only fires when Close() is called (e.g. via the title bar X button or Alt+F4), not
        // when MainWindow calls Hide() itself, so ClosedByUser correctly only fires for user-initiated closes.
        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
            ClosedByUser?.Invoke();
        };
    }

    /// <summary>
    /// Walks the full real visual tree starting at <see cref="LayoutInspectorTreeView.Root"/> (set by
    /// MainWindow.xaml.cs's call to <c>Tree.Load(this)</c>) and writes one line per
    /// <see cref="FrameworkElement"/> - indented by depth, with type, x:Name, ActualWidth/ActualHeight,
    /// Margin, Padding (for <see cref="Control"/>s), and HorizontalAlignment/VerticalAlignment - to a
    /// timestamped .txt file in the user's temp folder. Unlike the Elements panel, this always visits every
    /// node regardless of which rows have actually been expanded in the UI, since
    /// <see cref="VisualTreeHelper.GetChild"/> does not care about the tree view's own lazy-population state.
    /// </summary>
    private void ExportTreeToFile()
    {
        if (Tree.Root is null)
        {
            Title = "Layout Inspector - nothing loaded yet";
            return;
        }

        var builder = new StringBuilder();
        AppendElement(builder, Tree.Root, depth: 0);

        string fileName = $"VirtualController_LayoutInspectorExport_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt";
        string path = Path.Combine(Path.GetTempPath(), fileName);
        File.WriteAllText(path, builder.ToString());

        // No existing dark-themed message box in this app's Diagnostics code; reusing the window Title is
        // the simplest way to surface the saved path without introducing a new dialog just for this.
        Title = $"Layout Inspector - exported to {path}";
    }

    /// <summary>Recursively appends one line for <paramref name="element"/> and then every
    /// <see cref="FrameworkElement"/> descendant (skipping non-FrameworkElement visuals exactly like
    /// <see cref="LayoutInspectorTreeView"/>'s own tree building does, so depth lines up with what the
    /// Elements panel would show if every row were expanded).</summary>
    private static void AppendElement(StringBuilder builder, FrameworkElement element, int depth)
    {
        string indent = new string(' ', depth * 2);
        string name = string.IsNullOrEmpty(element.Name) ? string.Empty : $" #{element.Name}";
        string controlInfo = element is Control control
            ? $" Padding={control.Padding} HContentAlign={control.HorizontalContentAlignment} VContentAlign={control.VerticalContentAlignment}"
            : string.Empty;

        builder.Append(indent)
            .Append(element.GetType().Name).Append(name)
            .Append($"  Size={element.ActualWidth:0.#}x{element.ActualHeight:0.#}")
            .Append($"  Margin={element.Margin}").Append(controlInfo)
            .Append($"  HAlign={element.HorizontalAlignment}  VAlign={element.VerticalAlignment}")
            .AppendLine();

        int count = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < count; i++)
        {
            AppendChild(builder, VisualTreeHelper.GetChild(element, i), depth + 1);
        }
    }

    /// <summary>Like <see cref="AppendElement"/>, but accepts any <see cref="DependencyObject"/> child:
    /// non-FrameworkElement visuals (e.g. a Run inside a TextBlock, or internal chrome visuals) are skipped
    /// without emitting a line, recursing directly into their children at the same depth - matching
    /// LayoutInspectorTreeView.PopulateChildren's behavior so the exported file's structure matches what the
    /// Elements panel would show.</summary>
    private static void AppendChild(StringBuilder builder, DependencyObject child, int depth)
    {
        if (child is FrameworkElement frameworkElement)
        {
            AppendElement(builder, frameworkElement, depth);
            return;
        }

        int count = VisualTreeHelper.GetChildrenCount(child);
        for (int i = 0; i < count; i++)
        {
            AppendChild(builder, VisualTreeHelper.GetChild(child, i), depth);
        }
    }
}
