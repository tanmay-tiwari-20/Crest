using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Crest.UI;

/// <summary>All optional tools remain children of the existing Settings or island window.</summary>
public sealed class IntegrationsPanel : StackPanel
{
    private readonly App _app;
    private readonly bool _settings;
    private readonly Action<string> _message;
    private readonly StackPanel _content = new();
    private readonly Dictionary<string, Button> _tabs = new();
    private readonly Dictionary<string, TextBlock> _statuses = new();
    private string? _activeSection;

    public IntegrationsPanel(App app, Action<string> message, bool settings)
    {
        _app = app;
        _message = message;
        _settings = settings;
        var navigation = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetName(navigation, "Integration sources");
        foreach (var (name, glyph) in new[] { ("Weather", "\uE706"), ("Calendar", "\uE787"), ("Downloads", "\uE896"), ("AI", "\uE8BD"), ("Windows", "\uE770") })
        {
            var button = Design.Button(name, () => ShowSection(name));
            button.Margin = new Thickness(0, 0, 8, 8);
            button.Padding = new Thickness(12, 8, 12, 8);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            var icon = Design.Icon(glyph, settings ? 18 : 14);
            icon.Margin = new Thickness(0, 0, 8, 0);
            var text = new StackPanel();
            text.Children.Add(Design.Text(name, 13, "Text", true));
            if (settings)
            {
                button.MinWidth = 100;
                var status = Design.Text("", 11, "Muted");
                _statuses.Add(name, status);
                text.Children.Add(status);
            }
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(icon);
            content.Children.Add(text);
            button.Content = content;
            _tabs.Add(name, button);
            navigation.Children.Add(button);
        }
        Children.Add(navigation);
        Children.Add(_content);
        ShowSection(_app.Preferences.Calendar.Enabled && !_app.Preferences.Weather.Enabled ? "Calendar" : "Weather");
    }

    public void ShowSection(string name)
    {
        if (!_tabs.ContainsKey(name)) return;
        if (_activeSection != name) Show(name);
        RefreshNavigation();
    }

    private void Show(string name)
    {
        _activeSection = name;
        _content.Children.Clear();
        void Save()
        {
            _app.SavePreferences();
            RefreshNavigation();
        }
        Action? weatherSettings = _settings ? null : () => _app.OpenIntegrationSettings("Weather");
        switch (name)
        {
            case "Weather": _content.Children.Add(new WeatherPanel(_app.Weather, _app.Preferences.Weather, Save, _message, weatherSettings,
                _settings ? () => _app.OpenWindowsTool("ms-settings:privacy-location") : null)); break;
            case "Calendar": _content.Children.Add(new CalendarPanel(_app.Calendar, _app.Preferences.Calendar, Save, _app.OpenIntegrationLink, _message, ChooseCalendar)); break;
            case "Downloads": _content.Children.Add(new DownloadPanel(_app.Downloads, _app.Preferences.Downloads, Save, _message, ChooseDownload, path => _ = _app.OpenTarget(() => _app.Local.Launch(new Core.LauncherEntry { Target = path })))); break;
            case "AI": _content.Children.Add(new AiAssistantPanel(_app.Ai, _app.Preferences.Ai, Save, _message)); break;
            case "Windows": _content.Children.Add(new WindowsIntegrationsPanel(_app.WindowsIntegrations, _app.Preferences.WindowsIntegrations, Save, _app.OpenWindowsTool, _settings)); break;
        }
        PanelLayout.Refine(_content);
    }

    private void RefreshNavigation()
    {
        var options = _app.Preferences;
        foreach (var (name, button) in _tabs)
        {
            bool enabled = name switch
            {
                "Weather" => options.Weather.Enabled,
                "Calendar" => options.Calendar.Enabled,
                "Downloads" => options.Downloads.Enabled,
                "AI" => options.Ai.Enabled,
                _ => options.WindowsIntegrations.NotificationsEnabled || options.WindowsIntegrations.AccessoryBatteryEnabled || options.WindowsIntegrations.PhoneEnabled
            };
            bool selected = name == _activeSection;
            PanelLayout.Select(button, selected, name);
            button.SetResourceReference(Control.BorderBrushProperty, selected ? "Accent" : "Divider");
            string status = !enabled ? "Off" : name switch
            {
                "Weather" when !options.Weather.Latitude.HasValue => "Choose location",
                "Calendar" when options.Calendar.Source == Features.CalendarSource.None => "Choose source",
                _ => "Enabled"
            };
            if (_statuses.TryGetValue(name, out var label)) label.Text = status;
            AutomationProperties.SetItemStatus(button, selected ? "Selected · " + status : status);
        }
    }

    private void ChooseCalendar(Action<string> selected)
    {
        _app.Controller.Dismiss();
        var dialog = new OpenFileDialog { Title = "Choose a local calendar", Filter = "iCalendar files|*.ics", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) selected(dialog.FileName);
        _app.Policy.Recheck();
    }
    private string? ChooseDownload()
    {
        _app.Controller.Dismiss();
        var dialog = new SaveFileDialog { Title = "Choose a new download filename", FileName = "download.bin", OverwritePrompt = false, AddExtension = false };
        string? result = dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
        _app.Policy.Recheck(); return result;
    }
}
