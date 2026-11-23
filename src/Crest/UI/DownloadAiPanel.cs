using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Crest.Features;

namespace Crest.UI;

public sealed class DownloadPanel : StackPanel
{
    private readonly DownloadService _service;
    private readonly DownloadPreferences _preferences;
    private readonly Action _save;
    private readonly Action<string> _message;
    private readonly Func<string?> _chooseDestination;
    private readonly Action<string>? _openDestination;
    private readonly TextBox _url = new() { MaxLength = 4096, Margin = new Thickness(0, 4, 0, 8) };
    private readonly TextBlock _status = Design.Text("", 11, "Muted");
    private readonly Button _start;
    private readonly StackPanel _rows = new();
    private readonly Dictionary<string, TextBlock> _labels = new();
    private string _signature = "";

    public DownloadPanel(DownloadService service, DownloadPreferences preferences, Action save, Action<string> message,
        Func<string?> chooseDestination, Action<string>? openDestination = null)
    {
        _service = service; _preferences = preferences; _save = save; _message = message;
        _chooseDestination = chooseDestination; _openDestination = openDestination;
        Children.Add(Design.Text("Downloads started here send the selected URL to its server and save bytes only to the filename you choose. Up to two transfers run at once, with a four GiB limit each. Other apps' downloads are not monitored.", 12, "Muted"));
        var enabled = new CheckBox { Content = "Enable Crest-managed downloads", IsChecked = preferences.Enabled, Margin = new Thickness(0, 8, 0, 8) };
        AutomationProperties.SetName(enabled, "Enable Crest-managed downloads");
        enabled.Checked += (_, _) => SetEnabled(true);
        enabled.Unchecked += (_, _) => SetEnabled(false);
        Children.Add(enabled);
        Children.Add(Design.Text("Download URL", 11, "Muted"));
        AutomationProperties.SetName(_url, "Crest download HTTP or HTTPS URL");
        Children.Add(_url);
        _start = Design.Button("Choose destination and download", Start, true);
        Children.Add(_start);
        Children.Add(_status);
        Children.Add(_rows);
        Children.Add(Design.Button("Cancel transfers and clear list", _service.Clear));
        Loaded += (_, _) => { _service.Changed += OnChanged; Refresh(); };
        Unloaded += (_, _) => _service.Changed -= OnChanged;
        Refresh();
    }

    private void SetEnabled(bool enabled)
    {
        _preferences.Enabled = enabled; _service.ApplyPreferences(); _save(); Refresh();
    }
    private void Start()
    {
        if (!DownloadService.TryGetUrl(_url.Text.Trim(), out _)) { _message("Enter an HTTP or HTTPS download URL first."); return; }
        string? path = _chooseDestination();
        if (path != null && _service.Start(_url.Text.Trim(), path) is string error) _message(error);
        Refresh();
    }
    private void OnChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(Refresh));
    }
    public void Refresh()
    {
        var items = _service.Items;
        _url.IsEnabled = _start.IsEnabled = _preferences.Enabled;
        _status.Text = !_preferences.Enabled ? "Downloads are off. Disabling cancels transfers and clears the in-memory list."
            : $"{items.Count} / {DownloadService.MaxItems} downloads · completed files remain in your chosen folders.";
        string signature = string.Join("|", items.Select(x => x.Id + ":" + x.Phase));
        if (signature != _signature)
        {
            _signature = signature;
            _rows.Children.Clear(); _labels.Clear();
            foreach (var item in items)
            {
                var row = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
                row.Children.Add(Design.Text(Path.GetFileName(item.Destination), 12, "Text", true));
                var label = Design.Text("", 11, "Muted"); _labels[item.Id] = label; row.Children.Add(label);
                if (item.Phase == DownloadPhase.Downloading) row.Children.Add(Design.Button("Cancel " + Path.GetFileName(item.Destination), () => _service.Cancel(item.Id)));
                else
                {
                    if (item.Phase is DownloadPhase.Failed or DownloadPhase.Cancelled)
                        row.Children.Add(Design.Button("Retry " + Path.GetFileName(item.Destination), () => { if (_service.Retry(item.Id) is string error) _message(error); }));
                    if (item.Phase == DownloadPhase.Completed && _openDestination != null)
                        row.Children.Add(Design.Button("Open downloaded file", () => _openDestination(item.Destination)));
                    row.Children.Add(Design.Button("Remove download from list", () => _service.Remove(item.Id)));
                }
                _rows.Children.Add(row);
            }
        }
        foreach (var item in items)
        {
            if (!_labels.TryGetValue(item.Id, out var label)) continue;
            string progress = item.Percentage is { } percentage ? $"{percentage:F0}% · {FormatBytes(item.ReceivedBytes)} / {FormatBytes(item.TotalBytes!.Value)}"
                : FormatBytes(item.ReceivedBytes) + " received · total size unavailable";
            label.Text = item.Phase + " · " + progress + "\n" + item.Message;
            AutomationProperties.SetName(label, Path.GetFileName(item.Destination) + " " + label.Text);
        }
    }
    private static string FormatBytes(long bytes) => bytes >= 1024 * 1024 ? (bytes / 1048576.0).ToString("F1") + " MiB" : (bytes / 1024.0).ToString("F1") + " KiB";
}

public sealed class AiAssistantPanel : StackPanel
{
    private readonly AiAssistantService _service;
    private readonly AiPreferences _preferences;
    private readonly Action _save;
    private readonly Action<string> _message;
    private readonly TextBox _endpoint = new() { MaxLength = 300, Margin = new Thickness(0, 4, 0, 8) };
    private readonly TextBox _model = new() { MaxLength = 100, Margin = new Thickness(0, 4, 0, 8) };
    private readonly TextBox _prompt = new() { MaxLength = AiAssistantService.MaxPromptCharacters, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, MaxHeight = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 4, 0, 8) };
    private readonly TextBlock _status = Design.Text("", 11, "Muted");
    private readonly StackPanel _conversation = new();
    private readonly Button _ask, _cancel;
    private readonly CheckBox _enabled;
    private string _signature = "";

    public AiAssistantPanel(AiAssistantService service, AiPreferences preferences, Action save, Action<string> message)
    {
        _service = service; _preferences = preferences; _save = save; _message = message;
        Children.Add(Design.Text("Read-only chat uses an existing Ollama model on this PC. Crest sends only the question and this conversation to the selected loopback service. It has no file, clipboard, system, or action tools. No model is installed or bundled; your local model may use substantial memory and CPU.", 12, "Muted"));
        Children.Add(Design.Text("Local Ollama endpoint", 11, "Muted"));
        _endpoint.Text = preferences.Endpoint;
        AutomationProperties.SetName(_endpoint, "Local Ollama endpoint, loopback only"); Children.Add(_endpoint);
        Children.Add(Design.Text("Installed model name", 11, "Muted"));
        _model.Text = preferences.Model;
        AutomationProperties.SetName(_model, "Installed Ollama model name"); Children.Add(_model);
        Children.Add(Design.Button("Save local model selection", () => SaveSelection()));
        _enabled = new CheckBox { Content = "Enable read-only local AI assistance", IsChecked = preferences.Enabled, Margin = new Thickness(0, 8, 0, 8) };
        AutomationProperties.SetName(_enabled, "Enable read-only local AI assistance");
        _enabled.Checked += (_, _) =>
        {
            if (!SaveSelection()) { _enabled.IsChecked = false; return; }
            preferences.Enabled = true; _service.ApplyPreferences(); _save(); Refresh();
        };
        _enabled.Unchecked += (_, _) => { preferences.Enabled = false; _service.ApplyPreferences(); _save(); Refresh(); };
        Children.Add(_enabled);
        Children.Add(_status);
        Children.Add(_conversation);
        Children.Add(Design.Text("Your question", 11, "Muted"));
        AutomationProperties.SetName(_prompt, "Read-only AI question"); Children.Add(_prompt);
        _ask = Design.Button("Ask local model", Ask, true); Children.Add(_ask);
        _cancel = Design.Button("Cancel response", _service.Cancel); Children.Add(_cancel);
        Children.Add(Design.Button("Clear conversation", () => { _prompt.Clear(); _service.Clear(); }));
        Loaded += (_, _) => { _service.Changed += OnChanged; Refresh(); };
        Unloaded += (_, _) => _service.Changed -= OnChanged;
        Refresh();
    }

    private bool SaveSelection()
    {
        if (!AiAssistantService.TryGetEndpoint(_endpoint.Text.Trim(), out _) || !AiAssistantService.ValidModel(_model.Text.Trim()))
        { _message("Use a loopback HTTP(S) endpoint such as http://127.0.0.1:11434/ and a valid installed model name."); return false; }
        _preferences.Endpoint = _endpoint.Text.Trim(); _preferences.Model = _model.Text.Trim();
        _service.ApplyPreferences(); _save(); Refresh(); return true;
    }
    private async void Ask()
    {
        string prompt = _prompt.Text;
        if (string.IsNullOrWhiteSpace(prompt)) { _message("Enter a question first."); return; }
        var task = _service.AskAsync(prompt); Refresh();
        string? error = await task;
        if (error != null) _message(error); else _prompt.Clear();
        Refresh();
    }
    private void OnChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(Refresh));
    }
    public void Refresh()
    {
        bool busy = _service.Busy;
        _prompt.IsEnabled = _ask.IsEnabled = _preferences.Enabled && !busy;
        _cancel.IsEnabled = busy;
        _status.Text = _service.Status;
        var messages = _service.Messages;
        string signature = string.Join("|", messages.Select(x => x.Role + ":" + x.Text));
        if (_signature == signature) return;
        _signature = signature;
        _conversation.Children.Clear();
        foreach (var item in messages)
        {
            _conversation.Children.Add(Design.Text(item.Role == "user" ? "You" : "Local model", 11, "Accent", true));
            var answer = new TextBox { Text = item.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 3, 0, 8) };
            AutomationProperties.SetName(answer, (item.Role == "user" ? "Your question" : "Local model answer") + ", read-only text");
            _conversation.Children.Add(answer);
        }
    }
}
