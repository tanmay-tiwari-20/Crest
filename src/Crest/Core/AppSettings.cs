using System.Collections.Generic;
namespace Crest.Core;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string Theme { get; set; } = "System";
    public string Motion { get; set; } = "System";
    public bool StrictDesktop { get; set; } = true;
    public bool AllowOverApps { get; set; } = false;
    public string SelectedDisplay { get; set; } = "";
    public bool FullWidthTrigger
    {
        get; set;
    }
    public int TriggerWidth { get; set; } = 240;
    public int TriggerDepth { get; set; } = 3;
    public int DwellMs { get; set; } = 120;
    public int GraceMs { get; set; } = 250;
    public double MotionSpeed { get; set; } = 1;
    public double SizeScale { get; set; } = 1;
    public bool TimerSound
    {
        get; set;
    }
    public bool AccessibilityShortcut
    {
        get; set;
    }
    public bool StartWithWindows
    {
        get; set;
    }
    public bool OnboardingComplete
    {
        get; set;
    }
    public List<string> EnabledModules { get; set; } = new() { "Music", "Controls", "Timer", "Launch", "Shelf", "Capture", "Workspaces", "Clipboard" };
    public List<string> ModuleOrder { get; set; } = new(ModuleCatalog.All);
    public string DoubleClickGesture { get; set; } = "Expand";
    public string MiddleClickGesture { get; set; } = "Dismiss";
    public string WheelGesture { get; set; } = "None";
    public bool ClipboardHistoryEnabled { get; set; }
    public int ClipboardRetentionDays { get; set; } = 7;
    public List<WorkspacePreset> WorkspacePresets { get; set; } = new();
    public Dictionary<string, DisplayPreferences> Displays { get; set; } = new();
    public List<LauncherEntry> Launchers { get; set; } = new();
    public string PreferredPlayer { get; set; } = "";

    public AppSettings ForDisplay(string deviceName)
    {
        if (!Displays.TryGetValue(deviceName, out var display)) return this;
        var result = (AppSettings)MemberwiseClone();
        result.Theme = display.Theme; result.Motion = display.Motion;
        result.SizeScale = display.SizeScale; result.MotionSpeed = display.MotionSpeed;
        result.FullWidthTrigger = display.FullWidthTrigger;
        result.TriggerWidth = display.TriggerWidth; result.TriggerDepth = display.TriggerDepth;
        result.DwellMs = display.DwellMs; result.GraceMs = display.GraceMs;
        return result;
    }

    public DisplayPreferences CustomizeDisplay(string deviceName)
    {
        if (!Displays.TryGetValue(deviceName, out var display))
            Displays[deviceName] = display = new DisplayPreferences
            {
                Theme = Theme, Motion = Motion, SizeScale = SizeScale, MotionSpeed = MotionSpeed,
                FullWidthTrigger = FullWidthTrigger, TriggerWidth = TriggerWidth, TriggerDepth = TriggerDepth,
                DwellMs = DwellMs, GraceMs = GraceMs
            };
        return display;
    }
}

public static class ModuleCatalog
{
    public static readonly string[] All = { "Music", "Controls", "Timer", "Launch", "Shelf", "Capture", "Workspaces", "Clipboard" };
    public static readonly string[] Themes = { "System", "Dark", "Light", "Ocean", "Forest", "Rose" };
    public static readonly string[] Gestures = { "None", "Expand", "Collapse", "Dismiss", "Next module", "Previous module", "Settings" };
}
public sealed class DisplayPreferences
{
    public string Theme { get; set; } = "System";
    public string Motion { get; set; } = "System";
    public double SizeScale { get; set; } = 1;
    public double MotionSpeed { get; set; } = 1;
    public bool FullWidthTrigger { get; set; }
    public int TriggerWidth { get; set; } = 240;
    public int TriggerDepth { get; set; } = 3;
    public int DwellMs { get; set; } = 120;
    public int GraceMs { get; set; } = 250;
}
public sealed class WorkspacePreset
{
    public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Workspace";
    public List<LauncherEntry> Targets { get; set; } = new();
}
public sealed class LauncherEntry
{
    public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public string Kind { get; set; } = "App";
}
