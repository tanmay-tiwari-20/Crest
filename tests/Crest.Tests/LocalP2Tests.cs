using Crest.Core;
using Crest.Features;

internal static class LocalP2Tests
{
    public static int Run()
    {
        int passed = 0;
        string tempParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string root = Path.Combine(tempParent, "CrestLocalP2Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
        void Check(bool value) { if (!value) throw new Exception("Local P2 outcome assertion failed."); }
        (SettingsStore Store, Clock Clock, LocalHub Hub) Create(string name)
        { var store = new SettingsStore(Path.Combine(root, name)); var clock = new Clock(); return (store, clock, new(store, () => new(), clock)); }
        try
        {
            Test("Focus elapsed is monotonic and pausing excludes hidden downtime", () =>
            {
                var env = Create("focus-clock"); using var hub = env.Hub;
                hub.StartFocusSession(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), 2);
                env.Clock.Advance(TimeSpan.FromMinutes(5)); env.Clock.UtcNow += TimeSpan.FromDays(3);
                Check(hub.Focus.RemainingSeconds == 1200 && hub.Focus.Stage == FocusStage.Work);
                hub.PauseFocus(); env.Clock.Advance(TimeSpan.FromDays(1)); Check(hub.Focus.RemainingSeconds == 1200);
                hub.ResumeFocus(); env.Clock.Advance(TimeSpan.FromMinutes(20)); hub.Tick();
                Check(hub.Focus.Phase == TimerPhase.Completed && hub.Focus.CompletedRounds == 1);
            });
            Test("Focus work and break completion require explicit continuation and fire once", () =>
            {
                var env = Create("focus-rounds"); using var hub = env.Hub; int events = 0;
                hub.FocusCompleted += (_, _) => events++;
                hub.StartFocusSession(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), 2);
                env.Clock.Advance(TimeSpan.FromHours(1)); hub.Tick(); hub.Tick();
                Check(events == 1 && hub.Focus.CompletedRounds == 1 && hub.Focus.Stage == FocusStage.Work);
                hub.NextFocusPhase(); Check(hub.Focus.Stage == FocusStage.Break);
                env.Clock.Advance(TimeSpan.FromSeconds(1)); hub.Tick(); Check(events == 2 && hub.Focus.CompletedRounds == 1);
                hub.NextFocusPhase(); env.Clock.Advance(TimeSpan.FromSeconds(2)); hub.Tick(); hub.NextFocusPhase();
                Check(events == 3 && hub.Focus.Phase == TimerPhase.Completed && hub.Focus.CompletedRounds == 2 && hub.Focus.Stage == FocusStage.Work);
                hub.DismissFocus(); Check(hub.Focus.Phase == TimerPhase.Idle);
            });
            Test("Focus running recovery retains one overdue stage and paused recovery holds time", () =>
            {
                var env = Create("focus-restart"); env.Hub.StartFocusSession(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(5));
                env.Hub.Dispose(); env.Clock.Advance(TimeSpan.FromMinutes(1));
                using var restored = new LocalHub(env.Store, () => new(), env.Clock);
                Check(restored.Focus.Phase == TimerPhase.Completed && restored.Focus.CompletedRounds == 1);
                restored.NextFocusPhase(); restored.PauseFocus(); restored.Dispose(); env.Clock.Advance(TimeSpan.FromDays(1));
                using var paused = new LocalHub(env.Store, () => new(), env.Clock);
                Check(paused.Focus.Phase == TimerPhase.Paused && paused.Focus.RemainingSeconds == 5 && paused.Focus.Stage == FocusStage.Break);
                paused.DismissFocus(); paused.Dispose();
                using var dismissed = new LocalHub(env.Store, () => new(), env.Clock); Check(dismissed.Focus.Phase == TimerPhase.Idle);
            });
            Test("Focus validation rejects malformed durations and capacity without affecting timers", () =>
            {
                var env = Create("focus-invalid"); using var hub = env.Hub;
                hub.AddTimer(TimeSpan.FromMinutes(3), "Independent");
                bool rejected = false;
                try { hub.StartFocusSession(TimeSpan.Zero, TimeSpan.FromMinutes(5)); } catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected && hub.Focus.Phase == TimerPhase.Idle && hub.Timers.Count == 1);
                env.Store.WriteState("focus.json", new LocalHub.FocusState { Phase = TimerPhase.Running, WorkSeconds = 60, BreakSeconds = 30, RemainingSeconds = 60 });
                using var corrupt = new LocalHub(env.Store, () => new(), env.Clock); Check(corrupt.Focus.Phase == TimerPhase.Idle);
            });
            Test("Reminders become due once, snooze repeatedly, and dismissal persists", () =>
            {
                var env = Create("reminder-snooze"); using var hub = env.Hub; int events = 0;
                hub.ReminderDue += (_, _) => events++;
                string id = hub.AddReminder("Drink water", env.Clock.UtcNow + TimeSpan.FromSeconds(10));
                env.Clock.Advance(TimeSpan.FromSeconds(10)); hub.Tick(); hub.Tick();
                Check(events == 1 && hub.Reminders.Single().Phase == ReminderPhase.Due);
                hub.SnoozeReminder(id, TimeSpan.FromMinutes(5)); env.Clock.Advance(TimeSpan.FromMinutes(5)); hub.Tick();
                Check(events == 2 && hub.Reminders.Single().Id == id);
                hub.DismissReminder(id); hub.Dispose();
                using var restored = new LocalHub(env.Store, () => new(), env.Clock); Check(restored.Reminders.Count == 0);
            });
            Test("Reminder overdue restart retains due state without replaying live alerts", () =>
            {
                var env = Create("reminder-restart"); env.Hub.AddReminder("Appointment", env.Clock.UtcNow + TimeSpan.FromMinutes(1));
                env.Hub.Dispose(); env.Clock.Advance(TimeSpan.FromMinutes(10));
                using var restored = new LocalHub(env.Store, () => new(), env.Clock); int events = 0; restored.ReminderDue += (_, _) => events++;
                restored.Tick(); Check(restored.Reminders.Single().Phase == ReminderPhase.Due && events == 0);
                restored.SnoozeReminder(restored.Reminders[0].Id, TimeSpan.FromSeconds(2)); restored.Dispose();
                env.Clock.Advance(TimeSpan.FromSeconds(1)); using var scheduled = new LocalHub(env.Store, () => new(), env.Clock);
                Check(scheduled.Reminders.Single().Phase == ReminderPhase.Scheduled);
            });
            Test("Reminder scheduling follows wall time while focus remains monotonic", () =>
            {
                var env = Create("reminder-clock"); using var hub = env.Hub;
                hub.StartFocusSession(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(2)); hub.AddReminder("Local appointment", env.Clock.UtcNow + TimeSpan.FromMinutes(5));
                env.Clock.UtcNow += TimeSpan.FromHours(1); hub.Tick();
                Check(hub.Reminders[0].Phase == ReminderPhase.Due && hub.Focus.RemainingSeconds == 600);
                env.Clock.UtcNow -= TimeSpan.FromHours(2); hub.Tick(); Check(hub.Reminders[0].Phase == ReminderPhase.Due);
            });
            Test("Reminder bounds and reset remove durable focus and reminder data", () =>
            {
                var env = Create("reminder-bounds"); using var hub = env.Hub;
                for (int i = 0; i < LocalHub.MaxReminders; i++) hub.AddReminder(new string('x', 300), env.Clock.UtcNow + TimeSpan.FromMinutes(i + 1));
                bool rejected = false; try { hub.AddReminder("Over capacity", env.Clock.UtcNow); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected && hub.Reminders.Count == LocalHub.MaxReminders && hub.Reminders.All(x => x.Text.Length == 240));
                hub.StartFocusSession(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)); hub.ResetStopwatch(); Check(hub.Focus.Phase == TimerPhase.Running);
                hub.ResetProductivity(); Check(hub.Reminders.Count == 0 && hub.Focus.Phase == TimerPhase.Idle && !File.Exists(Path.Combine(env.Store.DirectoryPath, "focus.json")) && !File.Exists(Path.Combine(env.Store.DirectoryPath, "reminders.json")));
            });
            Test("Reset during focus notification suppresses queued reminder alerts", () =>
            {
                var env = Create("p2-notification-reset"); using var hub = env.Hub; int focusEvents = 0, reminderEvents = 0;
                hub.StartFocusSession(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                hub.AddReminder("Due alongside focus", env.Clock.UtcNow + TimeSpan.FromSeconds(1));
                hub.FocusCompleted += (_, _) => { focusEvents++; hub.ResetProductivity(); };
                hub.ReminderDue += (_, _) => reminderEvents++;
                env.Clock.Advance(TimeSpan.FromSeconds(1)); hub.Tick(); hub.Tick();
                Check(focusEvents == 1 && reminderEvents == 0 && hub.Focus.Phase == TimerPhase.Idle && hub.Reminders.Count == 0);
            });
            Test("Shelf pinning restores only selected references and unpinning removes persistence", () =>
            {
                var env = Create("shelf-pin"); string first = Path.Combine(root, "first.txt"), second = Path.Combine(root, "second.txt"); File.WriteAllText(first, "original"); File.WriteAllText(second, "session");
                env.Hub.AddShelfFiles(new[] { first, second }); string id = env.Hub.Shelf[0].Id; env.Hub.SetShelfPinned(id, true); env.Hub.Dispose();
                using var restored = new LocalHub(env.Store, () => new(), env.Clock);
                Check(restored.Shelf.Count == 1 && restored.Shelf[0].Id == id && restored.Shelf[0].Pinned && File.ReadAllText(first) == "original");
                restored.SetShelfPinned(id, false); restored.Dispose(); using var unpinned = new LocalHub(env.Store, () => new(), env.Clock); Check(unpinned.Shelf.Count == 0);
            });
            Test("Missing pinned references retain identity and relink safely to a chosen original", () =>
            {
                var env = Create("shelf-relink"); string old = Path.Combine(root, "old.txt"), moved = Path.Combine(root, "moved.txt"); File.WriteAllText(old, "kept bytes");
                env.Hub.AddShelfFiles(new[] { old }); string id = env.Hub.Shelf[0].Id; env.Hub.SetShelfPinned(id, true); File.Move(old, moved); env.Hub.Dispose();
                using var restored = new LocalHub(env.Store, () => new(), env.Clock);
                Check(!restored.Shelf[0].Available && restored.ShelfRecoveryFolder(id) == root);
                Check(restored.RelinkShelf(id, root) is not null && restored.RelinkShelf(id, moved) is null && restored.Shelf[0].Path == moved && restored.Shelf[0].Pinned);
                restored.Dispose(); using var relinked = new LocalHub(env.Store, () => new(), env.Clock); Check(relinked.Shelf[0].Id == id && relinked.Shelf[0].Available && File.ReadAllText(moved) == "kept bytes");
            });
            Test("Shelf recovery rejects duplicates and clear removes pinned references permanently", () =>
            {
                var env = Create("shelf-clear-pinned"); using var hub = env.Hub;
                string a = Path.Combine(root, "a.txt"), b = Path.Combine(root, "b.txt"); File.WriteAllText(a, "a"); File.WriteAllText(b, "b"); hub.AddShelfFiles(new[] { a, b });
                string id = hub.Shelf[0].Id; hub.SetShelfPinned(id, true); Check(hub.RelinkShelf(id, b) is not null && hub.Shelf.Count == 2);
                hub.ClearShelf(); hub.Dispose(); using var restored = new LocalHub(env.Store, () => new(), env.Clock); Check(restored.Shelf.Count == 0 && File.Exists(a) && File.Exists(b));
            });
            return passed;
        }
        finally
        {
            if (Path.GetFullPath(root).StartsWith(tempParent, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("CrestLocalP2Tests-", StringComparison.Ordinal) && Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    private sealed class Clock : ITimerClock
    {
        public long MonotonicMilliseconds { get; private set; }
        public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan time) { MonotonicMilliseconds += (long)time.TotalMilliseconds; UtcNow += time; }
    }
}
