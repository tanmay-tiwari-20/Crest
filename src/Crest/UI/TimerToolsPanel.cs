using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Crest.Features;

namespace Crest.UI;

/// <summary>Productivity controls within the island's existing focus and visibility surface.</summary>
public sealed class TimerToolsPanel : StackPanel
{
    private readonly LocalHub _local;
    private readonly Action<string> _message;
    private readonly StackPanel _timers = new(), _stopwatch = new(), _rows = new(), _laps = new();
    private readonly Dictionary<string, TimerRow> _timerRows = new();
    private readonly List<Button> _addButtons = new();
    private readonly TextBlock _count = Design.Text("", 11, "Muted");
    private readonly TextBlock _elapsed = Design.Text("0:00.00", 38, "Text", true);
    private readonly TextBlock _stopwatchStatus = Design.Text("Ready when you are.", 11, "Muted");
    private readonly TextBox _name = new() { MaxLength = 40 }, _minutes = new() { Text = "10", MaxLength = 12 };
    private readonly Button _timersTab, _stopwatchTab, _focusTab, _remindersTab, _stopwatchAction, _lap, _reset;
    private readonly DispatcherTimer _displayClock = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly ProductivityExtrasPanel _extras;
    private bool _showStopwatch, _busy;
    private int _lapCount = -1;

    public TimerToolsPanel(LocalHub local, Action<string> message)
    {
        _local = local;
        _message = message;
        var tabs = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 12) };
        _timersTab = Design.Button("Timers", () => ShowTool(false));
        _stopwatchTab = Design.Button("Stopwatch", () => ShowTool(true));
        _timersTab.Margin = new Thickness(0, 0, 4, 0);
        _stopwatchTab.Margin = new Thickness(0, 0, 4, 0);
        Grid.SetColumn(_stopwatchTab, 1);
        tabs.Children.Add(_timersTab);
        tabs.Children.Add(_stopwatchTab);
        _extras = new ProductivityExtrasPanel(local, message);
        _focusTab = Design.Button("Focus", () => ShowExtraTool(false));
        _remindersTab = Design.Button("Reminders", () => ShowExtraTool(true));
        _focusTab.Margin = new Thickness(0, 0, 4, 0);
        _remindersTab.Margin = new Thickness(0);
        foreach (var tab in new[] { _timersTab, _stopwatchTab, _focusTab, _remindersTab })
            tab.Padding = new Thickness(6, 8, 6, 8);
        tabs.Children.Add(_focusTab);
        tabs.Children.Add(_remindersTab);
        Children.Add(tabs);
        Children.Add(_timers);
        Children.Add(_stopwatch);
        Children.Add(_extras);

        var presets = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 8) };
        foreach (int minutes in new[] { 5, 15, 25 })
        {
            var preset = Design.Button("+ " + minutes + " min", () => Add(TimeSpan.FromMinutes(minutes)));
            AutomationProperties.SetName(preset, "Add a " + minutes + " minute timer");
            preset.Margin = new Thickness(0, 0, 4, 0);
            presets.Children.Add(preset);
            _addButtons.Add(preset);
        }
        _timers.Children.Add(presets);
        var create = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        create.ColumnDefinitions.Add(new ColumnDefinition());
        create.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        create.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        var nameField = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        nameField.Children.Add(Design.Text("Name (optional)", 10, "Muted"));
        nameField.Children.Add(_name);
        AutomationProperties.SetName(_name, "New timer name, optional");
        var minutesField = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        minutesField.Children.Add(Design.Text("Minutes", 10, "Muted"));
        minutesField.Children.Add(_minutes);
        AutomationProperties.SetName(_minutes, "New timer duration in minutes");
        var add = Design.Button("Add", AddCustom, true);
        add.VerticalAlignment = VerticalAlignment.Bottom;
        AutomationProperties.SetName(add, "Add custom timer");
        _addButtons.Add(add);
        Grid.SetColumn(minutesField, 1);
        Grid.SetColumn(add, 2);
        create.Children.Add(nameField);
        create.Children.Add(minutesField);
        create.Children.Add(add);
        _timers.Children.Add(create);
        _timers.Children.Add(_count);
        _timers.Children.Add(_rows);

        _elapsed.HorizontalAlignment = HorizontalAlignment.Center;
        _elapsed.Margin = new Thickness(0, 4, 0, 0);
        _stopwatch.Children.Add(_elapsed);
        _stopwatchStatus.HorizontalAlignment = HorizontalAlignment.Center;
        _stopwatch.Children.Add(_stopwatchStatus);
        var actions = new UniformGrid { Columns = 3, Margin = new Thickness(0, 12, 0, 10) };
        _stopwatchAction = Design.Button("Start", () => Execute(() =>
        {
            if (_local.Stopwatch.Phase == StopwatchPhase.Running) _local.PauseStopwatch();
            else _local.StartStopwatch();
        }), true);
        _lap = Design.Button("Lap", () => Execute(() => _local.LapStopwatch()));
        _reset = Design.Button("Reset", () => Execute(_local.ResetStopwatch));
        foreach (var button in new[] { _stopwatchAction, _lap, _reset })
        {
            button.Margin = new Thickness(0, 0, 4, 0);
            actions.Children.Add(button);
        }
        _stopwatch.Children.Add(actions);
        _stopwatch.Children.Add(_laps);
        _displayClock.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) => UpdateDisplayClock();
        Unloaded += (_, _) => _displayClock.Stop();
        PanelLayout.Refine(this);
        ShowTool(false);
    }

    private void ShowTool(bool stopwatch)
    {
        _showStopwatch = stopwatch;
        _extras.Visibility = Visibility.Collapsed;
        _timers.Visibility = stopwatch ? Visibility.Collapsed : Visibility.Visible;
        _stopwatch.Visibility = stopwatch ? Visibility.Visible : Visibility.Collapsed;
        PanelLayout.Select(_timersTab, !stopwatch, "Timers");
        PanelLayout.Select(_stopwatchTab, stopwatch, "Stopwatch");
        PanelLayout.Select(_focusTab, false, "Focus sessions");
        PanelLayout.Select(_remindersTab, false, "Reminders");
        Refresh();
    }

    private void ShowExtraTool(bool reminders)
    {
        _showStopwatch = false;
        _timers.Visibility = _stopwatch.Visibility = Visibility.Collapsed;
        _extras.Visibility = Visibility.Visible;
        _extras.ShowReminders(reminders);
        PanelLayout.Select(_timersTab, false, "Timers");
        PanelLayout.Select(_stopwatchTab, false, "Stopwatch");
        PanelLayout.Select(_focusTab, !reminders, "Focus sessions");
        PanelLayout.Select(_remindersTab, reminders, "Reminders");
        Refresh();
    }
    public void ShowFocusSessions() => ShowExtraTool(false);
    public void ShowReminders() => ShowExtraTool(true);

    private void AddCustom()
    {
        if (!double.TryParse(_minutes.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double minutes)
            || !double.IsFinite(minutes) || minutes < 1d / 60 || minutes > 10080)
        {
            _message("Enter minutes between 1 second and 7 days (10,080 minutes).");
            _minutes.Focus();
            return;
        }
        Add(TimeSpan.FromMinutes(minutes));
    }

    private void Add(TimeSpan duration)
    {
        string name = _name.Text.Trim();
        Execute(() => _local.AddTimer(duration, name));
    }

    // Persistence and file work stay off the UI thread. Serial actions avoid duplicate starts.
    private async void Execute(Action action)
    {
        if (_busy) return;
        _busy = true;
        Refresh();
        try
        {
            await Task.Run(action);
            if (_local.LastError is { } error) _message(error);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            _message(error.Message);
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    public void Refresh()
    {
        _extras.Refresh();
        var timers = _local.Timers;
        var ids = timers.Select(timer => timer.Id).ToHashSet();
        foreach (string id in _timerRows.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            var row = _timerRows[id];
            bool hadFocus = row.Surface.IsKeyboardFocusWithin;
            _rows.Children.Remove(row.Surface);
            _timerRows.Remove(id);
            if (hadFocus && IsVisible) _timersTab.Focus();
        }
        foreach (var timer in timers)
        {
            if (!_timerRows.TryGetValue(timer.Id, out var row))
            {
                row = CreateRow(timer.Id);
                _timerRows.Add(timer.Id, row);
                _rows.Children.Add(row.Surface);
            }
            string time = Countdown(timer.RemainingSeconds);
            if (row.Select.Content is TextBlock name) name.Text = timer.Name;
            row.Select.SetResourceReference(Control.ForegroundProperty, timer.Id == _local.SelectedTimerId ? "Accent" : "Text");
            AutomationProperties.SetName(row.Select, "Select " + timer.Name + ", " + timer.Phase + ", " + time + (timer.Id == _local.SelectedTimerId ? ", selected" : ""));
            row.Time.Text = time;
            row.Time.FontSize = timer.RemainingSeconds >= 360000 ? 12 : timer.RemainingSeconds >= 3600 ? 17 : 23;
            row.Status.Text = timer.Phase.ToString();
            row.Action.Content = timer.Phase == TimerPhase.Completed ? "Done" : timer.Phase == TimerPhase.Paused ? "Resume" : "Pause";
            AutomationProperties.SetName(row.Action, (timer.Phase == TimerPhase.Completed ? "Acknowledge " : timer.Phase == TimerPhase.Paused ? "Resume " : "Pause ") + timer.Name);
            AutomationProperties.SetName(row.Cancel, "Cancel " + timer.Name);
            row.Select.IsEnabled = row.Action.IsEnabled = row.Cancel.IsEnabled = !_busy;
        }
        int complete = timers.Count(timer => timer.Phase == TimerPhase.Completed);
        _count.Text = timers.Count == 0 ? "Add your first timer. Timers continue while Crest is hidden." : $"{timers.Count} of {LocalHub.MaxTimers} timers" + (complete > 0 ? $" · {complete} complete" : " · running timers continue while hidden");
        foreach (var add in _addButtons) add.IsEnabled = !_busy && timers.Count < LocalHub.MaxTimers;
        if (timers.Count == LocalHub.MaxTimers) _count.Text += " · remove one to add another";

        var watch = _local.Stopwatch;
        string elapsed = StopwatchTime(watch.ElapsedSeconds);
        _elapsed.Text = elapsed;
        AutomationProperties.SetName(_elapsed, "Stopwatch elapsed " + elapsed);
        _stopwatchStatus.Text = watch.Phase switch
        {
            StopwatchPhase.Running => "Running · includes time hidden and asleep.",
            StopwatchPhase.Paused => "Paused · elapsed time is saved.",
            _ => "Ready when you are."
        };
        _stopwatchAction.Content = watch.Phase == StopwatchPhase.Running ? "Pause" : watch.Phase == StopwatchPhase.Paused ? "Resume" : "Start";
        AutomationProperties.SetName(_stopwatchAction, _stopwatchAction.Content + " stopwatch");
        _stopwatchAction.IsEnabled = !_busy;
        _lap.IsEnabled = !_busy && watch.Phase == StopwatchPhase.Running && watch.Laps.Count < LocalHub.MaxStopwatchLaps;
        AutomationProperties.SetName(_lap, "Record stopwatch lap");
        _reset.IsEnabled = !_busy && watch.Phase != StopwatchPhase.Idle;
        AutomationProperties.SetName(_reset, "Reset stopwatch and clear laps");
        if (_lapCount != watch.Laps.Count)
        {
            _lapCount = watch.Laps.Count;
            _laps.Children.Clear();
            _laps.Children.Add(Design.Text(_lapCount == 0 ? "Record laps while running." : $"{_lapCount} of {LocalHub.MaxStopwatchLaps} laps · split / total", 11, "Muted"));
            foreach (var lap in watch.Laps.Reverse())
                _laps.Children.Add(Design.Text($"Lap {lap.Number}     {StopwatchTime(lap.SplitSeconds)} / {StopwatchTime(lap.TotalSeconds)}", 12));
        }
        UpdateDisplayClock();
    }

    private TimerRow CreateRow(string id)
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var info = new Grid();
        info.ColumnDefinitions.Add(new ColumnDefinition());
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        info.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        info.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var select = Design.Button("", () => Execute(() => _local.SelectTimer(id)));
        select.Content = new TextBlock { MaxWidth = 110, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        ((TextBlock)select.Content).SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = select });
        select.HorizontalContentAlignment = HorizontalAlignment.Left;
        select.Padding = new Thickness(0);
        var time = Design.Text("", 23, "Text", true);
        time.Margin = new Thickness(4, 0, 8, 0);
        time.VerticalAlignment = VerticalAlignment.Center;
        var status = Design.Text("", 10, "Muted");
        Grid.SetColumn(time, 1);
        Grid.SetRow(status, 1);
        Grid.SetColumnSpan(status, 2);
        info.Children.Add(select);
        info.Children.Add(time);
        info.Children.Add(status);
        content.Children.Add(info);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var action = Design.Button("Pause", () => Execute(() =>
        {
            var timer = _local.Timers.FirstOrDefault(timer => timer.Id == id);
            if (timer?.Phase == TimerPhase.Paused) _local.ResumeTimer(id);
            else if (timer?.Phase == TimerPhase.Completed) _local.AcknowledgeTimer(id);
            else _local.PauseTimer(id);
        }));
        var cancel = Design.IconButton("\uE711", "Cancel timer", () => Execute(() => _local.CancelTimer(id)));
        cancel.Margin = new Thickness(4, 0, 0, 0);
        actions.Children.Add(action);
        actions.Children.Add(cancel);
        Grid.SetColumn(actions, 1);
        content.Children.Add(actions);
        var surface = Design.Card(content, new Thickness(12, 8, 12, 12));
        surface.Margin = new Thickness(0, 8, 0, 0);
        return new(surface, select, time, status, action, cancel);
    }

    private void UpdateDisplayClock()
    {
        if (IsVisible && _showStopwatch && _local.Stopwatch.Phase == StopwatchPhase.Running) _displayClock.Start();
        else _displayClock.Stop();
    }

    private static string Countdown(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, Math.Ceiling(seconds)));
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes}:{time.Seconds:00}";
    }
    private static string StopwatchTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? $"{(long)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds / 10:00}" : $"{time.Minutes}:{time.Seconds:00}.{time.Milliseconds / 10:00}";
    }
    private sealed record TimerRow(Border Surface, Button Select, TextBlock Time, TextBlock Status, Button Action, Button Cancel);
}
