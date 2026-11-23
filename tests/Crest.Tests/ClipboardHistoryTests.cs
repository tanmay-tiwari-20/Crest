using Crest.Core;
using Crest.Features;
using System.Security.Cryptography;

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
                using var hub = new ClipboardHistoryHub(store, () => new(), () => now, false, new TestProtection());
                Check(!hub.Enabled && !hub.CaptureText("private text") && hub.Search().Count == 0
                    && !File.Exists(Path.Combine(store.DirectoryPath, "clipboard.json")));
            });
            Test("Search is case-insensitive and repeated text retains identity and pin state", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "search"));
                using var hub = new ClipboardHistoryHub(store, () => new(true), () => now, false, new TestProtection());
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
                using (var hub = new ClipboardHistoryHub(store, () => new(true), () => now, false, new TestProtection()))
                {
                    hub.CaptureText("retained");
                    hub.SetPaused(true);
                    pausedGeneration = hub.CaptureGeneration;
                    Check(hub.Paused && !hub.CaptureText("while paused"));
                }
                using var recovered = new ClipboardHistoryHub(store, () => new(true), () => now, false, new TestProtection());
                Check(recovered.Paused && recovered.Search().Single().Text == "retained");
                long previous = recovered.CaptureGeneration;
                recovered.SetPaused(false);
                Check(!recovered.Paused && recovered.CaptureGeneration > previous && pausedGeneration > 0 && recovered.CaptureText("after resume"));
            });
            Test("Retention removes expired unpinned text while pins survive until unpinned", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "retention"));
                using var hub = new ClipboardHistoryHub(store, () => new(true, 2), () => now, false, new TestProtection());
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
                using var hub = new ClipboardHistoryHub(store, () => new(true), () => now, false, new TestProtection());
                hub.CaptureText("clear me");
                hub.SetPinned(hub.Search().Single().Id, true);
                hub.SetPaused(true);
                long generation = hub.CaptureGeneration;
                hub.Clear();
                Check(hub.Search().Count == 0 && hub.Paused && hub.CaptureGeneration > generation);
                hub.SetPaused(false);
                Check(!hub.CaptureText("queued before clear", generation) && hub.Search().Count == 0);
                hub.SetPaused(true);
                using var recovered = new ClipboardHistoryHub(store, () => new(true), () => now, false, new TestProtection());
                Check(recovered.Search().Count == 0 && recovered.Paused);
            });
            Test("Disabling opt-in erases history and temporary files and reset restores privacy defaults", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "disable"));
                var options = new ClipboardHistoryOptions(true);
                using var hub = new ClipboardHistoryHub(store, () => options, () => now, false, new TestProtection());
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
                using var hub = new ClipboardHistoryHub(store, () => new(true), () => now, false, new TestProtection());
                hub.CaptureText("keep");
                string pin = hub.Search().Single().Id;
                hub.SetPinned(pin, true);
                for (int i = 0; i < 120; i++) { now += TimeSpan.FromSeconds(1); Check(hub.CaptureText("item-" + i)); }
                Check(hub.Search().Count == ClipboardHistoryHub.MaxItems && hub.Search().Any(x => x.Id == pin)
                    && !hub.Search().Any(x => x.Text == "item-0"));
                Check(hub.CaptureText(new string('x', ClipboardHistoryHub.MaxTextCharacters + 1)) && !hub.CaptureText(" \r\n"));
                Check(hub.Search().Single(x => x.Text.StartsWith("xxx")).Truncated
                    && hub.Search().Single(x => x.Truncated).Text.Length == ClipboardHistoryHub.MaxTextCharacters);
            });
            Test("A full pinned character budget rejects new text without deleting existing unpinned text", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "pin-budget"));
                using var hub = new ClipboardHistoryHub(store, () => new(true), () => now, false, new TestProtection());
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
                using var recovered = new ClipboardHistoryHub(store, () => new(true), () => now, false, new TestProtection());
                Check(recovered.Search().Count == 15);
                now += TimeSpan.FromDays(8);
                Check(!hub.CaptureText(new string('z', ClipboardHistoryHub.MaxTextCharacters)));
                using var pruned = new ClipboardHistoryHub(store, () => new(true), () => now, false, new TestProtection());
                Check(pruned.Search().Count == 14);
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
                using (var hub = new ClipboardHistoryHub(store, () => new(true, -1), () => now, false, new TestProtection()))
                    Check(hub.RetentionDays == 1 && hub.Search().Count == 1);
                using var disabled = new ClipboardHistoryHub(store, () => new(), () => now, false, new TestProtection());
                Check(disabled.Search().Count == 0 && !disabled.Capturing && !File.Exists(Path.Combine(store.DirectoryPath, "clipboard.json")));
            });
            Test("Persisted clipboard payload protects text and restores pin, pause and truncation state", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "protected"));
                using (var hub = new ClipboardHistoryHub(store, () => new(true), () => now, false, new TestProtection()))
                {
                    Check(hub.CaptureText("Sensitive synthetic saved text"));
                    hub.SetPinned(hub.Search().Single().Id, true);
                    hub.CaptureText(new string('t', ClipboardHistoryHub.MaxTextCharacters + 3));
                    hub.SetPaused(true);
                    string persisted = File.ReadAllText(Path.Combine(store.DirectoryPath, "clipboard.json"));
                    Check(!persisted.Contains("Sensitive synthetic saved text") && !persisted.Contains("CapturedUtc")
                        && store.ReadState<ClipboardHistoryState>("clipboard.json") is { SchemaVersion: 2, Items.Count: 0 });
                }
                using var recovered = new ClipboardHistoryHub(store, () => new(true), () => now, false, new TestProtection());
                Check(recovered.Paused && recovered.Search().Count == 2 && recovered.Search().Any(x => x.Pinned)
                    && recovered.Search().Any(x => x.Truncated));
            });
            Test("Legacy retained clipboard migrates to protection immediately", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "migration"));
                store.WriteState("clipboard.json", new ClipboardHistoryState { Paused = true, Items = new()
                { new("migration-id", "Synthetic legacy clipboard secret", now, true) } });
                using var hub = new ClipboardHistoryHub(store, () => new(true), () => now, false, new TestProtection());
                Check(hub.Paused && hub.Search().Single().Id == "migration-id"
                    && !File.ReadAllText(Path.Combine(store.DirectoryPath, "clipboard.json")).Contains("Synthetic legacy clipboard secret")
                    && store.ReadState<ClipboardHistoryState>("clipboard.json")?.SchemaVersion == 2);
            });
            Test("Tampered protected history fails closed with an actionable recovery state", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "tamper"));
                using (var hub = new ClipboardHistoryHub(store, () => new(true), () => now, false, new TestProtection())) hub.CaptureText("tamper secret");
                var saved = store.ReadState<ProtectedClipboardHistoryState>("clipboard.json")!;
                byte[] payload = Convert.FromBase64String(saved.ProtectedPayload);
                payload[^1] ^= 1;
                saved.ProtectedPayload = Convert.ToBase64String(payload);
                store.WriteState("clipboard.json", saved);
                using var recovered = new ClipboardHistoryHub(store, () => new(true), () => now, false, new TestProtection());
                Check(recovered.Search().Count == 0 && recovered.LastError?.Contains("could not be unlocked") == true
                    && recovered.Capturing && recovered.CaptureText("fresh"));
                Check(recovered.LastError == null && recovered.Search().Single().Text == "fresh");
            });
            Test("Failed clipboard protection removes legacy plaintext and retains only session data", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "protection-failure"));
                store.WriteState("clipboard.json", new ClipboardHistoryState { Items = new()
                { new("legacy", "Synthetic failure secret", now, true) } });
                using var hub = new ClipboardHistoryHub(store, () => new(true), () => now, false, new FailingProtection());
                Check(hub.Search().Count == 1 && !File.Exists(Path.Combine(store.DirectoryPath, "clipboard.json"))
                    && hub.LastError?.Contains("only for this session") == true && hub.CaptureText("session text"));
                Check(!File.Exists(Path.Combine(store.DirectoryPath, "clipboard.json")));
            });
            Test("Clipboard truncation preserves Unicode pairs and identifies shortened text", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "unicode-truncate"));
                using var hub = new ClipboardHistoryHub(store, () => new(true), () => now, false, new TestProtection());
                Check(hub.CaptureText(new string('x', ClipboardHistoryHub.MaxTextCharacters - 1) + "\U0001F642suffix"));
                var item = hub.Search().Single();
                Check(item.Truncated && item.Text.Length == ClipboardHistoryHub.MaxTextCharacters - 1
                    && !char.IsSurrogate(item.Text[^1]));
            });
            if (OperatingSystem.IsWindows()) Test("Windows DPAPI restores saved text without plaintext storage", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "dpapi"));
                using (var hub = new ClipboardHistoryHub(store, () => new(true), () => now, false))
                    Check(hub.CaptureText("Synthetic Windows protected text") && hub.LastError == null);
                Check(!File.ReadAllText(Path.Combine(store.DirectoryPath, "clipboard.json")).Contains("Synthetic Windows protected text"));
                using var recovered = new ClipboardHistoryHub(store, () => new(true), () => now, false);
                Check(recovered.Search().Single().Text == "Synthetic Windows protected text" && recovered.LastError == null);
            });
            return passed;
        }
        finally
        {
            if (root.StartsWith(tempParent, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("CrestClipboardTests-", StringComparison.Ordinal)
                && Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class TestProtection : IClipboardHistoryProtection
    {
        private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);
        public byte[] Protect(byte[] plaintext)
        {
            byte[] result = new byte[28 + plaintext.Length];
            RandomNumberGenerator.Fill(result.AsSpan(0, 12));
            using var cipher = new AesGcm(Key, 16);
            cipher.Encrypt(result.AsSpan(0, 12), plaintext, result.AsSpan(28), result.AsSpan(12, 16));
            return result;
        }
        public byte[] Unprotect(byte[] protectedData)
        {
            if (protectedData.Length < 28) throw new CryptographicException();
            byte[] result = new byte[protectedData.Length - 28];
            using var cipher = new AesGcm(Key, 16);
            cipher.Decrypt(protectedData.AsSpan(0, 12), protectedData.AsSpan(28), protectedData.AsSpan(12, 16), result);
            return result;
        }
    }
    private sealed class FailingProtection : IClipboardHistoryProtection
    {
        public byte[] Protect(byte[] plaintext) => throw new CryptographicException();
        public byte[] Unprotect(byte[] protectedData) => throw new CryptographicException();
    }
}
