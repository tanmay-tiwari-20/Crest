using Crest.Core;
using Crest.Features;

internal static class ClipboardHistoryTests
{
    public static int Run()
    {
        int passed = 0;
        string tempParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string root = Path.GetFullPath(Path.Combine(tempParent, "CrestClipboardTests-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
        void Check(bool value) { if (!value) throw new Exception("Clipboard history outcome assertion failed."); }
        DateTimeOffset now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        try
        {
            Test("Clipboard capture starts off and never persists text before opt-in", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "off"));
                using var hub = new ClipboardHistoryHub(store, () => new(), () => now, false);
                Check(!hub.Enabled && !hub.CaptureText("private text") && hub.Search().Count == 0
                    && !File.Exists(Path.Combine(store.DirectoryPath, "clipboard.json")));
            });
            Test("Search is case-insensitive and repeated text retains identity and pin state", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "search"));
                using var hub = new ClipboardHistoryHub(store, () => new(true), () => now, false);
                Check(hub.CaptureText("Alpha\nBeta") && hub.CaptureText("second"));
                string firstId = hub.Search("ALPHA").Single().Id;
                hub.SetPinned(firstId, true);
                now += TimeSpan.FromMinutes(1);
                Check(hub.CaptureText("Alpha\nBeta"));
                Check(hub.Search().Count == 2 && hub.Search()[0].Id == firstId && hub.Search()[0].Pinned
                    && hub.Search("missing").Count == 0 && hub.Search()[0].CapturedUtc == now);
            });
            Test("Pause survives restart, rejects capture, and resume opens a new capture generation", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "pause"));
                long pausedGeneration;
                using (var hub = new ClipboardHistoryHub(store, () => new(true), () => now, false))
                {
                    hub.CaptureText("retained");
                    hub.SetPaused(true);
                    pausedGeneration = hub.CaptureGeneration;
                    Check(hub.Paused && !hub.CaptureText("while paused"));
                }
                using var recovered = new ClipboardHistoryHub(store, () => new(true), () => now, false);
                Check(recovered.Paused && recovered.Search().Single().Text == "retained");
                long previous = recovered.CaptureGeneration;
                recovered.SetPaused(false);
                Check(!recovered.Paused && recovered.CaptureGeneration > previous && pausedGeneration > 0 && recovered.CaptureText("after resume"));
            });
            Test("Retention removes expired unpinned text while pins survive until unpinned", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "retention"));
                using var hub = new ClipboardHistoryHub(store, () => new(true, 2), () => now, false);
                hub.CaptureText("pinned");
                string pinnedId = hub.Search().Single().Id;
                hub.SetPinned(pinnedId, true);
                hub.CaptureText("temporary");
                hub.SetPaused(true);
                now += TimeSpan.FromDays(3);
                hub.Refresh();
                Check(hub.Search().Count == 1 && hub.Search().Single().Pinned);
                hub.SetPinned(pinnedId, false);
                Check(hub.Search().Count == 0);
            });
            Test("Clear invalidates pending capture generation and clears pins while preserving pause", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "clear"));
                using var hub = new ClipboardHistoryHub(store, () => new(true), () => now, false);
                hub.CaptureText("clear me");
                hub.SetPinned(hub.Search().Single().Id, true);
                hub.SetPaused(true);
                long generation = hub.CaptureGeneration;
                hub.Clear();
                Check(hub.Search().Count == 0 && hub.Paused && hub.CaptureGeneration > generation);
                hub.SetPaused(false);
                Check(!hub.CaptureText("queued before clear", generation) && hub.Search().Count == 0);
                hub.SetPaused(true);
                using var recovered = new ClipboardHistoryHub(store, () => new(true), () => now, false);
                Check(recovered.Search().Count == 0 && recovered.Paused);
            });
            Test("Disabling opt-in erases history and temporary files and reset restores privacy defaults", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "disable"));
                var options = new ClipboardHistoryOptions(true);
                using var hub = new ClipboardHistoryHub(store, () => options, () => now, false);
                hub.CaptureText("secret");
                File.WriteAllText(Path.Combine(store.DirectoryPath, "clipboard.json.tmp"), "secret");
                options = new(false);
                hub.ApplyPreferences();
                Check(!hub.Enabled && hub.Search().Count == 0 && !hub.CaptureText("another")
                    && !File.Exists(Path.Combine(store.DirectoryPath, "clipboard.json"))
                    && !File.Exists(Path.Combine(store.DirectoryPath, "clipboard.json.tmp")));
                options = new(true);
                hub.ApplyPreferences();
                hub.CaptureText("new");
                hub.Reset();
                Check(!hub.Capturing && hub.Search().Count == 0 && !File.Exists(Path.Combine(store.DirectoryPath, "clipboard.json")));
            });
            Test("Item and character bounds evict old unpinned entries and never evict pins", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "bounds"));
                using var hub = new ClipboardHistoryHub(store, () => new(true), () => now, false);
                hub.CaptureText("keep");
                string pin = hub.Search().Single().Id;
                hub.SetPinned(pin, true);
                for (int i = 0; i < 120; i++) { now += TimeSpan.FromSeconds(1); Check(hub.CaptureText("item-" + i)); }
                Check(hub.Search().Count == ClipboardHistoryHub.MaxItems && hub.Search().Any(x => x.Id == pin)
                    && !hub.Search().Any(x => x.Text == "item-0"));
                Check(!hub.CaptureText(new string('x', ClipboardHistoryHub.MaxTextCharacters + 1)) && !hub.CaptureText(" \r\n"));
            });
            Test("A full pinned character budget rejects new text without deleting existing unpinned text", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "pin-budget"));
                using var hub = new ClipboardHistoryHub(store, () => new(true), () => now, false);
                for (int i = 0; i < 14; i++)
                {
                    now += TimeSpan.FromSeconds(1);
                    string text = i.ToString("00") + new string('x', ClipboardHistoryHub.MaxTextCharacters - 2);
                    Check(hub.CaptureText(text));
                    hub.SetPinned(hub.Search().Single(x => x.Text == text).Id, true);
                }
                Check(hub.CaptureText("temporary"));
                var previous = hub.Search();
                Check(!hub.CaptureText(new string('y', ClipboardHistoryHub.MaxTextCharacters)) && hub.Search().SequenceEqual(previous));
                Check(new FileInfo(Path.Combine(store.DirectoryPath, "clipboard.json")).Length < 1024 * 1024);
                using var recovered = new ClipboardHistoryHub(store, () => new(true), () => now, false);
                Check(recovered.Search().Count == 15);
                now += TimeSpan.FromDays(8);
                Check(!hub.CaptureText(new string('z', ClipboardHistoryHub.MaxTextCharacters))
                    && store.ReadState<ClipboardHistoryState>("clipboard.json")!.Items.Count == 14);
            });
            Test("Malformed recovery data is bounded and does not enable clipboard capture", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "recovery"));
                store.WriteState("clipboard.json", new ClipboardHistoryState { Items = new()
                {
                    new("valid", "valid text", now, true),
                    new("future", "future text", now.AddDays(2), true),
                    new("huge", new string('x', ClipboardHistoryHub.MaxTextCharacters + 1), now, true),
                    new("valid", "duplicate ID", now, false)
                } });
                using (var hub = new ClipboardHistoryHub(store, () => new(true, -1), () => now, false))
                    Check(hub.RetentionDays == 1 && hub.Search().Count == 1);
                using var disabled = new ClipboardHistoryHub(store, () => new(), () => now, false);
                Check(disabled.Search().Count == 0 && !disabled.Capturing && !File.Exists(Path.Combine(store.DirectoryPath, "clipboard.json")));
            });
            return passed;
        }
        finally
        {
            if (root.StartsWith(tempParent, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("CrestClipboardTests-", StringComparison.Ordinal)
                && Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
