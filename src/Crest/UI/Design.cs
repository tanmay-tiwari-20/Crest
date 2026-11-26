using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Crest.Core;
namespace Crest.UI;

public static class Design
{
    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    public static TextBlock Text(string text, double size = 13, string color = "Text", bool bold = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), LineHeight = size * 1.45 };
        t.SetResourceReference(TextBlock.ForegroundProperty, color);
        return t;
    }
    public static TextBlock Icon(string glyph, double size = 17)
    {
        var icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        return icon;
    }
    public static Button Button(string label, Action action, bool primary = false)
    {
        var b = new Button { Content = label };
        AutomationProperties.SetName(b, label);
        if (primary)
        {
            b.SetResourceReference(Control.BackgroundProperty, "AccentFill");
            b.SetResourceReference(Control.ForegroundProperty, "AccentText");
        }
        b.Click += (_, _) => action();
        return b;
    }
    public static Button IconButton(string glyph, string name, Action action)
    {
        var icon = Icon(glyph);
        icon.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        var b = new Button { Content = icon, Padding = new Thickness(8), MinWidth = 36, MinHeight = 36 };
        RegisterTooltip(b, name);
        AutomationProperties.SetName(b, name);
        b.Click += (_, _) => action();
        return b;
    }
    public static Border Card(UIElement child, Thickness? padding = null)
    {
        var b = new Border { CornerRadius = new CornerRadius(16), Padding = padding ?? new Thickness(16), BorderThickness = new Thickness(1), Child = child };
        b.SetResourceReference(Border.BackgroundProperty, "Raised");
        b.SetResourceReference(Border.BorderBrushProperty, "Divider");
        return b;
    }
    public static void Apply(AppSettings settings)
    {
        var resources = Application.Current.Resources;
        bool highContrast = SystemParameters.HighContrast;
        string windowsTheme = ThemePalettes.WindowsTheme;
        ThemePalettes.Apply(resources, ThemePalettes.Resolve(settings.Theme, windowsTheme), highContrast);

        SetResource(ThemePicker.SystemAppearanceResource, highContrast ? "High contrast active" : "Windows uses " + windowsTheme.ToLowerInvariant());
        SetResource(ThemePicker.SwatchVisibilityResource, highContrast ? Visibility.Collapsed : Visibility.Visible);
        double pressedOpacity = highContrast ? 1.0 : 0.92;
        double pressedScale = highContrast || ReducedMotion(settings) ? 1.0 : 0.985;
        SetResource("ButtonHoverOpacity", highContrast ? 0.0 : 0.08);
        SetResource("ButtonPressedOpacity", pressedOpacity);
        SetResource("CirclePressedOpacity", pressedOpacity);
        SetResource("ButtonPressedScale", pressedScale);

        void SetResource(string name, object value)
        {
            if (!Equals(resources[name], value))
                resources[name] = value;
        }
    }
    public static bool ReducedMotion(AppSettings settings) => settings.Motion == "Reduced" || (settings.Motion == "System" && (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast));
    public static void RegisterTooltip(FrameworkElement element, string text)
    {
        if (element.ToolTip is ToolTip existing && existing.Content is TextBlock content && content.Text == text)
            return;
        if (element.ToolTip is ToolTip old)
            old.IsOpen = false;
        var tip = new ToolTip { Content = Text(text, 11, "IslandText"), MaxWidth = 280, Padding = new Thickness(10), Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        tip.SetResourceReference(Control.BackgroundProperty, "IslandCard");
        tip.SetResourceReference(Control.BorderBrushProperty, "IslandLine");
        IntPtr handle = IntPtr.Zero;
        tip.Opened += (_, _) => { if (Application.Current is Crest.App app && app.Policy != null && System.Windows.PresentationSource.FromVisual(tip) is System.Windows.Interop.HwndSource source) { handle = source.Handle; app.Policy.RegisterSurface(handle, false); } };
        tip.Closed += (_, _) => { if (handle != IntPtr.Zero && Application.Current is Crest.App app && app.Policy != null) { app.Policy.UnregisterSurface(handle); handle = IntPtr.Zero; } };
        element.ToolTip = tip;
    }
}
