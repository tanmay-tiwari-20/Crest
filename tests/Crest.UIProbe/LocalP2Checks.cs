using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Crest.Core;
using Crest.Features;
using Crest.UI;

internal static class LocalP2Checks
{
    public static async Task Run(Action<bool, string> check)
    {
        string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string directory = Path.Combine(parent, "CrestLocalP2UI-" + Guid.NewGuid().ToString("N"));
        var clock = new Clock();
        using var hub = new LocalHub(new SettingsStore(directory), () => new(), clock);
        var messages = new List<string>();
        var panel = new TimerToolsPanel(hub, messages.Add);
        var window = new Window { Title = "Crest local P2 control probe", Width = 420, Height = 380, ShowInTaskbar = false, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        try
        {
            window.Show(); window.UpdateLayout(); await Task.Delay(30);
            await Click(panel, "Focus sessions");
            Input(panel, "Work minutes").Text = (3d / 60).ToString(CultureInfo.CurrentCulture);
            Input(panel, "Break minutes").Text = (1d / 60).ToString("R", CultureInfo.CurrentCulture);
            Input(panel, "Rounds").Text = "2";
            await Click(panel, "Start focus session");
            check(hub.Focus.Phase == TimerPhase.Running && hub.Focus.TargetRounds == 2 && hub.Focus.RemainingSeconds == 3, "Focus tab starts the configured independent work and break session");
            await Click(panel, "Pause focus"); clock.Advance(60); hub.Tick(); panel.Refresh();
            check(hub.Focus.Phase == TimerPhase.Paused && hub.Focus.RemainingSeconds == 3, "Focus pause controls exclude elapsed hidden time");
            await Click(panel, "Resume focus"); clock.Advance(3); hub.Tick(); panel.Refresh();
            check(hub.Focus.Phase == TimerPhase.Completed && hub.Focus.CompletedRounds == 1 && hub.Focus.Stage == FocusStage.Work, "Completed work remains pending until its continuation control is selected");
            await Click(panel, "Start break"); clock.Advance(1); hub.Tick(); panel.Refresh();
            await Click(panel, "Start next work round"); clock.Advance(3); hub.Tick(); panel.Refresh();
            check(hub.Focus.Phase == TimerPhase.Completed && hub.Focus.CompletedRounds == 2 && !Button(panel, "Start break").IsEnabled, "Final work completion retains a completed session and prevents an extra break");
            await Click(panel, "Dismiss focus session"); check(hub.Focus.Phase == TimerPhase.Idle, "Focus dismissal clears the retained session");

            await Click(panel, "Reminders");
            clock.SyncUtc();
            Input(panel, "Reminder text").Text = "Local reminder test";
            Input(panel, "Minutes from now").Text = (3d / 60).ToString(CultureInfo.CurrentCulture);
            await Click(panel, "Add in…"); clock.Advance(4); hub.Tick(); panel.Refresh();
            check(hub.Reminders.Count == 1 && hub.Reminders[0].Phase == ReminderPhase.Due, "Reminder controls create a local scheduled reminder and expose its due state");
            await Click(panel, "Snooze Local reminder test for 5 minutes");
            check(hub.Reminders[0].Phase == ReminderPhase.Scheduled, "Reminder snooze reschedules the existing due reminder");
            clock.Advance(300); hub.Tick(); panel.Refresh();
            await Click(panel, "Dismiss reminder Local reminder test"); check(hub.Reminders.Count == 0, "Reminder dismissal removes the due reminder and its durable state");
            Input(panel, "Reminder text").Text = "Selected local time";
            Input(panel, "Local date and time").Text = DateTime.Now.AddDays(1).ToString("g", CultureInfo.CurrentCulture);
            await Click(panel, "Add at chosen time");
            check(hub.Reminders.Count == 1 && hub.Reminders[0].Text == "Selected local time" && hub.Reminders[0].Phase == ReminderPhase.Scheduled && messages.Count == 0, "Reminder chosen-time control accepts the user's local date and time format");
            hub.DismissReminder(hub.Reminders[0].Id); panel.Refresh();
            Input(panel, "Reminder text").Text = ""; await Click(panel, "Add at chosen time");
            check(messages.Count == 1 && hub.Reminders.Count == 0, "Empty reminder text reports validation without creating an activity");
        }
        finally
        {
            window.Close(); hub.ResetProductivity(); hub.Dispose();
            if (Path.GetFullPath(directory).StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(directory).StartsWith("CrestLocalP2UI-", StringComparison.Ordinal) && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
    private static TextBox Input(DependencyObject panel, string name) => Descendants<TextBox>(panel).Single(x => AutomationProperties.GetName(x) == name);
    private static Button Button(DependencyObject panel, string name) => Descendants<Button>(panel).Single(x => x.IsVisible && AutomationProperties.GetName(x) == name);
    private static async Task Click(DependencyObject panel, string name)
    {
        var button = Button(panel, name); if (!button.IsEnabled) throw new InvalidOperationException("Disabled local P2 control: " + name);
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        for (int i = 0; i < 100; i++)
        {
            await Task.Delay(20);
            // This hidden creation control is disabled throughout every asynchronous extras action.
            var readiness = Descendants<Button>(panel).Single(x => AutomationProperties.GetName(x) == "Add in…");
            if (readiness.IsEnabled) { ((FrameworkElement)panel).UpdateLayout(); return; }
        }
        throw new TimeoutException("Local P2 control did not settle: " + name);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private sealed class Clock : ITimerClock
    {
        public long MonotonicMilliseconds { get; private set; }
        public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.UtcNow;
        public void Advance(double seconds) { MonotonicMilliseconds += (long)(seconds * 1000); UtcNow += TimeSpan.FromSeconds(seconds); }
        public void SyncUtc() => UtcNow = DateTimeOffset.UtcNow;
    }
}
