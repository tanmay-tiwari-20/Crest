using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Crest.Core;
using Crest.Features;
using Crest.UI;

/// <summary>Application/Settings integration wiring against an isolated profile; no provider refresh or access request.</summary>
internal static class IntegrationWiringChecks
{
    public static async Task Run(Crest.App app, Action<bool, string> check)
    {
        app.Dispatcher.VerifyAccess();
        string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string directory = Path.Combine(parent, "CrestIntegrationWiringUI-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string[] properties = ["Preferences", "Store", "Hardware", "Weather", "Calendar", "Downloads", "Ai", "WindowsIntegrations"];
        var originals = properties.ToDictionary(name => name, name => typeof(Crest.App).GetProperty(name)!.GetValue(app));
        var flag = typeof(Crest.App).GetField("_hadIntegrationEnabled", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object? originalFlag = flag.GetValue(app);
        var settings = new AppSettings { Theme = "Dark" };
        var store = new SettingsStore(directory);
        // Construction only establishes managed adapters; native initialization is never requested.
        using var hardware = new HardwareHub(); hardware.Dispose();
        SettingsWindow? window = null;
        bool initialized = false;
        try
        {
            Set(app, "Preferences", settings); Set(app, "Store", store); Set(app, "Hardware", hardware);
            Invoke(app, "InitializeIntegrations"); initialized = true;
            window = new SettingsWindow(app); window.Show(); await Settle();
            var navigation = (StackPanel)typeof(SettingsWindow).GetField("_nav", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var integrationNavigation = navigation.Children.OfType<Button>().Single(x => AutomationProperties.GetName(x) == "Integrations");
            integrationNavigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
            var panel = Descendants<IntegrationsPanel>(window).Single();
            bool allBuilt = false;
            foreach (string name in new[] { "Weather", "Calendar", "Downloads", "AI", "Windows" })
            {
                FindButton(panel, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
                bool valid = name switch
                {
                    "Weather" => Descendants<WeatherPanel>(panel).Any() && FindButton(panel, "Use Windows location").IsEnabled && !FindButton(panel, "Refresh weather").IsEnabled,
                    "Calendar" => Descendants<CalendarPanel>(panel).Any() && FindButton(panel, "Connect Outlook").IsEnabled && !FindButton(panel, "Refresh calendar").IsEnabled,
                    "Downloads" => Descendants<DownloadPanel>(panel).Any() && !FindButton(panel, "Choose destination and download").IsEnabled,
                    "AI" => Descendants<AiAssistantPanel>(panel).Any() && !FindButton(panel, "Ask local model").IsEnabled,
                    _ => Descendants<WindowsIntegrationsPanel>(panel).Any() && !FindButton(panel, "Request Windows notification access").IsEnabled
                        && !FindButton(panel, "Refresh accessory batteries").IsEnabled && !FindButton(panel, "Check Phone Link support").IsEnabled
                };
                if (!valid) throw new InvalidOperationException("Settings integration page did not build its initial source controls: " + name);
                allBuilt = true;
            }
            check(allBuilt && app.Weather.Current == null && app.Calendar.Events.Count == 0 && app.Downloads.Items.Count == 0
                && app.Ai.Messages.Count == 0 && app.WindowsIntegrations.Notifications.Count == 0 && app.WindowsIntegrations.Accessories.Count == 0
                && !app.WindowsIntegrations.PhoneAvailable && !settings.EnabledModules.Contains("Integrations"),
                "Actual Settings Integrations navigation builds all five optional panels with providers off and no retained activity");

            window.ShowIntegrationSource("Calendar"); await Settle();
            check(Descendants<CalendarPanel>(window).Any() && AutomationProperties.GetItemStatus(FindButton(panel, "Calendar")) == "Selected · Off",
                "Opening Settings for a specific integration selects that source and exposes its off state");

            settings.Downloads.Enabled = true; ApplyAndSave();
            var firstOptIn = store.Load();
            check(firstOptIn.Downloads.Enabled && firstOptIn.EnabledModules.Count(x => x == "Integrations") == 1
                && !firstOptIn.Weather.Enabled && !firstOptIn.Calendar.Enabled && !firstOptIn.Ai.Enabled
                && !firstOptIn.WindowsIntegrations.NotificationsEnabled && !firstOptIn.WindowsIntegrations.AccessoryBatteryEnabled && !firstOptIn.WindowsIntegrations.PhoneEnabled,
                "First optional-source opt-in adds one Integrations dock preference while preserving disabled sources");

            settings.EnabledModules.Remove("Integrations"); settings.Theme = "Ocean"; ApplyAndSave(); ApplyAndSave();
            var hiddenGroup = store.Load();
            check(hiddenGroup.Downloads.Enabled && !hiddenGroup.EnabledModules.Contains("Integrations") && hiddenGroup.Theme == "Ocean",
                "An explicitly hidden Integrations module stays hidden across unrelated preference applications and storage reload");

            settings.Weather.Enabled = true; settings.Downloads.Enabled = false; ApplyAndSave();
            var independent = store.Load();
            check(independent.Weather.Enabled && !independent.Downloads.Enabled && !independent.Calendar.Enabled && !independent.Ai.Enabled
                && !independent.EnabledModules.Contains("Integrations") && app.Downloads.Items.Count == 0,
                "Disabling one optional source preserves another enabled source and the user's hidden module preference");
        }
        finally
        {
            window?.Close();
            if (initialized) Invoke(app, "DisposeIntegrations");
            foreach (var original in originals) Set(app, original.Key, original.Value);
            flag.SetValue(app, originalFlag); Design.Apply(app.EffectivePreferences);
            if (Path.GetFullPath(directory).StartsWith(parent, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(directory).StartsWith("CrestIntegrationWiringUI-", StringComparison.Ordinal) && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        void ApplyAndSave() { Invoke(app, "ApplyIntegrationPreferences"); store.Save(settings); }
    }
    private static Button FindButton(DependencyObject root, string name)
        => Descendants<Button>(root).Single(x => AutomationProperties.GetName(x) == name);
    private static void Set(Crest.App app, string name, object? value) => typeof(Crest.App).GetProperty(name)!.SetValue(app, value);
    private static void Invoke(Crest.App app, string name) => typeof(Crest.App).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)!.Invoke(app, null);
    private static async Task Settle() { await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); await Task.Delay(20); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
