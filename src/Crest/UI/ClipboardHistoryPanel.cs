using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Crest.Features;

namespace Crest.UI;

public sealed class ClipboardHistoryPanel : StackPanel
{
    private readonly ClipboardHistoryHub _history;
    private readonly Func<string, string?> _copy;
    private readonly Action<string> _message;
    private readonly Func<string>? _captureStatus;
    private readonly TextBox _search = new() { MaxLength = 200, Margin = new Thickness(0, 4, 0, 8) };
    private readonly TextBlock _status = Design.Text("", 11, "Muted");
    private readonly StackPanel _rows = new();
    private readonly Button _pause, _clear;
    private string _signature = "";
    private bool _busy;

    public ClipboardHistoryPanel(ClipboardHistoryHub history, Func<string, string?> copy, Action<string> message, Func<string>? captureStatus = null)
    {
        _history = history;
        _copy = copy;
        _message = message;
        _captureStatus = captureStatus;
        _status.Margin = new Thickness(0, 0, 0, 12);
        Children.Add(_status);
        Children.Add(Design.Text("Search saved text", 10, "Muted"));
        AutomationProperties.SetName(_search, "Search clipboard history");
        _search.TextChanged += (_, _) => Refresh();
        Children.Add(_search);
        var actions = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        _pause = Design.Button("Pause capture", () => Execute(() => _history.SetPaused(!_history.Paused)));
        _clear = Design.Button("Clear history", () => Execute(_history.Clear));
        _pause.Margin = new Thickness(0, 0, 4, 0);
        _clear.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(_clear, 1);
        actions.Children.Add(_pause);
        actions.Children.Add(_clear);
        Children.Add(actions);
        Children.Add(_rows);
        PanelLayout.Refine(this);
        Refresh();
    }

    public void Refresh()
    {
        var items = _history.Search(_search.Text);
        bool enabled = _history.Enabled;
        _search.IsEnabled = enabled;
        _pause.IsEnabled = enabled && !_busy;
        _clear.IsEnabled = enabled && !_busy;
        _pause.Content = _history.Paused ? "Resume capture" : "Pause capture";
        AutomationProperties.SetName(_pause, (string)_pause.Content);
        _status.Text = !enabled ? "Clipboard history is off. Enable it in Settings → Privacy to save new text locally."
            : (_history.Paused ? "Capture paused · " : "Capturing new text · ")
              + $"{items.Count} matching / {ClipboardHistoryHub.MaxItems} maximum · {_history.RetentionDays}-day retention. Pins stay until removed or cleared.";
        if (enabled && !_history.Paused && _captureStatus?.Invoke() is { } status
            && status != "Capturing new text copied on this PC.") _status.Text += " " + status;
        if (enabled && _history.LastError is { } storageError) _status.Text += " " + storageError;
        string signature = enabled + ":" + _busy + ":" + _search.Text + ":"
            + string.Join("|", items.Select(x => x.Id + ":" + x.Pinned + ":" + x.Truncated + ":" + x.CapturedUtc.UtcTicks));
        if (_signature == signature) return;
        _signature = signature;
        _rows.Children.Clear();
        if (!enabled) return;
        if (items.Count == 0)
        {
            _rows.Children.Add(Design.Text(string.IsNullOrEmpty(_search.Text)
                ? "Copy new text after enabling capture. Existing clipboard contents are never imported automatically."
                : "No saved text matches this search.", 12, "Muted"));
            return;
        }
        foreach (var item in items)
        {
            var content = new StackPanel { Margin = new Thickness(0, 0, 0, 7) };
            string preview = item.Text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            if (preview.Length > 110) preview = preview[..110] + "…";
            var copyButton = Design.Button(preview, () =>
            {
                string? error = _copy(item.Text);
                _message(error ?? "Copied saved text to the Windows clipboard.");
            });
            AutomationProperties.SetName(copyButton, "Copy saved text: " + preview);
            copyButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            copyButton.IsEnabled = !_busy;
            content.Children.Add(copyButton);
            var bottom = new Grid { Margin = new Thickness(0, 3, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition());
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottom.Children.Add(Design.Text(item.CapturedUtc.ToLocalTime().ToString("g") + (item.Pinned ? " · pinned" : "")
                + (item.Truncated ? $" · shortened to {item.Text.Length:N0} characters" : ""), 10, "Muted"));
            var pin = Design.Button(item.Pinned ? "Unpin" : "Pin", () => Execute(() => _history.SetPinned(item.Id, !item.Pinned)));
            var remove = Design.IconButton("\uE711", "Remove saved text: " + preview, () => Execute(() => _history.Remove(item.Id)));
            pin.Padding = new Thickness(7, 2, 7, 2);
            pin.MinHeight = 32;
            remove.Padding = new Thickness(4);
            remove.MinHeight = 32;
            remove.MinWidth = 32;
            pin.IsEnabled = remove.IsEnabled = !_busy;
            AutomationProperties.SetName(pin, (item.Pinned ? "Unpin saved text: " : "Pin saved text: ") + preview);
            Grid.SetColumn(pin, 1);
            Grid.SetColumn(remove, 2);
            bottom.Children.Add(pin);
            bottom.Children.Add(remove);
            content.Children.Add(bottom);
            var card = Design.Card(content, new Thickness(12));
            card.Margin = new Thickness(0, 0, 0, 8);
            _rows.Children.Add(card);
        }
    }

    private async void Execute(Action action)
    {
        if (_busy) return;
        _busy = true;
        Refresh();
        try
        {
            await Task.Run(action);
            if (_history.LastError is { } error) _message(error);
        }
        catch (InvalidOperationException) { _message("Clipboard history could not be updated. Try again."); }
        finally { _busy = false; Refresh(); }
    }
}
