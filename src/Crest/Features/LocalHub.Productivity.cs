using System;
using System.Collections.Generic;
using System.Linq;

namespace Crest.Features;

public enum FocusStage { Work, Break }
public enum ReminderPhase { Scheduled, Due }
public sealed record FocusSnapshot(TimerPhase Phase, FocusStage Stage, double RemainingSeconds, double TotalSeconds, string Name, int CompletedRounds, int TargetRounds, string Id = "");
public sealed record ReminderSnapshot(string Id, string Text, DateTimeOffset DueUtc, ReminderPhase Phase);
public sealed class FocusCompletedEventArgs(FocusSnapshot focus) : EventArgs { public FocusSnapshot Focus { get; } = focus; }
public sealed class ReminderDueEventArgs(ReminderSnapshot reminder) : EventArgs { public ReminderSnapshot Reminder { get; } = reminder; }

public sealed partial class LocalHub
{
    public const int MaxReminders = 32;
    private FocusState? _focus;
    private long _focusDeadline;
    private readonly List<ReminderSnapshot> _reminders = new();
    public event EventHandler<FocusCompletedEventArgs>? FocusCompleted;
    public event EventHandler<ReminderDueEventArgs>? ReminderDue;
    public FocusSnapshot Focus { get { lock (_gate) return FocusSnapshot(); } }
    public IReadOnlyList<ReminderSnapshot> Reminders { get { lock (_gate) return _reminders.OrderBy(x => x.Phase == ReminderPhase.Due ? 0 : 1).ThenBy(x => x.DueUtc).ToArray(); } }

    public void StartFocusSession(TimeSpan work, TimeSpan rest, int rounds = 4, string? name = null)
    {
        ValidateDuration(work); ValidateDuration(rest);
        if (rounds is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(rounds), "Choose between 1 and 12 focus rounds.");
        lock (_gate)
        {
            ThrowIfDisposed();
            _focus = new FocusState { Id = Guid.NewGuid().ToString("N"), Name = TimerName(name, "Focus session"), WorkSeconds = work.TotalSeconds, BreakSeconds = rest.TotalSeconds, TargetRounds = rounds, Phase = TimerPhase.Running, RemainingSeconds = work.TotalSeconds, DeadlineUtc = UtcDeadline(work.TotalSeconds) };
            _focusDeadline = MonotonicDeadline(work.TotalSeconds);
            PersistFocus(); UpdateTicker();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void PauseFocus()
    {
        FocusSnapshot? completed = null;
        lock (_gate)
        {
            if (_disposed || _focus is not { Phase: TimerPhase.Running }) return;
            _focus.RemainingSeconds = FocusRemaining();
            _focus.Phase = _focus.RemainingSeconds > 0 ? TimerPhase.Paused : TimerPhase.Completed;
            _focus.DeadlineUtc = null;
            if (_focus.Phase == TimerPhase.Completed) { CompleteFocusRound(); completed = FocusSnapshot(); }
            PersistFocus(); UpdateTicker();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        NotifyFocusAndReminders(completed, Array.Empty<ReminderSnapshot>());
    }
    public void ResumeFocus()
    {
        lock (_gate)
        {
            if (_disposed || _focus is not { Phase: TimerPhase.Paused }) return;
            _focus.Phase = TimerPhase.Running;
            _focusDeadline = MonotonicDeadline(_focus.RemainingSeconds);
            _focus.DeadlineUtc = UtcDeadline(_focus.RemainingSeconds);
            PersistFocus(); UpdateTicker();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void NextFocusPhase()
    {
        lock (_gate)
        {
            if (_disposed || _focus is not { Phase: TimerPhase.Completed } || _focus.CompletedRounds >= _focus.TargetRounds) return;
            _focus.Stage = _focus.Stage == FocusStage.Work ? FocusStage.Break : FocusStage.Work;
            _focus.RemainingSeconds = _focus.Stage == FocusStage.Work ? _focus.WorkSeconds : _focus.BreakSeconds;
            _focus.Phase = TimerPhase.Running;
            _focusDeadline = MonotonicDeadline(_focus.RemainingSeconds);
            _focus.DeadlineUtc = UtcDeadline(_focus.RemainingSeconds);
            PersistFocus(); UpdateTicker();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void DismissFocus()
    {
        lock (_gate) { if (_disposed) return; _focus = null; PersistFocus(); UpdateTicker(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public string AddReminder(string text, DateTimeOffset dueUtc)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Enter reminder text.", nameof(text));
        string id = Guid.NewGuid().ToString("N");
        ReminderSnapshot reminder;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_reminders.Count >= MaxReminders) throw new InvalidOperationException($"Crest holds {MaxReminders} reminders. Dismiss one to make room.");
            if ((dueUtc - _clock.UtcNow).TotalDays > 366) throw new ArgumentOutOfRangeException(nameof(dueUtc), "Choose a reminder within the next year.");
            string bounded = text.Trim(); bounded = bounded[..Math.Min(bounded.Length, 240)];
            reminder = new(id, bounded, dueUtc.ToUniversalTime(), dueUtc <= _clock.UtcNow ? ReminderPhase.Due : ReminderPhase.Scheduled);
            _reminders.Add(reminder); PersistReminders(); UpdateTicker();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        if (reminder.Phase == ReminderPhase.Due) NotifyFocusAndReminders(null, new[] { reminder });
        return id;
    }
    public void SnoozeReminder(string id, TimeSpan duration)
    {
        if (duration.TotalSeconds < 1 || duration.TotalDays > 7) throw new ArgumentOutOfRangeException(nameof(duration), "Snooze between one second and seven days.");
        lock (_gate)
        {
            if (_disposed) return;
            int index = _reminders.FindIndex(x => x.Id == id);
            if (index < 0) return;
            _reminders[index] = _reminders[index] with { DueUtc = UtcDeadline(duration.TotalSeconds), Phase = ReminderPhase.Scheduled };
            PersistReminders(); UpdateTicker();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void DismissReminder(string id)
    {
        lock (_gate) { if (_disposed) return; _reminders.RemoveAll(x => x.Id == id); PersistReminders(); UpdateTicker(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private FocusSnapshot FocusSnapshot() => _focus is { } f ? new(f.Phase, f.Stage, FocusRemaining(), f.Stage == FocusStage.Work ? f.WorkSeconds : f.BreakSeconds, f.Name, f.CompletedRounds, f.TargetRounds, f.Id) : new(TimerPhase.Idle, FocusStage.Work, 0, 0, "Focus session", 0, 4);
    private double FocusRemaining() => _focus is not { } f ? 0 : f.Phase == TimerPhase.Running ? Math.Clamp(((double)_focusDeadline - _clock.MonotonicMilliseconds) / 1000, 0, MaxActivitySeconds) : f.RemainingSeconds;
    private bool HasRunningFocusOrReminders() => _focus?.Phase == TimerPhase.Running || _reminders.Any(x => x.Phase == ReminderPhase.Scheduled);
    private void CompleteFocusRound() { if (_focus?.Stage == FocusStage.Work) _focus.CompletedRounds = Math.Min(_focus.TargetRounds, _focus.CompletedRounds + 1); }
    private void TickFocusAndReminders(ref FocusSnapshot? completed, List<ReminderSnapshot> due)
    {
        if (_focus is { Phase: TimerPhase.Running } && FocusRemaining() <= 0)
        {
            _focus.RemainingSeconds = 0; _focus.Phase = TimerPhase.Completed; _focus.DeadlineUtc = null;
            CompleteFocusRound(); completed = FocusSnapshot(); PersistFocus();
        }
        for (int i = 0; i < _reminders.Count; i++)
        {
            if (_reminders[i].Phase != ReminderPhase.Scheduled || _reminders[i].DueUtc > _clock.UtcNow) continue;
            _reminders[i] = _reminders[i] with { Phase = ReminderPhase.Due };
            due.Add(_reminders[i]);
        }
        if (due.Count > 0) PersistReminders();
    }
    private void NotifyFocusAndReminders(FocusSnapshot? completed, IReadOnlyList<ReminderSnapshot> due)
    {
        if (completed is not null)
        {
            bool current;
            lock (_gate) current = !_disposed && _focus?.Phase == TimerPhase.Completed && _focus.Id == completed.Id && _focus.Stage == completed.Stage && _focus.CompletedRounds == completed.CompletedRounds;
            if (current) FocusCompleted?.Invoke(this, new(completed));
        }
        foreach (var reminder in due)
        {
            lock (_gate) { if (_disposed || !_reminders.Any(x => x.Id == reminder.Id && x.Phase == ReminderPhase.Due)) continue; }
            ReminderDue?.Invoke(this, new(reminder));
        }
    }
    private void ClearFocusAndReminders() { _focus = null; _reminders.Clear(); _store.DeleteState("focus.json"); _store.DeleteState("reminders.json"); }
    private void PersistFocus()
    {
        if (_focus is null) { _store.DeleteState("focus.json"); return; }
        _focus.RemainingSeconds = FocusRemaining(); _store.WriteState("focus.json", _focus);
    }
    private void PersistReminders()
    {
        if (_reminders.Count == 0) { _store.DeleteState("reminders.json"); return; }
        _store.WriteState("reminders.json", new ReminderState { Reminders = _reminders.ToList() });
    }
    private void RestoreFocusAndReminders()
    {
        var saved = _store.ReadState<FocusState>("focus.json");
        if (saved is { SchemaVersion: 1 } && Enum.IsDefined(saved.Stage) && Enum.IsDefined(saved.Phase) && saved.Phase != TimerPhase.Idle && saved.WorkSeconds >= 1 && saved.WorkSeconds <= 604800 && saved.BreakSeconds >= 1 && saved.BreakSeconds <= 604800 && ValidElapsed(saved.RemainingSeconds) && saved.TargetRounds is >= 1 and <= 12 && saved.CompletedRounds >= 0 && saved.CompletedRounds <= saved.TargetRounds)
        {
            if (saved.Phase != TimerPhase.Running || saved.DeadlineUtc is not null)
            {
                _focus = saved; saved.Name = TimerName(saved.Name, "Focus session");
                saved.Id = string.IsNullOrWhiteSpace(saved.Id) || saved.Id.Length > 64 ? Guid.NewGuid().ToString("N") : saved.Id;
                if (saved.Phase == TimerPhase.Running)
                {
                    saved.RemainingSeconds = Math.Max(0, (saved.DeadlineUtc!.Value - _clock.UtcNow).TotalSeconds);
                    if (!ValidElapsed(saved.RemainingSeconds)) _focus = null;
                    else if (saved.RemainingSeconds <= 0) { saved.Phase = TimerPhase.Completed; saved.DeadlineUtc = null; CompleteFocusRound(); }
                    else _focusDeadline = MonotonicDeadline(saved.RemainingSeconds);
                }
                if (_focus?.Phase == TimerPhase.Completed) _focus.RemainingSeconds = 0;
            }
        }
        var reminders = _store.ReadState<ReminderState>("reminders.json");
        if (reminders?.SchemaVersion == 1)
            foreach (var item in (reminders.Reminders ?? new()).Take(MaxReminders))
            {
                if (item is null || string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 64 || string.IsNullOrWhiteSpace(item.Text) || !Enum.IsDefined(item.Phase) || _reminders.Any(x => x.Id == item.Id)) continue;
                _reminders.Add(item with { Text = item.Text[..Math.Min(item.Text.Length, 240)], Phase = item.Phase == ReminderPhase.Due || item.DueUtc <= _clock.UtcNow ? ReminderPhase.Due : ReminderPhase.Scheduled });
            }
        PersistFocus(); PersistReminders();
    }
    public sealed class FocusState
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = "";
        public string Name { get; set; } = "Focus session";
        public TimerPhase Phase { get; set; }
        public FocusStage Stage { get; set; }
        public double WorkSeconds { get; set; }
        public double BreakSeconds { get; set; }
        public double RemainingSeconds { get; set; }
        public DateTimeOffset? DeadlineUtc { get; set; }
        public int TargetRounds { get; set; } = 4;
        public int CompletedRounds { get; set; }
    }
    public sealed class ReminderState { public int SchemaVersion { get; set; } = 1; public List<ReminderSnapshot>? Reminders { get; set; } = new(); }
}
