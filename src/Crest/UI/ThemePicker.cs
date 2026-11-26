using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Crest.Core;

namespace Crest.UI;

internal sealed class ThemePicker : UniformGrid
{
    internal const string SystemAppearanceResource = "SystemAppearanceCaption";
    internal const string SwatchVisibilityResource = "ThemeSwatchVisibility";

    public ThemePicker(string selected, Action<string> changed)
    {
        Columns = 3;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Once);
        if (!ModuleCatalog.Themes.Contains(selected))
            selected = "System";

        foreach (string theme in ModuleCatalog.Themes)
        {
            var choice = new RadioButton
            {
                Content = CreatePreview(theme),
                IsChecked = theme == selected,
                IsTabStop = theme == selected
            };
            choice.SetResourceReference(FrameworkElement.StyleProperty, "ThemeOption");
            AutomationProperties.SetName(choice, theme);
            if (theme == "System")
                choice.SetResourceReference(AutomationProperties.HelpTextProperty, SystemAppearanceResource);
            Children.Add(choice);
            choice.Checked += (_, _) =>
            {
                foreach (RadioButton option in Children)
                    option.IsTabStop = option == choice;
                changed(theme);
            };
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None || Keyboard.FocusedElement is not RadioButton current)
            return;

        int index = Children.IndexOf(current);
        if (index < 0)
            return;

        int next = e.Key switch
        {
            Key.Left => index - 1,
            Key.Right => index + 1,
            Key.Up => index - Columns,
            Key.Down => index + Columns,
            Key.Home => 0,
            Key.End => Children.Count - 1,
            _ => index
        };
        if (next == index)
            return;

        var choice = (RadioButton)Children[(next + Children.Count) % Children.Count];
        if (choice.Focus())
            choice.IsChecked = true;
        e.Handled = true;
    }

    private static StackPanel CreatePreview(string theme)
    {
        var content = new StackPanel { IsHitTestVisible = false };
        var label = Design.Text(theme, 13, "Text", true);
        label.Margin = new Thickness(0, 0, 21, 0);
        content.Children.Add(label);
        if (theme == "System")
        {
            var status = Design.Text("", 11, "Muted");
            status.SetResourceReference(TextBlock.TextProperty, SystemAppearanceResource);
            content.Children.Add(status);
        }

        var palette = ThemePalettes.Get(theme);
        var swatches = new Grid();
        Brush[] colors = theme == "System"
            ? new[] { palette["Canvas"], palette["IslandAccent"], ThemePalettes.Get("Light")["Canvas"] }
            : new[] { palette["Canvas"], palette["Raised"], palette["AccentFill"] };
        for (int index = 0; index < colors.Length; index++)
        {
            swatches.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(index == 0 ? 2 : 1, GridUnitType.Star) });
            var segment = new Border { Background = colors[index], CornerRadius = index switch
            {
                0 => new CornerRadius(4, 0, 0, 4),
                2 => new CornerRadius(0, 4, 4, 0),
                _ => new CornerRadius(0)
            } };
            Grid.SetColumn(segment, index);
            swatches.Children.Add(segment);
        }
        var preview = new Border
        {
            Child = swatches,
            Height = 16,
            Margin = new Thickness(0, 5, 0, 0),
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1)
        };
        preview.SetResourceReference(Border.BorderBrushProperty, "Line");
        preview.SetResourceReference(VisibilityProperty, SwatchVisibilityResource);
        content.Children.Add(preview);
        return content;
    }
}
