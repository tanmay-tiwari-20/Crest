using System;
using System.Collections.Generic;
using System.Globalization;
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

public sealed partial class IslandWindow : Window
{
    private const double DeckWidth = 480, TotalHeight = 560;
    private static readonly Rect CompactRect = new(104, 0, 272, 48);
    private static readonly Rect IdleRect = new(147, 0, 186, 48);
    private static readonly Rect DetailRect = new(20, 0, 440, 336);
    private Rect _targetRect = IdleRect, _compactRect = IdleRect;
    private enum IslandPresentation { Compact, Activity, Tools, Detail }
    private IslandPresentation _presentation = IslandPresentation.Compact;
    private readonly App _app;
    private readonly Grid _surface = new(), _shell = new(), _compact = new();
    private readonly ActivityDeck _expanded;
    private readonly Border _dock = new(), _detail = new();
    private readonly RectangleGeometry _clip = new(IdleRect, 24, 24);
    private readonly IslandEnvelope _envelope;
    private readonly TranslateTransform _slide = new(), _detailSlide = new();
    private readonly TranslateTransform _dockSlide = new(), _deckSlide = new();
    private readonly TranslateTransform _compactSlide = new();
    private readonly SpringMotion _motion = new();
    private readonly StackPanel _body = new(), _tabs = new();
    private readonly Button[] _secondary = new Button[2];
    private readonly Rect[] _secondaryRects = { new(386, 2, 44, 44), new(436, 2, 44, 44) };
    private IslandActivity? _selectedActivity;
    private string? _selectedActivityId;
    private System.Collections.Generic.IReadOnlyList<IslandActivity> _activities = Array.Empty<IslandActivity>();
    private int _viewGeneration;
    private ScrollViewer _detailScroll = null!;
    private Button _compactOpen = null!;
    private readonly Dictionary<string, TextBlock> _labels = new();
    private readonly Dictionary<string, Button> _buttons = new();
    private readonly Dictionary<string, Slider> _sliders = new();
    private readonly Dictionary<string, PendingControlEdit> _sliderEdits = new();
    private readonly Dictionary<string, DispatcherTimer> _sliderCommits = new();
    private readonly Image _compactArt = new() { Width = 28, Height = 28, Stretch = Stretch.UniformToFill };
    private FrameworkElement _compactBrand = null!;
    private Image _art = new() { Width = 54, Height = 54, Stretch = Stretch.UniformToFill };
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _refreshing, _wide, _playerPopupOpen, _updatingPlayers, _suppressing, _rendering, _checkingShelf;
    private string _module = "Overview", _playerSignature = "", _enabledSignature = "", _shelfSignature = "";
    private IntPtr _hwnd, _playerPopupHandle;
    private int _motionGeneration;
    private long _lastShelfCheck;
    private ComboBox? _playersCombo;
    private TextBlock? _artFallback;
    private TimerToolsPanel? _timerTools;
    private IntegrationsPanel? _integrationTools;
    private P2SystemPanel? _systemTools;
    private ClipboardHistoryPanel? _clipboardTools;
    private BatteryNoticeSnapshot? _shownBatteryNotice;
    private bool _shelfDropPending;
    private string? _shelfMessage;
    private long _shelfMessageGeneration = -1;
    private bool _compactActionBusy;
    public bool HoldsInteraction => IsKeyboardFocusWithin || _playerPopupOpen;
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
        Width = DeckWidth + 16;
        Height = TotalHeight + 8;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Content = _surface;
        _surface.Width = DeckWidth;
        _surface.Height = TotalHeight;
        _surface.HorizontalAlignment = HorizontalAlignment.Center;
        _surface.VerticalAlignment = VerticalAlignment.Top;
        _surface.Margin = new Thickness(8, 0, 8, 8);
        _surface.RenderTransform = _slide;
        _shell.Width = DeckWidth;
        _shell.Height = TotalHeight;
        _shell.VerticalAlignment = VerticalAlignment.Top;
        _shell.Clip = _clip;
        ApplyIslandResources();
        _envelope = new IslandEnvelope(_clip);
        _shell.Children.Add(_envelope);
        BuildCompact();
        _shell.Children.Add(_compact);
        _expanded = new ActivityDeck(app, Expand, app.OpenSettings) { Visibility = Visibility.Collapsed };
        _expanded.ShowHeader = false;
        _expanded.RenderTransform = _deckSlide;
        _expanded.Width = 376;
        _expanded.HorizontalAlignment = HorizontalAlignment.Left;
        _expanded.VerticalAlignment = VerticalAlignment.Top;
        _expanded.Margin = new Thickness(52, 68, 0, 0);
        _expanded.DesiredHeightChanged += (_, _) => { if (_presentation == IslandPresentation.Activity) UpdateContextSize(); };
        _expanded.ActivityActionCompleted += (_, _) => Refresh();
        _shell.Children.Add(_expanded);
        _surface.Children.Add(_shell);
        BuildDock();
        BuildDetail();
        BuildSecondaryActivities();
        SourceInitialized += (_, _) => { _hwnd = new WindowInteropHelper(this).Handle; SetWindowLong(_hwnd, -20, GetWindowLong(_hwnd, -20) | 0x08000000 | 0x80); HwndSource.FromHwnd(_hwnd)?.AddHook(WindowHook); _app.Policy.RegisterSurface(_hwnd, true, () => PixelBounds, ContainsVisiblePoint); PositionOnDisplay(); };
        PreviewMouseDown += (_, e) => { if (e.OriginalSource is DependencyObject d && Ancestor<TextBox>(d) != null) SetForegroundWindow(_hwnd); };
        PreviewMouseDown += (_, e) =>
        {
            if (e.OriginalSource is not DependencyObject source || HasPointerControl(source)) return;
            string? gesture = e.ChangedButton == MouseButton.Middle ? _app.Preferences.MiddleClickGesture
                : e.ChangedButton == MouseButton.Left && e.ClickCount == 2 ? _app.Preferences.DoubleClickGesture : null;
            if (gesture != null && gesture != "None") { PerformGesture(gesture); e.Handled = true; }
        };
        PreviewMouseWheel += (_, e) =>
        {
            if (_app.Preferences.WheelGesture != "Switch modules" || e.OriginalSource is not DependencyObject source || HasPointerControl(source)) return;
            PerformGesture(e.Delta < 0 ? "Next module" : "Previous module"); e.Handled = true;
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { _app.Controller.Dismiss(); e.Handled = true; } };
        AllowDrop = true;
        PreviewDragEnter += (_, e) =>
        {
            e.Handled = true;
            e.Effects = ShelfDropEffect(e);
            if (e.Effects != DragDropEffects.None)
                Expand("Shelf");
        };
        PreviewDragOver += (_, e) =>
        {
            e.Handled = true;
            e.Effects = ShelfDropEffect(e);
            if (e.Effects != DragDropEffects.None && _module != "Shelf")
                Expand("Shelf");
        };
        PreviewDrop += ImportShelfDrop;
        _clock.Tick += (_, _) => { Refresh(); if (_wide && _module == "Shelf" && !_checkingShelf && Environment.TickCount64 - _lastShelfCheck >= 3000) { _checkingShelf = true; _lastShelfCheck = Environment.TickCount64; _ = Task.Run(_app.Local.RefreshShelf).ContinueWith(_ => Dispatcher.BeginInvoke(() => { _checkingShelf = false; if (IsVisible && _wide && _module == "Shelf") { string current = string.Join("|", _app.Local.Shelf.Select(i => i.Id + ":" + i.Available)); if (current != _shelfSignature) BuildBody(); } })); } };
        Closed += (_, _) => { _motion.StopAll(); _clock.Stop(); _envelope.Dispose(); };
    }
    private static bool HasPointerControl(DependencyObject source)
    {
        while (source != null)
        {
            if (source is ButtonBase or TextBoxBase or Slider or ComboBox or ScrollViewer) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }
    private void PerformGesture(string action)
    {
        switch (action)
        {
            case "Expand": Expand("Overview"); break;
            case "Collapse": Collapse(); break;
            case "Dismiss": _app.Controller.Dismiss(); break;
            case "Settings": _app.OpenSettings(); break;
            case "Next module": case "Previous module":
                var modules = new[] { "Overview" }.Concat(_app.Preferences.ModuleOrder.Where(_app.Preferences.EnabledModules.Contains)).ToArray();
                int current = Array.IndexOf(modules, _module);
                Expand(modules[(Math.Max(0, current) + (action == "Next module" ? 1 : modules.Length - 1)) % modules.Length]); break;
        }
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
        if (!IsVisible || !IsHitTestVisible || Opacity <= 0 || _hwnd == IntPtr.Zero)
            return false;
        try
        {
            var p = _surface.PointFromScreen(new Point(screenPoint.X, screenPoint.Y));
            return ContainsSurfacePoint(p);
        }
        catch { return false; }
    }
    private bool ContainsSurfacePoint(Point point)
    {
        if (RoundedContains(point, _clip.Rect, _clip.RadiusX))
            return true;
        for (int i = 0; i < _secondary.Length; i++)
            if (_secondary[i].IsVisible && _secondary[i].IsHitTestVisible && _secondary[i].Opacity > 0 && RoundedContains(point, _secondaryRects[i], 22)) return true;
        return false;
    }
    private DragDropEffects ShelfDropEffect(DragEventArgs e)
    {
        var local = _app.Local;
        long generation = local.ShelfGeneration;
        var effect = ShelfSurfaceEffect(e);
        if (effect == DragDropEffects.None)
            return effect;
        try
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop, false))
                return DragDropEffects.None;
            // A COM data provider can pump messages and re-enter Reset or suppression.
            return ReferenceEquals(local, _app.Local) && generation == local.ShelfGeneration
                && ShelfSurfaceEffect(e) == effect ? effect : DragDropEffects.None;
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or ArgumentException)
        {
            return DragDropEffects.None;
        }
    }
    private DragDropEffects ShelfSurfaceEffect(DragEventArgs e)
    {
        if (_shelfDropPending || !IsVisible || !IsHitTestVisible || Opacity <= 0
            || !_app.Policy.IsEligible || !_app.Preferences.EnabledModules.Contains("Shelf"))
            return DragDropEffects.None;
        var effect = (e.AllowedEffects & DragDropEffects.Link) != 0 ? DragDropEffects.Link
            : (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
        if (effect == DragDropEffects.None)
            return effect;
        try
        {
            return ContainsSurfacePoint(e.GetPosition(_surface)) ? effect : DragDropEffects.None;
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or ArgumentException)
        {
            return DragDropEffects.None;
        }
    }
    private async void ImportShelfDrop(object sender, DragEventArgs e)
    {
        // OLE reads these values when the routed event returns, before any awaited work.
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        var local = _app.Local;
        long generation = local.ShelfGeneration;
        var effect = ShelfDropEffect(e);
        if (effect == DragDropEffects.None)
            return;
        string[] paths;
        try
        {
            if (e.Data.GetData(DataFormats.FileDrop, false) is not string[] source || source.Length == 0)
                return;
            paths = source.Take(100).ToArray();
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or ArgumentException)
        {
            if (ReferenceEquals(local, _app.Local) && generation == local.ShelfGeneration
                && ShelfSurfaceEffect(e) != DragDropEffects.None)
                RememberShelfMessage("Windows could not provide these file references. Drag the originals again.", generation);
            return;
        }
        if (!ReferenceEquals(local, _app.Local) || generation != local.ShelfGeneration || ShelfSurfaceEffect(e) != effect)
            return;
        _shelfDropPending = true;
        _shelfMessage = null;
        _shelfMessageGeneration = generation;
        e.Effects = effect; // Only Link or Copy; never authorize moving the source.
        UpdateShelfMessage();
        string? error;
        try { error = await Task.Run(() => local.AddShelfFiles(paths, generation)); }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or ArgumentException or System.IO.IOException or UnauthorizedAccessException)
        {
            error = "Crest could not add these references. Try dragging the originals again.";
        }
        finally { _shelfDropPending = false; UpdateShelfMessage(); }
        if (Dispatcher.HasShutdownStarted || !ReferenceEquals(local, _app.Local) || generation != local.ShelfGeneration)
            return;
        RememberShelfMessage(error, generation);
    }
    private bool ShowingShelf => IsVisible && _wide && _module == "Shelf"
        && _detail.IsVisible && _app.Preferences.EnabledModules.Contains("Shelf");
    private void RememberShelfMessage(string? message, long generation)
    {
        _shelfMessage = message;
        _shelfMessageGeneration = generation;
        if (ShowingShelf)
            BuildBody();
    }
    private void UpdateShelfMessage()
    {
        if (!ShowingShelf || !_labels.TryGetValue("bodyShelfNotice", out var notice))
            return;
        if (_shelfMessageGeneration != _app.Local.ShelfGeneration)
            _shelfMessage = null;
        notice.Text = _shelfDropPending ? "Adding file references…" : _shelfMessage ?? "";
        notice.Visibility = notice.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
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
        double dpi = _app.Policy.DpiScale, fit = Math.Min(screen.Width / ((DeckWidth + 16) * dpi), (System.Windows.Forms.Screen.FromRectangle(screen).WorkingArea.Bottom - screen.Top) / ((TotalHeight + 8) * dpi));
        double size = Math.Min(_app.EffectivePreferences.SizeScale, fit);
        _surface.LayoutTransform = new ScaleTransform(size, size);
        Width = (DeckWidth + 16) * size;
        Height = (TotalHeight + 8) * size;
        int width = (int)Math.Round(Width * dpi), height = (int)Math.Round(Height * dpi);
        SetWindowPos(_hwnd, IntPtr.Zero, screen.Left + (screen.Width - width) / 2, screen.Top, width, height, 0x0010 | 0x0004);
    }
    public void FocusForKeyboard()
    {
        if (!IsVisible || !_app.Policy.IsEligible || _hwnd == IntPtr.Zero)
            return;
        SetForegroundWindow(_hwnd);
        Activate();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (!IsVisible || !_app.Policy.IsEligible) return; var target = FindKeyboardControl(_presentation == IslandPresentation.Detail ? _body : _presentation == IslandPresentation.Tools ? _tabs : _presentation == IslandPresentation.Activity ? _expanded : _compact); if (target != null) { target.Focus(); Keyboard.Focus(target); } }));
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
        _compact.Width = IdleRect.Width;
        _compact.Height = 48;
        _compact.VerticalAlignment = VerticalAlignment.Top;
        _compact.HorizontalAlignment = HorizontalAlignment.Left;
        _compact.RenderTransform = _compactSlide;
        _compactSlide.X = IdleRect.X;
        _compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        _compact.ColumnDefinitions.Add(new ColumnDefinition());
        _compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        _compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        var cover = new Grid { Width = 23, Height = 23, Clip = new RectangleGeometry(new Rect(0, 0, 23, 23), 7, 7) };
        _compactGlyph = Design.Icon("\uE80F", 17);
        _compactGlyph.SetResourceReference(TextBlock.ForegroundProperty, "IslandAccent");
        _compactBrand = CrestBrand.Mark(20, "IslandAccent");
        _compactArt.Width = _compactArt.Height = 23;
        cover.Children.Add(_compactGlyph);
        cover.Children.Add(_compactBrand);
        cover.Children.Add(_compactArt);
        var coverButton = Design.Button("", OpenContext);
        coverButton.Content = cover;
        coverButton.Background = Brushes.Transparent;
        coverButton.BorderThickness = new Thickness(0);
        coverButton.Padding = new Thickness(0);
        coverButton.SetResourceReference(Control.ForegroundProperty, "IslandText");
        AutomationProperties.SetName(coverButton, "Expand current activity");
        _compact.Children.Add(coverButton);
        var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Label("compactTitle", "crest", 13, "IslandText", true);
        title.TextWrapping = TextWrapping.NoWrap;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        var subtitle = Label("compactDetail", "Idle", 11, "IslandMuted");
        subtitle.TextWrapping = TextWrapping.NoWrap;
        subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
        meta.Children.Add(title);
        meta.Children.Add(subtitle);
        _compactDetails = Design.Button("", OpenContext);
        _compactDetails.Content = meta;
        _compactDetails.Padding = new Thickness(2, 0, 2, 0);
        _compactDetails.Background = Brushes.Transparent;
        _compactDetails.BorderThickness = new Thickness(0);
        _compactDetails.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(_compactDetails, 1);
        _compact.Children.Add(_compactDetails);
        _compactPlay = Design.IconButton("\uE768", "Activity action", RunCompactAction);
        _compactPlay.Background = Brushes.Transparent;
        _compactPlay.BorderThickness = new Thickness(0);
        _compactPlay.Padding = new Thickness(8);
        _compactPlay.SetResourceReference(Control.ForegroundProperty, "IslandText");
        Grid.SetColumn(_compactPlay, 2);
        _compact.Children.Add(_compactPlay);
        _compactOpen = Design.IconButton("\uE70D", "Expand current activity", () => { if (_presentation == IslandPresentation.Compact) OpenContext(); else Collapse(); });
        _compactOpen.Background = Brushes.Transparent;
        _compactOpen.BorderThickness = new Thickness(0);
        _compactOpen.SetResourceReference(Control.ForegroundProperty, "IslandMuted");
        _compactOpen.Padding = new Thickness(8);
        Grid.SetColumn(_compactOpen, 3);
        _compact.Children.Add(_compactOpen);
    }
    private Button _compactPlay = null!;
    private Button _compactDetails = null!;
    private TextBlock _compactGlyph = null!;
    private void ExpandCompactDetails() => OpenContext();
    private async void RunCompactAction()
    {
        if (_compactActionBusy || _selectedActivity == null) return;
        var activity = _selectedActivity;
        if (activity.Kind == IslandActivityKind.BatteryWarning || activity.Kind == IslandActivityKind.Reminder
            || activity.Kind == IslandActivityKind.Timer && activity.Phase.ToString() == "Completed"
            || activity.Kind == IslandActivityKind.Focus && activity.Phase.ToString() == "Completed")
        {
            OpenActivityDetails(activity);
            return;
        }
        _compactActionBusy = true;
        _compactPlay.IsEnabled = false;
        try { await IslandActivityCatalog.InvokePrimaryAsync(_app, activity, Expand); }
        catch { ShowMessage("This activity is currently unavailable. Try again shortly."); }
        finally { _compactActionBusy = false; Refresh(); }
    }
    private void BuildDock()
    {
        _dock.Width = 440;
        _dock.HorizontalAlignment = HorizontalAlignment.Left;
        _dock.VerticalAlignment = VerticalAlignment.Top;
        _dock.Margin = new Thickness(20, 0, 0, 0);
        _dock.Padding = new Thickness(20, 18, 20, 18);
        _dock.RenderTransform = _dockSlide;
        _dock.Background = Brushes.Transparent;
        _dock.BorderThickness = new Thickness(0);
        _dock.Child = _tabs;
        _dock.Visibility = Visibility.Collapsed;
        _shell.Children.Add(_dock);
        BuildTabs();
    }
    private void BuildTabs()
    {
        _tabs.Children.Clear();
        var heading = new Grid { Margin = new Thickness(0, 0, 0, 16), Height = 40 };
        heading.ColumnDefinitions.Add(new ColumnDefinition());
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Design.Text("Your tools", 20, "IslandText", true);
        title.VerticalAlignment = VerticalAlignment.Center;
        heading.Children.Add(title);
        var close = Design.IconButton("\uE70E", "Collapse island", Collapse);
        close.Background = Brushes.Transparent;
        close.BorderThickness = new Thickness(0);
        close.SetResourceReference(Control.ForegroundProperty, "IslandMuted");
        Grid.SetColumn(close, 1);
        heading.Children.Add(close);
        _tabs.Children.Add(heading);
        var items = new[] { ("Music", "\uE8D6", "Music"), ("Timer", "\uE823", "Timers & focus"), ("Controls", "\uE9E9", "System controls"), ("Launch", "\uE8A7", "Launch shortcuts"), ("Shelf", "\uE8B7", "File shelf"), ("Capture", "\uE722", "Capture"), ("Workspaces", "\uE8A5", "Workspaces"), ("Clipboard", "\uE77F", "Clipboard"), ("Integrations", "\uE774", "Integrations") };
        var enabled = items.Where(i => _app.Preferences.EnabledModules.Contains(i.Item1)).OrderBy(i => _app.Preferences.ModuleOrder.IndexOf(i.Item1)).ToArray();
        for (int index = 0; index < enabled.Length; index += 2)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition());
            for (int offset = 0; offset < 2 && index + offset < enabled.Length; offset++)
            {
                var item = enabled[index + offset];
                var button = Design.Button(item.Item3, () => Expand(item.Item1));
                button.Height = 48;
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.Padding = new Thickness(12, 6, 12, 6);
                button.Margin = offset == 0 ? new Thickness(0, 0, 4, 0) : new Thickness(4, 0, 0, 0);
                button.BorderThickness = new Thickness(0);
                button.SetResourceReference(Control.BackgroundProperty, "IslandCard");
                button.SetResourceReference(Control.ForegroundProperty, "IslandText");
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                var icon = Design.Icon(item.Item2, 17);
                icon.SetResourceReference(TextBlock.ForegroundProperty, "IslandMuted");
                icon.Margin = new Thickness(0, 0, 10, 0);
                content.Children.Add(icon);
                var text = Design.Text(item.Item3, 12, "IslandText");
                text.VerticalAlignment = VerticalAlignment.Center;
                content.Children.Add(text);
                button.Content = content;
                AutomationProperties.SetAutomationId(button, "module-" + item.Item1);
                Grid.SetColumn(button, offset);
                row.Children.Add(button);
            }
            _tabs.Children.Add(row);
        }
        if (enabled.Length == 0) _tabs.Children.Add(Design.Text("Choose your modules in Settings.", 13, "IslandMuted"));
        var footer = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var current = Design.Button("Current activity", OpenContext);
        current.Background = Brushes.Transparent;
        current.BorderThickness = new Thickness(0);
        current.SetResourceReference(Control.ForegroundProperty, "IslandMuted");
        current.HorizontalContentAlignment = HorizontalAlignment.Left;
        current.Padding = new Thickness(0, 6, 0, 6);
        current.IsEnabled = _activities.Count > 0;
        footer.Children.Add(current);
        var settings = Design.Button("Settings", _app.OpenSettings);
        settings.Background = Brushes.Transparent;
        settings.BorderThickness = new Thickness(0);
        settings.SetResourceReference(Control.ForegroundProperty, "IslandMuted");
        settings.Padding = new Thickness(10, 6, 0, 6);
        Grid.SetColumn(settings, 1);
        footer.Children.Add(settings);
        _tabs.Children.Add(footer);
    }
    private void BuildDetail()
    {
        _detail.Width = DetailRect.Width;
        _detail.Height = DetailRect.Height;
        _detail.HorizontalAlignment = HorizontalAlignment.Left;
        _detail.VerticalAlignment = VerticalAlignment.Top;
        _detail.Margin = new Thickness(20, 0, 0, 0);
        _detail.Padding = new Thickness(20, 16, 20, 12);
        _detail.RenderTransform = _detailSlide;
        _detail.Background = Brushes.Transparent;
        _detail.BorderThickness = new Thickness(0);
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var back = Design.IconButton("\uE72B", "Back to tools", () => Expand("Tools"));
        back.Background = Brushes.Transparent;
        back.BorderThickness = new Thickness(0);
        back.SetResourceReference(Control.ForegroundProperty, "IslandMuted");
        back.Margin = new Thickness(-8, 0, 6, 0);
        head.Children.Add(back);
        var title = Label("detailTitle", "Your tools", 16, "IslandText", true);
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 1);
        head.Children.Add(title);
        var close = Design.IconButton("\uE70E", "Collapse island", Collapse);
        close.Background = Brushes.Transparent;
        close.BorderThickness = new Thickness(0);
        close.SetResourceReference(Control.ForegroundProperty, "IslandMuted");
        Grid.SetColumn(close, 2);
        head.Children.Add(close);
        grid.Children.Add(head);
        _detailScroll = new ScrollViewer { Content = _body, Padding = new Thickness(0, 0, 4, 0) };
        _body.SizeChanged += (_, _) => QueueDetailSizeUpdate();
        Grid.SetRow(_detailScroll, 1);
        grid.Children.Add(_detailScroll);
        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var status = Label("footer", _app.Preferences.AllowOverApps ? "LOCAL · OVER OTHER APPS" : "LOCAL · DESKTOP ONLY", 10, "IslandMuted");
        status.TextWrapping = TextWrapping.NoWrap;
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        footer.Children.Add(status);
        var clock = Label("clock", DateTime.Now.ToString("g", CultureInfo.CurrentCulture), 10, "IslandMuted");
        clock.TextWrapping = TextWrapping.NoWrap;
        AutomationProperties.SetName(clock, "Local date and time");
        Grid.SetColumn(clock, 1);
        footer.Children.Add(clock);
        Grid.SetRow(footer, 2);
        footer.VerticalAlignment = VerticalAlignment.Bottom;
        grid.Children.Add(footer);
        _detail.Child = grid;
        _detail.Visibility = Visibility.Collapsed;
        _shell.Children.Add(_detail);
    }
    public void Reveal(long version)
    {
        bool reversing = IsVisible;
        IsHitTestVisible = true;
        Design.Apply(_app.EffectivePreferences);
        if (!reversing)
        {
            _shownBatteryNotice = !_rendering && _app.Preferences.EnabledModules.Contains("Controls")
                ? _app.Hardware.PendingBatteryNotice : null;
            Set("footer", _app.Preferences.AllowOverApps ? "LOCAL · OVER OTHER APPS" : "LOCAL · DESKTOP ONLY");
            Collapse(false);
            Opacity = 0;
            _slide.Y = Design.ReducedMotion(_app.EffectivePreferences) ? 0 : -9;
            PositionOnDisplay();
            Show();
        }
        _app.Hardware.SetVisible(true);
        _app.RefreshVisibleIntegrations();
        _clock.Start();
        Refresh();
        _ = Task.Run(_app.Local.RefreshShelf).ContinueWith(_ => Dispatcher.BeginInvoke(() => { if (IsVisible && _module == "Shelf" && _wide) BuildBody(); }));
        var displayedNotice = _shownBatteryNotice;
        AnimateVisibility(1, 0, Design.ReducedMotion(_app.EffectivePreferences) ? 60 : 240, () =>
        {
            if (version != _app.Controller.TransitionVersion || !IsVisible)
                return;
            _app.Controller.CompleteReveal(version);
            if (displayedNotice == _shownBatteryNotice)
                AcknowledgeShownBatteryNotice();
        });
    }
    private void AcknowledgeShownBatteryNotice()
    {
        if (_shownBatteryNotice == null || _rendering || !IsVisible || Opacity < 0.99
            || !_app.Preferences.EnabledModules.Contains("Controls"))
            return;
        bool controlsNoticeVisible = _module == "Controls" && _detail.IsVisible && _detail.Opacity >= 0.99
            && _labels.TryGetValue("bodyBatteryNotice", out var noticeLabel) && noticeLabel.IsVisible;
        if (_presentation == IslandPresentation.Compact || _presentation == IslandPresentation.Activity && _selectedActivity?.Kind == IslandActivityKind.BatteryWarning && _expanded.Opacity >= 0.99 || controlsNoticeVisible)
            _app.Hardware.AcknowledgeBatteryNotice(_shownBatteryNotice.Id);
    }
    public void CloseAnimated(long version) => AnimateVisibility(0, -8, Design.ReducedMotion(_app.EffectivePreferences) ? 60 : 190, () => { if (version != _app.Controller.TransitionVersion) return; Suppress(); _app.Controller.CompleteClose(version); });
    private void AnimateVisibility(double opacity, double y, int ms, Action completed)
    {
        int generation = ++_motionGeneration;
        if (Design.ReducedMotion(_app.EffectivePreferences))
        {
            _motion.Stop(this, OpacityProperty);
            _motion.Stop(_slide, TranslateTransform.YProperty);
            _slide.Y = 0;
            var fade = new DoubleAnimation(Opacity, opacity, TimeSpan.FromMilliseconds(60));
            fade.Completed += (_, _) => { if (generation == _motionGeneration) { Opacity = opacity; BeginAnimation(OpacityProperty, null); completed(); } };
            BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            _motion.Double(this, OpacityProperty, opacity, _app.EffectivePreferences.MotionSpeed, .16, completed: () => { if (generation == _motionGeneration) completed(); });
            _motion.Double(_slide, TranslateTransform.YProperty, y, _app.EffectivePreferences.MotionSpeed, .26, .92);
        }
    }
    public void Suppress()
    {
        if (_suppressing)
            return;
        _suppressing = true;
        try
        {
            ++_motionGeneration;
            _motion.StopAll();
            IsHitTestVisible = false;
            ClosePlayerPopup();
            Hide();
            BeginAnimation(OpacityProperty, null);
            _slide.BeginAnimation(TranslateTransform.YProperty, null);
            _clip.BeginAnimation(RectangleGeometry.RectProperty, null);
            ++_viewGeneration;
            _dock.BeginAnimation(OpacityProperty, null);
            _detail.BeginAnimation(OpacityProperty, null);
            _expanded.BeginAnimation(OpacityProperty, null);
            _compact.BeginAnimation(OpacityProperty, null);
            _detailSlide.BeginAnimation(TranslateTransform.YProperty, null);
            _clock.Stop();
            _shownBatteryNotice = null;
            foreach (var secondary in _secondary) { secondary.Visibility = Visibility.Collapsed; secondary.IsHitTestVisible = false; }
            _app.Hardware.SetVisible(false);
            Keyboard.ClearFocus();
        }
        finally { _suppressing = false; }
    }
    public void Expand(string module)
    {
        if (!IsVisible && !_rendering) return;
        if (module == "Overview") { OpenContext(); return; }
        if (module == "Tools") { ShowTools(); return; }
        if (!_app.Preferences.EnabledModules.Contains(module)) { OpenContext(); return; }
        bool keyboard = IsVisible && GetForegroundWindow() == _hwnd && IsKeyboardFocusWithin;
        if (_presentation == IslandPresentation.Detail && _module == module) { SelectActivityDetails(_selectedActivity); Refresh(); UpdateDetailSize(); return; }
        ClosePlayerPopup();
        _module = module;
        Set("detailTitle", module switch { "Controls" => "System controls", "Launch" => "Launch shortcuts", "Timer" => "Timers & focus", "Shelf" => "File shelf", "Capture" => "Capture references", "Workspaces" => "Workspaces", "Clipboard" => "Clipboard", _ => module });
        BuildBody();
        SelectActivityDetails(_selectedActivity);
        _detailScroll.ScrollToTop();
        TransitionTo(IslandPresentation.Detail, GetDetailRect());
        if (keyboard) FocusForKeyboard();
    }
    private void Collapse() => Collapse(true);
    private void Collapse(bool animated)
    {
        bool keyboard = IsVisible && GetForegroundWindow() == _hwnd && IsKeyboardFocusWithin;
        ClosePlayerPopup();
        _selectedActivityId = null;
        ReadActivities();
        _module = "Overview";
        TransitionTo(IslandPresentation.Compact, _compactRect, animated);
        if (keyboard) FocusForKeyboard();
    }
    private void AnimateShape(Rect target)
    {
        _targetRect = target;
        if (Design.ReducedMotion(_app.EffectivePreferences) || _rendering)
        {
            _motion.Stop(_clip, RectangleGeometry.RectProperty);
            _motion.Stop(_clip, RectangleGeometry.RadiusXProperty);
            _motion.Stop(_clip, RectangleGeometry.RadiusYProperty);
            _clip.Rect = target;
            _clip.RadiusX = _clip.RadiusY = target.Height <= 48 ? 24 : 30;
            return;
        }
        _motion.Rect(_clip, RectangleGeometry.RectProperty, target, _app.EffectivePreferences.MotionSpeed);
        _motion.Double(_clip, RectangleGeometry.RadiusXProperty, target.Height <= 48 ? 24 : 30, _app.EffectivePreferences.MotionSpeed, .32, .86);
        _motion.Double(_clip, RectangleGeometry.RadiusYProperty, target.Height <= 48 ? 24 : 30, _app.EffectivePreferences.MotionSpeed, .32, .86);
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
        _integrationTools = null;
        _systemTools = null;
        _clipboardTools = null;
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
            case "Capture": BuildCaptures(); break;
            case "Workspaces": BuildWorkspaces(); break;
            case "Integrations": BuildIntegrations(); break;
            case "Clipboard":
                _clipboardTools = new ClipboardHistoryPanel(_app.ClipboardHistory, _app.ClipboardListener.CopyToClipboard, ShowMessage, () => _app.ClipboardListener.Status);
                _body.Children.Add(_clipboardTools); break;
        }
        Refresh();
        if (_presentation == IslandPresentation.Detail) UpdateDetailSize();
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
        play.SetResourceReference(Control.ForegroundProperty, "AccentText");
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
        var notice = Label("bodyBatteryNotice", "", 11, "Muted");
        notice.Visibility = Visibility.Collapsed;
        battery.Children.Add(notice);
        _body.Children.Add(Design.Card(battery, new Thickness(12)));
        AddSlider("volume", "System volume", () => _app.Hardware.Volume, v => _app.Hardware.SetVolumeAsync(v));
        AddSlider("brightness", "Laptop brightness", () => _app.Hardware.Brightness, v => _app.Hardware.SetBrightnessAsync(v));
        var mute = Design.Button("Mute audio", () => Run(_app.Hardware.ToggleMuteAsync));
        _buttons["mute"] = mute;
        _systemTools = new P2SystemPanel(_app.Hardware, _app.OpenWindowsTool, ShowMessage);
        _body.Children.Add(_systemTools);
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
        _body.Children.Add(Label("bodyShelfCount", $"{_app.Local.Shelf.Count} / 20 references · {_app.Local.Shelf.Count(x => x.Pinned)} pinned", 11, "Muted"));
        var notice = Label("bodyShelfNotice", "", 11, "Muted");
        notice.Visibility = Visibility.Collapsed;
        notice.Margin = new Thickness(0, 8, 0, 0);
        _body.Children.Add(notice);
        UpdateShelfMessage();
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
            var pin = Design.IconButton("\uE718", (item.Pinned ? "Unpin " : "Pin ") + item.Name, () => { _app.Local.SetShelfPinned(item.Id, !item.Pinned); BuildBody(); });
            if (item.Pinned) pin.SetResourceReference(Control.ForegroundProperty, "Accent");
            Grid.SetColumn(pin, 2); row.Children.Add(pin);
            _body.Children.Add(row);
            if (!item.Available)
            {
                _body.Children.Add(Design.Text(item.Path, 11, "Muted"));
                var recovery = new WrapPanel();
                recovery.Children.Add(Design.Button("Locate " + item.Name, () => RelinkShelf(item)));
                var folder = _app.Local.ShelfRecoveryFolder(item.Id);
                if (folder != null) recovery.Children.Add(Design.Button("Open nearby folder", () => _ = _app.OpenTarget(() => _app.Local.Launch(new LauncherEntry { Target = folder, Kind = "Folder" }))));
                _body.Children.Add(recovery);
            }
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
        _motion.StopAll();
        foreach (string theme in new[] { "Dark", "Light" })
        {
            _app.Preferences.Theme = theme;
            Design.Apply(_app.EffectivePreferences);
            ApplyIslandResources();
            Collapse(false);
            Refresh();
            SaveSurfacePreview(System.IO.Path.Combine(directory, theme.ToLowerInvariant() + "-compact.png"));
            Expand("Overview");
            SaveSurfacePreview(System.IO.Path.Combine(directory, theme.ToLowerInvariant() + "-overview.png"));
            Expand("Tools");
            SaveSurfacePreview(System.IO.Path.Combine(directory, theme.ToLowerInvariant() + "-tools.png"));
            foreach (string module in ModuleCatalog.All)
            {
                if (!_app.Preferences.EnabledModules.Contains(module)) continue;
                Expand(module);
                SaveSurfacePreview(System.IO.Path.Combine(directory, theme.ToLowerInvariant() + "-" + module.ToLowerInvariant() + ".png"));
            }
        }
        _rendering = false;
        _app.Preferences.Theme = previous;
        Design.Apply(_app.EffectivePreferences);
        ApplyIslandResources();
        Collapse(false);
    }
    private void SaveSurfacePreview(string path)
    {
        _surface.Measure(new Size(DeckWidth, TotalHeight));
        _surface.Arrange(new Rect(0, 0, DeckWidth, TotalHeight));
        _surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)DeckWidth * 2, (int)TotalHeight * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(_surface);
        var bounds = _targetRect;
        for (int i = 0; i < _secondary.Length; i++) if (_secondary[i].IsVisible) bounds.Union(_secondaryRects[i]);
        int x = Math.Max(0, (int)Math.Floor(bounds.X * 2) - 2), y = Math.Max(0, (int)Math.Floor(bounds.Y * 2));
        int right = Math.Min((int)DeckWidth * 2, (int)Math.Ceiling(bounds.Right * 2) + 2);
        int bottom = Math.Min((int)TotalHeight * 2, (int)Math.Ceiling(bounds.Bottom * 2) + 2);
        SaveBitmap(new CroppedBitmap(bitmap, new Int32Rect(x, y, right - x, bottom - y)), path);
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
        ApplyIslandResources();
        if (Design.ReducedMotion(_app.EffectivePreferences))
            FinishMotionForReducedPreference();
        string enabled = string.Join("|", _app.Preferences.ModuleOrder.Where(m => _app.Preferences.EnabledModules.Contains(m)));
        if (enabled != _enabledSignature)
        {
            _enabledSignature = enabled;
            BuildTabs();
            if (_wide && _module is not "Overview" and not "Tools" && !_app.Preferences.EnabledModules.Contains(_module))
            {
                Expand("Overview");
                return;
            }
        }
        _refreshing = true;
        try
        {
            var music = _app.Hardware.Music;
            var battery = _app.Hardware.Battery;
            var notice = _app.Preferences.EnabledModules.Contains("Controls") ? _shownBatteryNotice : null;
            ReadActivities();
            var now = DateTime.Now;
            Set("clock", now.ToString("g", CultureInfo.CurrentCulture));
            AutomationProperties.SetHelpText(_labels["clock"], now.ToString("F", CultureInfo.CurrentCulture));
            if (_labels.TryGetValue("bodyBatteryNotice", out var noticeLabel))
            {
                noticeLabel.Text = notice == null ? "" : (notice.Severity == BatteryWarningLevel.Critical ? "Earlier critical battery warning: " : "Earlier low-battery warning: ")
                    + notice.Percent.ToString(CultureInfo.CurrentCulture) + "% reported by Windows.";
                noticeLabel.Visibility = notice == null ? Visibility.Collapsed : Visibility.Visible;
            }
            if (!_wide)
                return;
            if (_presentation == IslandPresentation.Activity) { _expanded.ShowActivity(_selectedActivity); _expanded.Refresh(); UpdateContextSize(); }
            if (_presentation != IslandPresentation.Detail || _detail.Visibility != Visibility.Visible)
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
                _systemTools?.Refresh();
            }
            if (_module == "Timer")
            {
                _timerTools?.Refresh();
            }
            if (_module == "Clipboard") _clipboardTools?.Refresh();
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
