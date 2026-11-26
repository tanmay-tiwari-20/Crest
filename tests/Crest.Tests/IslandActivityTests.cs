using Crest.Core;
using Crest.Features;

internal static class IslandActivityTests
{
    public static int Run()
    {
        int passed = 0;
        void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
        void Check(bool value) { if (!value) throw new InvalidOperationException("Contextual island activity outcome failed."); }
        var now = DateTimeOffset.Parse("2026-11-25T00:00:00Z");
        var idle = new FocusSnapshot(TimerPhase.Idle, FocusStage.Work, 1500, 1500, "Focus", 0, 4);
        var noMusic = new IslandMediaActivity(false, false, false, "", "", "");
        var running = new TimerSnapshot(TimerPhase.Running, 240, 300, now.AddMinutes(4), "tea", "Tea");
        var other = new TimerSnapshot(TimerPhase.Running, 540, 600, now.AddMinutes(9), "reading", "Reading");
        var focus = new FocusSnapshot(TimerPhase.Running, FocusStage.Work, 600, 1500, "Writing session", 0, 4, "actual-focus");
        IReadOnlyList<IslandActivity> Read(IReadOnlyList<TimerSnapshot>? timers = null, FocusSnapshot? session = null,
            IReadOnlyList<ReminderSnapshot>? reminders = null, BatteryNoticeSnapshot? notice = null,
            IslandMediaActivity? music = null, IEnumerable<string>? modules = null, string? selectedTimer = null,
            IReadOnlyList<IslandDownloadActivity>? downloads = null, IslandAiActivity? ai = null)
            => IslandActivities.Read(modules ?? new[] { "Controls", "Timer", "Music" }, timers ?? Array.Empty<TimerSnapshot>(),
                session ?? idle, reminders ?? Array.Empty<ReminderSnapshot>(), notice, music ?? noMusic, selectedTimer, downloads, ai);

        Test("An idle catalog contains no invented timer, media, battery or scheduled-reminder activity", () =>
        {
            var scheduled = new ReminderSnapshot("later", "Future reminder", now.AddDays(1), ReminderPhase.Scheduled);
            var idleTimer = new TimerSnapshot(TimerPhase.Idle, 1500, 1500, null);
            Check(Read(new[] { idleTimer }, reminders: new[] { scheduled }).Count == 0);
        });

        Test("Real focus identity, phase and progress stay independent of the selected countdown", () =>
        {
            var activities = Read(new[] { running, other }, focus, selectedTimer: other.Id);
            var selected = activities.First();
            Check(selected.Kind == IslandActivityKind.Focus && selected.Id == "focus:actual-focus" && selected.EntityId == focus.Id
                && selected.Title == focus.Name && selected.Value == "10:00" && selected.Phase == "Running"
                && selected.PrimaryAction == IslandActivityAction.PauseFocus && selected.Progress == .6
                && activities.Single(item => item.Id == "timer:tea").Value == "4:00"
                && running.RemainingSeconds == 240 && other.RemainingSeconds == 540);
        });

        Test("A focus phase transition retains its activity identity while a new session replaces it", () =>
        {
            var before = Read(session: focus).Single();
            var paused = Read(session: focus with { Phase = TimerPhase.Paused }).Single();
            var rest = Read(session: focus with { Stage = FocusStage.Break, RemainingSeconds = 120, TotalSeconds = 300 }).Single();
            var replacement = Read(session: focus with { Id = "replacement-focus" }).Single();
            Check(before.Id == paused.Id && paused.PrimaryAction == IslandActivityAction.ResumeFocus
                && before.Id == rest.Id && rest.Subtitle.StartsWith("Break", StringComparison.Ordinal) && rest.Value == "2:00"
                && replacement.Id != before.Id);
        });

        Test("Concurrent real activities retain separate entities and prioritize warnings and due work", () =>
        {
            var completed = running with { Id = "finished", Name = "Finished tea", Phase = TimerPhase.Completed, RemainingSeconds = 0 };
            var reminder = new ReminderSnapshot("due", "Join planning", now, ReminderPhase.Due);
            var media = new IslandMediaActivity(true, true, true, "player", "A real song", "An artist", 40, 200);
            var activities = Read(new[] { running, other, completed }, focus, new[] { reminder }, new(7, BatteryWarningLevel.Critical, 3), media);
            Check(activities.Count == 7 && activities.Select(item => item.Id).Distinct().Count() == 7
                && activities[0].Kind == IslandActivityKind.BatteryWarning && activities[0].Value == "3%"
                && activities[1].Id == "timer:finished" && activities[1].PrimaryAction == IslandActivityAction.AcknowledgeTimer
                && activities[2].Id == "reminder:due" && activities[3].Id == "focus:actual-focus"
                && activities[^1].Id == "music:player" && activities[^1].Progress == .2);
        });

        Test("The preferred countdown stays addressable without overriding a separate focus activity", () =>
        {
            var timers = Read(new[] { running, other }, selectedTimer: other.Id);
            var alongsideFocus = Read(new[] { running, other }, focus, selectedTimer: other.Id);
            Check(timers[0].EntityId == other.Id && timers[0].PrimaryAction == IslandActivityAction.PauseTimer
                && alongsideFocus[0].EntityId == focus.Id && alongsideFocus[1].EntityId == other.Id
                && timers[1].EntityId == running.Id);
        });

        Test("Pausing the preferred countdown preserves its identity beside another running timer", () =>
        {
            var before = Read(new[] { running, other }, selectedTimer: running.Id).First();
            var after = Read(new[] { running with { Phase = TimerPhase.Paused }, other }, selectedTimer: running.Id).First();
            Check(before.Id == after.Id && after.EntityId == running.Id && after.PrimaryAction == IslandActivityAction.ResumeTimer
                && after.Value == before.Value && after.Phase == "Paused");
        });

        Test("Disabling modules removes their visible activities while retaining every source snapshot", () =>
        {
            var reminder = new ReminderSnapshot("due", "Join planning", now, ReminderPhase.Due);
            var media = new IslandMediaActivity(true, true, true, "player", "Song", "Artist");
            var notice = new BatteryNoticeSnapshot(7, BatteryWarningLevel.Low, 12);
            var onlyMusic = Read(new[] { running }, focus, new[] { reminder }, notice, media, new[] { "Music", "Capture", "Shelf" });
            Check(onlyMusic.Count == 1 && onlyMusic[0].Kind == IslandActivityKind.Music
                && Read(new[] { running }, focus, new[] { reminder }, notice, media, Array.Empty<string>()).Count == 0
                && focus.Phase == TimerPhase.Running && running.Phase == TimerPhase.Running && reminder.Phase == ReminderPhase.Due && notice.Percent == 12);
        });

        Test("Unsupported playback and unknown media duration expose no fabricated action or progress", () =>
        {
            var unsupported = Read(music: new(true, false, false, "player", "Song", "Artist")).Single();
            var supported = Read(music: new(true, false, true, "player", "Song", "Artist", 40, 200)).Single();
            Check(!unsupported.CanInvoke && unsupported.Progress == null && unsupported.Value == "Paused"
                && supported.PrimaryAction == IslandActivityAction.ResumeMusic && supported.Progress == .2
                && unsupported.Id == supported.Id);
        });

        Test("Only ongoing downloads show actual bytes and verified fractions, without inventing unknown-length progress", () =>
        {
            var known = new IslandDownloadActivity("known", "Report.pdf", 256, 1024, true);
            var unknown = new IslandDownloadActivity("unknown", "Archive.zip", 1536, null, true);
            var completed = new IslandDownloadActivity("completed", "Finished.pdf", 1024, 1024, false);
            var inputs = new[] { known, unknown, completed };
            var activities = Read(modules: new[] { "Integrations" }, downloads: inputs);
            var measured = activities.Single(item => item.Id == "download:known");
            var bounded = activities.Single(item => item.Id == "download:unknown");
            Check(activities.Count == 2 && measured.Title == known.Name && measured.Progress == .25 && measured.Value == "25%"
                && bounded.Progress == null && bounded.Value == "1.5 KB" && bounded.Subtitle == "1.5 KB received"
                && activities.All(item => item.Kind == IslandActivityKind.Download)
                && Read(modules: Array.Empty<string>(), downloads: inputs).Count == 0);
        });

        Test("Only a busy opted-in integration contributes an AI activity without response text or fabricated percentages", () =>
        {
            var busy = new IslandAiActivity(true, "llama3.2", "Generating reply");
            var activity = Read(modules: new[] { "Integrations" }, ai: busy).Single();
            Check(activity.Id == "ai:llama3.2" && activity.Kind == IslandActivityKind.Ai && activity.Module == "Integrations"
                && activity.Progress == null && activity.Value == "Working" && activity.Subtitle == "llama3.2 · Generating reply"
                && Read(modules: new[] { "Integrations" }, ai: busy with { Busy = false }).Count == 0
                && Read(modules: Array.Empty<string>(), ai: busy).Count == 0);
        });

        Test("Completed focus exposes the correct continuation or final dismissal without inventing another phase", () =>
        {
            var workDone = focus with { Phase = TimerPhase.Completed, RemainingSeconds = 0, CompletedRounds = 1 };
            var breakDone = workDone with { Stage = FocusStage.Break };
            var final = workDone with { CompletedRounds = 4 };
            var first = Read(session: workDone).Single();
            var second = Read(session: breakDone).Single();
            var last = Read(session: final).Single();
            Check(first.PrimaryAction == IslandActivityAction.ContinueFocus && first.PrimaryActionName == "Continue to break"
                && second.PrimaryActionName == "Continue to work" && last.PrimaryAction == IslandActivityAction.DismissFocus
                && first.Id == second.Id && second.Id == last.Id && last.Value == "Done");
        });

        Test("Focus actions bound to a replaced session cannot pause, resume, continue or dismiss its replacement", () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "CrestActivityIdentity-" + Guid.NewGuid().ToString("N"));
            var clock = new IdentityClock();
            try
            {
                using var hub = new LocalHub(new SettingsStore(directory), () => new AppSettings(), clock);
                hub.StartFocusSession(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), 2, "Old session");
                string oldId = hub.Focus.Id;
                hub.StartFocusSession(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 2, "Replacement");
                string replacementId = hub.Focus.Id;
                hub.PauseFocus(oldId); hub.DismissFocus(oldId);
                Check(hub.Focus.Id == replacementId && hub.Focus.Phase == TimerPhase.Running);
                hub.PauseFocus(replacementId); hub.ResumeFocus(oldId);
                Check(hub.Focus.Phase == TimerPhase.Paused && hub.Focus.Id == replacementId);
                hub.ResumeFocus(replacementId); clock.Advance(1); hub.Tick();
                hub.NextFocusPhase(oldId); hub.DismissFocus(oldId);
                Check(hub.Focus.Phase == TimerPhase.Completed && hub.Focus.Stage == FocusStage.Work && hub.Focus.Id == replacementId);
                hub.NextFocusPhase(replacementId);
                Check(hub.Focus.Phase == TimerPhase.Running && hub.Focus.Stage == FocusStage.Break);
                hub.DismissFocus(replacementId);
                Check(hub.Focus.Phase == TimerPhase.Idle);
            }
            finally
            {
                if (Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(directory).StartsWith("CrestActivityIdentity-", StringComparison.Ordinal) && Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        });
        return passed;
    }
    private sealed class IdentityClock : ITimerClock
    {
        public long MonotonicMilliseconds { get; private set; }
        public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.Parse("2026-11-25T00:00:00Z");
        public void Advance(double seconds) { MonotonicMilliseconds += (long)(seconds * 1000); UtcNow += TimeSpan.FromSeconds(seconds); }
    }
}
