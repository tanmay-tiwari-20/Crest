using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

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
            foreach (string name in new[] { "settings.json", "settings.json.recovered", "timer.json", "stopwatch.json", "settings.json.tmp", "timer.json.tmp", "stopwatch.json.tmp" })
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
        s.Theme = s.Theme is "Dark" or "Light" or "System" ? s.Theme : "System";
        s.Motion = s.Motion is "Reduced" or "Normal" or "System" ? s.Motion : "System";
        s.SelectedDisplay ??= "";
        s.PreferredPlayer ??= "";
        s.TriggerWidth = Math.Clamp(s.TriggerWidth, 80, 800);
        s.TriggerDepth = Math.Clamp(s.TriggerDepth, 1, 12);
        s.DwellMs = Math.Clamp(s.DwellMs, 80, 500);
        s.GraceMs = Math.Clamp(s.GraceMs, 100, 750);
        s.MotionSpeed = double.IsFinite(s.MotionSpeed) ? Math.Clamp(s.MotionSpeed, 0.5, 2) : 1;
        s.SizeScale = double.IsFinite(s.SizeScale) ? Math.Clamp(s.SizeScale, 0.85, 1.3) : 1;
        s.EnabledModules = (s.EnabledModules ?? new List<string> { "Music", "Controls", "Timer", "Launch", "Shelf" })
            .Where(x => x is "Music" or "Controls" or "Timer" or "Launch" or "Shelf").Distinct().ToList();
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
