using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// VirtualController.App has UseWindowsForms="true" (for the tray icon in App.xaml.cs), which brings
// System.Drawing/System.Windows.Forms into scope via implicit global usings. Several type names used below
// (Color, Brushes, FontFamily, Size, Control, Rectangle) exist in both System.Drawing(.Forms) and the WPF
// namespaces already used here, so explicit aliases are needed to disambiguate in favor of the WPF types.
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Size = System.Windows.Size;
using Control = System.Windows.Controls.Control;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace VirtualController.App.Diagnostics;

/// <summary>
/// Lightweight visual-tree inspector overlay, in the spirit of a browser's "Inspect element" tool. Toggled
/// by Ctrl+Shift+I (see MainWindow.xaml.cs), it draws a highlight rectangle around whichever
/// <see cref="FrameworkElement"/> is currently under the mouse cursor and a small info panel reporting its
/// type, x:Name, size, margin/padding, alignment, DataContext type, and ancestor path - useful for
/// diagnosing WPF layout/binding issues (e.g. a missing border, wrong ActualWidth, unexpected DataContext)
/// without attaching an external tool like Snoop.
///
/// Built entirely in code (no XAML) so it can be dropped into any Grid as the topmost sibling. Every visual
/// it draws has IsHitTestVisible="False" (which, per WPF, also excludes all of that visual's children from
/// hit testing), so the overlay itself is always skipped by hit testing and never blocks clicks on the real
/// UI underneath; MainWindow.xaml.cs instead drives it through window-level
/// PreviewMouseMove/PreviewMouseLeftButtonDown handlers and performs the actual VisualTreeHelper.HitTest.
/// </summary>
public sealed class LayoutInspectorOverlay : Canvas
{
    private readonly Rectangle _highlightBox;
    private readonly Border _infoPanel;
    private readonly TextBlock _infoText;

    /// <summary>Whether the inspector is currently shown and tracking the mouse.</summary>
    public bool IsActive { get; private set; }

    /// <summary>While true, <see cref="Show"/> ignores further updates so the highlighted element and its
    /// info panel stay fixed after a click, letting the info panel be read without the cursor covering the
    /// highlighted element.</summary>
    public bool IsPinned { get; private set; }

    /// <summary>The element the highlight box/info panel currently reflect, if any. Used by
    /// <see cref="Refresh"/> to decide whether a stale re-render request (e.g. after a property edit on an
    /// element that is no longer the one being inspected) should be ignored.</summary>
    public FrameworkElement? CurrentElement { get; private set; }

    public LayoutInspectorOverlay()
    {
        // The overlay must never participate in hit testing itself (see class remarks); otherwise MainWindow's
        // window-level HitTest would find the overlay's own visuals instead of the real UI underneath it.
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        Background = null;

        _highlightBox = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x1b, 0xba, 0xfa)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(40, 0x1b, 0xba, 0xfa)),
            IsHitTestVisible = false
        };

        _infoText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 12,
            FontFamily = new FontFamily("Consolas"),
            IsHitTestVisible = false
        };

        _infoPanel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(240, 0, 20, 40)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x1b, 0xba, 0xfa)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            IsHitTestVisible = false,
            Child = _infoText
        };

        Children.Add(_highlightBox);
        Children.Add(_infoPanel);
    }

    /// <summary>Shows the overlay and resumes tracking the mouse (unpinned).</summary>
    public void Activate()
    {
        IsActive = true;
        IsPinned = false;
        Visibility = Visibility.Visible;
    }

    /// <summary>Hides the overlay entirely.</summary>
    public void Deactivate()
    {
        IsActive = false;
        IsPinned = false;
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Toggles <see cref="IsPinned"/>; called on click while the inspector is active.</summary>
    public void TogglePin() => IsPinned = !IsPinned;

    /// <summary>
    /// Updates the highlight rectangle and info panel for <paramref name="element"/>, whose bounds relative
    /// to this overlay's parent are given by <paramref name="boundsInOverlay"/> (already converted via
    /// TransformToVisual by the caller, since this control has no knowledge of where it sits in the tree).
    /// Ignored while <see cref="IsPinned"/> is true.
    /// </summary>
    public void Show(FrameworkElement element, Rect boundsInOverlay)
    {
        if (IsPinned)
        {
            return;
        }

        UpdateVisual(element, boundsInOverlay);
    }

    /// <summary>
    /// Like <see cref="Show"/>, but always updates the highlight even while <see cref="IsPinned"/> is true.
    /// Used when hovering a node in the tree panel (<see cref="LayoutInspectorTreeView.ElementHovered"/>):
    /// browsing the tree should always preview the hovered element, regardless of whatever was previously
    /// pinned via a click on the app's own content.
    /// </summary>
    public void ForceShow(FrameworkElement element, Rect boundsInOverlay) => UpdateVisual(element, boundsInOverlay);

    /// <summary>
    /// Updates the highlight for <paramref name="element"/> and explicitly pins it (<see cref="IsPinned"/> =
    /// true), so it stays highlighted afterwards instead of reverting to following the mouse. Used when a
    /// node is clicked in the tree panel (<see cref="LayoutInspectorTreeView.ElementSelected"/>), mirroring
    /// what clicking the highlighted element directly on the canvas does via <see cref="TogglePin"/>.
    /// </summary>
    public void PinTo(FrameworkElement element, Rect boundsInOverlay)
    {
        UpdateVisual(element, boundsInOverlay);
        IsPinned = true;
    }

    /// <summary>
    /// Converts <paramref name="element"/>'s own bounds into this overlay's coordinate space via
    /// <see cref="Visual.TransformToVisual"/>, which works between any two visuals sharing a common root
    /// regardless of their actual ancestor/descendant relationship (unlike TransformToAncestor, which throws
    /// if the target is not a true ancestor - this overlay is a sibling of the elements it highlights, not
    /// an ancestor of them). Shared by every caller that needs to feed <see cref="Show"/>/
    /// <see cref="ForceShow"/>/<see cref="PinTo"/>/<see cref="Refresh"/>, so the transform logic lives in one
    /// place instead of being duplicated in MainWindow.xaml.cs and LayoutInspectorWindow.cs.
    /// </summary>
    public Rect GetBoundsRelativeToThis(FrameworkElement element)
        => element.TransformToVisual(this).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    /// <summary>
    /// Recomputes and re-applies the highlight for <paramref name="element"/> if it is the element currently
    /// being shown (<see cref="CurrentElement"/>), ignoring the call otherwise. Intended to be called after
    /// an edit in the property editor (see <see cref="LayoutInspectorPropertyEditor"/>) changes a property
    /// such as Width/Height/Margin that affects layout: WPF's layout pass runs asynchronously after
    /// SetValue, so callers should invoke this via Dispatcher.BeginInvoke at Render/Loaded priority rather
    /// than immediately after applying the edit, to give ActualWidth/ActualHeight time to update first.
    /// </summary>
    public void Refresh(FrameworkElement element)
    {
        if (!ReferenceEquals(element, CurrentElement))
        {
            return;
        }

        UpdateVisual(element, GetBoundsRelativeToThis(element));
    }

    private void UpdateVisual(FrameworkElement element, Rect boundsInOverlay)
    {
        CurrentElement = element;

        SetLeft(_highlightBox, boundsInOverlay.Left);
        SetTop(_highlightBox, boundsInOverlay.Top);
        _highlightBox.Width = Math.Max(0, boundsInOverlay.Width);
        _highlightBox.Height = Math.Max(0, boundsInOverlay.Height);

        _infoText.Text = BuildInfoText(element);

        // Measure the panel against its new text before positioning it, so DesiredSize below reflects the
        // text just assigned instead of a stale size from the previously inspected element.
        _infoPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size panelSize = _infoPanel.DesiredSize;

        double left = Math.Max(0, Math.Min(boundsInOverlay.Left, ActualWidth - panelSize.Width));
        double top = boundsInOverlay.Bottom + 4;

        // Flip the panel above the highlighted element when there is not enough room below it (e.g. for
        // elements near the bottom of the window), rather than letting it run off-screen.
        if (top + panelSize.Height > ActualHeight)
        {
            top = Math.Max(0, boundsInOverlay.Top - panelSize.Height - 4);
        }

        SetLeft(_infoPanel, left);
        SetTop(_infoPanel, top);
    }

    private static string BuildInfoText(FrameworkElement element)
    {
        var text = new StringBuilder();
        text.Append("Type: ").Append(element.GetType().Name);
        if (!string.IsNullOrEmpty(element.Name))
        {
            text.Append("   Name: ").Append(element.Name);
        }

        text.AppendLine();
        text.Append($"Size: {element.ActualWidth:0.#} x {element.ActualHeight:0.#}").AppendLine();
        text.Append($"Margin: {element.Margin}");
        if (element is Control control)
        {
            text.Append($"   Padding: {control.Padding}");
        }

        text.AppendLine();
        text.Append($"H/VAlign: {element.HorizontalAlignment} / {element.VerticalAlignment}").AppendLine();
        text.Append("DataContext: ").Append(element.DataContext?.GetType().Name ?? "null").AppendLine();
        text.Append(BuildAncestorPath(element));
        return text.ToString();
    }

    /// <summary>Builds a breadcrumb-style path from the window root down to <paramref name="element"/>
    /// (e.g. "Window > Grid > DockPanel > ... > Button"), mirroring the element path shown at the bottom of
    /// browser DevTools, to make it easier to see which template/container produced the hit element.</summary>
    private static string BuildAncestorPath(DependencyObject element)
    {
        var names = new List<string>();
        DependencyObject? current = element;
        while (current is not null)
        {
            names.Add(current.GetType().Name);
            current = VisualTreeHelper.GetParent(current);
        }

        names.Reverse();
        return "Path: " + string.Join(" > ", names);
    }
}
