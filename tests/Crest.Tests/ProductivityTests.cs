using Crest.Core;
using Crest.Features;

internal static class ProductivityTests
{
    public static int Run()
    {
        int passed = 0;
        string tempParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string root = Path.GetFullPath(Path.Combine(tempParent, "CrestProductivityTests-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
        void Check(bool value) { if (!value) throw new Exception("Productivity outcome assertion failed."); }
        void Equal(double expected, double actual) { if (Math.Abs(expected - actual) > .001) throw new Exception($"Expected {expected}; got {actual}"); }
        (SettingsStore Store, ProductivityClock Clock, LocalHub Hub) Create(string name)
        {
            var store = new SettingsStore(Path.Combine(root, name));
            var clock = new ProductivityClock();
            return (store, clock, new LocalHub(store, () => new(), clock));
        }
        try
        {
            Test("Multiple timers pause, resume, and cancel independently", () =>
            {
                var env = Create("independent"); using var hub = env.Hub;
                string tea = hub.AddTimer(TimeSpan.FromSeconds(90), "Tea");
                string focus = hub.AddTimer(TimeSpan.FromMinutes(10), "Focus");
                env.Clock.Advance(TimeSpan.FromSeconds(20));
                hub.PauseTimer(tea);
                env.Clock.Advance(TimeSpan.FromSeconds(30));
                Equal(70, hub.Timers.Single(x => x.Id == tea).RemainingSeconds);
                Equal(550, hub.Timers.Single(x => x.Id == focus).RemainingSeconds);
                hub.ResumeTimer(tea); env.Clock.Advance(TimeSpan.FromSeconds(10));
                Equal(60, hub.Timers.Single(x => x.Id == tea).RemainingSeconds);
                hub.CancelTimer(focus);
                Check(hub.Timers.Count == 1 && hub.Timer.Id == tea);
            });
            Test("Simultaneous timer completions are retained and emitted once per timer", () =>
            {
                var env = Create("completion"); using var hub = env.Hub;
                var ids = new[] { hub.AddTimer(TimeSpan.FromSeconds(5), "Tea"), hub.AddTimer(TimeSpan.FromSeconds(5), "Stretch") };
                var completions = new List<string>(); int sounds = 0;
                hub.TimerCompleted += (_, e) => completions.Add(e.Timer.Id);
                hub.Completion += (_, _) => sounds++;
                env.Clock.Advance(TimeSpan.FromHours(1)); hub.Tick(); hub.Tick();
                Check(hub.Timers.All(x => x.Phase == TimerPhase.Completed) && completions.SequenceEqual(ids) && sounds == 2);
                hub.AcknowledgeTimer(ids[0]);
                Check(hub.Timers.Count == 1 && hub.Timer.Id == ids[1] && hub.Timer.Phase == TimerPhase.Completed);
            });
            Test("Reset, acknowledgement, cancellation, or disposal during notification suppresses stale completion", () =>
            {
                foreach (string action in new[] { "reset", "acknowledge", "cancel", "dispose" })
                {
                    var env = Create("completion-invalidated-" + action); using var hub = env.Hub;
                    string id = hub.AddTimer(TimeSpan.FromSeconds(1));
                    int typed = 0, compatibility = 0;
                    bool invalidated = false;
                    hub.TimerCompleted += (_, _) => typed++;
                    hub.Completion += (_, _) => compatibility++;
                    hub.Changed += (_, _) =>
                    {
                        if (invalidated || !hub.Timers.Any(timer => timer.Phase == TimerPhase.Completed)) return;
                        invalidated = true;
                        switch (action)
                        {
                            case "reset": hub.ResetProductivity(); break;
                            case "acknowledge": hub.AcknowledgeTimer(id); break;
                            case "cancel": hub.CancelTimer(id); break;
                            case "dispose": hub.Dispose(); break;
                        }
                    };
                    env.Clock.Advance(TimeSpan.FromSeconds(2));
                    hub.Tick(); hub.Tick(); hub.PauseTimer(id);
                    Check(invalidated && typed == 0 && compatibility == 0);
                }
                var acknowledged = Create("completion-typed-acknowledgement"); using var typedHub = acknowledged.Hub;
                typedHub.AddTimer(TimeSpan.FromSeconds(1));
                int typedCount = 0, compatibilityCount = 0;
                typedHub.TimerCompleted += (_, e) => { typedCount++; typedHub.AcknowledgeTimer(e.Timer.Id); };
                typedHub.Completion += (_, _) => compatibilityCount++;
                acknowledged.Clock.Advance(TimeSpan.FromSeconds(1));
                typedHub.Tick();
                Check(typedCount == 1 && compatibilityCount == 0 && typedHub.Timers.Count == 0);
            });
            Test("Pausing at expiry completes once while paused timers remain silent", () =>
            {
                var env = Create("pause-at-expiry"); using var hub = env.Hub;
                string expired = hub.AddTimer(TimeSpan.FromSeconds(1));
                int typed = 0, compatibility = 0;
                hub.TimerCompleted += (_, e) => { Check(e.Timer.Id == expired); typed++; };
                hub.Completion += (_, _) => compatibility++;
                env.Clock.Advance(TimeSpan.FromSeconds(1));
                hub.PauseTimer(expired);
                hub.Tick(); hub.PauseTimer(expired); hub.ResumeTimer(expired);
                string paused = hub.AddTimer(TimeSpan.FromSeconds(10));
                env.Clock.Advance(TimeSpan.FromSeconds(3)); hub.PauseTimer(paused);
                env.Clock.Advance(TimeSpan.FromHours(1)); hub.Tick();
                Check(hub.Timers.Single(timer => timer.Id == expired).Phase == TimerPhase.Completed && typed == 1 && compatibility == 1);
                Check(hub.Timers.Single(timer => timer.Id == paused).Phase == TimerPhase.Paused);
                Equal(7, hub.Timers.Single(timer => timer.Id == paused).RemainingSeconds);
            });
            Test("Restored overdue timers stay quiet while future recovered timers still complete normally", () =>
            {
                var env = Create("quiet-overdue-restart");
                string overdue = env.Hub.AddTimer(TimeSpan.FromSeconds(1));
                string future = env.Hub.AddTimer(TimeSpan.FromMinutes(1));
                env.Hub.Dispose();
                env.Clock.Advance(TimeSpan.FromSeconds(5));
                using var restored = new LocalHub(env.Store, () => new(), env.Clock);
                var completions = new List<string>(); int compatibility = 0;
                restored.TimerCompleted += (_, e) => completions.Add(e.Timer.Id);
                restored.Completion += (_, _) => compatibility++;
                restored.Tick(); restored.Tick();
                Check(restored.Timers.Single(timer => timer.Id == overdue).Phase == TimerPhase.Completed && completions.Count == 0 && compatibility == 0);
                env.Clock.Advance(TimeSpan.FromMinutes(1)); restored.Tick(); restored.Tick();
                Check(completions.SequenceEqual(new[] { future }) && compatibility == 1 && restored.Timers.All(timer => timer.Phase == TimerPhase.Completed));
                restored.Dispose();
                using var again = new LocalHub(env.Store, () => new(), env.Clock);
                int replayed = 0;
                again.TimerCompleted += (_, _) => replayed++;
                again.Completion += (_, _) => replayed++;
                again.Tick();
                Check(again.Timers.Count == 2 && replayed == 0);
            });
            Test("Selection and compatibility start preserve other concurrent timers", () =>
            {
                var env = Create("selection"); using var hub = env.Hub;
                string first = hub.AddTimer(TimeSpan.FromMinutes(5), "First");
                string second = hub.AddTimer(TimeSpan.FromMinutes(10), "Second");
                hub.SelectTimer(first); hub.StartTimer(TimeSpan.FromMinutes(2));
                Equal(120, hub.Timer.RemainingSeconds);
                Equal(600, hub.Timers.Single(x => x.Id == second).RemainingSeconds);
                hub.SelectTimer("absent"); hub.AcknowledgeTimer(first);
                Check(hub.SelectedTimerId == first && hub.Timers.Count == 2);
            });
            Test("Timer capacity, names, and durations have explicit bounds", () =>
            {
                var env = Create("limits"); using var hub = env.Hub;
                for (int i = 0; i < LocalHub.MaxTimers; i++) hub.AddTimer(TimeSpan.FromSeconds(10), new string('x', 80));
                Check(hub.Timers.All(x => x.Name.Length == 40));
                bool full = false, shortDuration = false, longDuration = false;
                try { hub.AddTimer(TimeSpan.FromSeconds(10)); } catch (InvalidOperationException) { full = true; }
                try { hub.AddTimer(TimeSpan.FromMilliseconds(999)); } catch (ArgumentOutOfRangeException) { shortDuration = true; }
                try { hub.StartTimer(TimeSpan.FromDays(8)); } catch (ArgumentOutOfRangeException) { longDuration = true; }
                Check(full && shortDuration && longDuration && hub.Timers.Count == LocalHub.MaxTimers);
            });
            Test("Restart recovers running, paused, and completed timers with stable selection", () =>
            {
                var env = Create("restart");
                string running = env.Hub.AddTimer(TimeSpan.FromMinutes(10), "Running");
                string paused = env.Hub.AddTimer(TimeSpan.FromMinutes(5), "Paused");
                env.Hub.PauseTimer(paused);
                string overdue = env.Hub.AddTimer(TimeSpan.FromSeconds(5), "Overdue");
                env.Hub.SelectTimer(running); env.Hub.Dispose();
                env.Clock.Advance(TimeSpan.FromMinutes(2));
                using var restored = new LocalHub(env.Store, () => new(), env.Clock);
                Check(restored.Timer.Id == running && restored.Timers.Single(x => x.Id == overdue).Phase == TimerPhase.Completed);
                Equal(480, restored.Timer.RemainingSeconds);
                Equal(300, restored.Timers.Single(x => x.Id == paused).RemainingSeconds);
                restored.AcknowledgeTimer(overdue); restored.Dispose();
                using var again = new LocalHub(env.Store, () => new(), env.Clock);
                Check(again.Timers.Count == 2 && again.Timers.All(x => x.Id != overdue));
            });
            Test("Version-one single timer migrates without losing its pending completion", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "legacy")); var clock = new ProductivityClock();
                store.WriteState("timer.json", new LocalHub.TimerState { Phase = TimerPhase.Running, TotalSeconds = 30, RemainingSeconds = 30, DeadlineUtc = clock.UtcNow + TimeSpan.FromSeconds(30) });
                clock.Advance(TimeSpan.FromMinutes(1));
                using var hub = new LocalHub(store, () => new(), clock);
                Check(hub.Timers.Count == 1 && hub.Timer.Phase == TimerPhase.Completed && hub.Timer.Name == "Focus");
                Check(store.ReadState<LocalHub.TimerSetState>("timer.json")?.SchemaVersion == 2);
                hub.AcknowledgeTimer();
                Check(!File.Exists(Path.Combine(store.DirectoryPath, "timer.json")));
            });
            Test("Malformed timer entries cannot discard valid timers or exceed capacity", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "malformed")); var clock = new ProductivityClock();
                var states = new List<LocalHub.TimerState>
                {
                    null!,
                    new() { Id = "no-deadline", Phase = TimerPhase.Running, TotalSeconds = 5, RemainingSeconds = 5 },
                    new() { Id = "negative", Phase = TimerPhase.Paused, TotalSeconds = 5, RemainingSeconds = -1 },
                    new() { Id = "valid", Name = "Kept", Phase = TimerPhase.Paused, TotalSeconds = 60, RemainingSeconds = 40 },
                    new() { Id = "valid", Phase = TimerPhase.Paused, TotalSeconds = 60, RemainingSeconds = 20 }
                };
                store.WriteState("timer.json", new LocalHub.TimerSetState { SelectedTimerId = "missing", Timers = states });
                using var hub = new LocalHub(store, () => new(), clock);
                Check(hub.Timers.Count == 1 && hub.Timer.Id == "valid"); Equal(40, hub.Timer.RemainingSeconds);
                Check(store.ReadState<LocalHub.TimerSetState>("timer.json")?.Timers?.Count == 1);
            });
            Test("Stopwatch uses suspended monotonic time and excludes paused time", () =>
            {
                var env = Create("stopwatch"); using var hub = env.Hub;
                hub.StartStopwatch(); env.Clock.Advance(TimeSpan.FromSeconds(10));
                env.Clock.UtcNow += TimeSpan.FromDays(1); Equal(10, hub.Stopwatch.ElapsedSeconds);
                env.Clock.UtcNow -= TimeSpan.FromDays(2); Equal(10, hub.Stopwatch.ElapsedSeconds);
                hub.PauseStopwatch(); env.Clock.Advance(TimeSpan.FromHours(1)); Equal(10, hub.Stopwatch.ElapsedSeconds);
                hub.StartStopwatch(); env.Clock.Advance(TimeSpan.FromHours(1)); hub.Tick();
                Equal(3610, hub.Stopwatch.ElapsedSeconds);
                Check(hub.Stopwatch.Phase == StopwatchPhase.Running);
            });
            Test("Stopwatch lap splits and capacity retain the first hundred records", () =>
            {
                var env = Create("laps"); using var hub = env.Hub;
                Check(hub.LapStopwatch() is null); hub.StartStopwatch();
                env.Clock.Advance(TimeSpan.FromMilliseconds(1250));
                var first = hub.LapStopwatch()!; Equal(1.25, first.TotalSeconds); Equal(1.25, first.SplitSeconds);
                env.Clock.Advance(TimeSpan.FromMilliseconds(2500));
                var second = hub.LapStopwatch()!; Equal(3.75, second.TotalSeconds); Equal(2.5, second.SplitSeconds);
                for (int i = 2; i < LocalHub.MaxStopwatchLaps; i++) { env.Clock.Advance(TimeSpan.FromSeconds(1)); hub.LapStopwatch(); }
                Check(hub.LapStopwatch() is null && hub.Stopwatch.Laps.Count == LocalHub.MaxStopwatchLaps && hub.Stopwatch.Laps[0] == first);
                hub.PauseStopwatch(); Check(hub.LapStopwatch() is null);
            });
            Test("Stopwatch and laps recover across running and paused restarts", () =>
            {
                var env = Create("stopwatch-restart");
                env.Hub.StartStopwatch(); env.Clock.Advance(TimeSpan.FromSeconds(10)); env.Hub.LapStopwatch(); env.Hub.Dispose();
                env.Clock.Advance(TimeSpan.FromSeconds(20));
                using var restored = new LocalHub(env.Store, () => new(), env.Clock);
                Equal(30, restored.Stopwatch.ElapsedSeconds); Check(restored.Stopwatch.Laps.Count == 1);
                restored.PauseStopwatch(); restored.Dispose(); env.Clock.Advance(TimeSpan.FromDays(1));
                using var paused = new LocalHub(env.Store, () => new(), env.Clock);
                Check(paused.Stopwatch.Phase == StopwatchPhase.Paused); Equal(30, paused.Stopwatch.ElapsedSeconds);
                paused.ResetStopwatch(); Check(paused.Stopwatch.Phase == StopwatchPhase.Idle && paused.Stopwatch.Laps.Count == 0);
                Check(!File.Exists(Path.Combine(env.Store.DirectoryPath, "stopwatch.json")));
            });
            Test("Backwards UTC restart never erases stopwatch elapsed time or laps", () =>
            {
                var env = Create("stopwatch-backwards");
                env.Hub.StartStopwatch(); env.Clock.Advance(TimeSpan.FromSeconds(15)); env.Hub.LapStopwatch(); env.Hub.Dispose();
                env.Clock.UtcNow -= TimeSpan.FromDays(1);
                using var restored = new LocalHub(env.Store, () => new(), env.Clock);
                Equal(15, restored.Stopwatch.ElapsedSeconds); Check(restored.Stopwatch.Laps.Count == 1);
                env.Clock.Advance(TimeSpan.FromSeconds(5)); Equal(20, restored.Stopwatch.ElapsedSeconds);
            });
            Test("Malformed stopwatch records recover safely and lap totals are normalized", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "stopwatch-malformed")); var clock = new ProductivityClock();
                store.WriteState("stopwatch.json", new LocalHub.StopwatchState { Phase = StopwatchPhase.Paused, ElapsedSeconds = 30, Laps = new() { null!, new(88, 10, 999), new(2, 5, 2), new(3, 40, 30), new(12, 20, -1) } });
                using var hub = new LocalHub(store, () => new(), clock);
                Check(hub.Stopwatch.Laps.Count == 2 && hub.Stopwatch.Laps[0].Number == 1 && hub.Stopwatch.Laps[1].Number == 2);
                Equal(10, hub.Stopwatch.Laps[1].SplitSeconds);
                hub.Dispose(); store.WriteState("stopwatch.json", new LocalHub.StopwatchState { Phase = StopwatchPhase.Running, ElapsedSeconds = 20 });
                using var invalid = new LocalHub(store, () => new(), clock);
                Check(invalid.Stopwatch.Phase == StopwatchPhase.Idle);
            });
            Test("Corrupt extreme activity durations and maximum UTC cannot overflow controls", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "extremes")); var clock = new ProductivityClock();
                store.WriteState("timer.json", new LocalHub.TimerSetState { Timers = new()
                {
                    new() { Id = "extreme", Phase = TimerPhase.Paused, TotalSeconds = 60, RemainingSeconds = TimeSpan.MaxValue.TotalSeconds },
                    new() { Id = "future", Phase = TimerPhase.Running, TotalSeconds = 60, RemainingSeconds = 60, DeadlineUtc = DateTimeOffset.MaxValue },
                    new() { Id = "valid", Phase = TimerPhase.Paused, TotalSeconds = 60, RemainingSeconds = 40 }
                } });
                store.WriteState("stopwatch.json", new LocalHub.StopwatchState { Phase = StopwatchPhase.Paused, ElapsedSeconds = TimeSpan.MaxValue.TotalSeconds });
                using var hub = new LocalHub(store, () => new(), clock);
                Check(hub.Timers.Count == 1 && hub.Timer.Id == "valid" && hub.Stopwatch.Phase == StopwatchPhase.Idle);
                clock.UtcNow = DateTimeOffset.MaxValue;
                hub.ResumeTimer(); hub.PauseTimer(); hub.StartTimer(TimeSpan.FromDays(7));
                Check(hub.Timer.DeadlineUtc == DateTimeOffset.MaxValue);
                _ = TimeSpan.FromSeconds(Math.Ceiling(hub.Timer.RemainingSeconds));
                hub.ResetProductivity();
                store.WriteState("stopwatch.json", new LocalHub.StopwatchState { Phase = StopwatchPhase.Running, ElapsedSeconds = LocalHub.MaxActivitySeconds, StartedUtc = DateTimeOffset.MinValue });
                using var maximum = new LocalHub(store, () => new(), clock);
                clock.AdvanceMonotonic(TimeSpan.FromMinutes(1));
                Equal(LocalHub.MaxActivitySeconds, maximum.Stopwatch.ElapsedSeconds);
                _ = TimeSpan.FromSeconds(maximum.Stopwatch.ElapsedSeconds);
                maximum.PauseStopwatch(); maximum.StartStopwatch(); maximum.LapStopwatch();
            });
            Test("Productivity reset and data clear remove all timer and stopwatch records", () =>
            {
                var env = Create("reset"); using var hub = env.Hub;
                hub.AddTimer(TimeSpan.FromSeconds(10)); hub.AddTimer(TimeSpan.FromSeconds(20)); hub.StartStopwatch(); hub.LapStopwatch();
                hub.ResetProductivity();
                Check(hub.Timers.Count == 0 && hub.Timer.Phase == TimerPhase.Idle && hub.Stopwatch.Phase == StopwatchPhase.Idle);
                Check(!File.Exists(Path.Combine(env.Store.DirectoryPath, "timer.json")) && !File.Exists(Path.Combine(env.Store.DirectoryPath, "stopwatch.json")));
                hub.StartStopwatch(); env.Store.Clear();
                Check(!File.Exists(Path.Combine(env.Store.DirectoryPath, "stopwatch.json")));
                hub.ResetProductivity();
            });
            return passed;
        }
        finally
        {
            if (root.StartsWith(tempParent, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("CrestProductivityTests-", StringComparison.Ordinal) && Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
    private sealed class ProductivityClock : ITimerClock
    {
        public long MonotonicMilliseconds { get; private set; }
        public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan elapsed) { MonotonicMilliseconds += (long)elapsed.TotalMilliseconds; UtcNow += elapsed; }
        public void AdvanceMonotonic(TimeSpan elapsed) => MonotonicMilliseconds += (long)elapsed.TotalMilliseconds;
    }
}
