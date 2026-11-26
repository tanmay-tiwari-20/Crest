using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Crest.Core;
using Crest.Features;
using Crest.UI;

internal static class CompactLocalP2Checks
{
    public static async Task Run(Crest.App app, IslandWindow island, InteractionController controller, AppSettings settings, Action<bool, string> check)
    {
        string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string directory = Path.Combine(parent, "CrestCompactLocalP2-" + Guid.NewGuid().ToString("N"));
        var originalLocal = app.Local;
        var originalModules = settings.EnabledModules.ToList();
        var clock = new Clock();
        using var local = new LocalHub(new SettingsStore(directory), () => settings, clock);
        var machine = Field<InteractionStateMachine>(controller, "_machine");
        var labels = Field<Dictionary<string, TextBlock>>(island, "_labels");
        var action = Field<Button>(island, "_compactPlay");
        var details = Field<Button>(island, "_compactDetails");
        async Task Reveal()
        {
            island.Suppress(); machine.SetEligibility(false, false); machine.SetEligibility(true, false); machine.RevealAccessibility();
            island.Reveal(controller.TransitionVersion); await Task.Delay(180); island.Refresh();
        }
        try
        {
            typeof(Crest.App).GetProperty(nameof(Crest.App.Local))!.SetValue(app, local);
            settings.EnabledModules = new() { "Timer" }; app.Hardware.ClearBatteryNotices();
            string timerId = local.AddTimer(TimeSpan.FromMinutes(10), "Independent timer");
            local.StartFocusSession(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1), 2, "Compact focus");
            await Reveal();
            check(labels["compactTitle"].Text == "Compact focus" && AutomationProperties.GetName(action) == "Pause focus session" && IslandProbe.Selected(island)?.EntityId == local.Focus.Id, "Compact focus metadata and accessible action identify the displayed session");
            await Activate(action, island);
            bool paused = local.Focus.Phase == TimerPhase.Paused && local.Timers.Single(x => x.Id == timerId).Phase == TimerPhase.Running && AutomationProperties.GetName(action) == "Resume focus session";
            await Activate(action, island);
            check(paused && local.Focus.Phase == TimerPhase.Running && local.Timers.Single(x => x.Id == timerId).Phase == TimerPhase.Running, "Compact focus pause and resume leave the independent countdown running");
            clock.Advance(3); local.Tick(); await Reveal();
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); island.UpdateLayout();
            check(labels["compactTitle"].Text == "Compact focus" && IslandProbe.Selected(island)?.Phase == "Completed" && AutomationProperties.GetName(action) == "Open completed focus session" && Field<string>(island, "_module") == "Timer" && VisibleButton(island, "Start break") && local.Timers.Single(x => x.Id == timerId).Phase == TimerPhase.Running, "Compact completed focus opens the focus continuation controls without changing another timer");
            local.AddReminder("Compact reminder", clock.UtcNow);
            await Reveal(); action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); island.UpdateLayout();
            check(labels["compactTitle"].Text == "Reminder" && AutomationProperties.GetName(action) == "Open due reminders" && VisibleButton(island, "Dismiss reminder Compact reminder") && VisibleButton(island, "Snooze Compact reminder for 5 minutes"), "Compact due reminder action opens snooze and dismissal controls");
            await Reveal(); details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); island.UpdateLayout();
            check(IslandProbe.Presentation(island) == "Activity" && Field<ActivityDeck>(island, "_expanded").CurrentActivity?.EntityId == local.Reminders.Single().Id && VisibleButton(island, "Dismiss"), "Compact reminder details select the same reminder controls as its action");
            string completedTimerId = local.AddTimer(TimeSpan.FromSeconds(1), "Completed alongside reminder");
            clock.Advance(1); local.Tick(); local.SelectTimer(timerId);
            bool ConcurrentActivitiesRetained() => local.Timers.Single(x => x.Id == completedTimerId).Phase == TimerPhase.Completed
                && local.Timers.Single(x => x.Id == timerId).Phase == TimerPhase.Running
                && local.Focus.Phase == TimerPhase.Completed && local.Reminders.Single().Phase == ReminderPhase.Due;
            await Reveal(); action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); island.UpdateLayout();
            check(labels["compactTitle"].Text == "Completed alongside reminder" && AutomationProperties.GetName(action) == "Open completed timers"
                && VisibleButton(island, "Acknowledge Completed alongside reminder") && !VisibleButton(island, "Dismiss reminder Compact reminder")
                && !VisibleButton(island, "Start break") && ConcurrentActivitiesRetained(),
                "Compact completed-timer action opens acknowledgement when a due reminder, completed focus and running timer coexist");
            await Reveal(); details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); island.UpdateLayout();
            check(labels["compactTitle"].Text == "Completed alongside reminder" && VisibleButton(island, "Acknowledge Completed alongside reminder")
                && !VisibleButton(island, "Dismiss reminder Compact reminder") && !VisibleButton(island, "Start break") && ConcurrentActivitiesRetained(),
                "Compact completed-timer details select the displayed countdown instead of a concurrent reminder or focus session");
            settings.EnabledModules.Clear(); await Reveal();
            check(labels["compactTitle"].Text == "crest" && labels["compactDetail"].Text == "Idle" && !action.IsVisible && local.Focus.Phase == TimerPhase.Completed && local.Reminders.Single().Phase == ReminderPhase.Due, "Disabled Timer module hides focus and reminder metadata and actions while retaining local state");
        }
        finally
        {
            island.Suppress(); typeof(Crest.App).GetProperty(nameof(Crest.App.Local))!.SetValue(app, originalLocal); settings.EnabledModules = originalModules;
            local.ResetProductivity(); local.Dispose();
            if (Path.GetFullPath(directory).StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(directory).StartsWith("CrestCompactLocalP2-", StringComparison.Ordinal) && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
    private static bool VisibleButton(DependencyObject island, string name) => Descendants<Button>(island).Any(x => x.IsVisible && AutomationProperties.GetName(x) == name);
    private static async Task Activate(Button button, IslandWindow island)
    {
        if (!button.IsEnabled) throw new InvalidOperationException("Disabled compact local P2 action.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (int i = 0; i < 100; i++) { await Task.Delay(20); if (!Field<bool>(island, "_compactActionBusy")) return; }
        throw new TimeoutException("Compact local P2 action did not settle.");
    }
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private sealed class Clock : ITimerClock
    {
        public long MonotonicMilliseconds { get; private set; }
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);
        public void Advance(double seconds) { MonotonicMilliseconds += (long)(seconds * 1000); UtcNow += TimeSpan.FromSeconds(seconds); }
    }
}
