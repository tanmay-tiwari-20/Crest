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
        int appPid=args.Length>0&&int.TryParse(args[0],out int selected)?selected:0;
        var settings=new AppSettings();
        string settingsRead="safe-defaults";
        try
        {
            string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Crest","settings.json");
            if(File.Exists(file)&&new FileInfo(file).Length<=1024*1024)
            {
                using var document=JsonDocument.Parse(File.ReadAllText(file));var root=document.RootElement;
                if(root.TryGetProperty("StrictDesktop",out var strict))settings.StrictDesktop=strict.GetBoolean();
                if(root.TryGetProperty("SelectedDisplay",out var display))settings.SelectedDisplay=display.GetString()??"";
                if(root.TryGetProperty("TriggerWidth",out var width))settings.TriggerWidth=width.GetInt32();
                if(root.TryGetProperty("TriggerDepth",out var depth))settings.TriggerDepth=depth.GetInt32();
                if(root.TryGetProperty("FullWidthTrigger",out var full))settings.FullWidthTrigger=full.GetBoolean();
                if(root.TryGetProperty("DwellMs",out var dwell))settings.DwellMs=dwell.GetInt32();
                settingsRead="read-only-selected-settings";
            }
        }
        catch {settingsRead="settings-unreadable-safe-defaults";}
        using var policy=new DesktopPolicy(()=>settings);policy.Start();
        if (args.Contains("--watch")) { Watch(policy, settings, appPid, 20); return; }
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
        string reason=foreground==IntPtr.Zero?"foreground-unavailable":inputDesktop!="Default"?"input-desktop-not-default":policy.DisplayBounds.Width<=0?"display-or-DPI-unavailable":desktopRoots.Count==0?"verified-desktop-shell-not-found":!desktopRoots.Contains(foregroundRoot)?"foreground-is-not-actual-desktop":settings.StrictDesktop&&windows.Any(window=>window.WouldBlockStrict)?"strict-visible-window-coverage":virtualDesktop==null&&settings.StrictDesktop?"virtual-desktop-manager-unavailable":currentEligible?"eligible":"other-fail-closed-condition";
        GetCursorPos(out var pointer);
        int triggerWidth=settings.FullWidthTrigger?policy.DisplayBounds.Width:Math.Min(policy.DisplayBounds.Width,(int)Math.Round(Math.Clamp(settings.TriggerWidth,100,1000)*policy.DpiScale));
        var trigger=new Rectangle(policy.DisplayBounds.Left+(policy.DisplayBounds.Width-triggerWidth)/2,policy.DisplayBounds.Top,triggerWidth,Math.Max(1,(int)Math.Round(Math.Clamp(settings.TriggerDepth,1,12)*policy.DpiScale)));
        var recheckTimes=new List<double>();for(int i=0;i<100;i++){var clock=Stopwatch.StartNew();policy.Recheck();clock.Stop();recheckTimes.Add(clock.Elapsed.TotalMilliseconds);}recheckTimes.Sort();
        Console.WriteLine(JsonSerializer.Serialize(new {
            TimestampUtc=DateTimeOffset.UtcNow,SelectedCrestProcessId=appPid,SettingsRead=settingsRead,settings.StrictDesktop,settings.SelectedDisplay,settings.DwellMs,
            CurrentForegroundEligible=currentEligible,DiagnosticReason=reason,InputDesktop=inputDesktop,ForegroundHandle=Handle(foreground),ForegroundRoot=foregroundInfo,
            ShellHandle=Handle(GetShellWindow()),ShellPid=shellPid,VerifiedDesktopRoots=desktopRoots.Select(Handle),DesktopCandidates=candidates,VirtualDesktopManagerAvailable=virtualDesktop!=null,
            NativeHookCount=(Field(policy,"_hooks") as System.Collections.ICollection)?.Count,
            Screen=Bounds(policy.DisplayBounds),policy.DpiScale,Trigger=Bounds(trigger),Pointer=new {pointer.X,pointer.Y,InsideTrigger=trigger.Contains(pointer.X,pointer.Y)},
            StrictBlockers=windows.Where(window=>window.WouldBlockStrict),CrestWindows=windows.Where(window=>window.OwnerPid==(uint)appPid),RelevantTopLevelWindows=windows,
            SynchronousRecheck100Calls=new {P95Milliseconds=recheckTimes[94],P99Milliseconds=recheckTimes[98],MaximumMilliseconds=recheckTimes[99]},
            Limitations="Read-only native snapshot from a separate diagnostic process. Product interaction HWND registration is inferred only for a window titled Crest island; actual product private state is not read. Other-window titles and full paths are not recorded. No user windows or product policy are changed. Recheck timings exclude OS event delivery/dispatcher delay."
        },new JsonSerializerOptions{WriteIndented=true}));
    }

    private static void Watch(DesktopPolicy policy, AppSettings settings, int appPid, int seconds)
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
            string reason = foreground == IntPtr.Zero ? "foreground-unavailable" : inputDesktop != "Default" ? "input-desktop-not-default" : policy.DisplayBounds.Width <= 0 ? "display-or-DPI-unavailable" : roots.Count == 0 ? "verified-desktop-shell-not-found" : !roots.Contains(root) ? "foreground-is-not-actual-desktop" : settings.StrictDesktop && blockers.Count > 0 ? "strict-visible-window-coverage" : vd == null && settings.StrictDesktop ? "virtual-desktop-manager-unavailable" : policy.IsEligible ? "eligible" : "other-fail-closed-condition";
            GetCursorPos(out var pointer);
            int width = settings.FullWidthTrigger ? policy.DisplayBounds.Width : Math.Min(policy.DisplayBounds.Width,(int)Math.Round(Math.Clamp(settings.TriggerWidth,100,1000)*policy.DpiScale));
            var trigger = new Rectangle(policy.DisplayBounds.Left+(policy.DisplayBounds.Width-width)/2,policy.DisplayBounds.Top,width,Math.Max(1,(int)Math.Round(Math.Clamp(settings.TriggerDepth,1,12)*policy.DpiScale)));
            samples.Add(new {
                TimestampUtc = DateTimeOffset.UtcNow,
                ElapsedSeconds = Math.Round(clock.Elapsed.TotalSeconds,3),
                Eligible = policy.IsEligible,
                Reason = reason,
                Foreground = Describe(root,appPid,shellPid,roots,vd,policy.DisplayBounds),
                DesktopRoots = roots.Select(Handle).ToArray(),
                VirtualDesktopManagerAvailable = vd != null,
                Pointer = new {pointer.X,pointer.Y,InsideTrigger=trigger.Contains(pointer.X,pointer.Y)},
                Trigger = Bounds(trigger),
                Blockers = blockers,
                VisibleCrestWindows = crestWindows.Where(window=>window.Visible && !window.Minimized).ToArray()
            });
            System.Threading.Thread.Sleep(500);
        } while (clock.Elapsed.TotalSeconds < seconds);
        Console.WriteLine(JsonSerializer.Serialize(new {
            StartedUtc = DateTimeOffset.UtcNow.AddMilliseconds(-clock.Elapsed.TotalMilliseconds),
            DurationSeconds = Math.Round(clock.Elapsed.TotalSeconds,3),
            settings.StrictDesktop,
            policy.DpiScale,
            Samples = samples,
            Limitations = "Passive20-second native observations. No screenshots, input actions, window activation/minimization or product policy changes. This reads a separate diagnostic policy; it does not read the running product's controller fields."
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
[ComImport,Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IProbeVirtualDesktopManager
{
    [PreserveSig]int IsWindowOnCurrentVirtualDesktop(IntPtr window,[MarshalAs(UnmanagedType.Bool)]out bool current);
    [PreserveSig]int GetWindowDesktopId(IntPtr window,out Guid id);
    [PreserveSig]int MoveWindowToDesktop(IntPtr window,ref Guid id);
}
