using Crest.Core;
using Crest.Features;

internal static class ShelfTests
{
    public static int Run()
    {
        int passed = 0;
        string tempParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string root = Path.GetFullPath(Path.Combine(tempParent, "CrestShelfTests-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
        void Check(bool value) { if (!value) throw new Exception("Shelf outcome assertion failed."); }
        LocalHub Create(string name) => new(new SettingsStore(Path.Combine(root, name)), () => new(), new ShelfClock());
        string Original(string name)
        {
            string path = Path.Combine(root, name);
            File.WriteAllBytes(path, new byte[] { 0, 255, 19, 42, 128, 10 });
            return path;
        }
        try
        {
            Test("Folder separator and case aliases share one reference while drive roots remain intact", () =>
            {
                using var hub = Create("aliases");
                string folder = Path.Combine(root, "OriginalFolder");
                Directory.CreateDirectory(folder);
                Check(hub.AddShelfFiles(new[] { folder, folder + Path.DirectorySeparatorChar, folder.ToUpperInvariant() + Path.AltDirectorySeparatorChar }) is null);
                Check(hub.Shelf.Count == 1 && hub.Shelf[0].Path == folder && hub.Shelf[0].IsDirectory);
                string driveRoot = Path.GetPathRoot(folder)!;
                Check(hub.AddShelfFiles(new[] { driveRoot }) is null);
                Check(hub.Shelf.Count == 2 && hub.Shelf.Single(item => item.Path == driveRoot).IsDirectory);
            });
            Test("Clearing during drop preparation rejects the old batch without discarding new references", () =>
            {
                using var hub = Create("clear-pending");
                string existing = Original("existing.txt"), first = Original("pending-first.txt"), second = Original("pending-second.txt"), fresh = Original("after-clear.txt");
                hub.AddShelfFiles(new[] { existing });
                using var prepared = new ManualResetEventSlim();
                using var resume = new ManualResetEventSlim();
                var pending = Task.Run(() => hub.AddShelfFiles(PausePreparation(first, second, prepared, resume)));
                string? completion = null;
                long previousGeneration = hub.ShelfGeneration;
                int clearNotifications = 0;
                hub.Changed += (_, _) => { if (hub.ShelfGeneration != previousGeneration && hub.Shelf.Count == 0) clearNotifications++; };
                try
                {
                    Check(prepared.Wait(TimeSpan.FromSeconds(5)));
                    hub.ClearShelf();
                    Check(hub.AddShelfFiles(new[] { fresh }, hub.ShelfGeneration) is null);
                }
                finally { resume.Set(); completion = pending.GetAwaiter().GetResult(); }
                Check(completion is not null && clearNotifications == 1 && hub.Shelf.Count == 1 && hub.Shelf[0].Path == fresh);
                Check(File.ReadAllBytes(first).SequenceEqual(new byte[] { 0, 255, 19, 42, 128, 10 }));
            });
            Test("A drop queued before clear is rejected before enumeration even when its worker starts later", () =>
            {
                using var hub = Create("queued-before-clear");
                string file = Original("queued-file.txt");
                long captured = hub.ShelfGeneration;
                hub.ClearShelf();
                bool enumerated = false;
                IEnumerable<string> QueuedPaths() { enumerated = true; yield return file; }
                Check(hub.AddShelfFiles(QueuedPaths(), captured) is not null && !enumerated && hub.Shelf.Count == 0);
                Check(hub.AddShelfFiles(new[] { file }, hub.ShelfGeneration) is null && hub.Shelf.Count == 1);
            });
            Test("Invalid and over-capacity drops retain only bounded references and never change source bytes", () =>
            {
                using var hub = Create("caps-and-sources");
                var originals = Enumerable.Range(0, 21).Select(i => Original("bulk-" + i + ".bin")).ToArray();
                var bytes = originals.ToDictionary(path => path, File.ReadAllBytes);
                Check(hub.AddShelfFiles(new[] { Path.Combine(root, "missing.bin"), "\0", originals[0] }) is not null && hub.Shelf.Count == 1);
                Check(hub.AddShelfFiles(originals) is not null && hub.Shelf.Count == 20 && hub.Shelf.Select(item => item.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 20);
                hub.RemoveShelf(hub.Shelf[0].Id);
                hub.RefreshShelf();
                Check(hub.Shelf.Count == 19 && hub.Shelf.All(item => item.Available));
                hub.ClearShelf();
                Check(hub.Shelf.Count == 0 && bytes.All(pair => File.Exists(pair.Key) && File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)));
            });
            Test("Dispose during preparation cannot revive the shelf or enumerate future drops", () =>
            {
                using var hub = Create("dispose-pending");
                string first = Original("dispose-first.txt"), second = Original("dispose-second.txt");
                using var prepared = new ManualResetEventSlim();
                using var resume = new ManualResetEventSlim();
                var pending = Task.Run(() => hub.AddShelfFiles(PausePreparation(first, second, prepared, resume)));
                string? completion = null;
                try
                {
                    Check(prepared.Wait(TimeSpan.FromSeconds(5)));
                    hub.Dispose();
                }
                finally { resume.Set(); completion = pending.GetAwaiter().GetResult(); }
                bool enumerated = false;
                IEnumerable<string> AfterDispose() { enumerated = true; yield return first; }
                hub.ClearShelf();
                hub.RefreshShelf();
                Check(completion is not null && hub.AddShelfFiles(AfterDispose(), hub.ShelfGeneration) is not null && !enumerated && hub.Shelf.Count == 0);
                Check(File.Exists(first) && File.Exists(second));
            });
            Test("Removing one reference preserves an unrelated pending drop", () =>
            {
                using var hub = Create("remove-pending");
                string existing = Original("remove-existing.txt"), first = Original("remove-first.txt"), second = Original("remove-second.txt");
                hub.AddShelfFiles(new[] { existing });
                string removedId = hub.Shelf[0].Id;
                using var prepared = new ManualResetEventSlim();
                using var resume = new ManualResetEventSlim();
                var pending = Task.Run(() => hub.AddShelfFiles(PausePreparation(first, second, prepared, resume)));
                string? completion = null;
                try
                {
                    Check(prepared.Wait(TimeSpan.FromSeconds(5)));
                    hub.RemoveShelf(removedId);
                }
                finally { resume.Set(); completion = pending.GetAwaiter().GetResult(); }
                Check(completion is null && hub.Shelf.Count == 2 && hub.Shelf.All(item => item.Id != removedId && item.Path != existing));
            });
            return passed;
        }
        finally
        {
            if (root.StartsWith(tempParent, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("CrestShelfTests-", StringComparison.Ordinal) && Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static IEnumerable<string> PausePreparation(string first, string second, ManualResetEventSlim prepared, ManualResetEventSlim resume)
    {
        yield return first;
        prepared.Set();
        if (!resume.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Shelf preparation fixture was not resumed.");
        yield return second;
    }

    private sealed class ShelfClock : ITimerClock
    {
        public long MonotonicMilliseconds => 0;
        public DateTimeOffset UtcNow => new(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);
    }
}
