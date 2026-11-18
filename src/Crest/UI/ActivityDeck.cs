using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Crest.Core;
using Crest.Features;

namespace Crest.UI;

/// <summary>The wide activity view shares the island's one visibility controller.</summary>
public sealed class ActivityDeck : UserControl
{
    private static ImageSource? _focusArtwork;
    private static bool _focusArtworkLoaded;
    private readonly App _app;
    private readonly Action<string> _expand;
    private readonly Action _settings;
    private readonly Button[] _launcherButtons = new Button[2];
    private readonly LauncherEntry?[] _launcherEntries = new LauncherEntry?[2];
    private readonly TextBlock _focusTime = Label("25:00", 16, "IslandText", true);
    private readonly TextBlock _focusCaption = Label("FOCUS", 10, "IslandMuted", true);
    private readonly TextBlock _focusHint = Label("A little room to focus.", 10, "IslandMuted");
    private readonly TextBlock _focusActionText = Label("Start", 11, "IslandText", true);
    private readonly Button _focusAction;
    private readonly Button _focusHeader;
    private readonly Button _musicHeader;
    private readonly Button _albumButton;
    private readonly Image _art = new() { Width = 86, Height = 86, Stretch = Stretch.UniformToFill };
    private readonly Border _artFallback;
    private readonly TextBlock _title = Label("Nothing playing", 14, "IslandText", true);
    private readonly TextBlock _artist = Label("Open a music app", 11, "IslandMuted");
    private readonly TextBlock _player = Label("MEDIA CONTROLS", 9, "IslandMuted", true);
    private readonly Button _previous;
    private readonly Button _play;
    private readonly Button _next;
    private readonly TextBlock _playIcon = Glyph("\uE768", 16);
    private readonly Slider _seek = new() { Minimum = 0, Maximum = 1, MinHeight = 32, Height = 32, IsMoveToPointEnabled = true, SmallChange = 5, LargeChange = 15 };
    private readonly TextBlock _elapsed = Label("0:00", 9, "IslandMuted");
    private readonly TextBlock _duration = Label("0:00", 9, "IslandMuted");
    private readonly DispatcherTimer _seekCommit = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly Button _batteryButton;
    private readonly Path _batteryArc = new() { StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false };
    private readonly TextBlock _batteryPercent = Label("—", 17, "IslandText", true);
    private readonly TextBlock _batteryStatus = Label("Checking…", 10, "IslandMuted");
    private readonly TextBlock _chargingIcon = Glyph("\uE945", 11);
    private bool _refreshing;
    private bool _focusBusy;
    private bool _transportBusy;
    private bool _seekPointerDown;
    private readonly PendingControlEdit _seekEdit = new();
    private bool _chargingAnimating;
    private int _lastBatteryPercent = -1;

    public ActivityDeck(App app, Action<string> expand, Action settings)
    {
        _app = app;
        _expand = expand;
        _settings = settings;
        Width = 820;
        Height = 150;
        FontFamily = new FontFamily("Segoe UI");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        var deck = new Grid { Margin = new Thickness(12, 8, 12, 8) };
        foreach (double width in new[] { 40d, 8d, 188d, 8d, -1d, 8d, 112d })
            deck.ColumnDefinitions.Add(new ColumnDefinition { Width = width < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
        Content = deck;

        var rail = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        for (int index = 0; index < 2; index++)
        {
            int slot = index;
            var button = IconButton("\uE8A5", "No shortcut configured", () => LaunchShortcut(slot), 32, "IslandShell");
            button.Margin = new Thickness(0, 0, 0, 8);
            _launcherButtons[index] = button;
            rail.Children.Add(button);
        }
        rail.Children.Add(IconButton("\uE710", "Add shortcuts in Settings", _settings, 32, "IslandCard"));
        Place(deck, rail, 0);

        var focus = new Grid { Margin = new Thickness(10, 8, 10, 8) };
        focus.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
        focus.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        focus.Children.Add(FocusCharacter());
        var focusControls = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var focusHeaderContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        _focusCaption.FontSize = 9;
        _focusCaption.LineHeight = 12;
        _focusTime.FontSize = 22;
        _focusTime.LineHeight = 28;
        _focusTime.SetValue(Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        focusHeaderContent.Children.Add(_focusCaption);
        focusHeaderContent.Children.Add(_focusTime);
        _focusHeader = Button(focusHeaderContent, "Open timer controls", () => _expand("Timer"), 8, "Transparent");
        _focusHeader.Padding = new Thickness(0);
        _focusHeader.Height = 44;
        _focusHeader.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        focusControls.Children.Add(_focusHeader);
        _focusActionText.Foreground = new SolidColorBrush(Color.FromRgb(6, 29, 43));
        _focusAction = Button(_focusActionText, "Start a twenty-five minute focus timer", FocusAction, 18, "Transparent");
        _focusAction.Background = new SolidColorBrush(Color.FromRgb(72, 201, 250));
        _focusAction.Padding = new Thickness(12, 0, 12, 0);
        _focusAction.Height = 32;
        _focusAction.Width = _focusAction.MinWidth = 64;
        _focusAction.Padding = new Thickness(7, 0, 7, 0);
        _focusAction.HorizontalAlignment = HorizontalAlignment.Left;
        _focusAction.VerticalAlignment = VerticalAlignment.Center;
        _focusAction.Margin = new Thickness(0, 5, 0, 0);
        focusControls.Children.Add(_focusAction);
        Grid.SetColumn(focusControls, 1);
        focus.Children.Add(focusControls);
        _focusHint.Visibility = Visibility.Collapsed;
        Place(deck, Card(focus), 2);

        var music = new Grid { Margin = new Thickness(12, 10, 12, 10) };
        music.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        music.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        music.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var artwork = new Grid { Width = 86, Height = 86, Clip = new RectangleGeometry(new Rect(0, 0, 86, 86), 13, 13) };
        var emptyArtwork = Glyph("\uE189", 31);
        emptyArtwork.Foreground = new SolidColorBrush(Color.FromRgb(207, 222, 226));
        _artFallback = new Border
        {
            CornerRadius = new CornerRadius(13),
            Background = new LinearGradientBrush(Color.FromRgb(32, 58, 67), Color.FromRgb(18, 31, 39), 55),
            Child = emptyArtwork
        };
        artwork.Children.Add(_artFallback);
        artwork.Children.Add(_art);
        _albumButton = Button(artwork, "Open music controls", () => _expand("Music"), 13, "Transparent");
        _albumButton.Padding = new Thickness(0);
        _albumButton.Width = 86;
        _albumButton.Height = 86;
        _albumButton.VerticalAlignment = VerticalAlignment.Center;
        music.Children.Add(_albumButton);
        var musicContent = new Grid();
        musicContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
        musicContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        musicContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        var metadata = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _player.LineHeight = 12;
        _title.LineHeight = 18;
        _artist.LineHeight = 16;
        metadata.Children.Add(_player);
        metadata.Children.Add(_title);
        metadata.Children.Add(_artist);
        _musicHeader = Button(metadata, "Open music controls", () => _expand("Music"), 8, "Transparent");
        _musicHeader.Padding = new Thickness(0);
        _musicHeader.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        musicContent.Children.Add(_musicHeader);
        var transport = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _previous = IconButton("\uE892", "Previous track", () => Transport(_app.Hardware.PreviousAsync), 32, "Transparent");
        _previous.Margin = new Thickness(0, 0, 10, 0);
        _playIcon.Foreground = new SolidColorBrush(Color.FromRgb(7, 30, 27));
        _play = Button(_playIcon, "Play", () => Transport(_app.Hardware.TogglePlaybackAsync), 18, "Transparent");
        _play.Width = _play.Height = 34;
        _play.Padding = new Thickness(0);
        _play.Background = new SolidColorBrush(Color.FromRgb(130, 222, 200));
        _next = IconButton("\uE893", "Next track", () => Transport(_app.Hardware.NextAsync), 32, "Transparent");
        _next.Margin = new Thickness(10, 0, 0, 0);
        transport.Children.Add(_previous);
        transport.Children.Add(_play);
        transport.Children.Add(_next);
        Grid.SetRow(transport, 1);
        musicContent.Children.Add(transport);
        var seeking = new Grid();
        seeking.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        seeking.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        seeking.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        _elapsed.VerticalAlignment = _duration.VerticalAlignment = VerticalAlignment.Center;
        _duration.TextAlignment = TextAlignment.Right;
        _seek.Template = SeekTemplate();
        _seek.SetResourceReference(Control.ForegroundProperty, "IslandAccent");
        AutomationProperties.SetName(_seek, "Seek track");
        AutomationProperties.SetHelpText(_seek, "Drag then release, or use the arrow keys. Available when the player supports seeking.");
        _seek.PreviewMouseLeftButtonDown += (_, _) => _seekPointerDown = true;
        _seek.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (!_seekPointerDown)
                return;
            ScheduleSeek();
            _seekPointerDown = false;
        };
        _seek.KeyUp += (_, e) =>
        {
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
                ScheduleSeek();
        };
        _seek.ValueChanged += (_, _) =>
        {
            if (!_refreshing)
            {
                _seekEdit.Record(_seek.Value);
                _elapsed.Text = Time(_seek.Value);
                ScheduleSeek();
            }
        };
        _seekCommit.Tick += async (_, _) => await CommitSeekAsync();
        seeking.Children.Add(_elapsed);
        Grid.SetColumn(_seek, 1);
        seeking.Children.Add(_seek);
        Grid.SetColumn(_duration, 2);
        seeking.Children.Add(_duration);
        Grid.SetRow(seeking, 2);
        musicContent.Children.Add(seeking);
        Grid.SetColumn(musicContent, 2);
        music.Children.Add(musicContent);
        Place(deck, Card(music), 4);

        var battery = new Grid { Margin = new Thickness(10, 8, 10, 8) };
        battery.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
        battery.RowDefinitions.Add(new RowDefinition { Height = new GridLength(76) });
        battery.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        var batteryCaption = Label("LAPTOP", 9, "IslandMuted", true);
        batteryCaption.TextAlignment = TextAlignment.Center;
        battery.Children.Add(batteryCaption);
        var ring = new Grid { Width = 68, Height = 68, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var ringTrack = new Ellipse { Margin = new Thickness(5), StrokeThickness = 4, IsHitTestVisible = false };
        ringTrack.SetResourceReference(Shape.StrokeProperty, "IslandLine");
        _batteryArc.Stroke = new SolidColorBrush(Color.FromRgb(130, 222, 200));
        _batteryPercent.HorizontalAlignment = HorizontalAlignment.Center;
        _batteryPercent.VerticalAlignment = VerticalAlignment.Center;
        ring.Children.Add(ringTrack);
        ring.Children.Add(_batteryArc);
        ring.Children.Add(_batteryPercent);
        Grid.SetRow(ring, 1);
        battery.Children.Add(ring);
        var batteryFooter = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _chargingIcon.Margin = new Thickness(0, 0, 3, 0);
        _chargingIcon.Foreground = new SolidColorBrush(Color.FromRgb(130, 222, 200));
        _batteryStatus.MaxWidth = 82;
        batteryFooter.Children.Add(_chargingIcon);
        batteryFooter.Children.Add(_batteryStatus);
        Grid.SetRow(batteryFooter, 2);
        battery.Children.Add(batteryFooter);
        _batteryButton = Button(battery, "Open battery and laptop controls", () => _expand("Controls"), 20, "IslandCard");
        _batteryButton.Padding = new Thickness(0);
        _batteryButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _batteryButton.VerticalContentAlignment = VerticalAlignment.Stretch;
        _batteryButton.BorderThickness = new Thickness(1);
        _batteryButton.SetResourceReference(Control.BorderBrushProperty, "IslandOutline");
        Place(deck, _batteryButton, 6);

        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                _seekCommit.Stop();
                _seekPointerDown = false;
                if (_seekEdit.NeedsWrite && !_seekEdit.IsWriting) _ = CommitSeekAsync(allowHidden: true);
                else if (!_seekEdit.IsWriting) _seekEdit.Cancel();
                StopChargingMotion();
            }
            else
                Refresh();
        };
        Refresh();
    }

    private void ScheduleSeek()
    {
        if (!_seek.IsEnabled || !IsVisible || !_seekEdit.NeedsWrite)
            return;
        _seekCommit.Stop();
        _seekCommit.Start();
    }
    private async Task CommitSeekAsync(bool allowHidden = false)
    {
        _seekCommit.Stop();
        if (!_seek.IsEnabled || (!IsVisible && !allowHidden)) { _seekEdit.Cancel(); return; }
        if (_seekPointerDown && !allowHidden) return;
        if (!_seekEdit.TryBeginWrite(out var write)) return;
        bool succeeded = false;
        try { await Task.Run(() => _app.Hardware.SeekAsync(write.Value)); succeeded = true; }
        catch (Exception error) { App.WriteDiagnostic("seek-fault:" + error.GetType().Name); }
        finally
        {
            bool newerEdit = _seekEdit.CompleteWrite(write, Environment.TickCount64, succeeded && IsVisible ? 1500 : 0);
            if (newerEdit)
            {
                if (IsVisible) ScheduleSeek();
                else await CommitSeekAsync(allowHidden: true);
            }
            if (IsVisible) Refresh();
        }
    }

    private async void LaunchShortcut(int slot)
    {
        var entry = _launcherEntries[slot];
        if (entry is not null)
            await _app.OpenTarget(() => _app.Local.Launch(entry));
    }

    private async void FocusAction()
    {
        if (_focusBusy)
            return;
        _focusBusy = true;
        _focusAction.IsEnabled = false;
        var phase = _app.Local.Timer.Phase;
        try
        {
            await Task.Run(() =>
            {
                switch (phase)
                {
                    case TimerPhase.Running:
                        _app.Local.PauseTimer();
                        break;
                    case TimerPhase.Paused:
                        _app.Local.ResumeTimer();
                        break;
                    case TimerPhase.Completed:
                        _app.Local.AcknowledgeTimer();
                        break;
                    default:
                        _app.Local.StartTimer(TimeSpan.FromMinutes(25));
                        break;
                }
            });
        }
        finally { _focusBusy = false; Refresh(); }
    }

    private async void Transport(Func<Task> command)
    {
        if (_transportBusy)
            return;
        _transportBusy = true;
        Refresh();
        try
        {
            await Task.Run(command);
        }
        finally { _transportBusy = false; Refresh(); }
    }

    public void Refresh()
    {
        if (_refreshing)
            return;
        _refreshing = true;
        try
        {
            bool timerEnabled = _app.Preferences.EnabledModules.Contains("Timer");
            var timer = _app.Local.Timer;
            _focusTime.Text = Time(timer.Phase == TimerPhase.Idle ? 25 * 60 : timer.RemainingSeconds);
            _focusTime.FontSize = timer.RemainingSeconds >= 3600 ? 18 : 22;
            _focusCaption.Text = timer.Phase == TimerPhase.Completed ? "COMPLETE" : "FOCUS";
            _focusHint.Text = !timerEnabled ? "Timer is off in Settings." : timer.Phase switch
            {
                TimerPhase.Running => "One thing at a time.",
                TimerPhase.Paused => "Ready when you are.",
                TimerPhase.Completed => "Take a little break.",
                _ => "A little room to focus."
            };
            _focusActionText.Text = _focusBusy ? "…" : timer.Phase switch
            {
                TimerPhase.Running => "Pause",
                TimerPhase.Paused => "Resume",
                TimerPhase.Completed => "Done",
                _ => "Start"
            };
            _focusAction.IsEnabled = timerEnabled && !_focusBusy;
            _focusHeader.IsEnabled = timerEnabled;
            AutomationProperties.SetName(_focusAction, timer.Phase switch
            {
                TimerPhase.Running => "Pause timer",
                TimerPhase.Paused => "Resume timer",
                TimerPhase.Completed => "Acknowledge completed timer",
                _ => "Start a twenty-five minute focus timer"
            });
            AutomationProperties.SetName(_focusHeader, "Open timer controls, " + timer.Phase + ", " + _focusTime.Text);

            bool musicEnabled = _app.Preferences.EnabledModules.Contains("Music");
            var media = _app.Hardware.Music;
            _title.Text = !musicEnabled ? "Music is off" : media.Title;
            _artist.Text = !musicEnabled ? "Enable it in Settings" : media.HasSession ? media.Artist : "Open a music app";
            _player.Text = media.HasSession && musicEnabled ? media.PlayerName.ToUpper(CultureInfo.CurrentCulture) : "MEDIA CONTROLS";
            var artwork = musicEnabled ? media.Artwork : null;
            if (!ReferenceEquals(_art.Source, artwork))
                _art.Source = artwork;
            _artFallback.Visibility = _art.Source is null ? Visibility.Visible : Visibility.Collapsed;
            _musicHeader.IsEnabled = _albumButton.IsEnabled = musicEnabled;
            _previous.IsEnabled = musicEnabled && media.CanPrevious && !_transportBusy;
            _next.IsEnabled = musicEnabled && media.CanNext && !_transportBusy;
            _play.IsEnabled = musicEnabled && media.CanPlayPause && !_transportBusy;
            _playIcon.Text = media.IsPlaying ? "\uE769" : "\uE768";
            AutomationProperties.SetName(_play, media.IsPlaying ? "Pause playback" : "Play");
            AutomationProperties.SetName(_musicHeader, !musicEnabled ? "Music is disabled in Settings" : media.HasSession ? "Open music controls, " + media.Title + ", " + media.Artist + ", " + media.PlayerName : media.Status);
            AutomationProperties.SetName(_albumButton, AutomationProperties.GetName(_musicHeader));
            _seek.IsEnabled = musicEnabled && media.CanSeek && !_transportBusy;
            _seek.Maximum = Math.Max(1, media.DurationSeconds);
            if (!musicEnabled || !media.CanSeek) { _seekCommit.Stop(); _seekEdit.Cancel(); }
            double reportedPosition = _seekEdit.DisplayValue(media.PositionSeconds, Environment.TickCount64, 2);
            if (!_seekPointerDown)
                _seek.Value = Math.Clamp(reportedPosition, 0, _seek.Maximum);
            _elapsed.Text = Time(_seek.Value);
            _duration.Text = Time(media.DurationSeconds);

            bool controlsEnabled = _app.Preferences.EnabledModules.Contains("Controls");
            var battery = _app.Hardware.Battery;
            int percent = battery.Available && controlsEnabled ? Math.Clamp(battery.Percent, 0, 100) : -1;
            if (percent != _lastBatteryPercent)
            {
                _batteryArc.Data = RingArc(percent);
                _lastBatteryPercent = percent;
            }
            _batteryPercent.Text = percent < 0 ? "—" : percent.ToString(CultureInfo.CurrentCulture) + "%";
            _batteryStatus.Text = !controlsEnabled ? "Controls off" : battery.Available ? battery.Status : "No battery";
            _batteryButton.IsEnabled = controlsEnabled;
            _chargingIcon.Visibility = controlsEnabled && battery.Available && battery.Charging ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(_batteryButton, battery.Available ? "Open laptop controls, battery " + battery.Percent + " percent, " + battery.Status : "Open laptop controls, " + battery.Status);
            if (IsVisible && controlsEnabled && battery.Charging && !Design.ReducedMotion(_app.Preferences))
            {
                if (!_chargingAnimating)
                {
                    _chargingAnimating = true;
                    _chargingIcon.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(1100)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                }
            }
            else
                StopChargingMotion();

            bool launcherEnabled = _app.Preferences.EnabledModules.Contains("Launch");
            for (int index = 0; index < 2; index++)
            {
                var entry = index < _app.Preferences.Launchers.Count ? _app.Preferences.Launchers[index] : null;
                _launcherEntries[index] = entry;
                _launcherButtons[index].IsEnabled = launcherEnabled && entry is not null;
                string glyph = entry?.Kind switch
                {
                    "Folder" => "\uE8B7",
                    "Website" => "\uE774",
                    "Shortcut" => "\uE71B",
                    _ => index == 1 ? "\uE8B7" : "\uE8A5"
                };
                if (_launcherButtons[index].Content is TextBlock icon)
                    icon.Text = glyph;
                AutomationProperties.SetName(_launcherButtons[index], entry is null ? "No shortcut configured" : "Open " + entry.Name);
            }
        }
        finally { _refreshing = false; }
    }

    private void StopChargingMotion()
    {
        if (!_chargingAnimating)
            return;
        _chargingAnimating = false;
        _chargingIcon.BeginAnimation(OpacityProperty, null);
        _chargingIcon.Opacity = 1;
    }

    private static Geometry? RingArc(int percent)
    {
        if (percent <= 0)
            return null;
        if (percent >= 100)
            return new EllipseGeometry(new Point(34, 34), 29, 29);
        double angle = percent / 100d * Math.PI * 2 - Math.PI / 2;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(34, 5), false, false);
            context.ArcTo(new Point(34 + 29 * Math.Cos(angle), 34 + 29 * Math.Sin(angle)), new Size(29, 29), 0, percent > 50, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static FrameworkElement FocusCharacter()
    {
        if (!_focusArtworkLoaded)
        {
            _focusArtworkLoaded = true;
            try
            {
                var artwork = new BitmapImage();
                artwork.BeginInit();
                artwork.UriSource = new Uri("pack://application:,,,/Crest;component/focus-orb.png", UriKind.Absolute);
                artwork.CacheOption = BitmapCacheOption.OnLoad;
                artwork.DecodePixelWidth = 128;
                artwork.EndInit();
                artwork.Freeze();
                _focusArtwork = artwork;
            }
            catch { _focusArtwork = null; }
        }
        if (_focusArtwork is not null)
            return new Image { Source = _focusArtwork, Width = 64, Height = 64, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
        var canvas = new Canvas { Width = 58, Height = 60, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
        var shadow = new Ellipse { Width = 44, Height = 7, Fill = new SolidColorBrush(Color.FromArgb(55, 72, 201, 250)) };
        Canvas.SetLeft(shadow, 7);
        Canvas.SetTop(shadow, 48);
        canvas.Children.Add(shadow);
        var body = new Path
        {
            Data = Geometry.Parse("M 10,28 C 4,19 9,11 17,9 C 20,1 33,2 39,8 C 51,7 57,18 51,27 C 57,38 48,48 39,46 C 32,54 19,50 16,45 C 4,45 0,35 10,28 Z"),
            Fill = new LinearGradientBrush(Color.FromRgb(94, 218, 255), Color.FromRgb(37, 171, 235), 90)
        };
        canvas.Children.Add(body);
        foreach (double x in new[] { 21d, 34d })
        {
            var eye = new Ellipse { Width = 3.5, Height = 5.5, Fill = new SolidColorBrush(Color.FromRgb(4, 44, 68)) };
            Canvas.SetLeft(eye, x);
            Canvas.SetTop(eye, 24);
            canvas.Children.Add(eye);
        }
        var smile = new Path { Data = Geometry.Parse("M 25,33 Q 29,36 33,33"), Stroke = new SolidColorBrush(Color.FromRgb(4, 44, 68)), StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        canvas.Children.Add(smile);
        return new Viewbox { Child = canvas, Width = 64, Height = 64, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    }

    private static TextBlock Label(string text, double size, string color, bool bold = false)
    {
        var label = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        label.SetResourceReference(TextBlock.ForegroundProperty, color);
        return label;
    }

    private static TextBlock Glyph(string glyph, double size)
    {
        var text = Label(glyph, size, "IslandText");
        text.FontFamily = new FontFamily("Segoe Fluent Icons");
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        return text;
    }

    private static Border Card(UIElement content)
    {
        var card = new Border { CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), Child = content };
        card.SetResourceReference(Border.BackgroundProperty, "IslandCard");
        card.SetResourceReference(Border.BorderBrushProperty, "IslandOutline");
        return card;
    }

    private static Button IconButton(string glyph, string name, Action action, double size, string background)
    {
        var button = Button(Glyph(glyph, 15), name, action, size / 2, background);
        button.Width = button.Height = size;
        button.Padding = new Thickness(0);
        return button;
    }

    private static Button Button(UIElement content, string name, Action action, double radius, string background)
    {
        var button = new Button { Content = content, MinWidth = 32, MinHeight = 32, Cursor = Cursors.Hand, Template = ButtonTemplate(radius) };
        if (background == "Transparent")
            button.Background = Brushes.Transparent;
        else
            button.SetResourceReference(Control.BackgroundProperty, background);
        button.SetResourceReference(Control.ForegroundProperty, "IslandText");
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action();
        return button;
    }

    private static ControlTemplate ButtonTemplate(double radius)
    {
        var border = new FrameworkElementFactory(typeof(Border), "buttonSurface");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        foreach (var condition in new[] { (UIElement.IsMouseOverProperty, 0.80), (ButtonBase.IsPressedProperty, 0.60), (UIElement.IsEnabledProperty, 0.42) })
        {
            var trigger = new Trigger { Property = condition.Item1, Value = condition.Item1 == UIElement.IsEnabledProperty ? false : true };
            trigger.Setters.Add(new Setter(UIElement.OpacityProperty, condition.Item2, "buttonSurface"));
            template.Triggers.Add(trigger);
        }
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("IslandAccent"), "buttonSurface"));
        focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "buttonSurface"));
        template.Triggers.Add(focus);
        return template;
    }

    private static ControlTemplate SeekTemplate()
    {
        return (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Slider">
              <Border x:Name="focus" Height="32" BorderThickness="2" BorderBrush="Transparent" CornerRadius="8">
                <Track x:Name="PART_Track" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}" Orientation="Horizontal">
                  <Track.DecreaseRepeatButton>
                    <RepeatButton Command="{x:Static Slider.DecreaseLarge}" Focusable="False">
                      <RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Grid Background="Transparent"><Border Height="3" CornerRadius="1.5" VerticalAlignment="Center" Background="{DynamicResource IslandAccent}"/></Grid></ControlTemplate></RepeatButton.Template>
                    </RepeatButton>
                  </Track.DecreaseRepeatButton>
                  <Track.Thumb>
                    <Thumb Width="16" Height="28">
                      <Thumb.Template><ControlTemplate TargetType="Thumb"><Grid Background="Transparent"><Border Width="7" Height="15" CornerRadius="3.5" VerticalAlignment="Center" Background="{DynamicResource IslandAccent}"/></Grid></ControlTemplate></Thumb.Template>
                    </Thumb>
                  </Track.Thumb>
                  <Track.IncreaseRepeatButton>
                    <RepeatButton Command="{x:Static Slider.IncreaseLarge}" Focusable="False">
                      <RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Grid Background="Transparent"><Border Height="3" CornerRadius="1.5" VerticalAlignment="Center" Background="{DynamicResource IslandLine}"/></Grid></ControlTemplate></RepeatButton.Template>
                    </RepeatButton>
                  </Track.IncreaseRepeatButton>
                </Track>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="focus" Property="BorderBrush" Value="{DynamicResource IslandAccent}"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """);
    }

    private static void Place(Grid grid, UIElement content, int column)
    {
        Grid.SetColumn(content, column);
        grid.Children.Add(content);
    }
    private static string Time(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, Math.Ceiling(seconds)));
        return duration.TotalHours >= 1
            ? string.Format(CultureInfo.CurrentCulture, "{0}:{1:00}:{2:00}", (int)duration.TotalHours, duration.Minutes, duration.Seconds)
            : duration.ToString(@"m\:ss", CultureInfo.CurrentCulture);
    }
}
