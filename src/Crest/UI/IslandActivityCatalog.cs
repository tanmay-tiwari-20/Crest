using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Crest.Core;

namespace Crest.UI;

public static class IslandActivityCatalog
{
    private static readonly ConditionalWeakTable<App, SemaphoreSlim> Actions = new();
    public static bool IsActionBusy(App app) => Actions.TryGetValue(app, out var gate) && gate.CurrentCount == 0;

    public static IReadOnlyList<IslandActivity> Read(App app, BatteryNoticeSnapshot? shownBatteryNotice = null)
    {
        var media = app.Hardware.Music;
        string playerId = app.Hardware.Players.FirstOrDefault(player => player.Name == media.PlayerName)?.Id ?? media.PlayerName;
        var downloads = app.Preferences.Downloads.Enabled && app.Downloads != null
            ? app.Downloads.Items.Where(item => item.Phase == Crest.Features.DownloadPhase.Downloading)
                .Select(item => new IslandDownloadActivity(item.Id, Path.GetFileName(item.Destination), item.ReceivedBytes, item.TotalBytes, true)).ToArray()
            : Array.Empty<IslandDownloadActivity>();
        IslandAiActivity? ai = null;
        if (app.Preferences.Ai.Enabled && app.Ai != null && app.Ai.Busy)
        {
            string status = app.Ai.Status;
            if (app.Ai.Busy) ai = new(true, app.Preferences.Ai.Model, status);
        }
        return IslandActivities.Read(app.Preferences.EnabledModules, app.Local.Timers, app.Local.Focus, app.Local.Reminders,
            shownBatteryNotice ?? app.Hardware.PendingBatteryNotice,
            new(media.HasSession, media.IsPlaying, media.CanPlayPause, playerId, media.Title, media.Artist, media.PositionSeconds, media.DurationSeconds), app.Local.SelectedTimerId, downloads, ai);
    }

    public static async Task InvokePrimaryAsync(App app, IslandActivity activity, Action<string>? openModule = null)
    {
        var gate = Actions.GetValue(app, _ => new SemaphoreSlim(1, 1));
        if (!gate.Wait(0)) return;
        try
        {
            // A displayed warning is retained by the shell after Windows' pending notice is acknowledged.
            if (activity.Kind == IslandActivityKind.BatteryWarning && activity.PrimaryAction == IslandActivityAction.Open)
            {
                if (app.Preferences.EnabledModules.Contains("Controls")) openModule?.Invoke("Controls");
                return;
            }
            // A disappearing or changed source must not redirect an old button to another activity.
            var current = Read(app).FirstOrDefault(item => item.Id == activity.Id);
            if (current == null || current.PrimaryAction != activity.PrimaryAction || current.Phase != activity.Phase) return;
            if (activity.PrimaryAction == IslandActivityAction.Open) { openModule?.Invoke(activity.Module); return; }
            if (activity.PrimaryAction is IslandActivityAction.PauseMusic or IslandActivityAction.ResumeMusic)
            {
                await app.Hardware.TogglePlaybackAsync();
                return;
            }
            await Task.Run(() =>
            {
                switch (activity.PrimaryAction)
                {
                    case IslandActivityAction.PauseTimer: app.Local.PauseTimer(activity.EntityId); break;
                    case IslandActivityAction.ResumeTimer: app.Local.ResumeTimer(activity.EntityId); break;
                    case IslandActivityAction.AcknowledgeTimer: app.Local.AcknowledgeTimer(activity.EntityId); break;
                    case IslandActivityAction.PauseFocus: app.Local.PauseFocus(activity.EntityId); break;
                    case IslandActivityAction.ResumeFocus: app.Local.ResumeFocus(activity.EntityId); break;
                    case IslandActivityAction.ContinueFocus: app.Local.NextFocusPhase(activity.EntityId); break;
                    case IslandActivityAction.DismissFocus: app.Local.DismissFocus(activity.EntityId); break;
                }
            });
        }
        finally { gate.Release(); }
    }
}
