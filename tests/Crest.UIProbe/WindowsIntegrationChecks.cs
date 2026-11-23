using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Crest.Features;
using Crest.UI;

internal static class WindowsIntegrationChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var preferences = new WindowsIntegrationPreferences();
        using var service = new WindowsIntegrationService(() => preferences);
        await service.RequestNotificationAccessAsync();
        await service.RefreshNotificationsAsync();
        await service.RefreshAccessoriesAsync();
        await service.ProbePhoneAsync();
        check(service.Notifications.Count == 0 && service.Accessories.Count == 0 && !service.PhoneAvailable
            && service.NotificationStatus == "Notification access is off."
            && service.BatteryStatus == "Accessory battery access is off."
            && service.PhoneStatus == "Phone integration is off.",
            "Disabled Windows integrations retain privacy defaults without requesting any provider access");

        int saves = 0;
        var openedTools = new List<string>();
        void Save() { saves++; service.ApplyPreferences(); }
        var panel = new WindowsIntegrationsPanel(service, preferences, Save, openedTools.Add, settings: true);
        panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        try
        {
            check(Descendants<CheckBox>(panel).All(toggle => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(toggle)))
                && Descendants<Button>(panel).All(button => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(button))),
                "Every Windows integration toggle and action exposes an accessible name");
            check(Descendants<Button>(panel).Any(button => AutomationProperties.GetName(button) == "Request Windows notification access")
                && !Descendants<Button>(new WindowsIntegrationsPanel(service, preferences, Save, openedTools.Add))
                    .Any(button => AutomationProperties.GetName(button) == "Request Windows notification access"),
                "Windows notification permission can be requested only from the ordinary Settings surface");

            var inbox = (NotificationInbox)typeof(WindowsIntegrationService).GetField("_inbox", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(service)!;
            var notificationToggle = Descendants<CheckBox>(panel).Single(toggle => AutomationProperties.GetName(toggle) == "Read Windows notification previews");
            notificationToggle.IsChecked = true;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            const string syntheticBody = "Crest isolated sensitive notification body";
            inbox.Update(new[]
            {
                new NotificationPreview(7101, "Synthetic calendar", "Synthetic title", syntheticBody, now),
                new NotificationPreview(7102, "Synthetic messages", "Another synthetic title", "Another synthetic body", now)
            }, now);
            RefreshPanel(panel);
            check(preferences.NotificationsEnabled && saves > 0
                && !Descendants<TextBlock>(panel).Any(text => text.Text.Contains(syntheticBody, StringComparison.Ordinal))
                && Descendants<TextBlock>(panel).Any(text => text.Text.Contains("Synthetic calendar", StringComparison.Ordinal)),
                "Notification previews hide titles and bodies by default while showing source and time");
            var details = Descendants<CheckBox>(panel).Single(toggle => AutomationProperties.GetName(toggle) == "Show notification titles and bodies");
            details.IsChecked = true;
            await Pump();
            check(Descendants<TextBlock>(panel).Any(text => text.Text.Contains(syntheticBody, StringComparison.Ordinal)),
                "Explicit notification detail opt-in exposes the selected preview text");
            details.IsChecked = false;
            await Pump();
            check(!Descendants<TextBlock>(panel).Any(text => text.Text.Contains(syntheticBody, StringComparison.Ordinal)),
                "Turning notification detail visibility off immediately removes sensitive preview text");

            Click(panel, "Acknowledge Crest preview from Synthetic calendar");
            await Pump();
            check(service.Notifications.Count == 1 && service.Notifications.Single().Id == 7102,
                "Acknowledgement removes only the selected Crest notification preview");
            Click(panel, "Clear Crest notification previews");
            await Pump();
            check(service.Notifications.Count == 0
                && !Descendants<Button>(panel).Any(button => AutomationProperties.GetName(button).StartsWith("Acknowledge Crest preview", StringComparison.Ordinal)),
                "Clear notification previews removes retained content and acknowledgement controls");

            Descendants<CheckBox>(panel).Single(toggle => AutomationProperties.GetName(toggle) == "Read paired accessory batteries").IsChecked = true;
            Descendants<CheckBox>(panel).Single(toggle => AutomationProperties.GetName(toggle) == "Enable Phone Link access").IsChecked = true;
            Click(panel, "Disconnect Windows integrations");
            await Pump();
            check(!preferences.NotificationsEnabled && !preferences.AccessoryBatteryEnabled && !preferences.PhoneEnabled
                && !preferences.ShowNotificationDetails && service.Notifications.Count == 0 && service.Accessories.Count == 0
                && Descendants<CheckBox>(panel).All(toggle => toggle.IsChecked != true),
                "Disconnect clears Windows integration access and keeps visible toggles consistent with saved preferences");
            check(openedTools.Count == 0, "Windows integration verification never launches external apps or settings");
        }
        finally { panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
    }

    private static void RefreshPanel(WindowsIntegrationsPanel panel)
        => typeof(WindowsIntegrationsPanel).GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(panel, null);
    private static void Click(DependencyObject panel, string label)
        => Descendants<Button>(panel).Single(button => AutomationProperties.GetName(button) == label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Pump() => await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
