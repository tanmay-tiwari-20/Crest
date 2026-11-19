using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Crest.Core;

namespace Crest.Features;

public enum TimerPhase
{
    Idle, Running, Paused, Completed
}
public sealed record TimerSnapshot(TimerPhase Phase, double RemainingSeconds, double TotalSeconds, DateTimeOffset? DeadlineUtc, string Id = "", string Name = "Focus");
public enum StopwatchPhase { Idle, Running, Paused }
public sealed record StopwatchLap(int Number, double TotalSeconds, double SplitSeconds);
public sealed record StopwatchSnapshot(StopwatchPhase Phase, double ElapsedSeconds, IReadOnlyList<StopwatchLap> Laps);
public sealed class TimerCompletedEventArgs(TimerSnapshot timer) : EventArgs
{
    public TimerSnapshot Timer { get; } = timer;
}
public sealed record ShelfItem(string Id, string Name, string Path, bool Available, bool IsDirectory, bool Pinned = false);

public interface ITimerClock
{
    long MonotonicMilliseconds
    {
        get;
    }
    DateTimeOffset UtcNow
    {
        get;
    }
}
public sealed class WindowsTimerClock : ITimerClock
{
    [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();
    public long MonotonicMilliseconds => unchecked((long)GetTickCount64());
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Bounded durable timers, a stopwatch, and a memory-only file reference shelf.</summary>
public sealed partial class LocalHub : IDisposable
{
    public const int MaxTimers = 8;
    public const int MaxStopwatchLaps = 100;
    // Bounds corrupt recovery state and keeps every elapsed value safe for TimeSpan and display conversion.
    public const double MaxActivitySeconds = 3_155_760_000; // 100 years.
    private readonly object _gate = new();
    private readonly SettingsStore _store;
    private readonly ITimerClock _clock;
    private readonly bool _automaticTick;
    private readonly List<ShelfItem> _shelf = new();
    private long _shelfGeneration;
    private System.Threading.Timer? _ticker;
    private readonly List<ActiveTimer> _timers = new();
    private string? _selectedTimerId;
    private StopwatchPhase _stopwatchPhase;
    private double _stopwatchElapsed;
    private long _stopwatchStarted;
    private readonly List<StopwatchLap> _laps = new();
    private bool _disposed;
    public event EventHandler? Changed;
    public event EventHandler? Completion;
    public event EventHandler<TimerCompletedEventArgs>? TimerCompleted;
    public string? LastError => _store.LastError;

    public LocalHub(SettingsStore store, Func<AppSettings> settings) : this(store, settings, new WindowsTimerClock(), true) { }
    public LocalHub(SettingsStore store, Func<AppSettings> settings, ITimerClock clock, bool automaticTick = false)
    {
        _store = store;
        _clock = clock;
        _automaticTick = automaticTick;
        Restore();
    }

    public TimerSnapshot Timer
    {
        get
        {
            lock (_gate)
                return SelectedTimer() is { } timer ? Snapshot(timer) : new(TimerPhase.Idle, 0, 0, null);
        }
    }
    public IReadOnlyList<TimerSnapshot> Timers
    {
        get { lock (_gate) return _timers.Select(Snapshot).ToArray(); }
    }
    public string? SelectedTimerId
    {
        get { lock (_gate) return _selectedTimerId; }
    }
    public StopwatchSnapshot Stopwatch
    {
        get { lock (_gate) return new(_stopwatchPhase, StopwatchElapsed(), _laps.ToArray()); }
    }
    public IReadOnlyList<ShelfItem> Shelf
    {
        get
        {
            lock (_gate)
                return _shelf.ToArray();
        }
    }
    public long ShelfGeneration
    {
        get { lock (_gate) return _shelfGeneration; }
    }

    public void StartTimer(TimeSpan duration)
    {
        ValidateDuration(duration);
        lock (_gate)
        {
            ThrowIfDisposed();
            var timer = SelectedTimer();
            if (timer is null)
            {
                timer = new ActiveTimer { Id = Guid.NewGuid().ToString("N"), Name = "Focus" };
                _timers.Add(timer);
                _selectedTimerId = timer.Id;
            }
            Start(timer, duration);
            PersistTimers();
            UpdateTicker();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public string AddTimer(TimeSpan duration, string? name = null)
    {
        ValidateDuration(duration);
        string id;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_timers.Count >= MaxTimers)
                throw new InvalidOperationException($"Crest holds {MaxTimers} timers. Cancel or acknowledge a timer to make room.");
            var timer = new ActiveTimer { Id = Guid.NewGuid().ToString("N"), Name = TimerName(name, "Timer " + (_timers.Count + 1)) };
            Start(timer, duration);
            _timers.Add(timer);
            _selectedTimerId = id = timer.Id;
            PersistTimers();
            UpdateTicker();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return id;
    }
    public void SelectTimer(string id)
    {
        lock (_gate)
        {
            if (_disposed || FindTimer(id) is null || _selectedTimerId == id) return;
            _selectedTimerId = id;
            PersistTimers();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void RenameTimer(string id, string name)
    {
        lock (_gate)
        {
            if (_disposed || FindTimer(id) is not { } timer) return;
            timer.Name = TimerName(name, timer.Name);
            PersistTimers();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void PauseTimer() => PauseTimer(SelectedTimerId);
    public void PauseTimer(string? id)
    {
        TimerSnapshot? completed = null;
        lock (_gate)
        {
            if (_disposed || FindTimer(id) is not { Phase: TimerPhase.Running } timer) return;
            timer.RemainingSeconds = Remaining(timer);
            timer.Phase = timer.RemainingSeconds <= 0 ? TimerPhase.Completed : TimerPhase.Paused;
            timer.DeadlineUtc = null;
            if (timer.Phase == TimerPhase.Completed) completed = Snapshot(timer);
            PersistTimers();
            UpdateTicker();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        if (completed is not null) NotifyCompletion(completed);
    }
    public void ResumeTimer() => ResumeTimer(SelectedTimerId);
    public void ResumeTimer(string? id)
    {
        lock (_gate)
        {
            if (_disposed || FindTimer(id) is not { Phase: TimerPhase.Paused } timer) return;
            timer.Phase = TimerPhase.Running;
            timer.MonotonicDeadline = MonotonicDeadline(timer.RemainingSeconds);
            timer.DeadlineUtc = UtcDeadline(timer.RemainingSeconds);
            PersistTimers();
            UpdateTicker();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void CancelTimer() => CancelTimer(SelectedTimerId);
    public void CancelTimer(string? id) => RemoveTimer(id, false);
    public void AcknowledgeTimer() => AcknowledgeTimer(SelectedTimerId);
    public void AcknowledgeTimer(string? id) => RemoveTimer(id, true);
    private void RemoveTimer(string? id, bool completedOnly)
    {
        lock (_gate)
        {
            if (_disposed || FindTimer(id) is not { } timer || (completedOnly && timer.Phase != TimerPhase.Completed)) return;
            _timers.Remove(timer);
            if (_selectedTimerId == id) _selectedTimerId = _timers.FirstOrDefault()?.Id;
            PersistTimers();
            UpdateTicker();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void StartStopwatch()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_stopwatchPhase == StopwatchPhase.Running) return;
            _stopwatchPhase = StopwatchPhase.Running;
            _stopwatchStarted = _clock.MonotonicMilliseconds;
            PersistStopwatch();
            UpdateTicker();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void PauseStopwatch()
    {
        lock (_gate)
        {
            if (_disposed || _stopwatchPhase != StopwatchPhase.Running) return;
            _stopwatchElapsed = StopwatchElapsed();
            _stopwatchPhase = StopwatchPhase.Paused;
            PersistStopwatch();
            UpdateTicker();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public StopwatchLap? LapStopwatch()
    {
        StopwatchLap lap;
        lock (_gate)
        {
            if (_disposed || _stopwatchPhase != StopwatchPhase.Running || _laps.Count >= MaxStopwatchLaps) return null;
            double elapsed = StopwatchElapsed();
            lap = new(_laps.Count + 1, elapsed, Math.Max(0, elapsed - (_laps.LastOrDefault()?.TotalSeconds ?? 0)));
            _laps.Add(lap);
            PersistStopwatch();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return lap;
    }
    public void ResetStopwatch()
    {
        lock (_gate)
        {
            if (_disposed) return;
            ClearStopwatch();
            _store.DeleteState("stopwatch.json");
            UpdateTicker();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void ResetProductivity()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _timers.Clear();
            _selectedTimerId = null;
            ClearStopwatch();
            _store.DeleteState("timer.json");
            _store.DeleteState("stopwatch.json");
            ClearFocusAndReminders();
            UpdateTicker();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Tick()
    {
        var completed = new List<TimerSnapshot>();
        FocusSnapshot? focusCompleted = null;
        var remindersDue = new List<ReminderSnapshot>();
        lock (_gate)
        {
            if (_disposed || !HasRunningActivity()) return;
            foreach (var timer in _timers.Where(x => x.Phase == TimerPhase.Running))
            {
                timer.RemainingSeconds = Remaining(timer);
                if (timer.RemainingSeconds > 0) continue;
                timer.Phase = TimerPhase.Completed;
                timer.DeadlineUtc = null;
                completed.Add(Snapshot(timer));
            }
            if (completed.Count > 0) PersistTimers();
            TickFocusAndReminders(ref focusCompleted, remindersDue);
            UpdateTicker();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        foreach (var timer in completed) NotifyCompletion(timer);
        NotifyFocusAndReminders(focusCompleted, remindersDue);
    }

    public string? AddShelfFiles(IEnumerable<string> paths, long? expectedGeneration = null)
    {
        long generation;
        lock (_gate)
        {
            if (_disposed)
                return "The shelf is no longer available.";
            generation = _shelfGeneration;
            if (expectedGeneration is long expected && expected != generation)
                return "The shelf was cleared before these items could be added. Drag them again to retain new references.";
        }
        var prepared = new List<ShelfItem>();
        int missing = 0;
        foreach (string input in paths.Take(100))
        {
            try
            {
                string path = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(input));
                bool directory = Directory.Exists(path);
                bool exists = directory || File.Exists(path);
                if (!exists)
                {
                    missing++;
                    continue;
                }
                string name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
                if (name.Length == 0)
                    name = path;
                prepared.Add(new(Guid.NewGuid().ToString("N"), name, path, true, directory));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { missing++; }
        }
        bool full = false;
        lock (_gate)
        {
            if (_disposed)
                return "The shelf is no longer available.";
            if (_shelfGeneration != generation)
                return "The shelf was cleared before these items could be added. Drag them again to retain new references.";
            foreach (ShelfItem item in prepared)
            {
                if (_shelf.Any(existing => string.Equals(existing.Path, item.Path, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (_shelf.Count == 20)
                {
                    full = true;
                    break;
                }
                _shelf.Add(item);
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
        if (full)
            return "The shelf holds 20 items. Remove a reference to make room; your files stay where they are.";
        return missing > 0 ? "Some items are unavailable. Drag the original files from your desktop again." : null;
    }

    public void ClearShelf()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            ++_shelfGeneration;
            _shelf.Clear();
            PersistShelf();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveShelf(string id)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _shelf.RemoveAll(item => item.Id == id);
            PersistShelf();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshShelf()
    {
        ShelfItem[] snapshot;
        lock (_gate)
        {
            if (_disposed)
                return;
            snapshot = _shelf.ToArray();
        }
        var refreshed = snapshot.Select(item => item with { Available = item.IsDirectory ? Directory.Exists(item.Path) : File.Exists(item.Path) }).ToArray();
        bool changed = false;
        lock (_gate)
        {
            if (_disposed)
                return;
            foreach (ShelfItem item in refreshed)
            {
                int index = _shelf.FindIndex(x => x.Id == item.Id);
                if (index >= 0 && _shelf[index].Path == item.Path && _shelf[index].Available != item.Available)
                {
                    _shelf[index] = _shelf[index] with { Available = item.Available };
                    changed = true;
                }
            }
        }
        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public string? OpenShelf(string id)
    {
        ShelfItem? item;
        lock (_gate)
            item = _shelf.FirstOrDefault(x => x.Id == id);
        if (item is null)
            return "This shelf reference has been removed.";
        string? error = OpenTarget(item.Path);
        if (error is not null)
            RefreshShelf();
        return error;
    }

    public string? Launch(LauncherEntry entry) => OpenTarget(entry.Target);

    private static string? OpenTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return "Choose a target in Settings first.";
        try
        {
            bool website = Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https";
            if (!website && !File.Exists(target) && !Directory.Exists(target))
                return "The target is unavailable. Update this shortcut in Settings or select the file again.";
            if (!website)
                target = System.IO.Path.GetFullPath(target);
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return "Windows could not open this item. Check its location or associated application.";
        }
    }

    private static void ValidateDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 1 || duration.TotalDays > 7)
            throw new ArgumentOutOfRangeException(nameof(duration), "Choose a timer between one second and seven days.");
    }
    private static string TimerName(string? name, string fallback)
    {
        string value = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
        return value[..Math.Min(value.Length, 40)];
    }
    private ActiveTimer? FindTimer(string? id) => _timers.FirstOrDefault(x => x.Id == id);
    private ActiveTimer? SelectedTimer() => FindTimer(_selectedTimerId);
    private TimerSnapshot Snapshot(ActiveTimer timer) => new(timer.Phase, Remaining(timer), timer.TotalSeconds, timer.DeadlineUtc, timer.Id, timer.Name);
    private double Remaining(ActiveTimer timer) => timer.Phase == TimerPhase.Running ? Math.Clamp(((double)timer.MonotonicDeadline - _clock.MonotonicMilliseconds) / 1000d, 0, MaxActivitySeconds) : Math.Clamp(timer.RemainingSeconds, 0, MaxActivitySeconds);
    private long MonotonicDeadline(double seconds)
    {
        long now = _clock.MonotonicMilliseconds, duration = (long)(seconds * 1000);
        return now > long.MaxValue - duration ? long.MaxValue : now + duration;
    }
    private DateTimeOffset UtcDeadline(double seconds)
    {
        var now = _clock.UtcNow.ToUniversalTime();
        long durationTicks = (long)(seconds * TimeSpan.TicksPerSecond);
        return new DateTimeOffset(now.Ticks + Math.Min(durationTicks, DateTimeOffset.MaxValue.Ticks - now.Ticks), TimeSpan.Zero);
    }
    private void Start(ActiveTimer timer, TimeSpan duration)
    {
        timer.RemainingSeconds = timer.TotalSeconds = duration.TotalSeconds;
        timer.Phase = TimerPhase.Running;
        timer.MonotonicDeadline = MonotonicDeadline(duration.TotalSeconds);
        timer.DeadlineUtc = UtcDeadline(duration.TotalSeconds);
    }
    private double StopwatchElapsed() => Math.Min(MaxActivitySeconds, _stopwatchElapsed + (_stopwatchPhase == StopwatchPhase.Running ? Math.Max(0, ((double)_clock.MonotonicMilliseconds - _stopwatchStarted) / 1000d) : 0));
    private void ClearStopwatch()
    {
        _stopwatchPhase = StopwatchPhase.Idle;
        _stopwatchElapsed = 0;
        _stopwatchStarted = 0;
        _laps.Clear();
    }
    private bool HasRunningActivity() => _stopwatchPhase == StopwatchPhase.Running || _timers.Any(x => x.Phase == TimerPhase.Running) || HasRunningFocusOrReminders();
    private void UpdateTicker()
    {
        if (!HasRunningActivity()) { StopTicker(); return; }
        if (!_automaticTick) return;
        // Elapsed tools update each second. A distant reminder alone needs at most
        // one wake per minute, with earlier wakes close to its wall-clock deadline.
        int delay = _stopwatchPhase == StopwatchPhase.Running || _timers.Any(x => x.Phase == TimerPhase.Running) || _focus?.Phase == TimerPhase.Running
            ? 1000 : (int)Math.Clamp((_reminders.Where(x => x.Phase == ReminderPhase.Scheduled).Min(x => x.DueUtc) - _clock.UtcNow).TotalMilliseconds, 1000, 60000);
        _ticker ??= new System.Threading.Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
        _ticker.Change(delay, Timeout.Infinite);
    }
    private void NotifyCompletion(TimerSnapshot timer)
    {
        lock (_gate)
        {
            if (_disposed || FindTimer(timer.Id) is not { Phase: TimerPhase.Completed })
                return;
        }
        TimerCompleted?.Invoke(this, new(timer));
        // A subscriber can acknowledge, reset, or dispose while receiving the typed event.
        lock (_gate)
        {
            if (_disposed || FindTimer(timer.Id) is not { Phase: TimerPhase.Completed })
                return;
        }
        Completion?.Invoke(this, EventArgs.Empty);
    }
    private void StopTicker()
    {
        _ticker?.Dispose();
        _ticker = null;
    }
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LocalHub));
    }

    private void Restore()
    {
        TimerSetState? state = _store.ReadState<TimerSetState>("timer.json");
        bool normalize = false;
        if (state?.SchemaVersion == 2)
        {
            foreach (var saved in (state.Timers ?? new()).Take(MaxTimers))
            {
                if (saved is null || string.IsNullOrWhiteSpace(saved.Id) || saved.Id.Length > 64 || FindTimer(saved.Id) is not null || RestoreTimer(saved) is not { } timer)
                { normalize = true; continue; }
                _timers.Add(timer);
                normalize |= timer.Phase != saved.Phase;
            }
            _selectedTimerId = FindTimer(state.SelectedTimerId)?.Id ?? _timers.FirstOrDefault()?.Id;
            normalize |= _selectedTimerId != state.SelectedTimerId || (state.Timers?.Count ?? 0) != _timers.Count;
        }
        else if (state?.SchemaVersion == 1)
        {
            TimerState? legacy = _store.ReadState<TimerState>("timer.json");
            if (legacy is not null && RestoreTimer(legacy) is { } timer)
            {
                timer.Id = Guid.NewGuid().ToString("N");
                timer.Name = "Focus";
                _timers.Add(timer);
                _selectedTimerId = timer.Id;
            }
            normalize = true;
        }
        if (normalize) PersistTimers();
        RestoreStopwatch();
        RestoreFocusAndReminders();
        RestoreShelf();
        UpdateTicker();
    }
    private ActiveTimer? RestoreTimer(TimerState saved)
    {
        if (!Enum.IsDefined(saved.Phase) || saved.Phase == TimerPhase.Idle || !double.IsFinite(saved.TotalSeconds) || saved.TotalSeconds < 1 || saved.TotalSeconds > TimeSpan.FromDays(7).TotalSeconds || !ValidElapsed(saved.RemainingSeconds))
            return null;
        var timer = new ActiveTimer { Id = saved.Id, Name = TimerName(saved.Name, "Timer"), Phase = saved.Phase, TotalSeconds = saved.TotalSeconds, RemainingSeconds = saved.RemainingSeconds };
        if (timer.Phase == TimerPhase.Running)
        {
            if (saved.DeadlineUtc is not { } deadline) return null;
            // Live activity is monotonic; restart recovery deliberately compares the saved UTC deadline.
            timer.RemainingSeconds = Math.Max(0, (deadline - _clock.UtcNow).TotalSeconds);
            if (!ValidElapsed(timer.RemainingSeconds)) return null;
            timer.MonotonicDeadline = MonotonicDeadline(timer.RemainingSeconds);
            if (timer.RemainingSeconds <= 0) timer.Phase = TimerPhase.Completed;
            else timer.DeadlineUtc = deadline;
        }
        if (timer.Phase == TimerPhase.Completed || timer.RemainingSeconds <= 0)
        {
            timer.Phase = TimerPhase.Completed;
            timer.RemainingSeconds = 0;
            timer.DeadlineUtc = null;
        }
        return timer;
    }
    private static bool ValidElapsed(double seconds) => double.IsFinite(seconds) && seconds >= 0 && seconds <= MaxActivitySeconds;
    private void RestoreStopwatch()
    {
        var saved = _store.ReadState<StopwatchState>("stopwatch.json");
        if (saved is null || saved.SchemaVersion != 1 || !Enum.IsDefined(saved.Phase) || saved.Phase == StopwatchPhase.Idle || !ValidElapsed(saved.ElapsedSeconds)) return;
        if (saved.Phase == StopwatchPhase.Running && saved.StartedUtc is null) return;
        _stopwatchPhase = saved.Phase;
        _stopwatchElapsed = saved.ElapsedSeconds;
        if (saved.Phase == StopwatchPhase.Running)
        {
            // A backwards UTC change cannot make the saved elapsed duration negative or erase recorded laps.
            _stopwatchElapsed = Math.Min(MaxActivitySeconds, _stopwatchElapsed + Math.Max(0, (_clock.UtcNow - saved.StartedUtc!.Value).TotalSeconds));
            _stopwatchStarted = _clock.MonotonicMilliseconds;
        }
        double previous = 0;
        foreach (var lap in (saved.Laps ?? new()).Take(MaxStopwatchLaps))
        {
            if (lap is null || !ValidElapsed(lap.TotalSeconds) || lap.TotalSeconds < previous || lap.TotalSeconds > _stopwatchElapsed) continue;
            _laps.Add(new(_laps.Count + 1, lap.TotalSeconds, lap.TotalSeconds - previous));
            previous = lap.TotalSeconds;
        }
        PersistStopwatch();
    }
    private void PersistTimers()
    {
        if (_timers.Count == 0) { _store.DeleteState("timer.json"); return; }
        _store.WriteState("timer.json", new TimerSetState { SelectedTimerId = _selectedTimerId, Timers = _timers.Select(timer => new TimerState { Id = timer.Id, Name = timer.Name, Phase = timer.Phase, RemainingSeconds = Remaining(timer), TotalSeconds = timer.TotalSeconds, DeadlineUtc = timer.DeadlineUtc }).ToList() });
    }
    private void PersistStopwatch()
    {
        if (_stopwatchPhase == StopwatchPhase.Idle) { _store.DeleteState("stopwatch.json"); return; }
        // Save elapsed at this instant and a matching UTC anchor, never the original start time with a later elapsed value.
        _store.WriteState("stopwatch.json", new StopwatchState { Phase = _stopwatchPhase, ElapsedSeconds = StopwatchElapsed(), StartedUtc = _stopwatchPhase == StopwatchPhase.Running ? _clock.UtcNow : null, Laps = _laps.ToList() });
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            ++_shelfGeneration;
            StopTicker();
            PersistTimers();
            PersistStopwatch();
            PersistFocus();
            PersistReminders();
            PersistShelf();
            _shelf.Clear();
        }
    }

    public sealed class TimerState
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = "";
        public string Name { get; set; } = "Focus";
        public TimerPhase Phase
        {
            get; set;
        }
        public double RemainingSeconds
        {
            get; set;
        }
        public double TotalSeconds
        {
            get; set;
        }
        public DateTimeOffset? DeadlineUtc
        {
            get; set;
        }
    }
    public sealed class TimerSetState
    {
        public int SchemaVersion { get; set; } = 2;
        public string? SelectedTimerId { get; set; }
        public List<TimerState>? Timers { get; set; } = new();
    }
    public sealed class StopwatchState
    {
        public int SchemaVersion { get; set; } = 1;
        public StopwatchPhase Phase { get; set; }
        public double ElapsedSeconds { get; set; }
        public DateTimeOffset? StartedUtc { get; set; }
        public List<StopwatchLap>? Laps { get; set; } = new();
    }
    private sealed class ActiveTimer
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "Timer";
        public TimerPhase Phase { get; set; }
        public double RemainingSeconds { get; set; }
        public double TotalSeconds { get; set; }
        public DateTimeOffset? DeadlineUtc { get; set; }
        public long MonotonicDeadline { get; set; }
    }
}
