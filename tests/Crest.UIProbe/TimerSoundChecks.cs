using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Crest.Core;
using Crest.Features;
using Crest.UI;

/// <summary>Exercises alert lifecycle and the real Settings action without emitting test audio.</summary>
internal static class TimerSoundChecks
{
    public static async Task Run(Crest.App app, AppSettings settings, Action<bool, string> check)
    {
        string tempParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string temporary = Path.GetFullPath(Path.Combine(tempParent, "CrestSoundProbe-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(temporary);
        var store = new SettingsStore(temporary);
        var clock = new SoundClock();
        using var hub = new LocalHub(store, () => settings, clock);
        var originalStore = app.Store;
        var originalLocal = app.Local;
        bool originalSound = settings.TimerSound;
        var playerField = typeof(Crest.App).GetField("_timerAlertPlayer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var originalPlayer = playerField.GetValue(app);
        SettingsWindow? settingsWindow = null;
        try
        {
            bool enabled = false;
            var disabledPlayback = new ProbePlayback();
            using (var player = new TimerAlertPlayer(() => enabled, id => id == "current", disabledPlayback))
            {
                for (int index = 0; index < 20; index++) player.QueueCompletion("current");
                await Idle(player);
                bool discardedWhileDisabled = disabledPlayback.PlayCount == 0;
                enabled = true;
                player.QueueCompletion("acknowledged");
                await Idle(player);
                bool discardedWhenNoLongerCurrent = disabledPlayback.PlayCount == 0;
                player.QueueCompletion("current");
                await Idle(player);
                check(discardedWhileDisabled && discardedWhenNoLongerCurrent && disabledPlayback.PlayCount == 1,
                    "Disabled and no-longer-current timer completions never leave deferred audio behind");
            }

            async Task<bool> PendingIsDiscarded(bool disableSound)
            {
                bool soundEnabled = true, current = true;
                var playback = new ProbePlayback(blockFirst: true);
                using var player = new TimerAlertPlayer(() => soundEnabled, _ => current, playback);
                player.Preview();
                await playback.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                player.QueueCompletion("waiting");
                if (disableSound) soundEnabled = false;
                else current = false;
                playback.ReleaseFirst();
                await Idle(player);
                return playback.PlayCount == 1 && playback.MaximumConcurrent == 1;
            }
            check(await PendingIsDiscarded(true) && await PendingIsDiscarded(false),
                "Pending completion audio is discarded when sound is disabled or its timer is acknowledged");

            var pendingPlayback = new ProbePlayback(blockFirst: true);
            var validCompletions = Enumerable.Range(0, LocalHub.MaxTimers).Select(index => "old-" + index).ToHashSet(StringComparer.Ordinal);
            using (var player = new TimerAlertPlayer(() => true, validCompletions.Contains, pendingPlayback))
            {
                player.Preview();
                await pendingPlayback.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                foreach (string id in validCompletions) player.QueueCompletion(id);
                validCompletions.Clear();
                validCompletions.Add("fresh");
                player.QueueCompletion("fresh");
                pendingPlayback.ReleaseFirst();
                await Idle(player);
                check(pendingPlayback.PlayCount == 2 && pendingPlayback.MaximumConcurrent == 1,
                    "Acknowledged pending completions release queue capacity for a fresh timer alert after preview");
            }

            hub.ResetProductivity();
            var completionPlayback = new ProbePlayback(blockFirst: true);
            var observed = new HashSet<string>(StringComparer.Ordinal);
            int completionEvents = 0;
            using (var player = new TimerAlertPlayer(() => true, id => hub.Timers.Any(timer => timer.Id == id && timer.Phase == TimerPhase.Completed), completionPlayback))
            {
                void OnCompletion(object? sender, TimerCompletedEventArgs args)
                {
                    completionEvents++;
                    observed.Add(args.Timer.Id);
                    player.QueueCompletion(args.Timer.Id);
                }
                hub.TimerCompleted += OnCompletion;
                try
                {
                    hub.AddTimer(TimeSpan.FromSeconds(1), "First");
                    for (int index = 0; index < 7; index++) hub.AddTimer(TimeSpan.FromSeconds(2), "Batch " + index);
                    clock.Advance(1.1);
                    hub.Tick();
                    await completionPlayback.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    clock.Advance(2);
                    for (int index = 0; index < 20; index++) hub.Tick();
                    completionPlayback.ReleaseFirst();
                    await Idle(player);
                    for (int index = 0; index < 20; index++) hub.Tick();
                    await Idle(player);
                    check(observed.Count == 8 && completionEvents == 8 && hub.Timers.All(timer => timer.Phase == TimerPhase.Completed)
                        && completionPlayback.PlayCount == 1 && completionPlayback.MaximumConcurrent == 1,
                        "Eight independent timer completions coalesce during an active chime and repeated ticks produce no replay");
                }
                finally { hub.TimerCompleted -= OnCompletion; }
            }

            bool activeCompletionStopped;
            var stoppedPlayback = new ProbePlayback(blockFirst: true);
            using (var player = new TimerAlertPlayer(() => true, _ => true, stoppedPlayback))
            {
                player.QueueCompletion("active");
                await stoppedPlayback.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                player.CancelCompletionAlerts();
                await Idle(player);
                activeCompletionStopped = stoppedPlayback.StopCount >= 1 && stoppedPlayback.LastToken.IsCancellationRequested;
            }
            var previewPlayback = new ProbePlayback(blockFirst: true);
            using (var player = new TimerAlertPlayer(() => true, _ => true, previewPlayback))
            {
                player.Preview();
                await previewPlayback.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                player.QueueCompletion("pending");
                player.CancelCompletionAlerts();
                bool explicitPreviewPreserved = previewPlayback.StopCount == 0;
                previewPlayback.ReleaseFirst();
                await Idle(player);
                check(activeCompletionStopped && explicitPreviewPreserved && previewPlayback.PlayCount == 1,
                    "Cancelling completion alerts stops active completion audio while preserving an explicit preview");
            }

            var visibleBeforeShutdown = app.Windows.Cast<Window>().Where(window => window.IsVisible).ToArray();
            var shutdownPlayback = new ProbePlayback(blockFirst: true);
            using (var player = new TimerAlertPlayer(() => true, _ => true, shutdownPlayback))
            {
                player.Preview();
                await shutdownPlayback.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                player.Dispose();
                try { player.QueueCompletion("late"); player.Preview(); }
                catch (ObjectDisposedException) { }
                await Idle(player);
                check(shutdownPlayback.Disposed && shutdownPlayback.StopCount >= 1 && shutdownPlayback.LastToken.IsCancellationRequested
                    && shutdownPlayback.PlayCount == 1 && shutdownPlayback.CallsAfterDispose == 0
                    && visibleBeforeShutdown.SequenceEqual(app.Windows.Cast<Window>().Where(window => window.IsVisible)),
                    "Audio shutdown stops playback and rejects later work without opening an overlay");
            }

            settings.TimerSound = false;
            store.Save(settings);
            string settingsFile = Path.Combine(temporary, "settings.json");
            string savedBeforePreview = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(settingsFile)));
            typeof(Crest.App).GetProperty(nameof(Crest.App.Store))!.SetValue(app, store);
            typeof(Crest.App).GetProperty(nameof(Crest.App.Local))!.SetValue(app, hub);
            var settingsPlayback = new ProbePlayback(blockFirst: true);
            using (var player = new TimerAlertPlayer(() => settings.TimerSound, _ => false, settingsPlayback))
            {
                playerField.SetValue(app, player);
                settingsWindow = new SettingsWindow(app);
                var root = (FrameworkElement)settingsWindow.Content;
                root.Measure(new Size(880, 700));
                root.Arrange(new Rect(0, 0, 880, 700));
                root.UpdateLayout();
                var modules = Descendants<Button>(root).Single(button => AutomationProperties.GetName(button) == "Modules");
                modules.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                root.UpdateLayout();
                var preview = Descendants<Button>(root).Single(button => AutomationProperties.GetName(button) == "Test timer sound");
                var soundToggle = Descendants<CheckBox>(root).Single(toggle => AutomationProperties.GetName(toggle).Contains("sound", StringComparison.OrdinalIgnoreCase));
                preview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await settingsPlayback.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                for (int index = 0; index < 10; index++) preview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                settingsPlayback.ReleaseFirst();
                await Idle(player);
                check(settingsPlayback.PlayCount == 1 && settingsPlayback.MaximumConcurrent == 1 && !settings.TimerSound && soundToggle.IsChecked == false
                    && savedBeforePreview == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(settingsFile))) && BundledWaveIsPlayable(),
                    "The real Settings sound preview coalesces requests without enabling sound and its bundled WAV is valid and bounded");
                settingsWindow.Close();
                settingsWindow = null;
            }
        }
        finally
        {
            settingsWindow?.Close();
            playerField.SetValue(app, originalPlayer);
            typeof(Crest.App).GetProperty(nameof(Crest.App.Store))!.SetValue(app, originalStore);
            typeof(Crest.App).GetProperty(nameof(Crest.App.Local))!.SetValue(app, originalLocal);
            settings.TimerSound = originalSound;
            hub.Dispose();
            // Only this test-owned, verified child of the temporary directory is removed.
            if (temporary.StartsWith(tempParent, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(temporary).StartsWith("CrestSoundProbe-", StringComparison.Ordinal)
                && Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }

    private static async Task Idle(TimerAlertPlayer player) => await player.WhenIdle.WaitAsync(TimeSpan.FromSeconds(5));

    private static bool BundledWaveIsPlayable()
    {
        using var stream = typeof(TimerAlertPlayer).Assembly.GetManifestResourceStream("Crest.TimerComplete.wav");
        if (stream == null || stream.Length < 44 || stream.Length > 1024 * 1024) return false;
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") return false;
        uint riffLength = reader.ReadUInt32();
        if (riffLength != stream.Length - 8 || Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") return false;
        ushort format = 0, channels = 0, alignment = 0, bits = 0;
        uint sampleRate = 0, byteRate = 0, dataLength = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            string chunk = Encoding.ASCII.GetString(reader.ReadBytes(4));
            uint length = reader.ReadUInt32();
            long end = stream.Position + length;
            if (end > stream.Length) return false;
            if (chunk == "fmt " && length >= 16)
            {
                format = reader.ReadUInt16(); channels = reader.ReadUInt16();
                sampleRate = reader.ReadUInt32(); byteRate = reader.ReadUInt32();
                alignment = reader.ReadUInt16(); bits = reader.ReadUInt16();
            }
            else if (chunk == "data") dataLength = length;
            stream.Position = end + (length & 1);
        }
        return format == 1 && channels is 1 or 2 && bits is 8 or 16 && sampleRate is >= 8000 and <= 48000
            && alignment == channels * bits / 8 && byteRate == sampleRate * alignment
            && dataLength > 0 && dataLength % alignment == 0 && dataLength / (double)byteRate is > 0 and <= 4;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private sealed class ProbePlayback(bool blockFirst = false) : ITimerAlertPlayback
    {
        private readonly TaskCompletionSource<bool> _firstReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _plays, _stops, _active, _maximumConcurrent, _disposed, _afterDispose;
        public TaskCompletionSource<bool> FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int PlayCount => Volatile.Read(ref _plays);
        public int StopCount => Volatile.Read(ref _stops);
        public int MaximumConcurrent => Volatile.Read(ref _maximumConcurrent);
        public bool Disposed => Volatile.Read(ref _disposed) != 0;
        public int CallsAfterDispose => Volatile.Read(ref _afterDispose);
        public CancellationToken LastToken { get; private set; }
        public async Task PlayAsync(CancellationToken cancellationToken)
        {
            if (Disposed) { Interlocked.Increment(ref _afterDispose); throw new ObjectDisposedException(nameof(ProbePlayback)); }
            int call = Interlocked.Increment(ref _plays);
            int active = Interlocked.Increment(ref _active);
            int maximum = MaximumConcurrent;
            while (active > maximum)
            {
                int observed = Interlocked.CompareExchange(ref _maximumConcurrent, active, maximum);
                if (observed == maximum) break;
                maximum = observed;
            }
            LastToken = cancellationToken;
            FirstStarted.TrySetResult(true);
            try
            {
                if (blockFirst && call == 1) await _firstReleased.Task.WaitAsync(cancellationToken);
                else cancellationToken.ThrowIfCancellationRequested();
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public void ReleaseFirst() => _firstReleased.TrySetResult(true);
        public void Stop() { Interlocked.Increment(ref _stops); ReleaseFirst(); }
        public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
    }

    private sealed class SoundClock : ITimerClock
    {
        public long MonotonicMilliseconds { get; private set; }
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);
        public void Advance(double seconds) { MonotonicMilliseconds += (long)(seconds * 1000); UtcNow += TimeSpan.FromSeconds(seconds); }
    }
}
