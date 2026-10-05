using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// See LayoutInspectorOverlay.cs for why these aliases are needed: VirtualController.App has
// UseWindowsForms="true" (for the tray icon in App.xaml.cs), which brings System.Drawing/System.Windows.Forms
// into scope via implicit global usings, colliding with several WPF type names used below.
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Orientation = System.Windows.Controls.Orientation;
using Control = System.Windows.Controls.Control;
using Panel = System.Windows.Controls.Panel;
using TextBox = System.Windows.Controls.TextBox;

namespace VirtualController.App.Diagnostics;

/// <summary>
/// Property editor panel for the Ctrl+Shift+I layout inspector (see <see cref="LayoutInspectorWindow"/>),
/// in the spirit of a browser DevTools "Styles"/"Computed" pane. Shown next to the Elements tree
/// (<see cref="LayoutInspectorTreeView"/>), it lists a fixed set of commonly tweaked dependency properties
/// for whichever element is currently selected (see <see cref="Inspect"/>) and lets each be edited as plain
/// text, applying the change live to the real element in the main window the moment the text box loses
/// focus or Enter is pressed - useful for experimenting with a border/background/margin fix without
/// recompiling, before copying the final value back into XAML.
///
/// Only <see cref="DependencyProperty"/>-backed properties are listed (see <see cref="EditableProperty"/>),
/// since editing is implemented purely through <see cref="DependencyObject.SetValue"/> plus the property's
/// own <see cref="System.ComponentModel.TypeConverter"/> (the same mechanism XAML attribute values go
/// through), rather than hand-written parsing for every property type.
/// </summary>
public sealed class LayoutInspectorPropertyEditor : Border
{
    /// <summary>Describes one row in the editor: a display label and the backing
    /// <see cref="DependencyProperty"/> to read/write on the inspected element.</summary>
    private sealed record EditableProperty(string Label, DependencyProperty Property);

    // Deliberately a small, curated set of the properties most often tweaked while chasing a visual bug
    // (background/border colors, size, spacing, visibility) rather than every DependencyProperty on the
    // element (which would include dozens of rarely touched layout/animation properties and clutter the
    // panel). FrameworkElement.MarginProperty etc. are inherited by every element this editor targets.
    private static readonly EditableProperty[] EditableProperties =
    {
        new("Background", Panel.BackgroundProperty),
        new("Foreground", Control.ForegroundProperty),
        new("BorderBrush", Control.BorderBrushProperty),
        new("BorderThickness", Control.BorderThicknessProperty),
        new("Width", FrameworkElement.WidthProperty),
        new("Height", FrameworkElement.HeightProperty),
        new("Margin", FrameworkElement.MarginProperty),
        new("Padding", Control.PaddingProperty),
        new("Opacity", UIElement.OpacityProperty),
        new("FontSize", Control.FontSizeProperty),
        new("Visibility", UIElement.VisibilityProperty),
    };

    private readonly StackPanel _rowsPanel;
    private readonly TextBlock _titleText;

    private FrameworkElement? _inspectedElement;

    /// <summary>Raised at the end of <see cref="ApplyRow"/> after an edit was applied to the inspected
    /// element (whether or not the conversion actually succeeded - the text box may simply have reverted to
    /// the current value). Callers use this to refresh anything that depends on the element's current
    /// layout, e.g. re-measuring the Ctrl+Shift+I overlay's highlight box after a Width/Height/Margin edit
    /// (see <see cref="LayoutInspectorOverlay.Refresh"/>).</summary>
    public event Action<FrameworkElement>? PropertyApplied;

    public LayoutInspectorPropertyEditor()
    {
        Background = new SolidColorBrush(Color.FromArgb(246, 0, 14, 28));

        _titleText = new TextBlock
        {
            Text = "No element selected",
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };

        var title = new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x09, 0x34, 0x55)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = _titleText
        };
        DockPanel.SetDock(title, Dock.Top);

        _rowsPanel = new StackPanel { Margin = new Thickness(10) };

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _rowsPanel
        };

        var dockPanel = new DockPanel();
        dockPanel.Children.Add(title);
        dockPanel.Children.Add(scrollViewer);
        Child = dockPanel;

        BuildRows();
    }

    /// <summary>Builds one editable row per entry in <see cref="EditableProperties"/>, once, at construction
    /// time. <see cref="Inspect"/> only updates each row's text box afterwards; the rows themselves are
    /// static for the lifetime of this control.</summary>
    private void BuildRows()
    {
        foreach (EditableProperty property in EditableProperties)
        {
            var label = new TextBlock
            {
                Text = property.Label,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8a, 0xa6, 0xb8)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 2)
            };

            var textBox = new TextBox
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(4),
                Background = new SolidColorBrush(Color.FromRgb(0x00, 0x1c, 0x32)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x09, 0x34, 0x55)),
                Tag = property
            };

            textBox.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    ApplyRow(textBox, property);
                    e.Handled = true;
                }
            };
            textBox.LostFocus += (_, _) => ApplyRow(textBox, property);

            _rowsPanel.Children.Add(label);
            _rowsPanel.Children.Add(textBox);
        }
    }

    /// <summary>Loads <paramref name="element"/>'s current property values into each row's text box so the
    /// panel reflects whatever is selected in the Elements tree (<see cref="LayoutInspectorTreeView.ElementSelected"/>).
    /// Passing null (nothing selected, or the previous selection no longer applies) clears every row and
    /// disables editing.</summary>
    public void Inspect(FrameworkElement? element)
    {
        _inspectedElement = element;
        _titleText.Text = element is null
            ? "No element selected"
            : $"{element.GetType().Name}{(string.IsNullOrEmpty(element.Name) ? "" : $" #{element.Name}")}";

        foreach (TextBox textBox in EnumerateTextBoxes())
        {
            textBox.IsEnabled = element is not null;
            if (element is not null && textBox.Tag is EditableProperty property)
            {
                object? value = element.GetValue(property.Property);
                textBox.Text = ConvertToString(property.Property, value);
            }
            else
            {
                textBox.Text = string.Empty;
            }
        }
    }

    private IEnumerable<TextBox> EnumerateTextBoxes() => _rowsPanel.Children.OfType<TextBox>();

    /// <summary>
    /// Parses <paramref name="textBox"/>'s current text back into <paramref name="property"/>'s value type
    /// using that <see cref="DependencyProperty"/>'s own <see cref="System.ComponentModel.TypeConverter"/>
    /// (resolved through <see cref="DependencyPropertyDescriptor"/>) - the same mechanism WPF itself uses to
    /// turn a XAML attribute string (e.g. Background="#FF001C32") into the real typed value - and applies it
    /// to the currently inspected element via <see cref="DependencyObject.SetValue"/>. Invalid input (e.g. a
    /// malformed color or a non-numeric Width) is swallowed and the text box is reverted to the element's
    /// actual current value, since this editor must never crash the app or leave it in a half-applied state.
    /// </summary>
    private void ApplyRow(TextBox textBox, EditableProperty property)
    {
        if (_inspectedElement is null)
        {
            return;
        }

        try
        {
            System.ComponentModel.TypeConverter? converter = System.ComponentModel.DependencyPropertyDescriptor
                .FromProperty(property.Property, _inspectedElement.GetType())
                ?.Converter;

            object? newValue = converter is not null && converter.CanConvertFrom(typeof(string))
                ? converter.ConvertFromString(textBox.Text)
                : textBox.Text;

            _inspectedElement.SetValue(property.Property, newValue);
        }
        catch (Exception)
        {
            // Swallow conversion/apply failures (see summary); fall through to re-read the element's actual
            // value below so the text box always reflects reality instead of the rejected input.
        }

        textBox.Text = ConvertToString(property.Property, _inspectedElement.GetValue(property.Property));
        PropertyApplied?.Invoke(_inspectedElement);
    }

    private static string ConvertToString(DependencyProperty property, object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        System.ComponentModel.TypeConverter? converter = System.ComponentModel.TypeDescriptor.GetConverter(value.GetType());
        return converter is not null && converter.CanConvertTo(typeof(string))
            ? converter.ConvertToString(value) ?? value.ToString() ?? string.Empty
            : value.ToString() ?? string.Empty;
    }
}
