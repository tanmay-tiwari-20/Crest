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
public sealed record TimerSnapshot(TimerPhase Phase, double RemainingSeconds, double TotalSeconds, DateTimeOffset? DeadlineUtc);
public sealed record ShelfItem(string Id, string Name, string Path, bool Available, bool IsDirectory);

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

/// <summary>One durable timer and a bounded, memory-only file reference shelf.</summary>
public sealed class LocalHub : IDisposable
{
    private readonly object _gate = new();
    private readonly SettingsStore _store;
    private readonly ITimerClock _clock;
    private readonly bool _automaticTick;
    private readonly List<ShelfItem> _shelf = new();
    private System.Threading.Timer? _ticker;
    private TimerPhase _phase;
    private long _monotonicDeadline;
    private double _remaining;
    private double _total;
    private DateTimeOffset? _utcDeadline;
    private bool _disposed;
    public event EventHandler? Changed;
    public event EventHandler? Completion;
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
                return new(_phase, Remaining(), _total, _utcDeadline);
        }
    }
    public IReadOnlyList<ShelfItem> Shelf
    {
        get
        {
            lock (_gate)
                return _shelf.ToArray();
        }
    }

    public void StartTimer(TimeSpan duration)
    {
        if (duration.TotalSeconds < 1 || duration.TotalDays > 7)
            throw new ArgumentOutOfRangeException(nameof(duration), "Choose a timer between one second and seven days.");
        lock (_gate)
        {
            ThrowIfDisposed();
            _remaining = _total = duration.TotalSeconds;
            _phase = TimerPhase.Running;
            _monotonicDeadline = _clock.MonotonicMilliseconds + (long)(duration.TotalMilliseconds);
            _utcDeadline = _clock.UtcNow + duration;
            Persist();
            StartTicker();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void PauseTimer()
    {
        bool completed = false;
        lock (_gate)
        {
            if (_disposed || _phase != TimerPhase.Running)
                return;
            _remaining = Remaining();
            _phase = _remaining <= 0 ? TimerPhase.Completed : TimerPhase.Paused;
            completed = _phase == TimerPhase.Completed;
            _utcDeadline = null;
            StopTicker();
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        if (completed)
            Completion?.Invoke(this, EventArgs.Empty);
    }

    public void ResumeTimer()
    {
        lock (_gate)
        {
            if (_disposed || _phase != TimerPhase.Paused)
                return;
            _phase = TimerPhase.Running;
            _monotonicDeadline = _clock.MonotonicMilliseconds + (long)(_remaining * 1000);
            _utcDeadline = _clock.UtcNow + TimeSpan.FromSeconds(_remaining);
            Persist();
            StartTicker();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void CancelTimer() => ResetTimer();
    public void AcknowledgeTimer()
    {
        lock (_gate)
        {
            if (_phase != TimerPhase.Completed)
                return;
        }
        ResetTimer();
    }
    private void ResetTimer()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            StopTicker();
            _phase = TimerPhase.Idle;
            _total = _remaining = 0;
            _utcDeadline = null;
            _store.DeleteState("timer.json");
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Tick()
    {
        bool completed = false;
        lock (_gate)
        {
            if (_disposed || _phase != TimerPhase.Running)
                return;
            _remaining = Remaining();
            if (_remaining <= 0)
            {
                _phase = TimerPhase.Completed;
                _utcDeadline = null;
                StopTicker();
                Persist();
                completed = true;
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
        if (completed)
            Completion?.Invoke(this, EventArgs.Empty);
    }

    public string? AddShelfFiles(IEnumerable<string> paths)
    {
        var prepared = new List<ShelfItem>();
        int missing = 0;
        foreach (string input in paths.Take(100))
        {
            try
            {
                string path = System.IO.Path.GetFullPath(input);
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

    public void RemoveShelf(string id)
    {
        lock (_gate)
            _shelf.RemoveAll(item => item.Id == id);
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
                if (index >= 0 && _shelf[index].Available != item.Available)
                {
                    _shelf[index] = item;
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

    private double Remaining() => _phase == TimerPhase.Running ? Math.Max(0, (_monotonicDeadline - _clock.MonotonicMilliseconds) / 1000d) : Math.Max(0, _remaining);
    private void StartTicker()
    {
        if (!_automaticTick || _ticker is not null)
            return;
        _ticker = new System.Threading.Timer(_ => Tick(), null, 1000, 1000);
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
        TimerState? state = _store.ReadState<TimerState>("timer.json");
        if (state is null || state.SchemaVersion != 1 || !Enum.IsDefined(state.Phase) || state.Phase == TimerPhase.Idle || !double.IsFinite(state.TotalSeconds) || state.TotalSeconds < 1 || state.TotalSeconds > TimeSpan.FromDays(7).TotalSeconds || !double.IsFinite(state.RemainingSeconds))
            return;
        _total = state.TotalSeconds;
        _remaining = Math.Max(0, state.RemainingSeconds);
        _phase = Enum.IsDefined(state.Phase) ? state.Phase : TimerPhase.Idle;
        if (_phase == TimerPhase.Running && state.DeadlineUtc is { } deadline)
        {
            _utcDeadline = deadline;
            // Restart recovery deliberately uses UTC, including backwards clock changes.
            _remaining = Math.Max(0, (deadline - _clock.UtcNow).TotalSeconds);
            _monotonicDeadline = _clock.MonotonicMilliseconds + (long)(_remaining * 1000);
            if (_remaining <= 0)
            {
                _phase = TimerPhase.Completed;
                _utcDeadline = null;
                Persist();
            }
            else
                StartTicker();
        }
        else if (_phase == TimerPhase.Running)
        {
            _phase = TimerPhase.Idle;
            _remaining = _total = 0;
        }
        else if (_phase == TimerPhase.Completed)
        {
            _remaining = 0;
            _utcDeadline = null;
        }
        else if (_phase == TimerPhase.Paused && _remaining <= 0)
        {
            _phase = TimerPhase.Completed;
            Persist();
        }
    }
    private void Persist() => _store.WriteState("timer.json", new TimerState { SchemaVersion = 1, Phase = _phase, RemainingSeconds = Remaining(), TotalSeconds = _total, DeadlineUtc = _utcDeadline });

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            StopTicker();
            if (_phase != TimerPhase.Idle)
                Persist();
            _shelf.Clear();
        }
    }

    public sealed class TimerState
    {
        public int SchemaVersion { get; set; } = 1;
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
}
