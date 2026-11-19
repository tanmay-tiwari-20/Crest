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
    public List<string> EnabledModules { get; set; } = new() { "Music", "Controls", "Timer", "Launch", "Shelf" };
    public List<LauncherEntry> Launchers { get; set; } = new();
    public string PreferredPlayer { get; set; } = "";
}
public sealed class LauncherEntry
{
    public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public string Kind { get; set; } = "App";
}
