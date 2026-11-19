using Crest.Core;
using Crest.Features;

internal static class LocalWorkflowTests
{
    public static int Run()
    {
        int count = 0;
        string parent = Path.GetFullPath(Path.GetTempPath());
        string root = Path.Combine(parent, "CrestWorkflowTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Test("Selected captures persist references, bound recent entries and preserve original bytes", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "captures"));
                var hub = new LocalWorkflowHub(store);
                string[] files = Enumerable.Range(0, 14).Select(i => Path.Combine(root, i + ".png")).ToArray();
                foreach (var file in files) File.WriteAllText(file, "original");
                Check(hub.SelectCaptures(files) == null && hub.Captures.Count == 12);
                Check(hub.Captures[0].Path == files[^1]);
                hub.SelectCaptures(new[] { files[5] }); Check(hub.Captures[0].Path == files[5] && hub.Captures.Count == 12);
                var restored = new LocalWorkflowHub(store); Check(restored.Captures.Count == 12);
                File.Move(files[5], files[5] + ".moved"); Check(!restored.Captures[0].Available);
                restored.ClearCaptures(); Check(new LocalWorkflowHub(store).Captures.Count == 0);
                Check(File.ReadAllText(files[5] + ".moved") == "original");
            });
            Test("Capture selection rejects non-image protocols and unsupported recovery", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "invalid"));
                var hub = new LocalWorkflowHub(store);
                Check(hub.SelectCaptures(new[] { "https://example.com/pic.png", Path.Combine(root, "missing.png"), "relative.png" }) != null && hub.Captures.Count == 0);
                store.WriteState("captures.json", new LocalWorkflowHub.CaptureState { SchemaVersion = 99 });
                Check(new LocalWorkflowHub(store).Captures.Count == 0);
            });
            Test("Workspace launch continues after a missing target and reports named failures", () =>
            {
                var workspace = new WorkspacePreset { Targets = new() { new() { Name = "Editor" }, new() { Name = "Missing" }, new() { Name = "Docs" } } };
                var launched = new List<string>();
                string? error = LocalWorkflowHub.LaunchWorkspace(workspace, target => { launched.Add(target.Name); return target.Name == "Missing" ? "unavailable" : null; });
                Check(launched.SequenceEqual(new[] { "Editor", "Missing", "Docs" }) && error!.Contains("Missing: unavailable"));
                Check(LocalWorkflowHub.LaunchWorkspace(new(), _ => throw new Exception()) != null);
            });
            Test("Independent display preferences apply without changing defaults and normalize safely", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "displays"));
                var settings = new AppSettings { Theme = "Light", TriggerWidth = 240 };
                settings.CustomizeDisplay("DISPLAY1").Theme = "Ocean";
                settings.CustomizeDisplay("DISPLAY1").TriggerWidth = 600;
                settings.CustomizeDisplay("DISPLAY2").Theme = "Forest";
                settings.Displays["DISPLAY2"].SizeScale = double.PositiveInfinity;
                settings.Displays["DISPLAY2"].DwellMs = 9000;
                store.Save(settings); var restored = store.Load();
                Check(restored.ForDisplay("DISPLAY1").Theme == "Ocean" && restored.ForDisplay("DISPLAY1").TriggerWidth == 600);
                Check(restored.ForDisplay("DISPLAY2").Theme == "Forest" && restored.ForDisplay("DISPLAY2").SizeScale == 1 && restored.ForDisplay("DISPLAY2").DwellMs == 500);
                Check(restored.ForDisplay("DISCONNECTED").Theme == "Light" && restored.Theme == "Light" && restored.TriggerWidth == 240);
            });
            Test("Module order, gestures and bounded workspace settings survive recovery", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "preferences"));
                var settings = new AppSettings { Theme = "Rose", DoubleClickGesture = "Next module", WheelGesture = "Switch modules", ModuleOrder = new() { "Shelf", "Shelf", "invalid", "Clipboard" }, WorkspacePresets = Enumerable.Range(0, 22).Select(_ => new WorkspacePreset { Id = "same", Targets = Enumerable.Range(0, 22).Select(i => new LauncherEntry { Target = "https://example.com/" + i }).ToList() }).ToList() };
                store.Save(settings); var restored = store.Load();
                Check(restored.ModuleOrder[0] == "Shelf" && restored.ModuleOrder.Distinct().Count() == ModuleCatalog.All.Length);
                Check(restored.DoubleClickGesture == "Next module" && restored.WheelGesture == "Switch modules" && restored.Theme == "Rose");
                Check(restored.WorkspacePresets.Count == 20 && restored.WorkspacePresets.All(x => x.Targets.Count == 20) && restored.WorkspacePresets.Select(x => x.Id).Distinct().Count() == 20);
                Check(!restored.ClipboardHistoryEnabled && restored.ClipboardRetentionDays == 7);
            });
            Test("Reset removes every local P2 state file and temporary write", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "reset"));
                string[] states = { "focus.json", "reminders.json", "shelf.json", "clipboard.json", "captures.json" };
                Directory.CreateDirectory(store.DirectoryPath);
                foreach (var state in states) { File.WriteAllText(Path.Combine(store.DirectoryPath, state), "{}"); File.WriteAllText(Path.Combine(store.DirectoryPath, state + ".tmp"), "{}"); }
                store.Clear(); Check(!Directory.EnumerateFiles(store.DirectoryPath).Any());
            });
            return count;
        }
        finally { if (Path.GetFullPath(root).StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("CrestWorkflowTests-")) Directory.Delete(root, true); }
        void Test(string name, Action action) { action(); count++; Console.WriteLine("PASS " + name); }
        void Check(bool valid) { if (!valid) throw new Exception("Local workflow outcome failed"); }
    }
}
