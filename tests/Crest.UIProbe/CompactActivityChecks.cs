using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Crest.Core;
using Crest.Features;
using Crest.UI;

internal static class CompactActivityChecks
{
    public static async Task Run(Crest.App app, IslandWindow island, HardwareHub hardware,
        InteractionController controller, AppSettings settings, Action<bool, string> check)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "CrestCompactProbe-" + Guid.NewGuid().ToString("N"));
        var originalLocal = app.Local;
        var originalModules = settings.EnabledModules.ToList();
        var originalMusic = hardware.Music;
        var originalBattery = hardware.Battery;
        var clock = new Clock();
        using var local = new LocalHub(new SettingsStore(temporary), () => settings, clock);
        var machine = Field<InteractionStateMachine>(controller, "_machine");
        var labels = Field<Dictionary<string, TextBlock>>(island, "_labels");
        var details = Field<Button>(island, "_compactDetails");
        var action = Field<Button>(island, "_compactPlay");
        var artwork = Field<Image>(island, "_compactArt");
        var art = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 10, 80, 170, 255 }, 4);
        art.Freeze();
        async Task Reveal()
        {
            island.Suppress();
            machine.SetEligibility(false, false);
            machine.SetEligibility(true, false);
            machine.RevealAccessibility();
            island.Reveal(controller.TransitionVersion);
            await Task.Delay(180);
            island.Refresh();
        }
        try
        {
            typeof(Crest.App).GetProperty(nameof(Crest.App.Local))!.SetValue(app, local);
            typeof(HardwareHub).GetProperty(nameof(HardwareHub.Music))!.SetValue(hardware, new MusicSnapshot
            {
                HasSession = true, Title = "Visible song", Artist = "Visible artist", Artwork = art,
                IsPlaying = true, CanPlayPause = true
            });
            hardware.ClearBatteryNotices();
            string running = local.AddTimer(TimeSpan.FromMinutes(5), "Visible tea");
            local.AddTimer(TimeSpan.FromSeconds(1), "Completed tea");
            clock.Advance(2);
            local.Tick();
            local.SelectTimer(running);
            settings.EnabledModules.Clear();
            await Reveal();
            bool neutral = labels["compactTitle"].Text == "crest" && labels["compactDetail"].Text == "Idle"
                && artwork.Source is null && artwork.Visibility == Visibility.Collapsed && !action.IsVisible;
            details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(neutral && Field<string>(island, "_module") == "Tools" && IslandProbe.Selected(island) == null && local.Timers.Count == 2,
                "Disabled modules contribute no compact metadata, artwork, completion notice, or action");

            settings.EnabledModules.Add("Music");
            await Reveal();
            string musicName = new ButtonAutomationPeer(details).GetName();
            bool musicArtworkVisible = ReferenceEquals(artwork.Source, art) && artwork.IsVisible;
            details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(labels["compactTitle"].Text == "Visible song" && labels["compactDetail"].Text == "Visible artist"
                && musicArtworkVisible
                && musicName.Contains("Expand music", StringComparison.Ordinal)
                && musicName.Contains("Visible song", StringComparison.Ordinal)
                && AutomationProperties.GetName(action) == "Pause playback"
                && Field<string>(island, "_module") == "Music" && IslandProbe.Presentation(island) == "Activity"
                && Field<ActivityDeck>(island, "_expanded").CurrentActivity?.Kind == IslandActivityKind.Music && local.Timers.Any(timer => timer.Phase == TimerPhase.Completed),
                "A disabled Timer module cannot replace enabled music or redirect its details action");

            local.ResetProductivity();
            settings.EnabledModules.Add("Timer");
            running = local.AddTimer(TimeSpan.FromMinutes(5), "Visible tea");
            string other = local.AddTimer(TimeSpan.FromMinutes(15), "Later tea");
            local.SelectTimer(running);
            await Reveal();
            string timerName = new ButtonAutomationPeer(details).GetName();
            details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(labels["compactTitle"].Text == "Visible tea" && IslandProbe.Selected(island)?.Phase == "Running"
                && artwork.Source is null && AutomationProperties.GetName(action) == "Pause Visible tea"
                && timerName.Contains("Expand timer", StringComparison.Ordinal)
                && Field<string>(island, "_module") == "Timer" && Field<ActivityDeck>(island, "_expanded").CurrentActivity?.EntityId == running,
                "The displayed running timer has matching details, accessible status, and a timer action");

            await Reveal();
            await Activate(action, island);
            bool paused = local.Timers.Single(timer => timer.Id == running).Phase == TimerPhase.Paused
                && local.Timers.Single(timer => timer.Id == other).Phase == TimerPhase.Running
                && labels["compactDetail"].Text.StartsWith("Paused", StringComparison.Ordinal)
                && AutomationProperties.GetName(action) == "Resume Visible tea";
            await Activate(action, island);
            check(paused && local.Timers.Single(timer => timer.Id == running).Phase == TimerPhase.Running
                && local.Timers.Single(timer => timer.Id == other).Phase == TimerPhase.Running
                && AutomationProperties.GetName(action) == "Pause Visible tea",
                "Compact pause and resume address the displayed timer without changing another timer");

            local.AddTimer(TimeSpan.FromSeconds(1), "Completed tea");
            clock.Advance(2);
            local.Tick();
            local.SelectTimer(running);
            await Reveal();
            string completedName = new ButtonAutomationPeer(details).GetName();
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(labels["compactTitle"].Text == "Completed tea" && AutomationProperties.GetName(action) == "Open completed timers"
                && completedName.Contains("Timer complete", StringComparison.Ordinal)
                && Field<string>(island, "_module") == "Timer"
                && local.Timers.Single(timer => timer.Id == running).Phase == TimerPhase.Running,
                "A compact completion opens acknowledgements instead of pausing a different selected timer");

            settings.EnabledModules.Add("Controls");
            Field<BatteryNoticeState>(hardware, "_batteryNotices").Observe(true, 2, 12, false);
            typeof(HardwareHub).GetProperty(nameof(HardwareHub.Battery))!.SetValue(hardware,
                new BatterySnapshot { Available = true, Percent = 18, Charging = true, Status = "Charging" });
            await Reveal();
            string batteryName = new ButtonAutomationPeer(details).GetName();
            details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            bool detailsCorrect = Field<string>(island, "_module") == "Controls";
            typeof(IslandWindow).GetMethod("Collapse", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!.Invoke(island, null);
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(labels["compactTitle"].Text == "Battery was low" && batteryName.Contains("Expand batterywarning", StringComparison.Ordinal)
                && batteryName.Contains("12%", StringComparison.Ordinal) && AutomationProperties.GetName(action) == "Open laptop controls"
                && detailsCorrect && Field<string>(island, "_module") == "Controls"
                && labels["bodyBatteryNotice"].Text.Contains("12%", StringComparison.Ordinal),
                "Both battery-notice actions open Controls and expose the retained percentage to accessibility");

            typeof(IslandWindow).GetMethod("Collapse", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!.Invoke(island, null);
            settings.EnabledModules.Remove("Controls");
            settings.EnabledModules.Remove("Timer");
            island.Refresh();
            check(labels["compactTitle"].Text == "Visible song" && ReferenceEquals(artwork.Source, art)
                && new ButtonAutomationPeer(details).GetName().Contains("Expand music", StringComparison.Ordinal)
                && AutomationProperties.GetName(action) == "Pause playback",
                "Disabling the displayed notice modules immediately restores enabled music and its actions");
        }
        finally
        {
            island.Suppress();
            typeof(Crest.App).GetProperty(nameof(Crest.App.Local))!.SetValue(app, originalLocal);
            typeof(HardwareHub).GetProperty(nameof(HardwareHub.Music))!.SetValue(hardware, originalMusic);
            typeof(HardwareHub).GetProperty(nameof(HardwareHub.Battery))!.SetValue(hardware, originalBattery);
            hardware.ClearBatteryNotices();
            settings.EnabledModules = originalModules;
            local.ResetProductivity();
            local.Dispose();
            string resolved = Path.GetFullPath(temporary);
            if (resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(resolved).StartsWith("CrestCompactProbe-", StringComparison.Ordinal) && Directory.Exists(resolved))
                Directory.Delete(resolved, true);
        }
    }

    private static async Task Activate(Button button, IslandWindow island)
    {
        if (!button.IsEnabled) throw new InvalidOperationException("Compact activity action is disabled.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (int attempt = 0; attempt < 100; attempt++)
        {
            await Task.Delay(20);
            if (!Field<bool>(island, "_compactActionBusy")) return;
        }
        throw new TimeoutException("Compact activity action did not finish.");
    }

    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private sealed class Clock : ITimerClock
    {
        public long MonotonicMilliseconds { get; private set; }
        public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.Parse("2026-11-19T00:00:00Z");
        public void Advance(double seconds) { MonotonicMilliseconds += (long)(seconds * 1000); UtcNow += TimeSpan.FromSeconds(seconds); }
    }
}
