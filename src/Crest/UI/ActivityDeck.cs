using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Crest.Core;

namespace Crest.UI;

/// <summary>The selected activity's controls, inside the island's shared envelope.</summary>
public sealed class ActivityDeck : UserControl
{
    private readonly App _app;
    private readonly Action<string> _openModule;
    private readonly Action _settings;
    private readonly Grid _layout = new(), _header = new();
    private readonly StackPanel _body = new();
    private readonly TextBlock _title = Label("Crest", 16, "IslandText", true);
    private readonly TextBlock _subtitle = Label("", 12, "IslandMuted");
    private readonly TextBlock _glyph = Glyph("\uE8D6", 24);
    private readonly Image _art = new() { Width = 40, Height = 40, Stretch = Stretch.UniformToFill, Clip = new RectangleGeometry(new Rect(0, 0, 40, 40), 8, 8) };
    private readonly Grid _artHolder = new() { Width = 40, Height = 40 };
    private readonly TextBlock _largeValue = Label("", 38, "IslandText");
    private readonly TextBlock _reminderText = Label("", 13, "IslandText");
    private readonly TextBlock _operationStatus = Label("", 12, "IslandMuted");
    private readonly TextBlock _message = Label("", 11, "IslandMuted");
    private readonly TextBlock _elapsed = Label("", 11, "IslandMuted"), _duration = Label("", 11, "IslandMuted");
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 2, MinHeight = 2, Margin = new Thickness(0, 4, 0, 0), IsHitTestVisible = false };
    private readonly Slider _seek = new() { Minimum = 0, Maximum = 1, MinHeight = 32, Height = 32, IsMoveToPointEnabled = true, SmallChange = 5, LargeChange = 15 };
    private readonly DispatcherTimer _seekCommit = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly PendingControlEdit _seekEdit = new();
    private readonly Button _primary, _secondary, _previous, _play, _next, _moreControls, _tools;
    private readonly Grid _seekRow = new();
    private IslandActivity? _activity;
    private bool _showHeader = true, _refreshing, _busy, _seekPointerDown;
    private string _builtKind = "", _seekIdentity = "", _seekTrackIdentity = "";
    private double _targetHeight;

    public event EventHandler? DesiredHeightChanged;
    public event EventHandler? ActivityActionCompleted;
    public double TargetHeight => _targetHeight;
    public IslandActivity? CurrentActivity => _activity;
    public bool ShowHeader
    {
        get => _showHeader;
        set { if (_showHeader == value) return; _showHeader = value; _header.Visibility = value ? Visibility.Visible : Visibility.Collapsed; UpdateHeight(); }
    }

    public ActivityDeck(App app, Action<string> openModule, Action settings)
    {
        _app = app; _openModule = openModule; _settings = settings;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        _layout.Margin = new Thickness(20, 8, 20, 16);
        _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _header.Height = 56;
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _artHolder.Children.Add(_glyph); _artHolder.Children.Add(_art);
        _header.Children.Add(_artHolder);
        var captions = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        captions.Children.Add(_title); captions.Children.Add(_subtitle);
        Grid.SetColumn(captions, 1); _header.Children.Add(captions);
        _layout.Children.Add(_header);
        Grid.SetRow(_body, 1); _layout.Children.Add(_body);
        Content = _layout;

        _primary = ActionButton("\uE769", "Activity action", RunPrimary);
        _secondary = ActionButton("\uE711", "Dismiss activity", RunSecondary, false);
        _previous = ActionButton("\uE892", "Previous track", () => RunTransport(_app.Hardware.PreviousAsync), false);
        _play = ActionButton("\uE769", "Pause playback", RunPrimary);
        _next = ActionButton("\uE893", "Next track", () => RunTransport(_app.Hardware.NextAsync), false);
        _moreControls = TextButton("More controls", () => { if (_activity != null) _openModule(_activity.Module); });
        _tools = TextButton("Tools", () => _openModule("Tools"));

        _seek.Template = SeekTemplate();
        AutomationProperties.SetName(_seek, "Playback position in seconds");
        _seek.PreviewMouseLeftButtonDown += (_, _) => _seekPointerDown = true;
        _seek.PreviewMouseLeftButtonUp += (_, _) => { if (_seekPointerDown) ScheduleSeek(); _seekPointerDown = false; };
        _seek.KeyUp += (_, e) => { if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown) ScheduleSeek(); };
        _seek.ValueChanged += (_, _) => { if (!_refreshing) { _seekEdit.Record(_seek.Value); _elapsed.Text = IslandActivities.Time(_seek.Value); ScheduleSeek(); } };
        _seekCommit.Tick += async (_, _) => await CommitSeekAsync();
        _seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        _seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        _elapsed.VerticalAlignment = _duration.VerticalAlignment = VerticalAlignment.Center;
        _duration.TextAlignment = TextAlignment.Right;
        _seekRow.Children.Add(_elapsed); Grid.SetColumn(_seek, 1); _seekRow.Children.Add(_seek); Grid.SetColumn(_duration, 2); _seekRow.Children.Add(_duration);
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                _seekCommit.Stop(); _seekPointerDown = false;
                if (_seekEdit.NeedsWrite && !_seekEdit.IsWriting) _ = CommitSeekAsync(true);
                else if (!_seekEdit.IsWriting) _seekEdit.Cancel();
            }
            else Refresh();
        };
        Unloaded += (_, _) => _seekCommit.Stop();
        ShowActivity(null);
    }

    public void ShowActivity(IslandActivity? activity)
    {
        if (_activity?.Id != activity?.Id)
        {
            _seekCommit.Stop(); _seekEdit.Cancel(); _seekPointerDown = false;
            _seekIdentity = activity?.Kind == IslandActivityKind.Music ? activity.Id : "";
            _seekTrackIdentity = "";
        }
        _activity = activity;
        string kind = activity?.Kind.ToString() ?? "Idle";
        if (_builtKind != kind) { _builtKind = kind; BuildBody(); }
        Refresh();
    }

    private void BuildBody()
    {
        _body.Children.Clear();
        foreach (var element in new FrameworkElement[] { _largeValue, _primary, _secondary, _progress, _previous, _play, _next, _seekRow, _reminderText, _operationStatus, _moreControls, _tools, _message })
            if (element.Parent is Panel parent) parent.Children.Remove(element);
        if (_activity?.Kind is IslandActivityKind.Timer or IslandActivityKind.Focus)
        {
            var value = new Grid { Height = 56 };
            value.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            value.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _largeValue.VerticalAlignment = VerticalAlignment.Center; value.Children.Add(_largeValue);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(_primary); _secondary.Margin = new Thickness(8, 0, 0, 0); actions.Children.Add(_secondary);
            Grid.SetColumn(actions, 1); value.Children.Add(actions);
            _body.Children.Add(value); _body.Children.Add(_progress); _body.Children.Add(Footer());
        }
        else if (_activity?.Kind == IslandActivityKind.Music)
        {
            var transport = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Height = 44 };
            _previous.Margin = new Thickness(0, 0, 16, 0); _next.Margin = new Thickness(16, 0, 0, 0);
            transport.Children.Add(_previous); transport.Children.Add(_play); transport.Children.Add(_next);
            _body.Children.Add(transport); _body.Children.Add(_seekRow); _body.Children.Add(Footer());
        }
        else if (_activity?.Kind == IslandActivityKind.Reminder)
        {
            _reminderText.TextWrapping = TextWrapping.Wrap; _reminderText.MaxHeight = 108; _reminderText.LineHeight = 18;
            _body.Children.Add(_reminderText);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            actions.Children.Add(TextButton("Snooze 5m", () => Snooze(5), false));
            var snooze = TextButton("Snooze 15m", () => Snooze(15), false); snooze.Margin = new Thickness(8, 0, 0, 0); actions.Children.Add(snooze);
            var dismiss = TextButton("Dismiss", RunSecondary, false); dismiss.Margin = new Thickness(8, 0, 0, 0); actions.Children.Add(dismiss);
            _body.Children.Add(actions); _body.Children.Add(Footer());
        }
        else if (_activity?.Kind is IslandActivityKind.Download or IslandActivityKind.Ai)
        {
            _operationStatus.TextWrapping = TextWrapping.Wrap; _operationStatus.MaxHeight = 54; _operationStatus.LineHeight = 18;
            _body.Children.Add(_operationStatus);
            if (_activity.Kind == IslandActivityKind.Download) _body.Children.Add(_progress);
            _body.Children.Add(Footer());
        }
        else if (_activity?.Kind == IslandActivityKind.BatteryWarning) _body.Children.Add(Footer());
        else
        {
            var idle = new Grid(); idle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); idle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var open = TextButton("Open tools", () => _openModule("Tools"), false); idle.Children.Add(open);
            var settings = ActionButton("\uE713", "Settings", _settings, false); Grid.SetColumn(settings, 1); idle.Children.Add(settings);
            _body.Children.Add(idle);
        }
        _message.Margin = new Thickness(0, 4, 0, 0); _body.Children.Add(_message);
    }

    private Grid Footer()
    {
        var footer = new Grid { Margin = new Thickness(0, 8, 0, 0), Height = 40 };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _moreControls.HorizontalAlignment = HorizontalAlignment.Left; footer.Children.Add(_moreControls);
        _tools.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(_tools, 1); footer.Children.Add(_tools);
        return footer;
    }

    public void Refresh()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            // The shell selects identity. This view never substitutes another timer or focus session.
            if (_activity != null && _activity.Kind != IslandActivityKind.BatteryWarning)
                _activity = IslandActivityCatalog.Read(_app).FirstOrDefault(item => item.Id == _activity.Id);
            string kind = _activity?.Kind.ToString() ?? "Idle";
            if (_builtKind != kind) { _builtKind = kind; BuildBody(); }
            _title.Text = _activity?.Title ?? "Crest";
            _subtitle.Text = _activity?.Subtitle ?? "No active activities";
            _glyph.Text = _activity?.Glyph ?? "\uE713";
            _glyph.SetResourceReference(TextBlock.ForegroundProperty, _activity?.AccentKey ?? "IslandText");
            var media = _app.Hardware.Music;
            bool music = _activity?.Kind == IslandActivityKind.Music;
            _art.Source = music ? media.Artwork : null;
            _art.Visibility = _art.Source == null ? Visibility.Collapsed : Visibility.Visible;
            _glyph.Visibility = _art.Source == null ? Visibility.Visible : Visibility.Collapsed;
            _largeValue.Text = _activity?.Value ?? "";
            _largeValue.SetResourceReference(TextBlock.ForegroundProperty, _activity?.AccentKey ?? "IslandText");
            _primary.Content = Glyph(_activity?.PrimaryActionGlyph ?? "\uE768", 18, "IslandActionText");
            _primary.SetResourceReference(Control.BackgroundProperty, _activity?.AccentKey ?? "IslandAccentTimer");
            _primary.IsEnabled = !_busy && _activity?.CanInvoke == true;
            AutomationProperties.SetName(_primary, _activity?.PrimaryActionName ?? "Activity action");
            Design.RegisterTooltip(_primary, _activity?.PrimaryActionName ?? "Activity action");
            _secondary.IsEnabled = !_busy;
            _secondary.Visibility = _activity?.PrimaryAction == IslandActivityAction.DismissFocus ? Visibility.Collapsed : Visibility.Visible;
            string secondaryName = _activity?.Kind == IslandActivityKind.Focus ? "Dismiss focus session" : _activity?.Kind == IslandActivityKind.Timer ? "Cancel " + _activity.Title : "Dismiss reminder";
            AutomationProperties.SetName(_secondary, secondaryName); Design.RegisterTooltip(_secondary, secondaryName);
            _progress.Visibility = _activity?.Progress != null ? Visibility.Visible : Visibility.Collapsed;
            _progress.Value = (_activity?.Progress ?? 0) * 100;
            _progress.SetResourceReference(Control.ForegroundProperty, _activity?.AccentKey ?? "IslandAccentTimer");
            _progress.SetResourceReference(Control.BackgroundProperty, "IslandCard");
            _reminderText.Text = _activity?.Subtitle ?? "";
            _operationStatus.Text = _activity is { Kind: IslandActivityKind.Download, Progress: not null } download
                ? download.Value + " · " + download.Subtitle : _activity?.Subtitle ?? "";
            _moreControls.Content = _activity?.Kind switch { IslandActivityKind.Focus => "Focus controls", IslandActivityKind.Timer => "Timer controls", IslandActivityKind.Music => _app.Hardware.Players.Count > 1 ? "Music & player controls" : "Music controls", IslandActivityKind.Reminder => "Reminder controls", IslandActivityKind.Download => "Download controls", IslandActivityKind.Ai => "AI controls", _ => "Battery controls" };
            AutomationProperties.SetName(_moreControls, _moreControls.Content.ToString());
            if (music)
            {
                _previous.IsEnabled = !_busy && media.CanPrevious; _next.IsEnabled = !_busy && media.CanNext; _play.IsEnabled = !_busy && media.CanPlayPause;
                _play.Content = Glyph(media.IsPlaying ? "\uE769" : "\uE768", 18, "IslandActionText");
                _play.SetResourceReference(Control.BackgroundProperty, "IslandAccentMusic");
                AutomationProperties.SetName(_play, media.IsPlaying ? "Pause playback" : "Resume playback");
                string trackIdentity = TrackIdentity(_seekIdentity, media.Title, media.Artist);
                bool seekAvailable = media.CanSeek && double.IsFinite(media.DurationSeconds) && media.DurationSeconds > 0;
                if (!seekAvailable || _seekTrackIdentity.Length > 0 && _seekTrackIdentity != trackIdentity)
                {
                    _seekCommit.Stop(); _seekEdit.Cancel(); _seekPointerDown = false;
                }
                _seekTrackIdentity = trackIdentity;
                _seekRow.Visibility = seekAvailable ? Visibility.Visible : Visibility.Collapsed;
                _seek.Maximum = seekAvailable ? Math.Max(1, media.DurationSeconds) : 1; _seek.IsEnabled = seekAvailable;
                _seek.SetResourceReference(Control.ForegroundProperty, "IslandAccentMusic");
                double position = double.IsFinite(media.PositionSeconds) ? Math.Clamp(media.PositionSeconds, 0, _seek.Maximum) : 0;
                _seek.Value = _seekEdit.DisplayValue(position, Environment.TickCount64, 1);
                _elapsed.Text = IslandActivities.Time(_seek.Value); _duration.Text = IslandActivities.Time(media.DurationSeconds);
            }
            _message.Visibility = string.IsNullOrEmpty(_message.Text) ? Visibility.Collapsed : Visibility.Visible;
            UpdateHeight();
        }
        finally { _refreshing = false; }
    }

    private void UpdateHeight()
    {
        double body = _activity?.Kind switch
        {
            IslandActivityKind.Timer or IslandActivityKind.Focus => 110,
            IslandActivityKind.Music => 92 + (_seekRow.Visibility == Visibility.Visible ? 32 : 0),
            IslandActivityKind.Reminder => Math.Clamp(Math.Ceiling((_activity.Subtitle.Length + 1) / 46d), 1, 6) * 18 + 96,
            IslandActivityKind.Download or IslandActivityKind.Ai => Math.Clamp(Math.Ceiling((_activity.Subtitle.Length + 1) / 46d), 1, 3) * 18 + 48 + (_activity.Progress.HasValue ? 6 : 0),
            IslandActivityKind.BatteryWarning => 48,
            _ => 44
        };
        double target = body + 24 + (_showHeader ? 56 : 0) + (string.IsNullOrEmpty(_message.Text) ? 0 : 24);
        if (Math.Abs(target - _targetHeight) < 0.5) return;
        _targetHeight = target; Height = target;
        DesiredHeightChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void RunPrimary()
    {
        var selected = _activity;
        if (selected == null || _busy) return;
        await RunAction(() => IslandActivityCatalog.InvokePrimaryAsync(_app, selected, _openModule));
    }

    private async void RunSecondary()
    {
        var selected = _activity;
        if (selected == null || _busy) return;
        await RunAction(() => Task.Run(() =>
        {
            if (!_app.Preferences.EnabledModules.Contains(selected.Module)) return;
            switch (selected.Kind)
            {
                case IslandActivityKind.Focus: _app.Local.DismissFocus(selected.EntityId); break;
                case IslandActivityKind.Timer: _app.Local.CancelTimer(selected.EntityId); break;
                case IslandActivityKind.Reminder: _app.Local.DismissReminder(selected.EntityId); break;
            }
        }));
    }

    private async void Snooze(int minutes)
    {
        var selected = _activity;
        if (selected?.Kind != IslandActivityKind.Reminder || _busy) return;
        await RunAction(() => Task.Run(() => { if (_app.Preferences.EnabledModules.Contains("Timer") && _app.Local.Reminders.Any(item => item.Id == selected.EntityId && item.Phase == Crest.Features.ReminderPhase.Due)) _app.Local.SnoozeReminder(selected.EntityId, TimeSpan.FromMinutes(minutes)); }));
    }

    private async void RunTransport(Func<Task> command)
    {
        var selected = _activity;
        if (selected?.Kind != IslandActivityKind.Music || _busy) return;
        await RunAction(async () => { if (IslandActivityCatalog.Read(_app).Any(item => item.Id == selected.Id)) await command(); });
    }

    private async Task RunAction(Func<Task> action)
    {
        _busy = true; _message.Text = ""; Refresh();
        try { await action(); }
        catch (Exception error)
        { _message.Text = "This action is currently unavailable. Open its controls to check the source."; App.WriteDiagnostic("activity-action:" + error.GetType().Name); }
        finally { _busy = false; Refresh(); ActivityActionCompleted?.Invoke(this, EventArgs.Empty); }
    }

    private void ScheduleSeek()
    {
        if (!_seek.IsEnabled || !IsVisible || !_seekEdit.NeedsWrite) return;
        _seekCommit.Stop(); _seekCommit.Start();
    }
    private async Task CommitSeekAsync(bool allowHidden = false)
    {
        _seekCommit.Stop();
        if (!_seek.IsEnabled || (!IsVisible && !allowHidden) || _activity?.Id != _seekIdentity) { _seekEdit.Cancel(); return; }
        if (_seekPointerDown && !allowHidden) return;
        if (!_seekEdit.TryBeginWrite(out var write)) return;
        string identity = _seekIdentity;
        string trackIdentity = _seekTrackIdentity;
        bool succeeded = false;
        try
        {
            var media = _app.Hardware.Music;
            if (media.CanSeek && double.IsFinite(media.DurationSeconds) && media.DurationSeconds > 0
                && TrackIdentity(identity, media.Title, media.Artist) == trackIdentity
                && IslandActivityCatalog.Read(_app).Any(item => item.Id == identity))
            { await _app.Hardware.SeekAsync(write.Value); succeeded = true; }
        }
        catch (Exception error) { App.WriteDiagnostic("seek-fault:" + error.GetType().Name); }
        finally
        {
            bool newer = _seekEdit.CompleteWrite(write, Environment.TickCount64, succeeded && IsVisible ? 1500 : 0);
            if (newer) { if (IsVisible) ScheduleSeek(); else await CommitSeekAsync(true); }
            if (IsVisible) Refresh();
        }
    }
    private static string TrackIdentity(string player, string title, string artist) => player + "\u001F" + title + "\u001F" + artist;

    private static Button ActionButton(string glyph, string name, Action action, bool filled = true)
    {
        var button = Design.IconButton(glyph, name, action);
        button.Width = button.Height = button.MinWidth = button.MinHeight = 44; button.Padding = new Thickness(0); button.BorderThickness = new Thickness(0);
        button.Style = (Style)Application.Current.Resources["CircleButton"];
        button.SetResourceReference(Control.ForegroundProperty, filled ? "IslandActionText" : "IslandText");
        if (filled) button.SetResourceReference(Control.BackgroundProperty, "IslandAccentTimer"); else button.Background = Brushes.Transparent;
        return button;
    }
    private static Button TextButton(string name, Action action, bool quiet = true)
    {
        var button = Design.Button(name, action); button.FontSize = 12; button.MinHeight = 40; button.Padding = new Thickness(10, 8, 10, 8); button.BorderThickness = new Thickness(0);
        button.SetResourceReference(Control.ForegroundProperty, quiet ? "IslandMuted" : "IslandText");
        if (quiet) button.Background = Brushes.Transparent; else button.SetResourceReference(Control.BackgroundProperty, "IslandCard");
        return button;
    }
    private static TextBlock Label(string text, double size, string color, bool bold = false)
    {
        var label = Design.Text(text, size, color, bold); label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis; return label;
    }
    private static TextBlock Glyph(string glyph, double size, string color = "IslandText")
    {
        var label = Label(glyph, size, color); label.FontFamily = new FontFamily("Segoe Fluent Icons"); label.HorizontalAlignment = HorizontalAlignment.Center; label.VerticalAlignment = VerticalAlignment.Center; return label;
    }
    private static ControlTemplate SeekTemplate() => (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Slider">
          <Border x:Name="focus" Height="32" BorderThickness="2" BorderBrush="Transparent" CornerRadius="8">
            <Track x:Name="PART_Track" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}" Orientation="Horizontal">
              <Track.DecreaseRepeatButton><RepeatButton Command="{x:Static Slider.DecreaseLarge}" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Grid Background="Transparent"><Border Height="2" CornerRadius="1" VerticalAlignment="Center" Background="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Slider}}" /></Grid></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
              <Track.Thumb><Thumb Width="18" Height="28"><Thumb.Template><ControlTemplate TargetType="Thumb"><Grid Background="Transparent"><Border Width="8" Height="8" CornerRadius="4" VerticalAlignment="Center" Background="{DynamicResource IslandText}" /></Grid></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
              <Track.IncreaseRepeatButton><RepeatButton Command="{x:Static Slider.IncreaseLarge}" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Grid Background="Transparent"><Border Height="2" CornerRadius="1" VerticalAlignment="Center" Background="{DynamicResource IslandLine}" /></Grid></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
            </Track>
          </Border>
          <ControlTemplate.Triggers><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45" /></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="focus" Property="BorderBrush" Value="{DynamicResource IslandAccentMusic}" /></Trigger></ControlTemplate.Triggers>
        </ControlTemplate>
        """);
}
