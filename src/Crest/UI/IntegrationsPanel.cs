using System;
using System.IO;
using System.Windows;
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
    public IntegrationsPanel(App app, Action<string> message, bool settings)
    {
        _app = app; _message = message; _settings = settings;
        var navigation = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (string name in new[] { "Weather", "Calendar", "Downloads", "AI", "Windows" })
            navigation.Children.Add(Design.Button(name, () => Show(name)));
        Children.Add(navigation); Children.Add(_content); Show("Weather");
    }
    private void Show(string name)
    {
        _content.Children.Clear();
        Action save = _app.SavePreferences;
        switch (name)
        {
            case "Weather": _content.Children.Add(new WeatherPanel(_app.Weather, _app.Preferences.Weather, save, _message)); break;
            case "Calendar": _content.Children.Add(new CalendarPanel(_app.Calendar, _app.Preferences.Calendar, save, _app.OpenIntegrationLink, _message, ChooseCalendar)); break;
            case "Downloads": _content.Children.Add(new DownloadPanel(_app.Downloads, _app.Preferences.Downloads, save, _message, ChooseDownload, path => _ = _app.OpenTarget(() => _app.Local.Launch(new Core.LauncherEntry { Target = path })))); break;
            case "AI": _content.Children.Add(new AiAssistantPanel(_app.Ai, _app.Preferences.Ai, save, _message)); break;
            case "Windows": _content.Children.Add(new WindowsIntegrationsPanel(_app.WindowsIntegrations, _app.Preferences.WindowsIntegrations, save, _app.OpenWindowsTool, _settings)); break;
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
