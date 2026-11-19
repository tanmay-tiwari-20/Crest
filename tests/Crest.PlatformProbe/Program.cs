using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Crest.Core;
using Crest.Platform;

public static class Program
{
    private delegate bool EnumProc(IntPtr window, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left,Top,Right,Bottom; public Rectangle Bounds => Rectangle.FromLTRB(Left,Top,Right,Bottom); }
    [StructLayout(LayoutKind.Sequential)] private struct PointNative { public int X,Y; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window,uint flags);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback,IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent,EnumProc callback,IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window,out Rect rectangle);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointNative point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window,StringBuilder text,int maximum);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window,StringBuilder text,int maximum);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
    [DllImport("user32.dll")] private static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool GetUserObjectInformation(IntPtr handle,int index,StringBuilder data,uint size,out uint needed);
    private static readonly Dictionary<uint,string> ProcessNames = new();
    private static string ClassName(IntPtr window) { var value=new StringBuilder(256);GetClassName(window,value,value.Capacity);return value.ToString(); }
    private static string OwnTitle(IntPtr window,uint owner,int appPid) { if(owner!=(uint)appPid)return "";var value=new StringBuilder(128);GetWindowText(window,value,value.Capacity);return value.ToString(); }
    private static string OwnerName(uint owner) { if(ProcessNames.TryGetValue(owner,out string? cached))return cached;try { return ProcessNames[owner]=Process.GetProcessById((int)owner).ProcessName; }catch {return ProcessNames[owner]="unavailable";} }
    private static object? Field(DesktopPolicy policy,string name)=>typeof(DesktopPolicy).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(policy);
    private static string Handle(IntPtr window)=>"0x"+window.ToInt64().ToString("X");
    private static object Bounds(Rectangle rectangle)=>new {rectangle.X,rectangle.Y,rectangle.Width,rectangle.Height};
    private static string InputDesktopName() { IntPtr desktop=OpenInputDesktop(0,false,1);if(desktop==IntPtr.Zero)return "unavailable";try {var value=new StringBuilder(128);return GetUserObjectInformation(desktop,2,value,256,out _)?value.ToString():"unavailable";}finally {CloseDesktop(desktop);} }

    private sealed record WindowInfo(string Handle,string Class,uint OwnerPid,string OwnerProcess,string CrestTitle,bool Visible,bool Minimized,object? Rect,int CloakedResult,int Cloaked,string ExtendedStyle,bool? CurrentVirtualDesktop,int? VirtualDesktopResult,bool IntersectsDisplay,string CoverageReason,bool WouldBlockStrict);
    private static WindowInfo Describe(IntPtr window,int appPid,uint shellPid,HashSet<IntPtr> desktopRoots,IProbeVirtualDesktopManager? virtualDesktop,Rectangle display)
    {
        GetWindowThreadProcessId(window,out uint owner);
        string name=ClassName(window),title=OwnTitle(window,owner,appPid);
        bool visible=IsWindowVisible(window),minimized=IsIconic(window),rectValid=GetWindowRect(window,out var rectangle);
        int cloakedResult=DwmGetWindowAttribute(window,14,out int cloaked,4);
        bool? current=null;int? vdResult=null;
        if(virtualDesktop!=null)try {int result=virtualDesktop.IsWindowOnCurrentVirtualDesktop(window,out bool onCurrent);vdResult=result;if(result>=0)current=onCurrent;}catch(Exception error){vdResult=Marshal.GetHRForException(error);}
        bool intersects=rectValid&&rectangle.Bounds.IntersectsWith(display);
        string reason;
        if(!visible)reason="hidden";
        else if(minimized)reason="minimized";
        else if(cloakedResult==0&&cloaked!=0)reason="DWM-cloaked";
        else if(desktopRoots.Contains(window))reason="verified-desktop-surface";
        else if(name is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")reason="taskbar-structural-surface";
        else if(owner==shellPid&&(name is "Progman" or "WorkerW" or "SysDragImage"))reason="Explorer-structural-surface";
        else if(owner==(uint)appPid&&title=="Crest island")reason="Crest-island-assumed-registered";
        else if(!rectValid)reason="window-rectangle-query-failed";
        else if(rectangle.Bounds.Width<=1||rectangle.Bounds.Height<=1)reason="trivial-rectangle";
        else if(!intersects)reason="outside-selected-display";
        else if(virtualDesktop==null)reason="virtual-desktop-manager-unavailable";
        else if(vdResult<0||current==null)reason="virtual-desktop-query-failed";
        else if(current==false)reason="inactive-virtual-desktop";
        else reason="visible-window-intersects-selected-display";
        bool blocks=reason is "window-rectangle-query-failed" or "virtual-desktop-manager-unavailable" or "virtual-desktop-query-failed" or "visible-window-intersects-selected-display";
        return new(Handle(window),name,owner,OwnerName(owner),title,visible,minimized,rectValid?Bounds(rectangle.Bounds):null,cloakedResult,cloaked,"0x"+unchecked((uint)GetWindowLong(window,-20)).ToString("X"),current,vdResult,intersects,reason,blocks);
    }

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--geometry-self-test")) { GeometrySelfTest(); return; }
        int appPid=args.Length>0&&int.TryParse(args[0],out int selected)?selected:0;
        var settings=new AppSettings();
        string settingsRead="safe-defaults";
        int settingsIndex=Array.IndexOf(args,"--settings-file");
        if(settingsIndex>=0&&(settingsIndex+1>=args.Length||string.IsNullOrWhiteSpace(args[settingsIndex+1])))
            throw new ArgumentException("--settings-file requires an explicit settings file.");
        string settingsFile=settingsIndex>=0?Path.GetFullPath(args[settingsIndex+1]):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Crest","settings.json");
        string appDllHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(DesktopPolicy).Assembly.Location)));
        try
        {
            string file=settingsFile;
            if(settingsIndex>=0&&(!File.Exists(file)||new FileInfo(file).Length>1024*1024))
                throw new IOException("The explicit settings file is absent or exceeds the limit.");
            if(File.Exists(file)&&new FileInfo(file).Length<=1024*1024)
            {
                settings = ReadSettingsJson(File.ReadAllText(file));
                settingsRead=settingsIndex>=0?"read-only-explicit-settings":"read-only-selected-settings";
            }
        }
        catch when(settingsIndex<0) { settings = new AppSettings(); settingsRead="settings-unreadable-safe-defaults"; }
        // Diagnostic overrides are in-memory only; never write the user's selected mode.
        if(args.Contains("--allow-over-apps"))settings.AllowOverApps=true;
        if(args.Contains("--desktop-only"))settings.AllowOverApps=false;
        using var policy=new DesktopPolicy(()=>settings);policy.Start();
        if(args.Contains("--compare-access"))
        {
            var scene = GetForegroundWindow();
            GetWindowThreadProcessId(scene, out uint foregroundOwner);
            var accessResults = new List<object>();
            foreach(bool allow in new[] { false, true, false })
            {
                settings.AllowOverApps=allow;
                policy.Recheck();
                accessResults.Add(new { AllowOverApps=allow, Eligible=policy.IsEligible });
            }
            bool stableScene=scene==GetForegroundWindow();
            Console.WriteLine(JsonSerializer.Serialize(new {
                TimestampUtc=DateTimeOffset.UtcNow,
                AppDllSHA256=appDllHash, SettingsRead=settingsRead,
                ForegroundClass=ClassName(scene), ForegroundOwner=foregroundOwner,
                StableForeground=stableScene, InputDesktop=InputDesktopName(), policy.DpiScale,
                policy.DisplayDeviceName, EffectiveTriggerSettings = TriggerSettings(settings.ForDisplay(policy.DisplayDeviceName)),
                NativeHookCount=(Field(policy,"_hooks") as System.Collections.ICollection)?.Count,
                Results=accessResults,
                Limitations="Read-only comparison of a separate native policy with in-memory preferences. The user's preferences and foreground windows are untouched. Pointer/hotkey input and fullscreen presentation are separate acceptance checks."
            },new JsonSerializerOptions{WriteIndented=true}));
            return;
        }
        if (args.Contains("--watch")) { Watch(policy, settings, appPid, 20, appDllHash, settingsRead); return; }
        bool currentEligible=policy.IsEligible;
        var desktopRoots=Field(policy,"_desktopRoots") as HashSet<IntPtr>??new();
        uint shellPid=Field(policy,"_shellProcess") is uint pid?pid:0;
        IProbeVirtualDesktopManager? virtualDesktop=null;
        try {virtualDesktop=Field(policy,"_virtualDesktop") as IProbeVirtualDesktopManager;}catch{}
        IntPtr foreground=GetForegroundWindow(),foregroundRoot=GetAncestor(foreground,2);
        if(foregroundRoot==IntPtr.Zero)foregroundRoot=foreground;
        var candidates=new List<object>();
        var windows=new List<WindowInfo>();
        EnumWindows((window,_)=>
        {
            string name=ClassName(window);
            if(name is "Progman" or "WorkerW")
            {
                var children=new List<object>();
                EnumChildWindows(window,(child,_)=>{if(children.Count<128)children.Add(new {Handle=Handle(child),Class=ClassName(child),Visible=IsWindowVisible(child)});return true;},IntPtr.Zero);
                GetWindowThreadProcessId(window,out uint owner);
                candidates.Add(new {Handle=Handle(window),Class=name,OwnerPid=owner,OwnerProcess=OwnerName(owner),Verified=desktopRoots.Contains(window),Children=children});
            }
            var info=Describe(window,appPid,shellPid,desktopRoots,virtualDesktop,policy.DisplayBounds);
            if(info.Visible||info.OwnerPid==(uint)appPid||info.OwnerProcess.Contains("codex",StringComparison.OrdinalIgnoreCase)||info.OwnerProcess.Contains("sky",StringComparison.OrdinalIgnoreCase)||name is "Progman" or "WorkerW")windows.Add(info);
            return true;
        },IntPtr.Zero);
        var foregroundInfo=Describe(foregroundRoot,appPid,shellPid,desktopRoots,virtualDesktop,policy.DisplayBounds);
        string inputDesktop=InputDesktopName();
        string reason=currentEligible&&settings.AllowOverApps?"eligible-app-access":foreground==IntPtr.Zero?"foreground-unavailable":inputDesktop!="Default"?"input-desktop-not-default":policy.DisplayBounds.Width<=0?"display-or-DPI-unavailable":desktopRoots.Count==0?"verified-desktop-shell-not-found":!desktopRoots.Contains(foregroundRoot)?"foreground-is-not-actual-desktop":settings.StrictDesktop&&windows.Any(window=>window.WouldBlockStrict)?"strict-visible-window-coverage":virtualDesktop==null&&settings.StrictDesktop?"virtual-desktop-manager-unavailable":currentEligible?"eligible":"other-fail-closed-condition";
        GetCursorPos(out var pointer);
        var effectiveSettings = settings.ForDisplay(policy.DisplayDeviceName);
        var trigger = GetTrigger(effectiveSettings, policy.DisplayBounds, policy.DpiScale);
        var recheckTimes=new List<double>();for(int i=0;i<100;i++){var clock=Stopwatch.StartNew();policy.Recheck();clock.Stop();recheckTimes.Add(clock.Elapsed.TotalMilliseconds);}recheckTimes.Sort();
        Console.WriteLine(JsonSerializer.Serialize(new {
            TimestampUtc=DateTimeOffset.UtcNow,AppDllSHA256=appDllHash,SelectedCrestProcessId=appPid,SettingsRead=settingsRead,settings.AllowOverApps,settings.StrictDesktop,settings.SelectedDisplay,effectiveSettings.DwellMs,
            policy.DisplayDeviceName, EffectiveTriggerSettings = TriggerSettings(effectiveSettings),
            CurrentForegroundEligible=currentEligible,DiagnosticReason=reason,InputDesktop=inputDesktop,ForegroundHandle=Handle(foreground),ForegroundRoot=foregroundInfo,
            ShellHandle=Handle(GetShellWindow()),ShellPid=shellPid,VerifiedDesktopRoots=desktopRoots.Select(Handle),DesktopCandidates=candidates,VirtualDesktopManagerAvailable=virtualDesktop!=null,
            NativeHookCount=(Field(policy,"_hooks") as System.Collections.ICollection)?.Count,
            Screen=Bounds(policy.DisplayBounds),policy.DpiScale,Trigger=Bounds(trigger),Pointer=new {pointer.X,pointer.Y,InsideTrigger=trigger.Contains(pointer.X,pointer.Y)},
            StrictBlockers=windows.Where(window=>window.WouldBlockStrict),CrestWindows=windows.Where(window=>window.OwnerPid==(uint)appPid),RelevantTopLevelWindows=windows,
            SynchronousRecheck100Calls=new {P95Milliseconds=recheckTimes[94],P99Milliseconds=recheckTimes[98],MaximumMilliseconds=recheckTimes[99]},
            Limitations="Read-only native snapshot from a separate diagnostic process. Product interaction HWND registration is inferred only for a window titled Crest island; actual product private state is not read. Other-window titles and full paths are not recorded. No user windows or product policy are changed. Recheck timings exclude OS event delivery/dispatcher delay."
        },new JsonSerializerOptions{WriteIndented=true}));
    }

    private static void Watch(DesktopPolicy policy, AppSettings settings, int appPid, int seconds, string appDllHash, string settingsRead)
    {
        var samples = new List<object>();
        var clock = Stopwatch.StartNew();
        do
        {
            policy.Recheck();
            var roots = Field(policy,"_desktopRoots") as HashSet<IntPtr> ?? new();
            uint shellPid = Field(policy,"_shellProcess") is uint id ? id : 0;
            IProbeVirtualDesktopManager? vd = null;
            try { vd = Field(policy,"_virtualDesktop") as IProbeVirtualDesktopManager; } catch { }
            IntPtr foreground = GetForegroundWindow(), root = GetAncestor(foreground,2);
            if (root == IntPtr.Zero) root = foreground;
            var blockers = new List<WindowInfo>();
            var crestWindows = new List<WindowInfo>();
            EnumWindows((window,_) =>
            {
                var info = Describe(window,appPid,shellPid,roots,vd,policy.DisplayBounds);
                if (info.WouldBlockStrict) blockers.Add(info);
                if (info.OwnerPid == (uint)appPid) crestWindows.Add(info);
                return true;
            },IntPtr.Zero);
            string inputDesktop = InputDesktopName();
            string reason = policy.IsEligible && settings.AllowOverApps ? "eligible-app-access" : foreground == IntPtr.Zero ? "foreground-unavailable" : inputDesktop != "Default" ? "input-desktop-not-default" : policy.DisplayBounds.Width <= 0 ? "display-or-DPI-unavailable" : roots.Count == 0 ? "verified-desktop-shell-not-found" : !roots.Contains(root) ? "foreground-is-not-actual-desktop" : settings.StrictDesktop && blockers.Count > 0 ? "strict-visible-window-coverage" : vd == null && settings.StrictDesktop ? "virtual-desktop-manager-unavailable" : policy.IsEligible ? "eligible" : "other-fail-closed-condition";
            GetCursorPos(out var pointer);
            var effectiveSettings = settings.ForDisplay(policy.DisplayDeviceName);
            var trigger = GetTrigger(effectiveSettings, policy.DisplayBounds, policy.DpiScale);
            samples.Add(new {
                TimestampUtc = DateTimeOffset.UtcNow,
                ElapsedSeconds = Math.Round(clock.Elapsed.TotalSeconds,3),
                Eligible = policy.IsEligible,
                Reason = reason,
                Foreground = Describe(root,appPid,shellPid,roots,vd,policy.DisplayBounds),
                DesktopRoots = roots.Select(Handle).ToArray(),
                VirtualDesktopManagerAvailable = vd != null,
                policy.DisplayDeviceName, policy.DpiScale,
                EffectiveTriggerSettings = TriggerSettings(effectiveSettings),
                Pointer = new {pointer.X,pointer.Y,InsideTrigger=trigger.Contains(pointer.X,pointer.Y)},
                Trigger = Bounds(trigger),
                Blockers = blockers,
                VisibleCrestWindows = crestWindows.Where(window=>window.Visible && !window.Minimized).ToArray()
            });
            System.Threading.Thread.Sleep(500);
        } while (clock.Elapsed.TotalSeconds < seconds);
        Console.WriteLine(JsonSerializer.Serialize(new {
            StartedUtc = DateTimeOffset.UtcNow.AddMilliseconds(-clock.Elapsed.TotalMilliseconds),
            AppDllSHA256 = appDllHash,
            SettingsRead = settingsRead,
            DurationSeconds = Math.Round(clock.Elapsed.TotalSeconds,3),
            settings.StrictDesktop,
            settings.AllowOverApps,
            policy.DpiScale,
            Samples = samples,
            Limitations = "Passive20-second native observations. No screenshots, input actions, window activation/minimization or product policy changes. This reads a separate diagnostic policy; it does not read the running product's controller fields."
        },new JsonSerializerOptions{WriteIndented=true}));
    }

    private static AppSettings ReadSettingsJson(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? throw new JsonException("Settings are empty.");
        if (settings.SchemaVersion != 1) throw new JsonException("Unsupported settings version.");
        settings.SelectedDisplay ??= "";
        NormalizeTriggerSettings(settings);
        // Match the product's bounded display profiles without using SettingsStore.Load,
        // which can rename corrupt files in the owner's profile.
        settings.Displays = (settings.Displays ?? new()).Where(x => !string.IsNullOrWhiteSpace(x.Key) && x.Key.Length <= 100 && x.Value != null)
            .Take(16).ToDictionary(x => x.Key, x => x.Value);
        foreach (var display in settings.Displays.Values)
        {
            display.TriggerWidth = Math.Clamp(display.TriggerWidth, 80, 800);
            display.TriggerDepth = Math.Clamp(display.TriggerDepth, 1, 12);
            display.DwellMs = Math.Clamp(display.DwellMs, 80, 500);
            display.GraceMs = Math.Clamp(display.GraceMs, 100, 750);
        }
        return settings;
    }

    private static void NormalizeTriggerSettings(AppSettings settings)
    {
        settings.TriggerWidth = Math.Clamp(settings.TriggerWidth, 80, 800);
        settings.TriggerDepth = Math.Clamp(settings.TriggerDepth, 1, 12);
        settings.DwellMs = Math.Clamp(settings.DwellMs, 80, 500);
        settings.GraceMs = Math.Clamp(settings.GraceMs, 100, 750);
    }

    private static object TriggerSettings(AppSettings settings) => new {
        settings.FullWidthTrigger, settings.TriggerWidth, settings.TriggerDepth, settings.DwellMs, settings.GraceMs
    };

    private static Rectangle GetTrigger(AppSettings effectiveSettings, Rectangle display, double dpiScale)
    {
        // Use the same DIP bounds, DPI rounding and display fit as InteractionController.
        int width = effectiveSettings.FullWidthTrigger ? display.Width : (int)Math.Round(Math.Clamp(effectiveSettings.TriggerWidth, 80, 800) * dpiScale);
        width = Math.Min(width, display.Width);
        int depth = Math.Max(1, (int)Math.Round(Math.Clamp(effectiveSettings.TriggerDepth, 1, 12) * dpiScale));
        return new Rectangle(display.Left + (display.Width - width) / 2, display.Top, width, depth);
    }

    private static void GeometrySelfTest()
    {
        // This path creates no policy, native hooks, windows, clipboard access or user files.
        var checks = new List<string>();
        void Check(bool success, string label) { if (!success) throw new InvalidOperationException(label); checks.Add(label); }
        var display = new Rectangle(0, 0, 1920, 1080);
        var settings = new AppSettings { TriggerWidth = 80 };
        Check(GetTrigger(settings, display, 1) == new Rectangle(920, 0, 80, 3), "Accepted 80-DIP trigger remains 80 pixels at 100% DPI");
        settings.TriggerWidth = 0;
        Check(GetTrigger(settings, display, 1).Width == 80, "Widths below 80 DIP clamp to the product minimum");
        settings.TriggerWidth = 1000;
        Check(GetTrigger(settings, display, 1).Width == 800, "Widths above 800 DIP clamp to the product maximum");
        settings.TriggerDepth = 0;
        Check(GetTrigger(settings, display, 1).Height == 1, "Trigger depth below one DIP remains a one-pixel region");
        settings.TriggerDepth = 20;
        Check(GetTrigger(settings, display, 1.25) == new Rectangle(460, 0, 1000, 15), "Maximum width and depth scale together at 125% DPI");
        settings.FullWidthTrigger = true;
        Check(GetTrigger(settings, new Rectangle(-1920, -120, 1920, 1080), 1.5) == new Rectangle(-1920, -120, 1920, 18), "Full-width triggers retain the physical display origin and width");
        settings.FullWidthTrigger = false;
        Check(GetTrigger(settings, new Rectangle(10, 20, 75, 80), 1).Width == 75, "Centered trigger fits a display narrower than the configured width");
        var parsed = ReadSettingsJson("""
            { "SelectedDisplay": "disconnected", "TriggerWidth": 80, "DwellMs": 120,
              "Displays": { "active": { "TriggerWidth": 320, "TriggerDepth": 8, "DwellMs": 234, "GraceMs": 321 } } }
            """);
        var effective = parsed.ForDisplay("active");
        Check(GetTrigger(effective, display, 1.5) == new Rectangle(720, 0, 480, 12)
            && effective.DwellMs == 234 && effective.GraceMs == 321 && parsed.TriggerWidth == 80,
            "Parsed profiles follow the actual active display and leave defaults independent");
        Check(GetTrigger(parsed.ForDisplay("unconfigured"), display, 1) == new Rectangle(920, 0, 80, 3)
            && parsed.ForDisplay("unconfigured").DwellMs == 120, "Unconfigured displays use default geometry and dwell");
        parsed.Displays["active"].FullWidthTrigger = true;
        Check(GetTrigger(parsed.ForDisplay("active"), display, 1.5).Width == display.Width
            && GetTrigger(parsed.ForDisplay("unconfigured"), display, 1).Width == 80, "Full-width display override does not alter default coverage");
        Console.WriteLine(JsonSerializer.Serialize(new {
            RecordedAtUtc = DateTimeOffset.UtcNow,
            AppDllSHA256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(AppSettings).Assembly.Location))),
            Passed = checks.Count, Checks = checks,
            Limitations = "Pure diagnostic parser and geometry regressions. No native product, display transition, pointer interaction or acceptance gate was exercised."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
[ComImport,Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IProbeVirtualDesktopManager
{
    [PreserveSig]int IsWindowOnCurrentVirtualDesktop(IntPtr window,[MarshalAs(UnmanagedType.Bool)]out bool current);
    [PreserveSig]int GetWindowDesktopId(IntPtr window,out Guid id);
    [PreserveSig]int MoveWindowToDesktop(IntPtr window,ref Guid id);
}
