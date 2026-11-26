using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Crest.Features;

namespace Crest.Core;

public enum IslandActivityKind { BatteryWarning, Timer, Reminder, Focus, Music, Download, Ai }
public enum IslandActivityAction { None, Open, PauseTimer, ResumeTimer, AcknowledgeTimer, PauseFocus, ResumeFocus, ContinueFocus, DismissFocus, PauseMusic, ResumeMusic }

/// <summary>One immutable source of identity, status and actions for every island presentation.</summary>
public sealed record IslandActivity(
    string Id, IslandActivityKind Kind, string Module, string Title, string Subtitle,
    string Value, string Glyph, string Phase, string AccentKey,
    IslandActivityAction PrimaryAction, string PrimaryActionName, string EntityId = "", double? Progress = null)
{
    public bool CanInvoke => PrimaryAction != IslandActivityAction.None;
    public string PrimaryActionGlyph => PrimaryAction switch
    {
        IslandActivityAction.PauseTimer or IslandActivityAction.PauseFocus or IslandActivityAction.PauseMusic => "\uE769",
        IslandActivityAction.ResumeTimer or IslandActivityAction.ResumeFocus or IslandActivityAction.ResumeMusic or IslandActivityAction.ContinueFocus => "\uE768",
        IslandActivityAction.AcknowledgeTimer or IslandActivityAction.DismissFocus => "\uE73E",
        _ => "\uE76C"
    };
}

/// <summary>Media input deliberately contains no WPF or provider objects.</summary>
public sealed record IslandMediaActivity(bool HasSession, bool IsPlaying, bool CanPlayPause,
    string PlayerId, string Title, string Artist, double PositionSeconds = 0, double DurationSeconds = 0);
public sealed record IslandDownloadActivity(string Id, string Name, long ReceivedBytes, long? TotalBytes, bool Downloading);
public sealed record IslandAiActivity(bool Busy, string Model, string Status);

public static class IslandActivities
{
    public static IReadOnlyList<IslandActivity> Read(IEnumerable<string> enabledModules,
        IReadOnlyList<TimerSnapshot> timers, FocusSnapshot focus, IReadOnlyList<ReminderSnapshot> reminders,
        BatteryNoticeSnapshot? notice, IslandMediaActivity music, string? selectedTimerId = null,
        IReadOnlyList<IslandDownloadActivity>? downloads = null, IslandAiActivity? ai = null)
    {
        var enabled = new HashSet<string>(enabledModules, StringComparer.Ordinal);
        var result = new List<IslandActivity>();
        if (enabled.Contains("Controls") && notice is { Severity: BatteryWarningLevel.Low or BatteryWarningLevel.Critical, Percent: >= 0 and <= 100 })
            result.Add(new("battery:" + notice.Id.ToString(CultureInfo.InvariantCulture), IslandActivityKind.BatteryWarning, "Controls",
                notice.Severity == BatteryWarningLevel.Critical ? "Battery was critically low" : "Battery was low", "Earlier Windows warning",
                notice.Percent.ToString(CultureInfo.CurrentCulture) + "%", "\uE850", notice.Severity.ToString(), "IslandAccentNotice",
                IslandActivityAction.Open, "Open battery controls", notice.Id.ToString(CultureInfo.InvariantCulture)));

        if (enabled.Contains("Timer"))
        {
            foreach (var timer in timers.Where(timer => timer.Phase == TimerPhase.Completed)) result.Add(FromTimer(timer));
            foreach (var reminder in reminders.Where(reminder => reminder.Phase == ReminderPhase.Due))
                result.Add(new("reminder:" + reminder.Id, IslandActivityKind.Reminder, "Timer", "Reminder", reminder.Text,
                    "Due", "\uE823", "Due", "IslandAccentNotice", IslandActivityAction.Open, "Open reminders", reminder.Id));
            if (focus.Phase is TimerPhase.Running or TimerPhase.Paused or TimerPhase.Completed && !string.IsNullOrEmpty(focus.Id))
            {
                bool finished = focus.Phase == TimerPhase.Completed && focus.CompletedRounds >= focus.TargetRounds;
                var action = focus.Phase switch
                {
                    TimerPhase.Running => IslandActivityAction.PauseFocus,
                    TimerPhase.Paused => IslandActivityAction.ResumeFocus,
                    _ => finished ? IslandActivityAction.DismissFocus : IslandActivityAction.ContinueFocus
                };
                string name = action switch
                {
                    IslandActivityAction.PauseFocus => "Pause focus session",
                    IslandActivityAction.ResumeFocus => "Resume focus session",
                    IslandActivityAction.ContinueFocus => "Continue to " + (focus.Stage == FocusStage.Work ? "break" : "work"),
                    _ => "Dismiss completed focus session"
                };
                string subtitle = focus.Phase == TimerPhase.Completed
                    ? finished ? "Session complete" : focus.Stage + " complete · continue when ready"
                    : focus.Stage + " · round " + Math.Clamp(focus.CompletedRounds + (focus.Stage == FocusStage.Work ? 1 : 0), 1, Math.Max(1, focus.TargetRounds)).ToString(CultureInfo.CurrentCulture) + " of " + focus.TargetRounds.ToString(CultureInfo.CurrentCulture);
                result.Add(new("focus:" + focus.Id, IslandActivityKind.Focus, "Timer", focus.Name, subtitle,
                    focus.Phase == TimerPhase.Completed ? "Done" : Time(focus.RemainingSeconds), "\uE916", focus.Phase.ToString(), "IslandAccentTimer", action, name, focus.Id,
                    Progress(focus.TotalSeconds - focus.RemainingSeconds, focus.TotalSeconds)));
            }
            foreach (var timer in timers.Where(timer => timer.Phase is TimerPhase.Running or TimerPhase.Paused)
                .OrderBy(timer => timer.Id == selectedTimerId ? 0 : 1)
                .ThenBy(timer => timer.Phase == TimerPhase.Running ? 0 : 1).ThenBy(timer => timer.DeadlineUtc)) result.Add(FromTimer(timer));
        }
        if (enabled.Contains("Integrations"))
        {
            foreach (var download in downloads ?? Array.Empty<IslandDownloadActivity>())
            {
                if (!download.Downloading || string.IsNullOrEmpty(download.Id) || download.ReceivedBytes < 0) continue;
                double? progress = download.TotalBytes is > 0 ? Progress(download.ReceivedBytes, download.TotalBytes.Value) : null;
                result.Add(new("download:" + download.Id, IslandActivityKind.Download, "Integrations",
                    string.IsNullOrWhiteSpace(download.Name) ? "Download" : download.Name,
                    Bytes(download.ReceivedBytes) + (download.TotalBytes is > 0 ? " of " + Bytes(download.TotalBytes.Value) : " received"),
                    progress.HasValue ? (progress.Value * 100).ToString("0", CultureInfo.CurrentCulture) + "%" : Bytes(download.ReceivedBytes),
                    "\uE896", "Running", "IslandAccentTimer", IslandActivityAction.Open, "Open download controls", download.Id, progress));
            }
            if (ai is { Busy: true })
                result.Add(new("ai:" + ai.Model, IslandActivityKind.Ai, "Integrations", "AI request",
                    string.IsNullOrWhiteSpace(ai.Model) ? ai.Status : ai.Model + " · " + ai.Status,
                    "Working", "\uE99A", "Running", "IslandAccentMusic", IslandActivityAction.Open, "Open AI controls", ai.Model));
        }
        if (enabled.Contains("Music") && music.HasSession)
            result.Add(new("music:" + music.PlayerId, IslandActivityKind.Music, "Music",
                string.IsNullOrWhiteSpace(music.Title) ? "Audio" : music.Title, music.Artist,
                music.DurationSeconds > 0 ? Time(music.PositionSeconds) : music.IsPlaying ? "Playing" : "Paused",
                "\uE8D6", music.IsPlaying ? "Running" : "Paused", "IslandAccentMusic",
                music.CanPlayPause ? music.IsPlaying ? IslandActivityAction.PauseMusic : IslandActivityAction.ResumeMusic : IslandActivityAction.None,
                music.IsPlaying ? "Pause playback" : "Resume playback", music.PlayerId, Progress(music.PositionSeconds, music.DurationSeconds)));
        return result.AsReadOnly();
    }

    private static IslandActivity FromTimer(TimerSnapshot timer)
    {
        var action = timer.Phase switch
        {
            TimerPhase.Running => IslandActivityAction.PauseTimer,
            TimerPhase.Paused => IslandActivityAction.ResumeTimer,
            _ => IslandActivityAction.AcknowledgeTimer
        };
        return new("timer:" + timer.Id, IslandActivityKind.Timer, "Timer", timer.Name,
            timer.Phase == TimerPhase.Completed ? "Timer complete" : timer.Phase == TimerPhase.Paused ? "Paused" : "Timer running",
            timer.Phase == TimerPhase.Completed ? "Done" : Time(timer.RemainingSeconds), "\uE823", timer.Phase.ToString(), "IslandAccentTimer", action,
            (timer.Phase == TimerPhase.Completed ? "Acknowledge " : timer.Phase == TimerPhase.Paused ? "Resume " : "Pause ") + timer.Name,
            timer.Id, Progress(timer.TotalSeconds - timer.RemainingSeconds, timer.TotalSeconds));
    }

    private static double? Progress(double position, double total) => total > 0 && double.IsFinite(total) && double.IsFinite(position) ? Math.Clamp(position / total, 0, 1) : null;
    public static string Bytes(long bytes)
    {
        double value = Math.Max(0, bytes);
        var units = new[] { "B", "KB", "MB", "GB" };
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return value.ToString(unit == 0 ? "0" : "0.#", CultureInfo.CurrentCulture) + " " + units[unit];
    }
    public static string Time(double seconds)
    {
        var value = TimeSpan.FromSeconds(double.IsFinite(seconds) ? Math.Clamp(Math.Ceiling(seconds), 0, LocalHub.MaxActivitySeconds) : 0);
        return value.TotalHours >= 1
            ? string.Format(CultureInfo.CurrentCulture, "{0}:{1:00}:{2:00}", (int)value.TotalHours, value.Minutes, value.Seconds)
            : value.ToString(@"m\:ss", CultureInfo.CurrentCulture);
    }
}
