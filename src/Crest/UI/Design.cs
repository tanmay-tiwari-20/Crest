using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Crest.Core;
namespace Crest.UI;

public static class Design
{
    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    public static TextBlock Text(string text, double size = 13, string color = "Text", bool bold = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
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
        var b = new Button { Content = icon, Padding = new Thickness(8), MinWidth = 32, MinHeight = 32 };
        RegisterTooltip(b, name);
        AutomationProperties.SetName(b, name);
        b.Click += (_, _) => action();
        return b;
    }
    public static Border Card(UIElement child, Thickness? padding = null)
    {
        var b = new Border { CornerRadius = new CornerRadius(14), Padding = padding ?? new Thickness(14), Child = child };
        b.SetResourceReference(Border.BackgroundProperty, "Raised");
        return b;
    }
    public static void Apply(AppSettings settings)
    {
        bool light = settings.Theme == "Light" || (settings.Theme == "System" && (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) == 1);
        var colors = light ? new[] { "#F8F9FB", "#ECEFF5", "#172031", "#526075", "#7B869B", "#355AC7", "#6A8EFF" } : new[] { "#171A20", "#232730", "#F4F6FC", "#A5ADBF", "#6B778F", "#A8BDFF", "#6A8EFF" };
        var keys = new[] { "Surface", "Raised", "Text", "Muted", "Line", "Accent", "AccentFill" };
        for (int i = 0; i < keys.Length; i++)
            Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        Application.Current.Resources["AccentText"] = new SolidColorBrush(Color.FromRgb(14, 23, 45));
        Application.Current.Resources["ButtonBoundary"] = new Thickness(0);
        Application.Current.Resources["ButtonHoverOpacity"] = 0.76;
        Application.Current.Resources["ButtonPressedOpacity"] = 0.55;
        Application.Current.Resources["CircleHoverOpacity"] = 0.7;
        Application.Current.Resources["CirclePressedOpacity"] = 0.5;
        var islandKeys = new[] { "IslandShell", "IslandCard", "IslandLine", "IslandText", "IslandMuted", "IslandAccent", "IslandOutline" };
        var islandColors = light ? new[] { "#F3F6FC", "#FFFFFF", "#7B869B", "#172031", "#526075", "#355AC7", "#C8CED8" } : new[] { "#07090C", "#13171D", "#6B778F", "#F5F7FA", "#ADB4C2", "#70E0C0", "#303640" };
        for (int i = 0; i < islandKeys.Length; i++)
            Application.Current.Resources[islandKeys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(islandColors[i]));
        Application.Current.Resources["IslandCard"] = light ? new LinearGradientBrush(Color.FromRgb(255, 255, 255), Color.FromRgb(242, 245, 250), 135) : new LinearGradientBrush(Color.FromRgb(23, 27, 35), Color.FromRgb(15, 18, 23), 135);
        if (settings.Theme is "Ocean" or "Forest" or "Rose")
        {
            string[] palette = settings.Theme switch
            {
                "Ocean" => new[] { "#101E2C", "#1B3043", "#88DAFF", "#62C9F5", "#071421" },
                "Forest" => new[] { "#14251C", "#223C2D", "#A2E2B4", "#8DD3A4", "#0B1911" },
                _ => new[] { "#291A26", "#432C3E", "#FFB7D6", "#F59BC5", "#1B0E19" }
            };
            string[] themedKeys = { "Surface", "Raised", "Accent", "AccentFill", "IslandShell" };
            for (int i = 0; i < themedKeys.Length; i++) Application.Current.Resources[themedKeys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette[i]));
            Application.Current.Resources["IslandCard"] = Application.Current.Resources["Raised"];
            Application.Current.Resources["IslandAccent"] = Application.Current.Resources["Accent"];
        }
        if (SystemParameters.HighContrast)
        {
            Application.Current.Resources["Surface"] = SystemColors.WindowBrush;
            Application.Current.Resources["Raised"] = SystemColors.WindowBrush;
            Application.Current.Resources["Text"] = SystemColors.WindowTextBrush;
            Application.Current.Resources["Muted"] = SystemColors.WindowTextBrush;
            Application.Current.Resources["Line"] = SystemColors.WindowTextBrush;
            Application.Current.Resources["Accent"] = SystemColors.HighlightBrush;
            Application.Current.Resources["AccentFill"] = SystemColors.HighlightBrush;
            Application.Current.Resources["AccentText"] = SystemColors.HighlightTextBrush;
            Application.Current.Resources["ButtonBoundary"] = new Thickness(1);
            Application.Current.Resources["ButtonHoverOpacity"] = 1.0;
            Application.Current.Resources["ButtonPressedOpacity"] = 1.0;
            Application.Current.Resources["CircleHoverOpacity"] = 1.0;
            Application.Current.Resources["CirclePressedOpacity"] = 1.0;
            Application.Current.Resources["IslandShell"] = SystemColors.WindowBrush;
            Application.Current.Resources["IslandCard"] = SystemColors.WindowBrush;
            Application.Current.Resources["IslandText"] = SystemColors.WindowTextBrush;
            Application.Current.Resources["IslandMuted"] = SystemColors.WindowTextBrush;
            Application.Current.Resources["IslandLine"] = SystemColors.WindowTextBrush;
            Application.Current.Resources["IslandAccent"] = SystemColors.HighlightBrush;
            Application.Current.Resources["IslandOutline"] = SystemColors.WindowTextBrush;
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
