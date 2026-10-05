using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

// See LayoutInspectorOverlay.cs for why these aliases are needed: VirtualController.App has
// UseWindowsForms="true" (for the tray icon in App.xaml.cs), which brings System.Drawing/System.Windows.Forms
// into scope via implicit global usings, colliding with several WPF type names used below.
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;
using Orientation = System.Windows.Controls.Orientation;

namespace VirtualController.App.Diagnostics;

/// <summary>
/// Visual-tree browser panel for the Ctrl+Shift+I layout inspector (see <see cref="LayoutInspectorOverlay"/>
/// and MainWindow.xaml.cs), in the spirit of a browser DevTools "Elements" panel. Docked to the right edge
/// of the window, it lists the live visual tree starting at the root <see cref="Window"/> as an expandable,
/// indented list (built from plain nested <see cref="StackPanel"/>s rather than a native <see cref="TreeView"/>,
/// since there is no existing dark theme for TreeView/TreeViewItem in this app and native chrome would stand
/// out against the rest of the overlay). Hovering a row raises <see cref="ElementHovered"/> so MainWindow can
/// highlight that element via <see cref="LayoutInspectorOverlay.ForceShow"/>; clicking a row raises
/// <see cref="ElementSelected"/> so MainWindow can pin the highlight via <see cref="LayoutInspectorOverlay.PinTo"/>.
///
/// Children are built lazily on first expand (see <see cref="PopulateChildren"/>/the toggle's click handler)
/// rather than eagerly walking the entire visual tree on <see cref="Load"/>, since the full tree of a WPF
/// window (every template-expanded control) can easily be several thousand nodes.
/// </summary>
public sealed class LayoutInspectorTreeView : Border
{
    private static readonly SolidColorBrush TypeNameBrush = new(Color.FromRgb(0x1b, 0xba, 0xfa));
    private static readonly SolidColorBrush MutedBrush = new(Color.FromRgb(0x8a, 0xa6, 0xb8));
    private static readonly SolidColorBrush HoverRowBrush = new(Color.FromArgb(60, 0x1b, 0xba, 0xfa));
    private static readonly SolidColorBrush SelectedRowBrush = new(Color.FromArgb(110, 0x1b, 0xba, 0xfa));

    /// <summary>Holds everything <see cref="SelectAndReveal"/> needs to jump to a row from outside
    /// <see cref="BuildRow"/>: the row's header panel (for highlighting/scrolling into view) and an action
    /// that expands the row (building its lazily-populated children if needed), reusing the exact same logic
    /// the row's own expand/collapse toggle uses.</summary>
    private sealed record RowEntry(StackPanel Header, Action Expand);

    private readonly StackPanel _rootPanel;

    /// <summary>Every row currently built, keyed by its underlying element, so <see cref="SelectAndReveal"/>
    /// can look up and expand each ancestor on the way down to a target element. Rebuilt from scratch by
    /// <see cref="Load"/> and grown lazily as rows are expanded (see <see cref="PopulateChildren"/>).</summary>
    private readonly Dictionary<FrameworkElement, RowEntry> _rows = new();

    /// <summary>The header panel of the currently selected row, if any, kept separate from the transient
    /// per-row hover highlight so a selection stays visibly highlighted even after the mouse moves away.</summary>
    private StackPanel? _selectedHeaderPanel;

    /// <summary>The root element passed to the most recent <see cref="Load"/> call (typically MainWindow).
    /// Exposed so callers such as <see cref="LayoutInspectorWindow"/>'s element export feature can walk the
    /// full real visual tree starting from the same root, independent of which rows have actually been
    /// lazily expanded here (see class remarks on lazy child population).</summary>
    public FrameworkElement? Root { get; private set; }

    /// <summary>Raised when the mouse enters a row, with that row's underlying element. Does not fire again
    /// on mouse leave; the overlay simply keeps showing the most recently hovered element, matching how
    /// mouse-driven highlighting over the app's own content already behaves (see MainWindow.xaml.cs).</summary>
    public event Action<FrameworkElement>? ElementHovered;

    /// <summary>Raised when a row (other than its expand/collapse toggle) is clicked.</summary>
    public event Action<FrameworkElement>? ElementSelected;

    public LayoutInspectorTreeView()
    {
        Background = new SolidColorBrush(Color.FromArgb(246, 0, 14, 28));
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x1b, 0xba, 0xfa));
        BorderThickness = new Thickness(1, 0, 0, 0);

        var title = new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x09, 0x34, 0x55)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new TextBlock
            {
                Text = "Elements (Ctrl+Shift+I)",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12
            }
        };
        DockPanel.SetDock(title, Dock.Top);

        _rootPanel = new StackPanel { Margin = new Thickness(4) };

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _rootPanel
        };

        var dockPanel = new DockPanel();
        dockPanel.Children.Add(title);
        dockPanel.Children.Add(scrollViewer);
        Child = dockPanel;
    }

    /// <summary>Rebuilds the tree from scratch starting at <paramref name="root"/> (typically the owning
    /// <see cref="Window"/>), auto-expanding the first two levels. Called every time the inspector is
    /// activated (see MainWindow.xaml.cs) so the tree reflects whatever the UI currently looks like, since
    /// bound collections/tabs can add or remove elements between inspector sessions.</summary>
    public void Load(DependencyObject root)
    {
        _rootPanel.Children.Clear();
        _rows.Clear();
        _selectedHeaderPanel = null;
        Root = root as FrameworkElement;
        if (root is FrameworkElement rootElement)
        {
            _rootPanel.Children.Add(BuildRow(rootElement, depth: 0, autoExpand: true));
        }
    }

    /// <summary>
    /// Jumps the tree to <paramref name="element"/>'s row: expands every ancestor along the way (lazily
    /// building whatever children were not yet populated), then highlights and scrolls to the target row.
    /// Used when the user clicks an element directly on the app's own content while the inspector is active
    /// (see MainWindow.xaml.cs), so the Elements panel always reflects whatever was just clicked, mirroring
    /// how clicking an element in a browser's page also reveals/selects it in the DevTools Elements panel.
    /// </summary>
    public void SelectAndReveal(FrameworkElement element)
    {
        // Collect ancestors root-to-target, skipping non-FrameworkElement visuals: PopulateChildren does the
        // same when building rows, so this list lines up exactly with which rows actually exist in the tree.
        var path = new List<FrameworkElement>();
        DependencyObject? current = element;
        while (current is not null)
        {
            if (current is FrameworkElement frameworkElement)
            {
                path.Add(frameworkElement);
            }

            current = VisualTreeHelper.GetParent(current);
        }

        path.Reverse();

        RowEntry? targetEntry = null;
        for (int i = 0; i < path.Count; i++)
        {
            if (!_rows.TryGetValue(path[i], out RowEntry? entry))
            {
                // Tree doesn't know about this node (stale Load(), or the element lives outside this
                // panel's root) - nothing sensible to reveal, so stop instead of jumping to the wrong row.
                return;
            }

            targetEntry = entry;

            // Expand every ancestor on the way down, but not the target row itself - selecting an element
            // should not also force its own children open, matching how browser DevTools behaves.
            if (i < path.Count - 1)
            {
                entry.Expand();
            }
        }

        if (targetEntry is null)
        {
            return;
        }

        SelectRow(targetEntry.Header);
        targetEntry.Header.BringIntoView();
        ElementSelected?.Invoke(element);
    }

    private FrameworkElement BuildRow(FrameworkElement element, int depth, bool autoExpand)
    {
        bool hasChildren = VisualTreeHelper.GetChildrenCount(element) > 0;

        var toggle = new TextBlock
        {
            Text = hasChildren ? (autoExpand ? "\u25BC" : "\u25B6") : " ",
            Width = 14,
            Foreground = MutedBrush,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Cursor = hasChildren ? Cursors.Hand : Cursors.Arrow,
            VerticalAlignment = VerticalAlignment.Center
        };

        var label = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.Inlines.Add(new Run(element.GetType().Name) { Foreground = TypeNameBrush, FontWeight = FontWeights.Bold });
        if (!string.IsNullOrEmpty(element.Name))
        {
            label.Inlines.Add(new Run($"  #{element.Name}") { Foreground = MutedBrush });
        }

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(depth * 14, 1, 0, 1),
            Cursor = Cursors.Hand
        };
        header.Children.Add(toggle);
        header.Children.Add(label);

        header.MouseEnter += (_, _) =>
        {
            header.Background = HoverRowBrush;
            ElementHovered?.Invoke(element);
        };
        header.MouseLeave += (_, _) => header.Background = ReferenceEquals(header, _selectedHeaderPanel) ? SelectedRowBrush : null;
        header.MouseLeftButtonDown += (_, e) =>
        {
            SelectRow(header);
            ElementSelected?.Invoke(element);
            e.Handled = true;
        };

        var childrenHost = new StackPanel { Visibility = autoExpand ? Visibility.Visible : Visibility.Collapsed };
        bool childrenBuilt = false;
        bool isExpanded = autoExpand;

        void EnsureChildrenBuilt()
        {
            if (childrenBuilt)
            {
                return;
            }

            childrenBuilt = true;
            // Only the next level auto-expands further (depth 0/1); deeper levels start collapsed so Load()
            // never has to eagerly walk the whole tree (see class remarks).
            PopulateChildren(element, depth + 1, childrenHost, autoExpandChildren: depth + 1 < 1);
        }

        void ExpandRow()
        {
            if (!hasChildren || isExpanded)
            {
                return;
            }

            isExpanded = true;
            toggle.Text = "\u25BC";
            EnsureChildrenBuilt();
            childrenHost.Visibility = Visibility.Visible;
        }

        toggle.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (!hasChildren)
            {
                return;
            }

            if (isExpanded)
            {
                isExpanded = false;
                toggle.Text = "\u25B6";
                childrenHost.Visibility = Visibility.Collapsed;
            }
            else
            {
                ExpandRow();
            }
        };

        if (autoExpand && hasChildren)
        {
            EnsureChildrenBuilt();
        }

        // Recorded so SelectAndReveal can later expand this row (building its children lazily if needed)
        // and scroll/highlight it when jumping here from a click on the app's own content.
        _rows[element] = new RowEntry(header, ExpandRow);

        var row = new StackPanel();
        row.Children.Add(header);
        row.Children.Add(childrenHost);
        return row;
    }

    /// <summary>Marks <paramref name="header"/> as the single selected row, clearing the highlight of
    /// whichever row was previously selected. Shared by clicking a row directly and by
    /// <see cref="SelectAndReveal"/> jumping to a row from an app-content click.</summary>
    private void SelectRow(StackPanel header)
    {
        if (_selectedHeaderPanel is { } previous)
        {
            previous.Background = null;
        }

        _selectedHeaderPanel = header;
        header.Background = SelectedRowBrush;
    }

    /// <summary>
    /// Adds a row for each <see cref="FrameworkElement"/> child of <paramref name="parent"/> to
    /// <paramref name="host"/>. Non-<see cref="FrameworkElement"/> visuals (e.g. a <see cref="Run"/> inside a
    /// TextBlock, or internal chrome visuals) do not get their own row; instead this recurses directly into
    /// their children at the same depth, so the displayed tree only ever shows meaningful named/typed
    /// elements instead of every low-level drawing primitive.
    /// </summary>
    private void PopulateChildren(DependencyObject parent, int depth, StackPanel host, bool autoExpandChildren)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement childElement)
            {
                host.Children.Add(BuildRow(childElement, depth, autoExpandChildren));
            }
            else
            {
                PopulateChildren(child, depth, host, autoExpandChildren);
            }
        }
    }
}
