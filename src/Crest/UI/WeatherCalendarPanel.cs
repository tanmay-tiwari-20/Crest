using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Crest.Features;
using Microsoft.Win32;

namespace Crest.UI;

public sealed class WeatherPanel : StackPanel
{
    private readonly WeatherService _service;
    private readonly WeatherPreferences _preferences;
    private readonly Action _save;
    private readonly Action<string> _message;
    private readonly TextBlock _status = Design.Text("", 11, "Muted");
    private readonly StackPanel _forecast = new(), _cities = new();
    private readonly TextBox _city = new() { MaxLength = 100, Margin = new Thickness(0, 8, 0, 8) };
    private readonly CheckBox _enabled;
    private readonly Button _search, _refresh;
    private bool _busy, _listening;
    public WeatherPanel(WeatherService service, WeatherPreferences preferences, Action save, Action<string> message)
    {
        _service = service; _preferences = preferences; _save = save; _message = message;
        Children.Add(Design.Text("Weather for your city", 15, "Text", true));
        Children.Add(Design.Text("Open-Meteo provides city search and current weather over the internet. Your chosen city and coordinates are sent to Open-Meteo; Crest never reads your device location. No account or API key is required. Free non-commercial access is subject to the provider's terms.", 11, "Muted"));
        _enabled = new CheckBox { Content = "Enable city weather", IsChecked = preferences.Enabled, Margin = new Thickness(0, 10, 0, 8) };
        AutomationProperties.SetName(_enabled, "Enable city weather");
        _enabled.Checked += (_, _) => Toggle(true); _enabled.Unchecked += (_, _) => Toggle(false); Children.Add(_enabled);
        Children.Add(_status); Children.Add(_forecast);
        _city.Text = preferences.City; AutomationProperties.SetName(_city, "Weather city search"); Children.Add(_city);
        _search = Design.Button("Find city", () => Run(async () =>
        {
            _cities.Children.Clear(); var cities = await _service.FindCitiesAsync(_city.Text);
            if (!_preferences.Enabled) return;
            if (cities.Count == 0) _message("No cities were found. Check your connection or try a nearby city name.");
            foreach (var city in cities)
            {
                var button = Design.Button("Use " + city.Name, () => Run(async () => { _service.SelectCity(city); _city.Text = city.Name; _cities.Children.Clear(); _save(); await _service.RefreshAsync(); }));
                button.Margin = new Thickness(0, 4, 0, 0); _cities.Children.Add(button);
            }
        }));
        Children.Add(_search); Children.Add(_cities);
        _refresh = Design.Button("Refresh weather", () => Run(() => _service.RefreshAsync(force: true))); _refresh.Margin = new Thickness(0, 8, 0, 4); Children.Add(_refresh);
        Children.Add(Design.Button("Clear saved weather", () => { _service.Clear(); Refresh(); }));
        var disconnect = Design.Button("Disconnect weather", () => { _service.Disconnect(); _enabled.IsChecked = false; _city.Clear(); _cities.Children.Clear(); _save(); Refresh(); }); disconnect.Margin = new Thickness(0, 4, 0, 0); Children.Add(disconnect);
        Children.Add(Design.Text("Weather data: Open-Meteo · CC BY 4.0. Refreshes occur when you open this module or choose Refresh.", 10, "Muted"));
        Loaded += (_, _) => { if (!_listening) { _service.Changed += OnChanged; _listening = true; } if (_preferences.Enabled) Run(() => _service.RefreshAsync()); };
        Unloaded += (_, _) => { if (_listening) { _service.Changed -= OnChanged; _listening = false; } };
        Refresh();
    }
    public void Refresh()
    {
        _status.Text = _service.Status; _forecast.Children.Clear();
        if (_service.Current is { } weather)
        {
            _forecast.Children.Add(Design.Text(weather.City, 14, "Text", true));
            _forecast.Children.Add(Design.Text(weather.TemperatureCelsius.ToString("0.#", CultureInfo.CurrentCulture) + " °C · " + weather.Conditions, 18, "Text", true));
            _forecast.Children.Add(Design.Text("Wind " + weather.WindKilometresPerHour.ToString("0.#", CultureInfo.CurrentCulture) + " km/h" + (_service.IsStale ? " · saved data is stale" : ""), 11, "Muted"));
        }
        _city.IsEnabled = _search.IsEnabled = _preferences.Enabled && !_busy;
        _refresh.IsEnabled = _preferences.Enabled && _preferences.Latitude.HasValue && !_busy;
    }
    private void OnChanged(object? sender, EventArgs args) { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(Refresh)); }
    private void Toggle(bool enabled) { _preferences.Enabled = enabled; _service.ApplyPreferences(); if (!enabled) _cities.Children.Clear(); _save(); Refresh(); }
    private async void Run(Func<Task> work)
    {
        if (_busy) return; _busy = true; Refresh();
        try { await work(); }
        catch (Exception error) when (error is InvalidOperationException or OperationCanceledException) { _message("Weather could not be refreshed. Try again."); }
        finally { _busy = false; Refresh(); }
    }
}

public sealed class CalendarPanel : StackPanel
{
    private readonly CalendarService _service;
    private readonly CalendarPreferences _preferences;
    private readonly Action _save;
    private readonly Action<string> _launch, _message;
    private readonly Action<Action<string>>? _chooseSource;
    private readonly TextBlock _status = Design.Text("", 11, "Muted"), _source = Design.Text("", 11, "Muted");
    private readonly StackPanel _events = new();
    private readonly CheckBox _enabled, _details;
    private readonly Button _refresh, _select;
    private bool _busy, _listening;
    public CalendarPanel(CalendarService service, CalendarPreferences preferences, Action save, Action<string> launch, Action<string> message, Action<Action<string>>? chooseSource = null)
    {
        _service = service; _preferences = preferences; _save = save; _launch = launch; _message = message; _chooseSource = chooseSource;
        Children.Add(Design.Text("Calendar and meetings", 15, "Text", true));
        Children.Add(Design.Text("Read a local .ics calendar file you select. Crest reads event names, dates, locations, descriptions, and meeting links on this PC. Event previews stay in memory. No calendar account is connected and no event is changed.", 11, "Muted"));
        _enabled = new CheckBox { Content = "Enable local calendar", IsChecked = preferences.Enabled, Margin = new Thickness(0, 10, 0, 6) };
        AutomationProperties.SetName(_enabled, "Enable local calendar"); _enabled.Checked += (_, _) => Toggle(true); _enabled.Unchecked += (_, _) => Toggle(false); Children.Add(_enabled);
        _details = new CheckBox { Content = "Show event titles and details", IsChecked = preferences.ShowDetails, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(_details, "Show calendar event titles and details");
        _details.Checked += (_, _) => SetDetails(true); _details.Unchecked += (_, _) => SetDetails(false); Children.Add(_details);
        Children.Add(_source); Children.Add(_status);
        _select = Design.Button("Select local calendar", SelectSource); _select.Margin = new Thickness(0, 8, 0, 4); Children.Add(_select);
        _refresh = Design.Button("Refresh calendar", () => Run(() => _service.RefreshAsync())); Children.Add(_refresh);
        var clear = Design.Button("Clear event previews", () => { _service.Clear(); Refresh(); }); clear.Margin = new Thickness(0, 4, 0, 4); Children.Add(clear);
        Children.Add(Design.Button("Disconnect calendar", () => { _service.Disconnect(); _enabled.IsChecked = false; _save(); Refresh(); }));
        Children.Add(Design.Text("Up to 200 events over the next 90 days. Supports ordinary events and daily, weekly, monthly, or yearly recurrence; complex recurrence rules are reported and skipped. Refresh reads the selected file again.", 10, "Muted"));
        Children.Add(Design.Text("Meeting actions open validated Microsoft Teams, Zoom, or Google Meet HTTPS join links in your browser. Call mute and hang-up controls are unavailable. Join opens a provider page; it does not automatically join a call.", 10, "Muted"));
        Children.Add(_events);
        Loaded += (_, _) => { if (!_listening) { _service.Changed += OnChanged; _listening = true; } if (_preferences.Enabled) Run(() => _service.RefreshAsync()); };
        Unloaded += (_, _) => { if (_listening) { _service.Changed -= OnChanged; _listening = false; } };
        Refresh();
    }
    public void Refresh()
    {
        _source.Text = string.IsNullOrEmpty(_preferences.SourcePath) ? "No calendar selected." : "Selected: " + System.IO.Path.GetFileName(_preferences.SourcePath);
        _status.Text = _service.Status; _events.Children.Clear();
        _select.IsEnabled = _details.IsEnabled = _preferences.Enabled && !_busy;
        _refresh.IsEnabled = _preferences.Enabled && !string.IsNullOrEmpty(_preferences.SourcePath) && !_busy;
        foreach (var entry in _service.Events)
        {
            var row = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            string title = _preferences.ShowDetails ? entry.Title : "Busy";
            row.Children.Add(Design.Text(title, 13, "Text", true));
            string time = entry.AllDay ? (entry.CalendarDate?.ToString("D", CultureInfo.CurrentCulture) ?? entry.StartUtc.ToLocalTime().ToString("D")) + " · all day" : entry.StartUtc.ToLocalTime().ToString("g") + " – " + entry.EndUtc.ToLocalTime().ToString("t");
            row.Children.Add(Design.Text(time, 11, "Muted"));
            if (_preferences.ShowDetails)
            {
                if (!string.IsNullOrEmpty(entry.Location)) row.Children.Add(Design.Text(entry.Location, 11, "Muted"));
                if (!string.IsNullOrEmpty(entry.Description)) row.Children.Add(Design.Text(entry.Description.Length > 500 ? entry.Description[..500] + "…" : entry.Description, 11, "Muted"));
                if (entry.MeetingUrl is { } link && MeetingLinks.Validate(link) is { } validated)
                {
                    var join = Design.Button("Open " + validated.Provider + " meeting", () => _launch(validated.Url));
                    AutomationProperties.SetName(join, "Open " + validated.Provider + " meeting for " + title); row.Children.Add(join);
                }
            }
            _events.Children.Add(row);
        }
    }
    private void SelectSource()
    {
        if (_chooseSource != null) { _chooseSource(path => Run(async () => { await _service.SelectSourceAsync(path); _save(); })); return; }
        var dialog = new OpenFileDialog { Title = "Select a local calendar", Filter = "iCalendar files|*.ics", CheckFileExists = true };
        if (dialog.ShowDialog() == true) Run(async () => { await _service.SelectSourceAsync(dialog.FileName); _save(); });
    }
    private void OnChanged(object? sender, EventArgs args) { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(Refresh)); }
    private void Toggle(bool enabled) { _preferences.Enabled = enabled; _service.ApplyPreferences(); _save(); Refresh(); }
    private void SetDetails(bool details) { _preferences.ShowDetails = details; _save(); Refresh(); }
    private async void Run(Func<Task> work)
    {
        if (_busy) return; _busy = true; Refresh();
        try { await work(); }
        catch (Exception error) when (error is InvalidOperationException or OperationCanceledException) { _message("Calendar could not be refreshed. Select an available local file and try again."); }
        finally { _busy = false; Refresh(); }
    }
}
