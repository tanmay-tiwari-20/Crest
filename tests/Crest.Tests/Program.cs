using Crest.Core;
using Crest.Features;

int passed = 0;
string tempParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
string root = Path.GetFullPath(Path.Combine(tempParent, "CrestTests-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
try
{
    passed += FoundationTests.Run();
    passed += SpringDynamicsTests.Run();
    passed += IslandActivityTests.Run();
    passed += ControlEditTests.Run();
    passed += ProductivityTests.Run();
    passed += DiagnosticTests.Run();
    passed += VisibilityTests.Run();
    passed += BatteryNoticeTests.Run();
    passed += ShelfTests.Run();
    passed += LocalP2Tests.Run();
    passed += ClipboardHistoryTests.Run();
    passed += SystemStatisticsTests.Run();
    passed += LocalWorkflowTests.Run();
    passed += WeatherCalendarTests.Run();
    passed += DownloadAiTests.Run();
    passed += WindowsIntegrationTests.Run();
    Test("Live clock changes do not affect running countdown", () =>
    {
        var env = Create("clock");
        using var hub = env.Hub;
        hub.StartTimer(TimeSpan.FromMinutes(10));
        env.Clock.Advance(TimeSpan.FromMinutes(2));
        env.Clock.UtcNow += TimeSpan.FromDays(2);
        Equal(480, hub.Timer.RemainingSeconds);
        env.Clock.UtcNow -= TimeSpan.FromDays(4);
        Equal(480, hub.Timer.RemainingSeconds);
    });
    Test("Suspended elapsed time completes once and acknowledgement persists", () =>
    {
        var env = Create("sleep");
        using (var hub = env.Hub)
        {
            int completions = 0;
            hub.Completion += (_, _) => completions++;
            hub.StartTimer(TimeSpan.FromSeconds(30));
            env.Clock.Advance(TimeSpan.FromHours(1));
            hub.Tick(); hub.Tick();
            Check(hub.Timer.Phase == TimerPhase.Completed && completions == 1);
            hub.AcknowledgeTimer();
        }
        using var recovered = new LocalHub(env.Store, () => new(), env.Clock);
        Check(recovered.Timer.Phase == TimerPhase.Idle);
    });
    Test("Restart recovers against saved UTC deadline", () =>
    {
        var env = Create("restart");
        env.Hub.StartTimer(TimeSpan.FromMinutes(10));
        env.Hub.Dispose();
        env.Clock.Advance(TimeSpan.FromMinutes(3));
        using var recovered = new LocalHub(env.Store, () => new(), env.Clock);
        Equal(420, recovered.Timer.RemainingSeconds);
        Check(recovered.Timer.Phase == TimerPhase.Running);
    });
    Test("Restart uses UTC clock changes and retains one overdue completion", () =>
    {
        var env = Create("restart-changed-clock");
        env.Hub.StartTimer(TimeSpan.FromMinutes(10));
        env.Hub.Dispose();
        env.Clock.UtcNow += TimeSpan.FromDays(1);
        using (var recovered = new LocalHub(env.Store, () => new(), env.Clock)) Check(recovered.Timer.Phase == TimerPhase.Completed);
        using var again = new LocalHub(env.Store, () => new(), env.Clock);
        Check(again.Timer.Phase == TimerPhase.Completed);
    });
    Test("A backwards UTC change at restart uses the actual saved deadline", () =>
    {
        var env = Create("restart-backwards-clock");
        env.Hub.StartTimer(TimeSpan.FromMinutes(10)); env.Hub.Dispose();
        env.Clock.UtcNow -= TimeSpan.FromHours(1);
        using var recovered = new LocalHub(env.Store, () => new(), env.Clock);
        Equal(4200, recovered.Timer.RemainingSeconds);
        recovered.PauseTimer(); recovered.Dispose();
        env.Clock.Advance(TimeSpan.FromHours(2));
        using var paused = new LocalHub(env.Store, () => new(), env.Clock);
        Equal(4200, paused.Timer.RemainingSeconds);
    });
    Test("Paused duration survives restart and resume", () =>
    {
        var env = Create("paused");
        env.Hub.StartTimer(TimeSpan.FromMinutes(10));
        env.Clock.Advance(TimeSpan.FromMinutes(2));
        env.Hub.PauseTimer(); env.Hub.Dispose();
        env.Clock.Advance(TimeSpan.FromDays(1));
        using var recovered = new LocalHub(env.Store, () => new(), env.Clock);
        Check(recovered.Timer.Phase == TimerPhase.Paused);
        Equal(480, recovered.Timer.RemainingSeconds);
        recovered.ResumeTimer(); env.Clock.Advance(TimeSpan.FromSeconds(30));
        Equal(450, recovered.Timer.RemainingSeconds);
    });
    Test("Corrupt and unsupported settings recover safe privacy defaults", () =>
    {
        string dir = Path.Combine(root, "corrupt"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{broken");
        var store = new SettingsStore(dir);
        AppSettings settings = store.Load();
        Check(store.RecoveredCorruptSettings && settings.StrictDesktop && !settings.TimerSound && !settings.AccessibilityShortcut && !settings.StartWithWindows);
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"SchemaVersion\":999}");
        Check(store.Load().SchemaVersion == 1);
    });
    Test("Versioned preferences round trip and invalid values clamp", () =>
    {
        var store = new SettingsStore(Path.Combine(root, "settings"));
        var settings = new AppSettings { Theme = "Light", DwellMs = 9000, GraceMs = -1, Launchers = new() { new() { Name = "Docs", Target = "https://example.com", Kind = "Website" } } };
        store.Save(settings); AppSettings loaded = store.Load();
        Check(loaded.Theme == "Light" && loaded.DwellMs == 500 && loaded.GraceMs == 100 && loaded.Launchers.Count == 1);
    });
    Test("Shelf caps references, reports missing sources and never modifies files", () =>
    {
        var env = Create("shelf"); using var hub = env.Hub;
        string filesDir = Path.Combine(root, "files"); Directory.CreateDirectory(filesDir);
        string[] files = Enumerable.Range(0, 21).Select(i => Path.Combine(filesDir, "file-" + i + ".txt")).ToArray();
        foreach (string file in files) File.WriteAllText(file, "source remains untouched");
        Check(hub.AddShelfFiles(files) is not null && hub.Shelf.Count == 20);
        ShelfItem reference = hub.Shelf[0]; hub.RemoveShelf(reference.Id);
        Check(File.ReadAllText(reference.Path) == "source remains untouched");
        ShelfItem missing = hub.Shelf[0]; File.Move(missing.Path, missing.Path + ".moved"); hub.RefreshShelf();
        Check(!hub.Shelf.Single(x => x.Id == missing.Id).Available);
        Check(hub.OpenShelf(missing.Id) is not null);
        hub.Dispose(); Check(hub.Shelf.Count == 0);
        using var restarted = new LocalHub(env.Store, () => new(), env.Clock);
        Check(restarted.Shelf.Count == 0 && !File.Exists(Path.Combine(env.Store.DirectoryPath, "shelf.json")));
        Check(files.Skip(2).All(File.Exists));
    });
    Test("Launcher rejects absent targets and unselected URI protocols", () =>
    {
        var env = Create("launcher"); using var hub = env.Hub;
        Check(hub.Launch(new() { Target = "javascript:alert(1)" }) is not null);
        Check(hub.Launch(new() { Target = Path.Combine(root, "absent.exe") }) is not null);
    });
    Console.WriteLine($"PASS: {passed} outcome tests");
}
finally
{
    if (root.StartsWith(tempParent, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("CrestTests-", StringComparison.Ordinal) && Directory.Exists(root))
        Directory.Delete(root, true);
}

void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
void Check(bool value) { if (!value) throw new Exception("Outcome assertion failed."); }
void Equal(double expected, double actual) { if (Math.Abs(expected - actual) > .001) throw new Exception($"Expected {expected}; got {actual}"); }
(SettingsStore Store, FakeClock Clock, LocalHub Hub) Create(string name)
{
    var store = new SettingsStore(Path.Combine(root, name)); var clock = new FakeClock();
    return (store, clock, new LocalHub(store, () => new(), clock));
}
sealed class FakeClock : ITimerClock
{
    public long MonotonicMilliseconds { get; private set; }
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    public void Advance(TimeSpan elapsed) { MonotonicMilliseconds += (long)elapsed.TotalMilliseconds; UtcNow += elapsed; }
}
