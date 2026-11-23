using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Crest.Features;

namespace Crest.Core;

/// <summary>Versioned, local-only configuration. Failed writes never crash an island control.</summary>
public sealed class SettingsStore
{
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Crest");
    public string DirectoryPath
    {
        get;
    }
    public bool RecoveredCorruptSettings
    {
        get; private set;
    }
    public string? LastError
    {
        get; private set;
    }

    public SettingsStore(string? directory = null) => DirectoryPath = directory ?? DataDirectory;

    public AppSettings Load()
    {
        lock (_gate)
        {
            string file = Path.Combine(DirectoryPath, "settings.json");
            if (!File.Exists(file))
                return new AppSettings();
            try
            {
                if (new FileInfo(file).Length > 1024 * 1024)
                    throw new JsonException("Settings exceed the supported limit.");
                AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file)) ?? throw new JsonException("Settings are empty.");
                if (settings.SchemaVersion != 1)
                    throw new JsonException("Unsupported settings version.");
                Normalize(settings);
                LastError = null;
                return settings;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                RecoveredCorruptSettings = true;
                LastError = "Crest recovered safe default settings. Previous preferences could not be read.";
                try
                {
                    File.Move(file, file + ".recovered", true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            Normalize(settings);
            WriteJson("settings.json", settings);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            foreach (string name in new[] { "settings.json", "settings.json.recovered", "timer.json", "stopwatch.json", "focus.json", "reminders.json", "shelf.json", "clipboard.json", "captures.json", "weather.json", "weather.json.tmp", "settings.json.tmp", "timer.json.tmp", "stopwatch.json.tmp", "focus.json.tmp", "reminders.json.tmp", "shelf.json.tmp", "clipboard.json.tmp", "captures.json.tmp" })
            {
                try
                {
                    File.Delete(Path.Combine(DirectoryPath, name));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastError = "Some preferences could not be cleared. Close other Crest processes and try again."; }
            }
            RecoveredCorruptSettings = false;
        }
    }

    public T? ReadState<T>(string name)
    {
        lock (_gate)
        {
            try
            {
                string file = Path.Combine(DirectoryPath, name);
                if (!File.Exists(file) || new FileInfo(file).Length > 1024 * 1024)
                    return default;
                return JsonSerializer.Deserialize<T>(File.ReadAllText(file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return default; }
        }
    }

    public void WriteState<T>(string name, T value)
    {
        lock (_gate)
            WriteJson(name, value);
    }
    public void DeleteState(string name)
    {
        lock (_gate)
        {
            try
            {
                File.Delete(Path.Combine(DirectoryPath, name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastError = "Crest could not update saved activity state."; }
        }
    }

    private void WriteJson<T>(string name, T value)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            string file = Path.Combine(DirectoryPath, name);
            string temporary = file + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, value, JsonOptions);
                stream.Flush(true);
            }
            File.Move(temporary, file, true);
            LastError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            LastError = "Crest could not save preferences. Check that your user account can write to local application data.";
        }
    }

    private static void Normalize(AppSettings s)
    {
        s.SchemaVersion = 1;
        s.Theme = ModuleCatalog.Themes.Contains(s.Theme) ? s.Theme : "System";
        s.Motion = s.Motion is "Reduced" or "Normal" or "System" ? s.Motion : "System";
        s.SelectedDisplay ??= "";
        s.PreferredPlayer ??= "";
        s.Weather ??= new(); s.Calendar ??= new(); s.Downloads ??= new(); s.Ai ??= new(); s.WindowsIntegrations ??= new();
        WeatherPreferences.Normalize(s.Weather); CalendarPreferences.Normalize(s.Calendar);
        DownloadService.Normalize(s.Downloads); AiAssistantService.Normalize(s.Ai);
        s.TriggerWidth = Math.Clamp(s.TriggerWidth, 80, 800);
        s.TriggerDepth = Math.Clamp(s.TriggerDepth, 1, 12);
        s.DwellMs = Math.Clamp(s.DwellMs, 80, 500);
        s.GraceMs = Math.Clamp(s.GraceMs, 100, 750);
        s.MotionSpeed = double.IsFinite(s.MotionSpeed) ? Math.Clamp(s.MotionSpeed, 0.5, 2) : 1;
        s.SizeScale = double.IsFinite(s.SizeScale) ? Math.Clamp(s.SizeScale, 0.85, 1.3) : 1;
        s.EnabledModules = (s.EnabledModules ?? new List<string> { "Music", "Controls", "Timer", "Launch", "Shelf" })
            .Where(x => ModuleCatalog.All.Contains(x)).Distinct().ToList();
        s.ModuleOrder = (s.ModuleOrder ?? new()).Where(x => ModuleCatalog.All.Contains(x)).Concat(ModuleCatalog.All).Distinct().ToList();
        s.DoubleClickGesture = ModuleCatalog.Gestures.Contains(s.DoubleClickGesture) ? s.DoubleClickGesture : "Expand";
        s.MiddleClickGesture = ModuleCatalog.Gestures.Contains(s.MiddleClickGesture) ? s.MiddleClickGesture : "Dismiss";
        s.WheelGesture = s.WheelGesture is "None" or "Switch modules" ? s.WheelGesture : "None";
        s.ClipboardRetentionDays = Math.Clamp(s.ClipboardRetentionDays, 1, 30);
        s.Displays = (s.Displays ?? new()).Where(x => !string.IsNullOrWhiteSpace(x.Key) && x.Key.Length <= 100 && x.Value != null).Take(16).ToDictionary(x => x.Key, x => x.Value);
        foreach (var d in s.Displays.Values)
        {
            d.Theme = ModuleCatalog.Themes.Contains(d.Theme) ? d.Theme : "System";
            d.Motion = d.Motion is "Normal" or "Reduced" or "System" ? d.Motion : "System";
            d.SizeScale = double.IsFinite(d.SizeScale) ? Math.Clamp(d.SizeScale, .85, 1.3) : 1;
            d.MotionSpeed = double.IsFinite(d.MotionSpeed) ? Math.Clamp(d.MotionSpeed, .5, 2) : 1;
            d.TriggerWidth = Math.Clamp(d.TriggerWidth, 80, 800); d.TriggerDepth = Math.Clamp(d.TriggerDepth, 1, 12);
            d.DwellMs = Math.Clamp(d.DwellMs, 80, 500); d.GraceMs = Math.Clamp(d.GraceMs, 100, 750);
        }
        s.WorkspacePresets = (s.WorkspacePresets ?? new()).Where(x => x != null).Take(20).ToList();
        var workspaceIds = new HashSet<string>();
        foreach (var workspace in s.WorkspacePresets)
        {
            if (string.IsNullOrWhiteSpace(workspace.Id) || !workspaceIds.Add(workspace.Id)) { workspace.Id = Guid.NewGuid().ToString("N"); workspaceIds.Add(workspace.Id); }
            workspace.Name = string.IsNullOrWhiteSpace(workspace.Name) ? "Workspace" : workspace.Name[..Math.Min(workspace.Name.Length, 80)];
            workspace.Targets = (workspace.Targets ?? new()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.Target) && x.Target.Length <= 32768).Take(20).ToList();
            foreach (var target in workspace.Targets) { target.Name = string.IsNullOrWhiteSpace(target.Name) ? "Target" : target.Name[..Math.Min(target.Name.Length, 100)]; target.Kind = target.Kind is "App" or "Folder" or "Website" or "Shortcut" ? target.Kind : "App"; }
        }
        s.Launchers = (s.Launchers ?? new List<LauncherEntry>()).Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Target))
            .Take(100).Select(x =>
            {
                x.Id = string.IsNullOrWhiteSpace(x.Id) ? Guid.NewGuid().ToString("N") : x.Id;
                x.Name = string.IsNullOrWhiteSpace(x.Name) ? "Shortcut" : x.Name[..Math.Min(x.Name.Length, 100)];
                x.Kind = x.Kind is "App" or "Folder" or "Website" or "Shortcut" ? x.Kind : "App";
                return x;
            }).ToList();
    }
}
