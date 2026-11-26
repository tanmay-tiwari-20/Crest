using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Crest.Core;
using Crest.UI;
using Microsoft.Win32;

internal static class ThemePresentationChecks
{
    internal static void CheckPalettes(AppSettings settings, System.Windows.Shapes.Path islandMark, Action<bool, string> check)
    {
        var paletteType = typeof(Design).Assembly.GetType("Crest.UI.ThemePalettes")!;
        var apply = paletteType.GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic)!;
        var highContrast = new ResourceDictionary();
        apply.Invoke(null, [highContrast, "Rose", true]);
        check(ReferenceEquals(highContrast["Raised"], SystemColors.WindowBrush)
            && ReferenceEquals(highContrast["Text"], SystemColors.WindowTextBrush)
            && ReferenceEquals(highContrast["Accent"], SystemColors.HighlightBrush)
            && ReferenceEquals(highContrast["AccentText"], SystemColors.HighlightTextBrush)
            && ReferenceEquals(highContrast["IslandShell"], SystemColors.WindowBrush)
            && ReferenceEquals(highContrast["IslandAccentMusic"], SystemColors.HighlightBrush)
            && ReferenceEquals(highContrast["IslandActionText"], SystemColors.HighlightTextBrush),
            "High contrast applies Windows colors to Settings, the island and their action states");

        if (SystemParameters.HighContrast)
            return;

        string originalTheme = settings.Theme;
        string originalMotion = settings.Motion;
        string[] surfaces = ["Canvas", "Surface", "Raised", "Inset", "Hover", "Selected"];
        string[] foregrounds = ["Text", "Muted", "Subtle", "Accent", "Success", "Warning", "Danger"];
        string[] activityAccents = ["IslandAccentMusic", "IslandAccentTimer", "IslandAccentNotice"];
        var originalActivityColors = activityAccents.Select(ColorOf).ToArray();
        var canvases = new HashSet<Color>();
        var idleAccents = new HashSet<Color>();
        bool staticText = true, textContrast = true, buttonContrast = true, islandContrast = true, fixedActivityColors = true;
        try
        {
            settings.Motion = "Normal";
            foreach (string theme in ModuleCatalog.Themes.Skip(1))
            {
                settings.Theme = theme;
                Design.Apply(settings);
                canvases.Add(ColorOf("Canvas"));
                idleAccents.Add(ColorOf("IslandAccent"));
                staticText &= !((SolidColorBrush)Design.Brush("Text")).HasAnimatedProperties;
                textContrast &= surfaces.All(surface => foregrounds.All(text => Contrast(ColorOf(surface), ColorOf(text)) >= 4.5)
                    && Contrast(ColorOf(surface), ColorOf("Line")) >= 3);

                var actionLabel = ColorOf("AccentText");
                var actionFill = ColorOf("AccentFill");
                var hover = Blend(actionLabel, actionFill, (double)Application.Current.Resources["ButtonHoverOpacity"]);
                double pressed = (double)Application.Current.Resources["ButtonPressedOpacity"];
                buttonContrast &= Contrast(actionLabel, actionFill) >= 4.5 && Contrast(actionLabel, hover) >= 4.5
                    && surfaces.All(surface => Contrast(Blend(actionLabel, ColorOf(surface), pressed), Blend(hover, ColorOf(surface), pressed)) >= 4.5);

                var shell = ColorOf("IslandShell");
                var islandActionText = ColorOf("IslandActionText");
                var accent = ColorOf("IslandAccent");
                islandContrast &= shell == Colors.Black && Contrast(shell, ColorOf("IslandText")) >= 4.5
                    && Contrast(shell, ColorOf("IslandMuted")) >= 4.5 && Contrast(shell, accent) >= 4.5
                    && ((SolidColorBrush)islandMark.Fill).Color == accent
                    && activityAccents.Append("IslandAccent").All(key => Contrast(islandActionText, ColorOf(key)) >= 4.5);
                fixedActivityColors &= originalActivityColors.SequenceEqual(activityAccents.Select(ColorOf));
            }

            settings.Theme = "System";
            Design.Apply(settings);
            var systemCanvas = ColorOf("Canvas");
            var systemAccent = ColorOf("IslandAccent");
            settings.Theme = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) is 1
                ? "Light"
                : "Dark";
            Design.Apply(settings);
            check(systemCanvas == ColorOf("Canvas") && systemAccent == ColorOf("IslandAccent"),
                "System uses the Windows app appearance for Settings and the idle island accent");
        }
        finally
        {
            settings.Theme = originalTheme;
            settings.Motion = originalMotion;
            Design.Apply(settings);
        }
        check(canvases.Count == 5 && idleAccents.Count == 5 && staticText, "Each explicit palette applies immediately with a distinct canvas and idle accent");
        check(textContrast && buttonContrast, "Settings text, control boundaries and primary action states meet their contrast thresholds across palettes");
        check(islandContrast && fixedActivityColors, "The black island keeps legible labels, a themed idle mark and fixed activity colors");
    }

    internal static async Task CheckPicker(SettingsWindow window, AppSettings settings, SettingsStore store, Action<bool, string> check)
    {
        string originalTheme = settings.Theme;
        window.ShowSection("Appearance");
        window.Activate();
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        var choices = RadioButtons(window).ToArray();
        var selected = choices.Single(choice => choice.IsChecked == true);
        var peers = choices.Select(choice => new RadioButtonAutomationPeer(choice)).ToArray();
        check(peers.Select(peer => peer.GetName()).SequenceEqual(ModuleCatalog.Themes)
            && peers.All(peer => peer.GetAutomationControlType() == AutomationControlType.RadioButton)
            && choices.Count(choice => choice.IsTabStop) == 1 && selected.IsTabStop,
            "Appearance exposes one radio choice for each saved theme and one tab stop for the selected theme");
        string caption = peers[0].GetHelpText();
        check(SystemParameters.HighContrast ? caption == "High contrast active" : caption is "Windows uses light" or "Windows uses dark",
            "The System theme displays and announces the active Windows appearance");
        try
        {
            Keyboard.Focus(selected);
            RaiseKey(selected, Key.Home);
            RaiseKey(choices[0], Key.Right);
            var dark = choices[1];
            check(ReferenceEquals(Keyboard.FocusedElement, dark) && dark.IsTabStop && dark.IsChecked == true
                && choices.Count(choice => choice.IsChecked == true) == 1
                && dark.Template.FindName("focus", dark) is Border { Visibility: Visibility.Visible }
                && dark.Template.FindName("check", dark) is TextBlock { Visibility: Visibility.Visible }
                && settings.Theme == "Dark" && store.Load().Theme == "Dark",
                "Arrow selection keeps visible keyboard focus, a checkmark and the saved theme in sync");
            ((ISelectionItemProvider)peers[2].GetPattern(PatternInterface.SelectionItem)).Select();
            check(choices[2].IsChecked == true && choices[2].IsTabStop && settings.Theme == "Light" && store.Load().Theme == "Light",
                "Theme choices can be selected through their radio button automation pattern");
        }
        finally
        {
            selected.IsChecked = true;
            settings.Theme = originalTheme;
            Design.Apply(settings);
        }
    }

    private static IEnumerable<RadioButton> RadioButtons(DependencyObject root)
    {
        if (root is RadioButton radio)
            yield return radio;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var descendant in RadioButtons(VisualTreeHelper.GetChild(root, index)))
                yield return descendant;
    }

    private static void RaiseKey(RadioButton choice, Key key) =>
        choice.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(choice)!, Environment.TickCount, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    private static Color ColorOf(string key) => ((SolidColorBrush)Design.Brush(key)).Color;

    private static Color Blend(Color foreground, Color background, double opacity) => Color.FromRgb(
        (byte)Math.Round(foreground.R * opacity + background.R * (1 - opacity)),
        (byte)Math.Round(foreground.G * opacity + background.G * (1 - opacity)),
        (byte)Math.Round(foreground.B * opacity + background.B * (1 - opacity)));

    private static double Contrast(Color first, Color second)
    {
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    private static double Luminance(Color color) => .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);

    private static double Linear(byte channel)
    {
        double value = channel / 255d;
        return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
    }
}
