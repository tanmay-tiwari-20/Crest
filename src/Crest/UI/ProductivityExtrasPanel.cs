using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Crest.Features;

namespace Crest.UI;

public sealed class ProductivityExtrasPanel : StackPanel
{
    private readonly LocalHub _local;
    private readonly Action<string> _message;
    private readonly StackPanel _focus = new(), _reminders = new(), _reminderRows = new();
    private readonly TextBlock _focusTime = Design.Text("", 32, "Text", true), _focusStatus = Design.Text("", 12, "Muted");
    private readonly TextBox _work = new() { Text = "25", MaxLength = 8 }, _rest = new() { Text = "5", MaxLength = 8 }, _rounds = new() { Text = "4", MaxLength = 2 }, _reminderText = new() { MaxLength = 240 }, _reminderMinutes = new() { Text = "10", MaxLength = 8 }, _reminderDate = new() { Text = DateTime.Now.AddHours(1).ToString("g", CultureInfo.CurrentCulture), MaxLength = 100 };
    private readonly Button _startFocus, _focusAction, _dismissFocus, _addRelative, _addAt;
    private bool _busy;
    private string _signature = "";
    public ProductivityExtrasPanel(LocalHub local, Action<string> message)
    {
        _local = local; _message = message;
        Children.Add(_focus); Children.Add(_reminders);
        var fields = new UniformGrid { Columns = 3 };
        Field(fields, "Work minutes", _work); Field(fields, "Break minutes", _rest); Field(fields, "Rounds", _rounds);
        _focus.Children.Add(fields);
        _startFocus = Design.Button("Start focus session", StartFocus, true);
        _focus.Children.Add(_startFocus);
        _focusTime.HorizontalAlignment = HorizontalAlignment.Center;
        _focus.Children.Add(_focusTime); _focus.Children.Add(_focusStatus);
        _focusAction = Design.Button("Pause focus", () => Execute(() =>
        {
            if (_local.Focus.Phase == TimerPhase.Completed) _local.NextFocusPhase();
            else if (_local.Focus.Phase == TimerPhase.Paused) _local.ResumeFocus();
            else _local.PauseFocus();
        }));
        _dismissFocus = Design.Button("Dismiss focus session", () => Execute(_local.DismissFocus));
        _focus.Children.Add(_focusAction); _focus.Children.Add(_dismissFocus);
        _focus.Children.Add(Design.Text("Each completed work or break stays here until you continue. Completion never opens Crest.", 11, "Muted"));
        Field(_reminders, "Reminder text", _reminderText);
        var relative = new Grid { Margin = new Thickness(0, 4, 0, 6) };
        relative.ColumnDefinitions.Add(new ColumnDefinition()); relative.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Field(relative, "Minutes from now", _reminderMinutes);
        _addRelative = Design.Button("Add in…", AddRelative, true); _addRelative.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(_addRelative, 1); relative.Children.Add(_addRelative); _reminders.Children.Add(relative);
        Field(_reminders, "Local date and time", _reminderDate);
        _addAt = Design.Button("Add at chosen time", AddAt); _reminders.Children.Add(_addAt);
        _reminders.Children.Add(Design.Text("Reminders are saved locally and become due while hidden. Due reminders remain until snoozed or dismissed.", 11, "Muted"));
        _reminders.Children.Add(_reminderRows);
        ShowReminders(false);
    }
    private static void Field(Panel parent, string label, TextBox field)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 6, 6) };
        panel.Children.Add(Design.Text(label, 10, "Muted")); panel.Children.Add(field);
        AutomationProperties.SetName(field, label); parent.Children.Add(panel);
    }
    public void ShowReminders(bool reminders) { _focus.Visibility = reminders ? Visibility.Collapsed : Visibility.Visible; _reminders.Visibility = reminders ? Visibility.Visible : Visibility.Collapsed; Refresh(); }
    private bool Minutes(TextBox field, out double minutes)
    {
        if (double.TryParse(field.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out minutes) && double.IsFinite(minutes) && minutes >= 1d / 60 && minutes <= 10080) return true;
        _message("Enter minutes between 1 second and 7 days."); field.Focus(); return false;
    }
    private void StartFocus()
    {
        if (!Minutes(_work, out double work) || !Minutes(_rest, out double rest)) return;
        if (!int.TryParse(_rounds.Text, out int rounds) || rounds is < 1 or > 12) { _message("Choose between 1 and 12 focus rounds."); _rounds.Focus(); return; }
        Execute(() => _local.StartFocusSession(TimeSpan.FromMinutes(work), TimeSpan.FromMinutes(rest), rounds));
    }
    private void AddRelative()
    {
        if (!Minutes(_reminderMinutes, out double minutes)) return;
        string text = _reminderText.Text; var due = DateTimeOffset.UtcNow.AddMinutes(minutes);
        Execute(() => _local.AddReminder(text, due));
    }
    private void AddAt()
    {
        if (!DateTime.TryParse(_reminderDate.Text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var date)) { _message("Enter a valid local date and time using your Windows format."); _reminderDate.Focus(); return; }
        if (TimeZoneInfo.Local.IsInvalidTime(DateTime.SpecifyKind(date, DateTimeKind.Unspecified))) { _message("That local time does not exist during the daylight saving change. Choose another time."); return; }
        var due = new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Local)); string text = _reminderText.Text;
        Execute(() => _local.AddReminder(text, due));
    }
    private async void Execute(Action action)
    {
        if (_busy) return; _busy = true; Refresh();
        try { await Task.Run(action); if (_local.LastError is { } error) _message(error); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { _message(error.Message); }
        finally { _busy = false; Refresh(); }
    }
    public void Refresh()
    {
        var focus = _local.Focus;
        var time = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, focus.RemainingSeconds)));
        _focusTime.Text = time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes}:{time.Seconds:00}";
        _focusStatus.Text = focus.Phase == TimerPhase.Idle ? "Start a work and break sequence." : $"{focus.Stage} · {focus.Phase} · {focus.CompletedRounds} of {focus.TargetRounds} work rounds complete";
        AutomationProperties.SetName(_focusTime, "Focus " + _focusTime.Text + ", " + _focusStatus.Text);
        bool finished = focus.Phase == TimerPhase.Completed && focus.CompletedRounds >= focus.TargetRounds;
        _focusAction.Content = focus.Phase == TimerPhase.Paused ? "Resume focus" : focus.Phase == TimerPhase.Completed ? focus.Stage == FocusStage.Work ? "Start break" : "Start next work round" : "Pause focus";
        AutomationProperties.SetName(_focusAction, (string)_focusAction.Content);
        _focusAction.IsEnabled = !_busy && focus.Phase != TimerPhase.Idle && !finished;
        _dismissFocus.IsEnabled = !_busy && focus.Phase != TimerPhase.Idle;
        _startFocus.IsEnabled = !_busy && focus.Phase == TimerPhase.Idle;
        if (finished) _focusStatus.Text = $"Session complete · {focus.CompletedRounds} work rounds. Dismiss when ready.";
        _addRelative.IsEnabled = _addAt.IsEnabled = !_busy && _local.Reminders.Count < LocalHub.MaxReminders;
        var reminders = _local.Reminders;
        string signature = string.Join("|", reminders.Select(x => x.Id + ":" + x.Phase + ":" + x.DueUtc.Ticks)) + ":" + _busy;
        if (_signature == signature) return; _signature = signature;
        bool hadFocus = _reminderRows.IsKeyboardFocusWithin;
        _reminderRows.Children.Clear();
        _reminderRows.Children.Add(Design.Text($"{reminders.Count} of {LocalHub.MaxReminders} reminders", 11, "Muted"));
        foreach (var reminder in reminders)
        {
            var row = new StackPanel();
            row.Children.Add(Design.Text(reminder.Text, 13, reminder.Phase == ReminderPhase.Due ? "Accent" : "Text"));
            row.Children.Add(Design.Text((reminder.Phase == ReminderPhase.Due ? "Due · " : "Scheduled · ") + reminder.DueUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), 11, "Muted"));
            var actions = new UniformGrid { Columns = 3 };
            foreach (int minutes in new[] { 5, 15 })
            {
                var snooze = Design.Button("Snooze " + minutes + "m", () => Execute(() => _local.SnoozeReminder(reminder.Id, TimeSpan.FromMinutes(minutes))));
                AutomationProperties.SetName(snooze, "Snooze " + reminder.Text + " for " + minutes + " minutes"); snooze.IsEnabled = !_busy;
                actions.Children.Add(snooze);
            }
            var dismiss = Design.Button("Dismiss", () => Execute(() => _local.DismissReminder(reminder.Id)));
            AutomationProperties.SetName(dismiss, "Dismiss reminder " + reminder.Text); dismiss.IsEnabled = !_busy; actions.Children.Add(dismiss);
            row.Children.Add(actions); var card = Design.Card(row, new Thickness(8)); card.Margin = new Thickness(0, 6, 0, 0); _reminderRows.Children.Add(card);
        }
        if (hadFocus && _reminders.IsVisible) _reminderText.Focus();
    }
}
