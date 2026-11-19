using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Crest.Core;
using Microsoft.Win32;
using Screen = System.Windows.Forms.Screen;

namespace Crest.Platform;

/// <summary>Fail-closed desktop eligibility. This service never creates a hit-test window.</summary>
public sealed class DesktopPolicy : IDisposable
{
    private readonly Func<AppSettings> _settings;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<IntPtr, bool> _surfaces = new();
    private readonly Dictionary<IntPtr, Func<Rectangle>> _pointerBounds = new();
    private readonly Dictionary<IntPtr, Func<Point, bool>> _pointerContains = new();
    private readonly HashSet<IntPtr> _desktopRoots = new();
    private readonly List<IntPtr> _hooks = new();
    private readonly NativeMethods.WinEventProc _hookCallback;
    private readonly DispatcherTimer _reconcile;
    private IVirtualDesktopManager? _virtualDesktop;
    private bool _locked, _suspended, _started, _disposed, _queued, _monitoringPaused;
    private long _lastShellScan;
    private uint _shellProcess;
    private bool _visibilitySettingsRead, _lastAllowOverApps, _lastStrictDesktop;
    public bool IsEligible
    {
        get; private set;
    }
    public Rectangle DisplayBounds
    {
        get; private set;
    }
    public double DpiScale { get; private set; } = 1;
    public event EventHandler? Changed;

    public DesktopPolicy(Func<AppSettings> settings)
    {
        _settings = settings;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _hookCallback = OnWindowEvent;
        _reconcile = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _reconcile.Tick += (_, _) => Recheck();
        RefreshDisplay();
    }

    public void Start()
    {
        if (_started || _disposed)
            return;
        _started = true;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"));
            if (type != null)
                _virtualDesktop = Activator.CreateInstance(type) as IVirtualDesktopManager;
        }
        catch { _virtualDesktop = null; }
        InstallHooks();
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Recheck();
        _reconcile.Start();
    }

    public void RegisterSurface(IntPtr hwnd, bool acceptsInput = false, Func<Rectangle>? pointerBounds = null, Func<Point, bool>? pointerContains = null)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
            return;
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint owner);
        if (owner != (uint)Environment.ProcessId)
            return;
        _surfaces[hwnd] = acceptsInput;
        if (pointerBounds != null)
            _pointerBounds[hwnd] = pointerBounds;
        if (pointerContains != null)
            _pointerContains[hwnd] = pointerContains;
        Recheck();
    }

    public void UnregisterSurface(IntPtr hwnd)
    {
        _surfaces.Remove(hwnd);
        _pointerBounds.Remove(hwnd);
        _pointerContains.Remove(hwnd);
        Recheck();
    }

    public void SetMonitoringPaused(bool paused)
    {
        if (_disposed || _monitoringPaused == paused)
            return;
        _monitoringPaused = paused;
        if (paused)
        {
            _reconcile.Stop();
            foreach (var hook in _hooks)
                NativeMethods.UnhookWinEvent(hook);
            _hooks.Clear();
            SetEligibility(false, true);
        }
        else if (_started)
        {
            InstallHooks();
            _lastShellScan = 0;
            Recheck();
            _reconcile.Start();
        }
    }

    public bool IsRegisteredPointerSurface(Point pointer)
    {
        foreach (var item in _surfaces)
        {
            if (!NativeMethods.IsWindowVisible(item.Key))
                continue;
            if (_pointerContains.TryGetValue(item.Key, out var contains))
            {
                try
                {
                    if (contains(pointer))
                        return true;
                }
                catch { }
            }
            else if (_pointerBounds.TryGetValue(item.Key, out var provider))
            {
                try
                {
                    if (provider().Contains(pointer))
                        return true;
                }
                catch { }
            }
            else if (NativeMethods.GetWindowRect(item.Key, out var bounds) && bounds.ToRectangle().Contains(pointer))
                return true;
        }
        return false;
    }

    public void Recheck()
    {
        if (_disposed)
            return;
        if (!_dispatcher.CheckAccess())
        {
            QueueRecheck();
            return;
        }
        _queued = false;
        bool eligible = false;
        Rectangle oldBounds = DisplayBounds;
        double oldDpi = DpiScale;
        try
        {
            var settings = _settings();
            if (_visibilitySettingsRead && (_lastAllowOverApps != settings.AllowOverApps || _lastStrictDesktop != settings.StrictDesktop))
                SetEligibility(false, true); // Cancel old dwell/motion and require a fresh entry in the new mode.
            _visibilitySettingsRead = true;
            _lastAllowOverApps = settings.AllowOverApps;
            _lastStrictDesktop = settings.StrictDesktop;
            PruneInvalidSurfaces();
            RefreshDisplay();
            var foreground = NativeMethods.GetForegroundWindow();
            if (_started && !_monitoringPaused && !_locked && !_suspended && foreground != IntPtr.Zero && NativeMethods.IsNormalInputDesktop() && DisplayBounds.Width > 0 && _hooks.Count == 5)
            {
                IntPtr root = NativeMethods.GetAncestor(foreground, 2);
                if (root == IntPtr.Zero)
                    root = foreground;
                uint previousShell = _shellProcess;
                RefreshShellIdentities();
                if (previousShell != 0 && previousShell != _shellProcess)
                    SetEligibility(false, true);
                bool inputSurface = _surfaces.TryGetValue(root, out bool acceptsInput) && acceptsInput && NativeMethods.IsWindowVisible(root);
                bool actualDesktop = _desktopRoots.Contains(root) && NativeMethods.IsWindow(root);
                uint foregroundThread = NativeMethods.GetWindowThreadProcessId(root, out uint foregroundOwner);
                bool otherApp = foregroundThread != 0 && foregroundOwner != 0 && foregroundOwner != (uint)Environment.ProcessId
                    && NativeMethods.IsWindow(root) && NativeMethods.IsWindowVisible(root)
                    && !NativeMethods.IsIconic(root) && !NativeMethods.IsCloaked(root);
                // Unregistered own windows (Settings and tray dialogs) are never overlay targets.
                bool coveringApp = !settings.AllowOverApps && settings.StrictDesktop && (actualDesktop || inputSurface) && HasCoveringApp();
                eligible = VisibilityRules.Allows(settings, new(true, actualDesktop, inputSurface, otherApp, coveringApp));
            }
        }
        catch { eligible = false; }
        bool geometryChanged = oldBounds != DisplayBounds || Math.Abs(oldDpi - DpiScale) > 0.001;
        if (geometryChanged && IsEligible)
            SetEligibility(false, true);
        SetEligibility(eligible, geometryChanged);
    }

    private void SetEligibility(bool value, bool force = false)
    {
        if (value == IsEligible && !force)
            return;
        IsEligible = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void PruneInvalidSurfaces()
    {
        var invalid = new List<IntPtr>();
        foreach (var surface in _surfaces)
        {
            NativeMethods.GetWindowThreadProcessId(surface.Key, out uint owner);
            if (!NativeMethods.IsWindow(surface.Key) || owner != (uint)Environment.ProcessId)
                invalid.Add(surface.Key);
        }
        foreach (var hwnd in invalid)
        {
            _surfaces.Remove(hwnd);
            _pointerBounds.Remove(hwnd);
            _pointerContains.Remove(hwnd);
        }
    }

    private void RefreshDisplay()
    {
        var screens = Screen.AllScreens;
        if (screens.Length == 0)
        {
            DisplayBounds = Rectangle.Empty;
            return;
        }
        var display = Array.Find(screens, screen => screen.DeviceName == _settings().SelectedDisplay) ?? Screen.PrimaryScreen ?? screens[0];
        DisplayBounds = display.Bounds;
        var point = new NativeMethods.POINT { X = display.Bounds.Left + display.Bounds.Width / 2, Y = display.Bounds.Top + display.Bounds.Height / 2 };
        var monitor = NativeMethods.MonitorFromPoint(point, 2);
        if (monitor != IntPtr.Zero && NativeMethods.GetDpiForMonitor(monitor, 0, out uint x, out _) == 0 && x >= 96)
            DpiScale = x / 96.0;
        else
        {
            DisplayBounds = Rectangle.Empty;
            DpiScale = 1;
        } // Uncertain scaling must not arm a misplaced trigger.
    }

    private void RefreshShellIdentities()
    {
        IntPtr shell = NativeMethods.GetShellWindow();
        NativeMethods.GetWindowThreadProcessId(shell, out uint process);
        if (shell == IntPtr.Zero || process == 0)
        {
            _desktopRoots.Clear();
            _shellProcess = 0;
            return;
        }
        long now = Environment.TickCount64;
        if (_shellProcess == process && now - _lastShellScan < 1000 && _desktopRoots.Count > 0)
            return;
        _lastShellScan = now;
        _shellProcess = process;
        _desktopRoots.Clear();
        NativeMethods.EnumWindows((window, _) =>
        {
            string name = NativeMethods.ClassName(window);
            if (name != "Progman" && name != "WorkerW")
                return true;
            NativeMethods.GetWindowThreadProcessId(window, out uint owner);
            if (owner != process)
                return true;
            bool hasDesktopView = false;
            NativeMethods.EnumChildWindows(window, (child, _) =>
            {
                if (NativeMethods.ClassName(child) == "SHELLDLL_DefView")
                    hasDesktopView = true;
                return true;
            }, IntPtr.Zero);
            if (hasDesktopView)
                _desktopRoots.Add(window);
            return true;
        }, IntPtr.Zero);
        // GetShellWindow is the real shell API, never a process-name-only guess.
        if (_desktopRoots.Count > 0 && NativeMethods.ClassName(shell) == "Progman")
            _desktopRoots.Add(shell);
    }

    private bool HasCoveringApp()
    {
        if (_virtualDesktop == null || _desktopRoots.Count == 0)
            return true;
        bool blocked = false;
        bool enumerationSucceeded = NativeMethods.EnumWindows((window, _) =>
        {
            if (_surfaces.ContainsKey(window) || _desktopRoots.Contains(window) || !NativeMethods.IsWindowVisible(window) || NativeMethods.IsIconic(window) || NativeMethods.IsCloaked(window))
                return true;
            string name = NativeMethods.ClassName(window);
            // Shell wallpaper hosts and real taskbars are structural surfaces, not app content.
            if (name == "Shell_TrayWnd" || name == "Shell_SecondaryTrayWnd")
                return true;
            if (name == "SysDragImage")
            {
                NativeMethods.GetWindowThreadProcessId(window, out uint owner);
                // Explorer's OLE drag thumbnail is a desktop adornment. Explorer foreground
                // is still independently rejected, so this cannot summon Crest from a folder.
                if (owner == _shellProcess)
                    return true;
            }
            if (name == "Progman" || name == "WorkerW")
            {
                NativeMethods.GetWindowThreadProcessId(window, out uint owner);
                if (owner == _shellProcess)
                    return true;
            }
            if (!NativeMethods.GetWindowRect(window, out var rect))
            {
                blocked = true;
                return false;
            }
            var bounds = rect.ToRectangle();
            if (bounds.Width <= 1 || bounds.Height <= 1 || !bounds.IntersectsWith(DisplayBounds))
                return true;
            try
            {
                int result = _virtualDesktop.IsWindowOnCurrentVirtualDesktop(window, out bool current);
                if (result < 0)
                {
                    blocked = true;
                    return false;
                }
                if (!current)
                    return true;
            }
            catch { blocked = true; return false; }
            blocked = true;
            return false;
        }, IntPtr.Zero);
        return blocked || !enumerationSucceeded;
    }

    private void AddHook(uint minimum, uint maximum)
    {
        var handle = NativeMethods.SetWinEventHook(minimum, maximum, IntPtr.Zero, _hookCallback, 0, 0, 0);
        if (handle != IntPtr.Zero)
            _hooks.Add(handle);
    }

    private void InstallHooks()
    {
        AddHook(3, 3); // Foreground changes, including own Settings window.
        AddHook(16, 17); // Minimize transitions.
        AddHook(0x20, 0x20); // Input desktop switches, including secure desktop transitions.
        AddHook(0x8001, 0x8003); // Destroy/show/hide; shell destruction must invalidate cached identity.
        AddHook(0x800B, 0x800B); // Window position and coverage changes.
    }

    private void OnWindowEvent(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId, uint threadId, uint time)
    {
        if (_disposed || _monitoringPaused || (eventType >= 0x8000 && (objectId != 0 || childId != 0)))
            return;
        if (eventType == 0x20 || (eventType == 0x8001 && _desktopRoots.Contains(hwnd)))
        {
            _lastShellScan = 0;
            InvalidateAndRecheck();
            return;
        }
        QueueRecheck();
    }

    private void QueueRecheck()
    {
        if (_disposed || _dispatcher.HasShutdownStarted || _queued)
            return;
        _queued = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(Recheck));
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.SessionLogoff || e.Reason == SessionSwitchReason.RemoteDisconnect || e.Reason == SessionSwitchReason.ConsoleDisconnect)
            _locked = true;
        if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.SessionLogon || e.Reason == SessionSwitchReason.ConsoleConnect || e.Reason == SessionSwitchReason.RemoteConnect)
            _locked = false;
        InvalidateAndRecheck();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
            _suspended = true;
        if (e.Mode == PowerModes.Resume)
        {
            _suspended = false;
            _lastShellScan = 0;
        }
        if (e.Mode == PowerModes.Suspend || e.Mode == PowerModes.Resume)
            InvalidateAndRecheck();
        else
            QueueRecheck();
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        InvalidateAndRecheck();
    }

    private void InvalidateAndRecheck()
    {
        if (_disposed || _dispatcher.HasShutdownStarted)
            return;
        _dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
        {
            if (_disposed)
                return;
            SetEligibility(false, true);
            Recheck();
        }));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _reconcile.Stop();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        foreach (var hook in _hooks)
            NativeMethods.UnhookWinEvent(hook);
        _hooks.Clear();
        _surfaces.Clear();
        _pointerBounds.Clear();
        _pointerContains.Clear();
        if (_virtualDesktop != null && Marshal.IsComObject(_virtualDesktop))
            Marshal.ReleaseComObject(_virtualDesktop);
        _virtualDesktop = null;
        IsEligible = false;
    }
}
