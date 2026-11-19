using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Crest.Core;
using Crest.Features;
namespace Crest.UI;

public sealed class IslandWindow : Window
{
    private const double DeckWidth = 820, DeckHeight = 150, TotalHeight = 522, DockWidth = 284;
    private static readonly Rect CompactRect = new(280, 0, 260, 44);
    private static readonly Rect OpenRect = new(0, 0, DeckWidth, DeckHeight);
    private static readonly Rect DockRect = new((DeckWidth - DockWidth) / 2, 158, DockWidth, 40);
    private static readonly Rect DetailRect = new(200, 206, 420, 316);
    private readonly App _app;
    private readonly Grid _surface = new(), _shell = new(), _compact = new();
    private readonly ActivityDeck _expanded;
    private readonly Border _dock = new(), _detail = new();
    private readonly RectangleGeometry _clip = new(CompactRect, 26, 26);
    private readonly TranslateTransform _slide = new(), _detailSlide = new();
    private readonly StackPanel _body = new(), _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly Dictionary<string, TextBlock> _labels = new();
    private readonly Dictionary<string, Button> _buttons = new();
    private readonly Dictionary<string, Slider> _sliders = new();
    private readonly Dictionary<string, PendingControlEdit> _sliderEdits = new();
    private readonly Dictionary<string, DispatcherTimer> _sliderCommits = new();
    private readonly Image _compactArt = new() { Width = 28, Height = 28, Stretch = Stretch.UniformToFill };
    private Image _art = new() { Width = 54, Height = 54, Stretch = Stretch.UniformToFill };
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _refreshing, _wide, _playerPopupOpen, _updatingPlayers, _suppressing, _rendering, _checkingShelf;
    private string _module = "Overview", _playerSignature = "", _enabledSignature = "", _shelfSignature = "";
    private IntPtr _hwnd, _playerPopupHandle;
    private int _motionGeneration;
    private int _dockMotionGeneration;
    private long _lastShelfCheck;
    private ComboBox? _playersCombo;
    private TextBlock? _artFallback;
    private TimerToolsPanel? _timerTools;
    public bool HoldsInteraction => IsKeyboardFocusWithin || _playerPopupOpen;
    private static readonly string[] Modules = { "Music", "Controls", "Timer", "Launch", "Shelf" };
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    public IslandWindow(App app)
    {
        _app = app;
        Title = "Crest island";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Width = 836;
        Height = 530;
        Content = _surface;
        _surface.Width = DeckWidth;
        _surface.Height = TotalHeight;
        _surface.HorizontalAlignment = HorizontalAlignment.Center;
        _surface.VerticalAlignment = VerticalAlignment.Top;
        _surface.Margin = new Thickness(8, 0, 8, 8);
        _surface.RenderTransform = _slide;
        _shell.Width = DeckWidth;
        _shell.Height = DeckHeight;
        _shell.VerticalAlignment = VerticalAlignment.Top;
        _shell.Clip = _clip;
        var background = new Border { CornerRadius = new CornerRadius(26), BorderThickness = new Thickness(1) };
        background.SetResourceReference(Border.BackgroundProperty, "IslandShell");
        background.SetResourceReference(Border.BorderBrushProperty, "IslandOutline");
        _shell.Children.Add(background);
        BuildCompact();
        _shell.Children.Add(_compact);
        _expanded = new ActivityDeck(app, Expand, app.OpenSettings) { Visibility = Visibility.Collapsed };
        _shell.Children.Add(_expanded);
        _surface.Children.Add(_shell);
        BuildDock();
        BuildDetail();
        SourceInitialized += (_, _) => { _hwnd = new WindowInteropHelper(this).Handle; SetWindowLong(_hwnd, -20, GetWindowLong(_hwnd, -20) | 0x08000000 | 0x80); HwndSource.FromHwnd(_hwnd)?.AddHook(WindowHook); _app.Policy.RegisterSurface(_hwnd, true, () => PixelBounds, ContainsVisiblePoint); PositionOnDisplay(); };
        PreviewMouseDown += (_, e) => { if (e.OriginalSource is DependencyObject d && Ancestor<TextBox>(d) != null) SetForegroundWindow(_hwnd); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { _app.Controller.Dismiss(); e.Handled = true; } };
        AllowDrop = true;
        DragEnter += (_, e) => { if (_app.Preferences.EnabledModules.Contains("Shelf") && e.Data.GetDataPresent(DataFormats.FileDrop)) { Expand("Shelf"); e.Effects = DragDropEffects.Link; e.Handled = true; } };
        DragOver += (_, e) => { e.Effects = _app.Preferences.EnabledModules.Contains("Shelf") && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };
        Drop += async (_, e) => { if (_app.Preferences.EnabledModules.Contains("Shelf") && e.Data.GetData(DataFormats.FileDrop) is string[] paths) { var error = await Task.Run(() => _app.Local.AddShelfFiles(paths)); BuildBody(); if (error != null) ShowMessage(error); } e.Handled = true; };
        _clock.Tick += (_, _) => { Refresh(); if (_wide && _module == "Shelf" && !_checkingShelf && Environment.TickCount64 - _lastShelfCheck >= 3000) { _checkingShelf = true; _lastShelfCheck = Environment.TickCount64; _ = Task.Run(_app.Local.RefreshShelf).ContinueWith(_ => Dispatcher.BeginInvoke(() => { _checkingShelf = false; if (IsVisible && _wide && _module == "Shelf") { string current = string.Join("|", _app.Local.Shelf.Select(i => i.Id + ":" + i.Available)); if (current != _shelfSignature) BuildBody(); } })); } };
    }
    private static T? Ancestor<T>(DependencyObject d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t)
                return t;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }
    private IntPtr WindowHook(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg == 0x21)
        {
            handled = true;
            return (IntPtr)3;
        }
        if (msg == 0x84)
        {
            int packed = unchecked((int)lp.ToInt64());
            var point = new System.Drawing.Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff));
            if (!ContainsVisiblePoint(point))
            {
                handled = true;
                return (IntPtr)(-1);
            }
        }
        return IntPtr.Zero;
    }
    public bool ContainsVisiblePoint(System.Drawing.Point screenPoint)
    {
        if (!IsVisible || _hwnd == IntPtr.Zero)
            return false;
        try
        {
            var p = _surface.PointFromScreen(new Point(screenPoint.X, screenPoint.Y));
            return RoundedContains(p, _clip.Rect, 26) || (_dock.Visibility == Visibility.Visible && RoundedContains(p, DockRect, 20)) || (_detail.Visibility == Visibility.Visible && RoundedContains(p, DetailRect, 22));
        }
        catch { return false; }
    }
    private static bool RoundedContains(Point p, Rect r, double radius)
    {
        if (!r.Contains(p))
            return false;
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        double x = Math.Clamp(p.X, r.Left + radius, r.Right - radius), y = Math.Clamp(p.Y, r.Top + radius, r.Bottom - radius);
        return (p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y) <= radius * radius;
    }
    public System.Drawing.Rectangle PixelBounds
    {
        get
        {
            if (!IsVisible || _hwnd == IntPtr.Zero)
                return System.Drawing.Rectangle.Empty;
            try
            {
                var r = _clip.Rect;
                var a = _surface.PointToScreen(new Point(r.X, r.Y));
                var b = _surface.PointToScreen(new Point(r.Right, r.Bottom));
                return System.Drawing.Rectangle.FromLTRB((int)a.X, (int)a.Y, (int)b.X, (int)b.Y);
            }
            catch { return System.Drawing.Rectangle.Empty; }
        }
    }
    public void PositionOnDisplay()
    {
        if (_hwnd == IntPtr.Zero)
            return;
        var screen = _app.Policy.DisplayBounds;
        if (screen.Width <= 0 || screen.Height <= 0)
            return;
        double dpi = _app.Policy.DpiScale, fit = Math.Min(screen.Width / (836 * dpi), (System.Windows.Forms.Screen.FromRectangle(screen).WorkingArea.Bottom - screen.Top) / (530 * dpi));
        double size = Math.Min(_app.Preferences.SizeScale, fit);
        _surface.LayoutTransform = new ScaleTransform(size, size);
        Width = 836 * size;
        Height = 530 * size;
        int width = (int)Math.Round(Width * dpi), height = (int)Math.Round(Height * dpi);
        SetWindowPos(_hwnd, IntPtr.Zero, screen.Left + (screen.Width - width) / 2, screen.Top, width, height, 0x0010 | 0x0004);
    }
    public void FocusForKeyboard()
    {
        if (!IsVisible || !_app.Policy.IsEligible || _hwnd == IntPtr.Zero)
            return;
        SetForegroundWindow(_hwnd);
        Activate();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (!IsVisible || !_app.Policy.IsEligible) return; var target = FindKeyboardControl(_detail.Visibility == Visibility.Visible ? _body : _wide ? _expanded : _compact); if (target != null) { target.Focus(); Keyboard.Focus(target); } }));
    }
    private static Control? FindKeyboardControl(DependencyObject root)
    {
        if (root is Control control && control.Focusable && control.IsVisible && control.IsEnabled)
            return control;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindKeyboardControl(VisualTreeHelper.GetChild(root, i));
            if (found != null)
                return found;
        }
        return null;
    }
    private TextBlock Label(string id, string text, double size = 13, string color = "Text", bool bold = false)
    {
        var t = Design.Text(text, size, color, bold);
        _labels[id] = t;
        return t;
    }
    private void BuildCompact()
    {
        _compact.Width = 260;
        _compact.Height = 44;
        _compact.VerticalAlignment = VerticalAlignment.Top;
        _compact.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (double width in new[] { 40d, 156, 32, 32 })
            _compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        var cover = new Grid { Width = 28, Height = 28 };
        var glyph = Design.Icon("\uE8D6", 15);
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "IslandAccent");
        cover.Children.Add(glyph);
        cover.Children.Add(_compactArt);
        var coverButton = Design.Button("", () => Expand(_app.Local.Timers.Any(timer => timer.Phase == TimerPhase.Completed) ? "Timer" : "Music"));
        coverButton.Content = cover;
        coverButton.Background = Brushes.Transparent;
        coverButton.Padding = new Thickness(0);
        AutomationProperties.SetName(coverButton, "Open music details");
        _compact.Children.Add(coverButton);
        var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Label("compactTitle", "crest", 12, "IslandText", true);
        title.TextWrapping = TextWrapping.NoWrap;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        meta.Children.Add(title);
        var artist = Label("compactDetail", "Your desktop, in reach", 9, "IslandMuted");
        artist.TextWrapping = TextWrapping.NoWrap;
        artist.TextTrimming = TextTrimming.CharacterEllipsis;
        meta.Children.Add(artist);
        Grid.SetColumn(meta, 1);
        _compact.Children.Add(meta);
        var play = Design.IconButton("\uE768", "Play or pause music", () => Run(_app.Hardware.TogglePlaybackAsync));
        play.Background = Brushes.Transparent;
        play.Padding = new Thickness(5);
        play.MinWidth = 32;
        play.MinHeight = 32;
        play.Margin = new Thickness(0, 6, 0, 6);
        Grid.SetColumn(play, 2);
        _compact.Children.Add(play);
        _compactPlay = play;
        var open = Design.IconButton("\uE70D", "Expand activity deck", () => Expand("Overview"));
        open.Background = Brushes.Transparent;
        open.Padding = new Thickness(5);
        open.Margin = new Thickness(0, 6, 2, 6);
        Grid.SetColumn(open, 3);
        _compact.Children.Add(open);
    }
    private Button _compactPlay = null!;
    private void BuildDock()
    {
        _dock.Width = DockWidth;
        _dock.Height = 40;
        _dock.CornerRadius = new CornerRadius(20);
        _dock.HorizontalAlignment = HorizontalAlignment.Center;
        _dock.VerticalAlignment = VerticalAlignment.Top;
        _dock.Margin = new Thickness(0, 158, 0, 0);
        _dock.BorderThickness = new Thickness(1);
        _dock.Padding = new Thickness(5, 3, 5, 3);
        _dock.SetResourceReference(Border.BackgroundProperty, "IslandShell");
        _dock.SetResourceReference(Border.BorderBrushProperty, "IslandOutline");
        _dock.Child = _tabs;
        _tabs.HorizontalAlignment = HorizontalAlignment.Center;
        _tabs.VerticalAlignment = VerticalAlignment.Center;
        _dock.Visibility = Visibility.Collapsed;
        _surface.Children.Add(_dock);
        BuildTabs();
    }
    private void BuildTabs()
    {
        _tabs.Children.Clear();
        var items = new[] { ("Overview", "\uE80F", "Activity overview"), ("Music", "\uE8D6", "Music"), ("Timer", "\uE823", "Timers and stopwatch"), ("Controls", "\uE9E9", "Laptop controls"), ("Launch", "\uE8A7", "Launch shortcuts"), ("Shelf", "\uE8B7", "File shelf") };
        foreach (var (module, glyph, name) in items.Where(i => i.Item1 == "Overview" || _app.Preferences.EnabledModules.Contains(i.Item1)))
        {
            var b = Design.IconButton(glyph, name, () => Expand(module));
            b.Style = (Style)FindResource("CircleButton");
            b.Width = 32;
            b.Height = 32;
            b.MinHeight = 32;
            b.Padding = new Thickness(6);
            b.Margin = new Thickness(1, 0, 1, 0);
            if (module == _module && _wide)
            {
                b.Background = Design.Brush("IslandText");
                b.Foreground = Design.Brush("IslandShell");
            }
            else
            {
                b.Background = Brushes.Transparent;
                b.SetResourceReference(Control.ForegroundProperty, "IslandMuted");
            }
            _tabs.Children.Add(b);
        }
        var settings = Design.IconButton("\uE713", "Crest settings", _app.OpenSettings);
        settings.Style = (Style)FindResource("CircleButton");
        settings.Width = 32;
        settings.Height = 32;
        settings.Padding = new Thickness(6);
        settings.Background = Brushes.Transparent;
        settings.SetResourceReference(Control.ForegroundProperty, "IslandMuted");
        _tabs.Children.Add(settings);
        var close = Design.IconButton("\uE70E", "Collapse island", Collapse);
        close.Style = (Style)FindResource("CircleButton");
        close.Width = 32;
        close.Height = 32;
        close.Padding = new Thickness(6);
        close.Background = Brushes.Transparent;
        close.SetResourceReference(Control.ForegroundProperty, "IslandMuted");
        _tabs.Children.Add(close);
    }
    private void BuildDetail()
    {
        _detail.Width = DetailRect.Width;
        _detail.Height = DetailRect.Height;
        _detail.HorizontalAlignment = HorizontalAlignment.Center;
        _detail.VerticalAlignment = VerticalAlignment.Top;
        _detail.Margin = new Thickness(0, DetailRect.Top, 0, 0);
        _detail.CornerRadius = new CornerRadius(22);
        _detail.BorderThickness = new Thickness(1);
        _detail.Padding = new Thickness(16, 12, 16, 12);
        _detail.RenderTransform = _detailSlide;
        _detail.SetResourceReference(Border.BackgroundProperty, "IslandShell");
        _detail.SetResourceReference(Border.BorderBrushProperty, "IslandOutline");
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Label("detailTitle", "Music", 12, "IslandText", true));
        var close = Design.IconButton("\uE711", "Close module details", () => Expand("Overview"));
        close.Width = 32;
        close.Height = 32;
        close.Background = Brushes.Transparent;
        close.Padding = new Thickness(7);
        Grid.SetColumn(close, 1);
        head.Children.Add(close);
        grid.Children.Add(head);
        var scroll = new ScrollViewer { Content = _body };
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);
        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var status = Label("footer", _app.Preferences.AllowOverApps ? "LOCAL · OVER OTHER APPS" : "LOCAL · DESKTOP ONLY", 9, "IslandMuted");
        status.TextWrapping = TextWrapping.NoWrap;
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        footer.Children.Add(status);
        var clock = Label("clock", DateTime.Now.ToString("t"), 9, "IslandMuted");
        Grid.SetColumn(clock, 1);
        footer.Children.Add(clock);
        Grid.SetRow(footer, 2);
        footer.VerticalAlignment = VerticalAlignment.Bottom;
        grid.Children.Add(footer);
        _detail.Child = grid;
        _detail.Visibility = Visibility.Collapsed;
        _surface.Children.Add(_detail);
    }
    public void Reveal(long version)
    {
        bool reversing = IsVisible;
        IsHitTestVisible = true;
        Design.Apply(_app.Preferences);
        if (!reversing)
        {
            Set("footer", _app.Preferences.AllowOverApps ? "LOCAL · OVER OTHER APPS" : "LOCAL · DESKTOP ONLY");
            Collapse(false);
            Opacity = 0;
            _slide.Y = -9;
            PositionOnDisplay();
            Show();
        }
        _app.Hardware.SetVisible(true);
        _clock.Start();
        Refresh();
        _ = Task.Run(_app.Local.RefreshShelf).ContinueWith(_ => Dispatcher.BeginInvoke(() => { if (IsVisible && _module == "Shelf" && _wide) BuildBody(); }));
        AnimateVisibility(1, 0, Design.ReducedMotion(_app.Preferences) ? 60 : 240, () => _app.Controller.CompleteReveal(version));
    }
    public void CloseAnimated(long version) => AnimateVisibility(0, -8, Design.ReducedMotion(_app.Preferences) ? 60 : 190, () => { if (version != _app.Controller.TransitionVersion) return; Suppress(); _app.Controller.CompleteClose(version); });
    private void AnimateVisibility(double opacity, double y, int ms, Action completed)
    {
        int generation = ++_motionGeneration;
        var time = TimeSpan.FromMilliseconds(ms / _app.Preferences.MotionSpeed);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(Opacity, opacity, time) { EasingFunction = ease };
        fade.Completed += (_, _) => { if (generation == _motionGeneration) completed(); };
        BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(_slide.Y, y, time) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
    }
    public void Suppress()
    {
        if (_suppressing)
            return;
        _suppressing = true;
        try
        {
            ++_motionGeneration;
            IsHitTestVisible = false;
            ClosePlayerPopup();
            Hide();
            BeginAnimation(OpacityProperty, null);
            _slide.BeginAnimation(TranslateTransform.YProperty, null);
            _clip.BeginAnimation(RectangleGeometry.RectProperty, null);
            ++_dockMotionGeneration;
            _dock.BeginAnimation(OpacityProperty, null);
            _detail.BeginAnimation(OpacityProperty, null);
            _detailSlide.BeginAnimation(TranslateTransform.YProperty, null);
            _clock.Stop();
            _app.Hardware.SetVisible(false);
            Keyboard.ClearFocus();
        }
        finally { _suppressing = false; }
    }
    public void Expand(string module)
    {
        if (!IsVisible && !_rendering)
            return;
        if (module != "Overview" && !_app.Preferences.EnabledModules.Contains(module))
            module = "Overview";
        if (_wide && _module == module && (module == "Overview" || _detail.Visibility == Visibility.Visible))
        {
            Refresh();
            return;
        }
        bool keyboard = IsVisible && GetForegroundWindow() == _hwnd && IsKeyboardFocusWithin;
        bool detailWasVisible = _detail.Visibility == Visibility.Visible;
        _module = module;
        _wide = true;
        BuildTabs();
        ClosePlayerPopup();
        _compact.Visibility = Visibility.Collapsed;
        _expanded.Visibility = Visibility.Visible;
        AnimateDock(true, true);
        _detail.Visibility = module == "Overview" ? Visibility.Collapsed : Visibility.Visible;
        if (module != "Overview")
        {
            Set("detailTitle", module switch
            {
                "Controls" => "Laptop controls",
                "Launch" => "Your shortcuts",
                "Shelf" => "File shelf",
                "Timer" => "Timers and stopwatch",
                _ => module
            });
            BuildBody();
            double opacity = detailWasVisible ? _detail.Opacity : 0;
            double offset = detailWasVisible ? _detailSlide.Y : -6;
            var duration = TimeSpan.FromMilliseconds(Design.ReducedMotion(_app.Preferences) ? 60 : 220 / _app.Preferences.MotionSpeed);
            _detail.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, 1, duration), HandoffBehavior.SnapshotAndReplace);
            _detailSlide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }, HandoffBehavior.SnapshotAndReplace);
        }
        AnimateShape(OpenRect);
        Refresh();
        if (keyboard)
            FocusForKeyboard();
    }
    private void Collapse() => Collapse(true);
    private void Collapse(bool animated)
    {
        bool keyboard = IsVisible && GetForegroundWindow() == _hwnd && IsKeyboardFocusWithin;
        ClosePlayerPopup();
        _wide = false;
        _module = "Overview";
        _expanded.Visibility = Visibility.Collapsed;
        AnimateDock(false, animated);
        _detail.Visibility = Visibility.Collapsed;
        _compact.Visibility = Visibility.Visible;
        _detail.BeginAnimation(OpacityProperty, null);
        _detailSlide.BeginAnimation(TranslateTransform.YProperty, null);
        if (animated)
            AnimateShape(CompactRect);
        else
        {
            _clip.BeginAnimation(RectangleGeometry.RectProperty, null);
            _clip.Rect = CompactRect;
        }
        if (keyboard)
            FocusForKeyboard();
    }
    private void AnimateShape(Rect target)
    {
        var a = new RectAnimation(_clip.Rect, target, TimeSpan.FromMilliseconds(Design.ReducedMotion(_app.Preferences) ? 60 : 220 / _app.Preferences.MotionSpeed)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        _clip.BeginAnimation(RectangleGeometry.RectProperty, a, HandoffBehavior.SnapshotAndReplace);
    }
    private void AnimateDock(bool show, bool animated)
    {
        int generation = ++_dockMotionGeneration;
        double from = _dock.Visibility == Visibility.Visible ? _dock.Opacity : 0;
        _dock.IsHitTestVisible = show;
        if (show)
            _dock.Visibility = Visibility.Visible;
        if (!animated)
        {
            _dock.BeginAnimation(OpacityProperty, null);
            _dock.Opacity = show ? 1 : 0;
            _dock.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        var duration = TimeSpan.FromMilliseconds(Design.ReducedMotion(_app.Preferences) ? 60 : 220 / _app.Preferences.MotionSpeed);
        var fade = new DoubleAnimation(from, show ? 1 : 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        fade.Completed += (_, _) =>
        {
            if (generation != _dockMotionGeneration)
                return;
            _dock.Opacity = show ? 1 : 0;
            _dock.BeginAnimation(OpacityProperty, null);
            if (!show)
                _dock.Visibility = Visibility.Collapsed;
        };
        _dock.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    private void BuildBody()
    {
        foreach (var timer in _sliderCommits.Values) timer.Stop();
        _sliderCommits.Clear();
        _sliderEdits.Clear();
        ClosePlayerPopup();
        _playersCombo = null;
        _artFallback = null;
        _playerSignature = "";
        _body.Children.Clear();
        _timerTools = null;
        foreach (string k in _labels.Keys.Where(k => k.StartsWith("body")).ToArray())
            _labels.Remove(k);
        _buttons.Clear();
        _sliders.Clear();
        switch (_module)
        {
            case "Music":
                BuildMusic();
                break;
            case "Controls":
                BuildControls();
                break;
            case "Timer":
                BuildTimer();
                break;
            case "Launch":
                BuildLauncher();
                break;
            case "Shelf":
                BuildShelf();
                break;
        }
        Refresh();
    }
    private void BuildMusic()
    {
        _art = new Image { Width = 54, Height = 54, Stretch = Stretch.UniformToFill };
        var track = new Grid { Margin = new Thickness(0, 4, 0, 14) };
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
        track.ColumnDefinitions.Add(new ColumnDefinition());
        var artwork = new Grid();
        _artFallback = Design.Icon("\uE8D6", 24);
        _artFallback.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        artwork.Children.Add(_artFallback);
        artwork.Children.Add(_art);
        var art = Design.Card(artwork, new Thickness(0));
        art.Width = 54;
        art.Height = 54;
        art.ClipToBounds = true;
        track.Children.Add(art);
        var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        meta.Children.Add(Label("bodyTitle", "No music playing", 17, "Text", true));
        meta.Children.Add(Label("bodyArtist", "Open a player that shares a Windows media session.", 11, "Muted"));
        Grid.SetColumn(meta, 1);
        track.Children.Add(meta);
        _body.Children.Add(track);
        _body.Children.Add(Label("bodyPlayer", "WINDOWS MEDIA SESSION", 9, "Muted"));
        var progress = new Slider { Minimum = 0, Maximum = 1 };
        AutomationProperties.SetName(progress, "Track position in seconds");
        _sliders["seek"] = progress;
        AttachSliderWrite("seek", progress, _app.Hardware.SeekAsync, releaseOnly: true);
        _body.Children.Add(progress);
        var transport = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
        var previous = Design.IconButton("\uE892", "Previous track", () => Run(_app.Hardware.PreviousAsync));
        var play = Design.IconButton("\uE768", "Play or pause", () => Run(_app.Hardware.TogglePlaybackAsync));
        var next = Design.IconButton("\uE893", "Next track", () => Run(_app.Hardware.NextAsync));
        play.Width = 60;
        play.Height = 42;
        play.Margin = new Thickness(12, 0, 12, 0);
        play.SetResourceReference(Control.BackgroundProperty, "AccentFill");
        play.Foreground = Brushes.Black;
        _buttons["play"] = play;
        _buttons["previous"] = previous;
        _buttons["next"] = next;
        transport.Children.Add(previous);
        transport.Children.Add(play);
        transport.Children.Add(next);
        _body.Children.Add(transport);
        var players = new ComboBox { DisplayMemberPath = "Name", SelectedValuePath = "Id", Margin = new Thickness(0, 0, 0, 8) };
        _playersCombo = players;
        AutomationProperties.SetName(players, "Preferred media player");
        players.SelectionChanged += (_, _) => { if (!_updatingPlayers && players.SelectedValue is string id) { _app.Hardware.SelectPlayer(id); _app.Preferences.PreferredPlayer = id; _app.SavePreferences(); } };
        players.DropDownOpened += (_, _) => { _playerPopupOpen = true; RegisterPlayerPopup(players); if (_playerPopupHandle == IntPtr.Zero) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => RegisterPlayerPopup(players))); };
        players.DropDownClosed += (_, _) => UnregisterPlayerPopup();
        _body.Children.Add(players);
        UpdatePlayerList();
        _body.Children.Add(Label("bodyStatus", "", 11, "Muted"));
    }
    private void UpdatePlayerList()
    {
        if (_playersCombo == null || _playerPopupOpen || _playersCombo.IsKeyboardFocusWithin)
            return;
        var players = _app.Hardware.Players;
        string signature = string.Join("|", players.Select(p => p.Id + ":" + p.Name)) + "|" + _app.Preferences.PreferredPlayer;
        if (signature == _playerSignature)
            return;
        _playerSignature = signature;
        _updatingPlayers = true;
        try
        {
            _playersCombo.ItemsSource = players;
            _playersCombo.SelectedValue = players.FirstOrDefault(p => p.Id == _app.Preferences.PreferredPlayer)?.Id ?? players.FirstOrDefault(p => p.Name == _app.Hardware.Music.PlayerName)?.Id ?? players.FirstOrDefault()?.Id;
            _playersCombo.Visibility = players.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _updatingPlayers = false; }
    }
    private void RegisterPlayerPopup(ComboBox players)
    {
        if (!IsVisible || !_playerPopupOpen || !players.IsDropDownOpen || players != _playersCombo)
            return;
        if (players.Template.FindName("PART_Popup", players) is not Popup popup || popup.Child is not FrameworkElement child)
            return;
        if (PresentationSource.FromVisual(child) is not HwndSource source || source.Handle == IntPtr.Zero)
            return;
        _playerPopupHandle = source.Handle;
        _app.Policy.RegisterSurface(_playerPopupHandle, true, () =>
        {
            if (!popup.IsOpen || !child.IsVisible)
                return System.Drawing.Rectangle.Empty;
            try
            {
                var a = child.PointToScreen(new Point());
                var b = child.PointToScreen(new Point(child.ActualWidth, child.ActualHeight));
                return System.Drawing.Rectangle.FromLTRB((int)a.X, (int)a.Y, (int)b.X, (int)b.Y);
            }
            catch { return System.Drawing.Rectangle.Empty; }
        });
    }
    private void UnregisterPlayerPopup()
    {
        _playerPopupOpen = false;
        var handle = _playerPopupHandle;
        _playerPopupHandle = IntPtr.Zero;
        if (handle != IntPtr.Zero)
            _app.Policy.UnregisterSurface(handle);
    }
    private void ClosePlayerPopup()
    {
        _playerPopupOpen = false;
        if (_playersCombo != null)
            _playersCombo.IsDropDownOpen = false;
        UnregisterPlayerPopup();
    }
    private void BuildControls()
    {
        var battery = new StackPanel();
        battery.Children.Add(Label("bodyBattery", "Battery", 23, "Text", true));
        battery.Children.Add(Label("bodyBatteryStatus", "Reading Windows battery status…", 11, "Muted"));
        _body.Children.Add(Design.Card(battery, new Thickness(12)));
        AddSlider("volume", "System volume", () => _app.Hardware.Volume, v => _app.Hardware.SetVolumeAsync(v));
        AddSlider("brightness", "Laptop brightness", () => _app.Hardware.Brightness, v => _app.Hardware.SetBrightnessAsync(v));
        var mute = Design.Button("Mute audio", () => Run(_app.Hardware.ToggleMuteAsync));
        _buttons["mute"] = mute;
        mute.Margin = new Thickness(0, 7, 0, 0);
        _body.Children.Add(mute);
    }
    private void AddSlider(string id, string name, Func<double> value, Func<double, Task> change)
    {
        var row = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(Design.Text(name, 12));
        var v = Label("body" + id + "Value", ((int)value()) + "%", 12, "Accent", true);
        Grid.SetColumn(v, 1);
        row.Children.Add(v);
        _body.Children.Add(row);
        var slider = new Slider { Value = Math.Clamp(value(), 0, 100) };
        AutomationProperties.SetName(slider, name);
        _sliders[id] = slider;
        AttachSliderWrite(id, slider, change, preview: target => v.Text = ((int)target) + "%");
        _body.Children.Add(slider);
        _body.Children.Add(Label("body" + id + "Status", "", 10, "Muted"));
    }
    private void AttachSliderWrite(string id, Slider slider, Func<double, Task> change, bool releaseOnly = false, Action<double>? preview = null)
    {
        var edit = new PendingControlEdit();
        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        bool detached = false;
        _sliderEdits[id] = edit;
        _sliderCommits[id] = debounce;
        void Schedule()
        {
            if (!edit.NeedsWrite || !slider.IsEnabled) return;
            if (detached) { if (!edit.IsWriting) _ = CommitAsync(); return; }
            debounce.Stop();
            debounce.Start();
        }
        void StopForHidden()
        {
            debounce.Stop();
            detached = true;
            if (edit.NeedsWrite && !edit.IsWriting) _ = CommitAsync();
            else if (!edit.IsWriting) edit.Cancel();
        }
        slider.ValueChanged += (_, _) =>
        {
            if (_refreshing || detached) return;
            edit.Record(slider.Value);
            preview?.Invoke(slider.Value);
            Schedule();
        };
        async Task CommitAsync()
        {
            debounce.Stop();
            if (!slider.IsEnabled) { edit.Cancel(); return; }
            if (releaseOnly && slider.IsMouseCaptureWithin && !detached) return;
            if (!edit.TryBeginWrite(out var write)) return;
            bool succeeded = false;
            try { await change(write.Value); succeeded = true; }
            catch { ShowMessage("This control is currently unavailable."); }
            finally
            {
                bool newerEdit = edit.CompleteWrite(write, Environment.TickCount64, releaseOnly && succeeded && !detached ? 1500 : 0);
                if (newerEdit)
                {
                    if (detached) await CommitAsync();
                    else Schedule();
                }
                if (!Dispatcher.HasShutdownStarted) _ = Dispatcher.BeginInvoke(new Action(Refresh));
            }
        }
        debounce.Tick += async (_, _) => await CommitAsync();
        if (releaseOnly)
        {
            slider.PreviewMouseLeftButtonUp += (_, _) => Schedule();
            slider.LostMouseCapture += (_, _) => Schedule();
        }
        slider.Unloaded += (_, _) => StopForHidden();
        slider.IsVisibleChanged += (_, _) => { if (!slider.IsVisible) StopForHidden(); };
    }
    private void BuildTimer()
    {
        _timerTools = new TimerToolsPanel(_app.Local, ShowMessage);
        _body.Children.Add(_timerTools);
    }
    private void BuildLauncher()
    {
        _body.Children.Add(Design.Text("Go somewhere.", 22, "Text", true));
        _body.Children.Add(Design.Text("Your apps, folders, and favorite places.", 11, "Muted"));
        if (_app.Preferences.Launchers.Count == 0)
        {
            var empty = Design.Text("Add your first shortcut in Settings. Only the places you choose appear here.", 13, "Muted");
            empty.Margin = new Thickness(0, 18, 0, 18);
            _body.Children.Add(empty);
            _body.Children.Add(Design.Button("Add shortcuts in Settings", _app.OpenSettings));
        }
        foreach (var entry in _app.Preferences.Launchers)
        {
            var b = Design.Button(entry.Name, () => _ = _app.OpenTarget(() => _app.Local.Launch(entry)));
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            b.Margin = new Thickness(0, 8, 0, 0);
            _body.Children.Add(b);
        }
    }
    private void BuildShelf()
    {
        _shelfSignature = string.Join("|", _app.Local.Shelf.Select(i => i.Id + ":" + i.Available));
        _body.Children.Add(Design.Text("Keep it close.", 22, "Text", true));
        _body.Children.Add(Label("bodyShelfCount", $"{_app.Local.Shelf.Count} / 20 file references · this session", 11, "Muted"));
        if (_app.Local.Shelf.Count == 0)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(Design.Icon("\uE8B7", 28));
            var msg = Design.Text("Drop a file from your desktop", 14, "Text", true);
            msg.Margin = new Thickness(0, 12, 0, 4);
            stack.Children.Add(msg);
            stack.Children.Add(Design.Text("Your originals stay exactly where they are.", 11, "Muted"));
            var card = Design.Card(stack, new Thickness(12, 22, 12, 22));
            card.Margin = new Thickness(0, 16, 0, 0);
            _body.Children.Add(card);
        }
        foreach (var item in _app.Local.Shelf)
        {
            var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            Point? origin = null;
            bool suppressClick = false;
            var b = Design.Button(item.Name + (item.Available ? "" : " · missing"), () => { if (!suppressClick) _ = _app.OpenTarget(() => _app.Local.OpenShelf(item.Id)); });
            Design.RegisterTooltip(b, item.Available ? "Open original file" : "The source was moved or removed.");
            row.Children.Add(b);
            b.PreviewMouseLeftButtonDown += (_, e) => { origin = e.GetPosition(b); suppressClick = false; };
            b.PreviewMouseLeftButtonUp += (_, e) => { origin = null; if (suppressClick) e.Handled = true; };
            b.PreviewMouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    origin = null;
                    return;
                }
                if (!item.Available || origin is not Point start)
                    return;
                var current = e.GetPosition(b);
                if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
                    return;
                origin = null;
                suppressClick = true;
                e.Handled = true;
                b.ReleaseMouseCapture();
                try
                {
                    var data = new DataObject(DataFormats.FileDrop, new[] { item.Path });
                    DragDrop.DoDragDrop(b, data, DragDropEffects.Link | DragDropEffects.Copy);
                }
                catch { ShowMessage("Windows could not start this drag. Try opening the original file instead."); }
                finally { Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => suppressClick = false)); }
            };
            var remove = Design.IconButton("\uE711", "Remove reference to " + item.Name, () => { _app.Local.RemoveShelf(item.Id); BuildBody(); });
            remove.Margin = new Thickness(4, 0, 0, 0);
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            _body.Children.Add(row);
        }
    }
    private async void Run(Func<Task> action)
    {
        try
        {
            await action();
            Refresh();
        }
        catch { ShowMessage("This action is currently unavailable. Try again shortly."); }
    }
    public void ShowMessage(string message)
    {
        if (IsVisible)
        {
            if (_detail.Visibility != Visibility.Visible)
                Expand("Launch");
            Set("footer", message);
            _labels["footer"].TextWrapping = TextWrapping.NoWrap;
            _labels["footer"].TextTrimming = TextTrimming.CharacterEllipsis;
            Design.RegisterTooltip(_labels["footer"], message);
        }
    }
    private void Set(string id, string text)
    {
        if (_labels.TryGetValue(id, out var t) && t.Text != text)
            t.Text = text;
    }

    public void RenderPreviews(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);
        string previous = _app.Preferences.Theme;
        _rendering = true;
        _surface.LayoutTransform = Transform.Identity;
        _surface.Margin = new Thickness(0);
        foreach (string theme in new[] { "Dark", "Light" })
        {
            _app.Preferences.Theme = theme;
            Design.Apply(_app.Preferences);
            foreach (string module in new[] { "Overview", "Music", "Controls", "Timer", "Launch", "Shelf" })
            {
                _module = module;
                _wide = true;
                _compact.Visibility = Visibility.Collapsed;
                _expanded.Visibility = Visibility.Visible;
                AnimateDock(true, false);
                _detail.Visibility = module == "Overview" ? Visibility.Collapsed : Visibility.Visible;
                _clip.BeginAnimation(RectangleGeometry.RectProperty, null);
                _clip.Rect = OpenRect;
                _detail.BeginAnimation(OpacityProperty, null);
                _detail.Opacity = 1;
                _detailSlide.Y = 0;
                Set("detailTitle", module);
                BuildTabs();
                if (module != "Overview")
                    BuildBody();
                Refresh();
                _surface.Measure(new Size(DeckWidth, TotalHeight));
                _surface.Arrange(new Rect(0, 0, DeckWidth, TotalHeight));
                _surface.UpdateLayout();
                SaveVisual(_surface, System.IO.Path.Combine(directory, theme.ToLowerInvariant() + "-" + module.ToLowerInvariant() + ".png"), (int)(DeckWidth * 2), module == "Overview" ? 400 : (int)(TotalHeight * 2));
            }
            Collapse(false);
            Refresh();
            _surface.Measure(new Size(DeckWidth, TotalHeight));
            _surface.Arrange(new Rect(0, 0, DeckWidth, TotalHeight));
            _surface.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)DeckWidth * 2, 88, 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(_surface);
            SaveBitmap(new CroppedBitmap(bitmap, new Int32Rect(560, 0, 520, 88)), System.IO.Path.Combine(directory, theme.ToLowerInvariant() + "-compact.png"));
        }
        _rendering = false;
        _app.Preferences.Theme = previous;
        Design.Apply(_app.Preferences);
        Collapse(false);
    }
    private static void SaveVisual(Visual visual, string path, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        SaveBitmap(bitmap, path);
    }
    private static void SaveBitmap(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }
    public void Refresh()
    {
        if (!IsVisible && !_rendering)
            return;
        string enabled = string.Join("|", Modules.Where(m => _app.Preferences.EnabledModules.Contains(m)));
        if (enabled != _enabledSignature)
        {
            _enabledSignature = enabled;
            BuildTabs();
            if (_wide && _module != "Overview" && !_app.Preferences.EnabledModules.Contains(_module))
            {
                Expand("Overview");
                return;
            }
        }
        _refreshing = true;
        try
        {
            var music = _app.Hardware.Music;
            var timer = _app.Local.Timer;
            var completedTimers = _app.Local.Timers.Count(item => item.Phase == TimerPhase.Completed);
            var battery = _app.Hardware.Battery;
            Set("compactTitle", completedTimers > 0 ? completedTimers == 1 ? "Timer complete" : completedTimers + " timers complete" : music.HasSession ? music.Title : "crest");
            Set("compactDetail", completedTimers > 0 ? "Open timers to acknowledge" : timer.Phase == TimerPhase.Running ? timer.Name + " · " + FormatTime(timer.RemainingSeconds) : music.HasSession ? music.Artist : "Your desktop, in reach");
            _compactArt.Source = music.Artwork;
            _compactArt.Visibility = music.Artwork == null ? Visibility.Collapsed : Visibility.Visible;
            _compactPlay.IsEnabled = _app.Preferences.EnabledModules.Contains("Music") && music.CanPlayPause;
            if (_compactPlay.Content is TextBlock compactGlyph)
                compactGlyph.Text = music.IsPlaying ? "\uE769" : "\uE768";
            Set("clock", DateTime.Now.ToString("t"));
            if (!_wide)
                return;
            _expanded.Refresh();
            if (_detail.Visibility != Visibility.Visible)
                return;
            if (_module == "Music")
            {
                Set("bodyTitle", music.Title);
                Set("bodyArtist", music.Artist);
                Set("bodyPlayer", music.PlayerName.ToUpperInvariant());
                Set("bodyStatus", music.Status);
                _art.Source = music.Artwork;
                _art.Visibility = music.Artwork == null ? Visibility.Collapsed : Visibility.Visible;
                if (_artFallback != null)
                    _artFallback.Visibility = music.Artwork == null ? Visibility.Visible : Visibility.Collapsed;
                UpdatePlayerList();
                if (_sliders.TryGetValue("seek", out var seek))
                {
                    seek.IsEnabled = music.CanSeek;
                    seek.Maximum = Math.Max(1, music.DurationSeconds);
                    if (!music.CanSeek && _sliderEdits.TryGetValue("seek", out var disabledSeek)) disabledSeek.Cancel();
                    double position = _sliderEdits.TryGetValue("seek", out var seekEdit)
                        ? seekEdit.DisplayValue(music.PositionSeconds, Environment.TickCount64, 2)
                        : music.PositionSeconds;
                    if (!seek.IsMouseCaptureWithin)
                        seek.Value = Math.Clamp(position, 0, seek.Maximum);
                }
                _buttons["play"].IsEnabled = music.CanPlayPause;
                if (_buttons["play"].Content is TextBlock playGlyph)
                    playGlyph.Text = music.IsPlaying ? "\uE769" : "\uE768";
                _buttons["previous"].IsEnabled = music.CanPrevious;
                _buttons["next"].IsEnabled = music.CanNext;
            }
            if (_module == "Controls")
            {
                Set("bodyBattery", battery.Available ? $"{battery.Percent}% · {battery.Status}" : "No battery detected");
                Set("bodyBatteryStatus", battery.RemainingMinutes is int minutes ? $"Windows estimate · {minutes} min remaining" : "Reported by Windows");
                UpdateSlider("volume", _app.Hardware.Volume, _app.Hardware.AudioAvailable, _app.Hardware.AudioStatus);
                UpdateSlider("brightness", _app.Hardware.Brightness, _app.Hardware.BrightnessAvailable, _app.Hardware.BrightnessStatus);
                _buttons["mute"].Content = _app.Hardware.IsMuted ? "Unmute audio" : "Mute audio";
                _buttons["mute"].IsEnabled = _app.Hardware.AudioAvailable;
            }
            if (_module == "Timer")
            {
                _timerTools?.Refresh();
            }
        }
        finally { _refreshing = false; }
    }
    private void UpdateSlider(string id, double value, bool available, string status)
    {
        if (_sliders.TryGetValue(id, out var slider))
        {
            slider.IsEnabled = available;
            if (!available && _sliderEdits.TryGetValue(id, out var disabledEdit)) disabledEdit.Cancel();
            double displayed = _sliderEdits.TryGetValue(id, out var edit)
                ? edit.DisplayValue(value, Environment.TickCount64)
                : value;
            if (!slider.IsMouseCaptureWithin)
                slider.Value = Math.Clamp(displayed, 0, 100);
            Set("body" + id + "Value", available ? $"{(int)slider.Value}%" : "Unavailable");
            Set("body" + id + "Status", status);
            if (_labels.TryGetValue("body" + id + "Status", out var label))
                label.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        }
    }
    private static string FormatTime(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, Math.Ceiling(seconds)));
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{(int)span.TotalMinutes:00}:{span.Seconds:00}";
    }
}
