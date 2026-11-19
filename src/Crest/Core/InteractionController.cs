using System;
using System.Drawing;
using System.Windows.Interop;
using System.Windows.Threading;
using Crest.Platform;

namespace Crest.Core;

/// <summary>The only OS-facing reveal controller. There is no hidden overlay or mouse hook.</summary>
public sealed class InteractionController : IDisposable
{
    private readonly DesktopPolicy _policy;
    private readonly Func<AppSettings> _settings;
    private readonly Func<Rectangle> _surfaceBounds;
    private readonly Func<bool> _hasKeyboardFocus;
    private readonly Func<Point, bool>? _surfaceContains;
    private readonly DispatcherTimer _pointerTimer;
    private readonly InteractionStateMachine _machine = new();
    private HwndSource? _hotKeySource;
    private bool _paused, _started, _disposed, _shortcutRegistered;
    private const int ShortcutId = 0x4352;
    public IslandState State => _machine.State;
    public long TransitionVersion => _machine.TransitionVersion;
    public event EventHandler? RevealRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler? SuppressRequested;
    public event EventHandler? StateChanged;
    public event EventHandler? KeyboardFocusRequested;

    public InteractionController(DesktopPolicy policy, Func<AppSettings> settings, Func<Rectangle> surfaceBounds, Func<bool> hasKeyboardFocus, Func<Point, bool>? surfaceContains = null)
    {
        _policy = policy;
        _settings = settings;
        _surfaceBounds = surfaceBounds;
        _hasKeyboardFocus = hasKeyboardFocus;
        _surfaceContains = surfaceContains;
        _pointerTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(30) };
        _pointerTimer.Tick += (_, _) => Tick();
        _policy.Changed += OnEligibilityChanged;
        _machine.RevealRequested += (_, e) => RevealRequested?.Invoke(this, e);
        _machine.CloseRequested += (_, e) => CloseRequested?.Invoke(this, e);
        _machine.SuppressRequested += (_, e) => SuppressRequested?.Invoke(this, e);
        _machine.StateChanged += (_, e) => StateChanged?.Invoke(this, e);
    }

    public void Start()
    {
        if (_started || _disposed)
            return;
        _started = true;
        _policy.Start();
        OnEligibilityChanged(this, EventArgs.Empty);
    }

    public void SetPaused(bool paused)
    {
        if (_disposed || _paused == paused)
            return;
        _paused = paused;
        _machine.SetPaused(paused, IsInTrigger(GetPointer()));
        _policy.SetMonitoringPaused(paused);
        if (paused)
        {
            _pointerTimer.Stop();
            UnregisterShortcut();
        }
        else
        {
            _policy.Recheck();
            OnEligibilityChanged(this, EventArgs.Empty);
        }
    }

    public void Dismiss() => _machine.Dismiss();
    public void CompleteReveal() => _machine.CompleteReveal(TransitionVersion);
    public void CompleteReveal(long version) => _machine.CompleteReveal(version);
    public void CompleteClose() => _machine.CompleteClose(TransitionVersion);
    public void CompleteClose(long version) => _machine.CompleteClose(version);

    private void OnEligibilityChanged(object? sender, EventArgs e)
    {
        if (_disposed || !_started)
            return;
        _machine.SetEligibility(_policy.IsEligible, IsInTrigger(GetPointer()));
        if (_paused || !_policy.IsEligible)
        {
            _pointerTimer.Stop();
            UnregisterShortcut();
            return;
        }
        UpdateShortcut();
        _pointerTimer.Start();
    }

    private void Tick()
    {
        if (_disposed || _paused || !_policy.IsEligible)
        {
            OnEligibilityChanged(this, EventArgs.Empty);
            return;
        }
        UpdateShortcut();
        Point point = GetPointer();
        bool inTrigger = IsInTrigger(point);
        Rectangle surface = _surfaceBounds();
        bool inside = false;
        if (surface.Width > 0 && surface.Height > 0)
        {
            try
            {
                inside = _surfaceContains != null ? _surfaceContains(point) : surface.Contains(point);
            }
            catch { inside = false; }
        }
        Rectangle trigger = GetTrigger();
        int corridorLeft = Math.Max(trigger.Left, surface.Left);
        int corridorRight = Math.Min(trigger.Right, surface.Right);
        bool inCorridor = corridorRight > corridorLeft && point.X >= corridorLeft && point.X < corridorRight && point.Y >= _policy.DisplayBounds.Top && point.Y <= surface.Top;
        bool interacting = inside || inCorridor || _policy.IsRegisteredPointerSurface(point) || _hasKeyboardFocus();
        _machine.DwellMs = _settings().DwellMs;
        _machine.GraceMs = _settings().GraceMs;
        _machine.Tick(Environment.TickCount64, inTrigger, interacting);
    }

    private Point GetPointer() => NativeMethods.GetCursorPos(out var point) ? new Point(point.X, point.Y) : new Point(int.MinValue, int.MinValue);
    private bool IsInTrigger(Point point) => GetTrigger().Contains(point);
    private Rectangle GetTrigger()
    {
        var bounds = _policy.DisplayBounds;
        var settings = _settings();
        int width = settings.FullWidthTrigger ? bounds.Width : (int)Math.Round(Math.Clamp(settings.TriggerWidth, 80, 800) * _policy.DpiScale);
        width = Math.Min(width, bounds.Width);
        int depth = Math.Max(1, (int)Math.Round(Math.Clamp(settings.TriggerDepth, 1, 12) * _policy.DpiScale));
        return new Rectangle(bounds.Left + (bounds.Width - width) / 2, bounds.Top, width, depth);
    }

    private void UpdateShortcut()
    {
        if (!_settings().AccessibilityShortcut || _paused || !_policy.IsEligible)
        {
            UnregisterShortcut();
            return;
        }
        if (_shortcutRegistered)
            return;
        if (_hotKeySource == null)
        {
            _hotKeySource = new HwndSource(new HwndSourceParameters("Crest accessibility messages") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) });
            _hotKeySource.AddHook(HotKeyHook);
        }
        _shortcutRegistered = NativeMethods.RegisterHotKey(_hotKeySource.Handle, ShortcutId, 0x4003, 0x20);
    }

    private IntPtr HotKeyHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam.ToInt32() == ShortcutId)
        {
            handled = true;
            _policy.Recheck();
            if (_policy.IsEligible && !_paused && _settings().AccessibilityShortcut)
            {
                _machine.RevealAccessibility();
                KeyboardFocusRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        return IntPtr.Zero;
    }

    private void UnregisterShortcut()
    {
        if (_hotKeySource != null && _shortcutRegistered)
            NativeMethods.UnregisterHotKey(_hotKeySource.Handle, ShortcutId);
        _shortcutRegistered = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _machine.SetEligibility(false, false);
        _disposed = true;
        _pointerTimer.Stop();
        UnregisterShortcut();
        _policy.Changed -= OnEligibilityChanged;
        _hotKeySource?.Dispose();
        _hotKeySource = null;
    }
}
