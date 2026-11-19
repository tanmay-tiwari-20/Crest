using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Crest.Core;

namespace Crest.Features;

public sealed record CaptureReference(string Id, string Path, DateTimeOffset SelectedUtc)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public bool Available => File.Exists(Path);
}

/// <summary>Only explicit capture selections are retained; no image folders are watched.</summary>
public sealed class LocalWorkflowHub
{
    public const int MaxCaptures = 12;
    private readonly SettingsStore _store;
    private readonly List<CaptureReference> _captures = new();
    public IReadOnlyList<CaptureReference> Captures => _captures.ToArray();
    public LocalWorkflowHub(SettingsStore store)
    {
        _store = store;
        var saved = store.ReadState<CaptureState>("captures.json");
        if (saved?.SchemaVersion != 1) return;
        foreach (var item in (saved.Items ?? new()).Where(x => x != null).Take(MaxCaptures))
        {
            if (IsCapturePath(item.Path) && !string.IsNullOrWhiteSpace(item.Id) && !_captures.Any(x => x.Id == item.Id || string.Equals(x.Path, item.Path, StringComparison.OrdinalIgnoreCase)))
                _captures.Add(item);
        }
    }
    public string? SelectCaptures(IEnumerable<string> paths)
    {
        int rejected = 0;
        foreach (string path in paths.Take(100))
        {
            try
            {
                if (!IsCapturePath(path) || !File.Exists(path)) { rejected++; continue; }
                string full = Path.GetFullPath(path);
                _captures.RemoveAll(x => string.Equals(x.Path, full, StringComparison.OrdinalIgnoreCase));
                _captures.Insert(0, new(Guid.NewGuid().ToString("N"), full, DateTimeOffset.UtcNow));
                if (_captures.Count > MaxCaptures) _captures.RemoveAt(_captures.Count - 1);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { rejected++; }
        }
        Persist();
        return rejected > 0 ? "Some selections were unavailable or were not supported image files." : _store.LastError;
    }
    public void RemoveCapture(string id) { _captures.RemoveAll(x => x.Id == id); Persist(); }
    public void ClearCaptures() { _captures.Clear(); _store.DeleteState("captures.json"); }
    private void Persist() => _store.WriteState("captures.json", new CaptureState { Items = _captures.ToList() });
    public static bool IsCapturePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 32768 || !Path.IsPathFullyQualified(path)) return false;
        return Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";
    }
    public static string? LaunchWorkspace(WorkspacePreset preset, Func<LauncherEntry, string?> launch)
    {
        if (preset.Targets.Count == 0) return "Add apps, folders, or websites to this workspace in Settings.";
        var errors = new List<string>();
        foreach (var target in preset.Targets.Take(20))
        {
            try { if (launch(target) is string error) errors.Add(target.Name + ": " + error); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { errors.Add(target.Name + ": Windows could not open this target."); }
        }
        return errors.Count == 0 ? null : $"{errors.Count} workspace target(s) could not open.\n" + string.Join("\n", errors);
    }
    public sealed class CaptureState
    {
        public int SchemaVersion { get; set; } = 1;
        public List<CaptureReference> Items { get; set; } = new();
    }
}

public static class WindowsTools
{
    public static string? Open(string uri)
    {
        if (uri is not ("ms-settings:sound" or "ms-settings:network-wifi" or "ms-settings:bluetooth" or "ms-screenclip:"))
            return "This Windows tool is not supported.";
        try { using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); return null; }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        { return uri == "ms-screenclip:" ? "Windows could not open screen capture. Press Win+Shift+S to use the screenshot tool." : "Windows could not open these settings. Open Windows Settings from Start."; }
    }
}
