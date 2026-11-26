using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Crest.Features;
using Microsoft.Win32;

namespace Crest.UI;

public sealed class WeatherPanel : StackPanel
{
    private readonly WeatherService _service;
    private readonly WeatherPreferences _preferences;
    private readonly Action _save;
    private readonly Action<string> _message;
    private readonly TextBlock _status = IntegrationControls.Status();
    private readonly StackPanel _forecast = new(), _cities = new();
    private readonly TextBox _city = new() { MaxLength = 100, MinWidth = 118, Margin = new Thickness(0, 0, 8, 8) };
    private readonly CheckBox _enabled;
    private readonly Button _location, _search, _refresh, _clear, _disconnect;
    private readonly Border _forecastCard;
    private CancellationTokenSource? _locationRequest;
    private bool _busy, _listening, _updating;

    public WeatherPanel(WeatherService service, WeatherPreferences preferences, Action save, Action<string> message,
        Action? openLocationSettings = null, Action? openWindowsPrivacy = null)
    {
        _service = service;
        _preferences = preferences;
        _save = save;
        _message = message;

        _enabled = IntegrationControls.Toggle("Enable weather", "Enable city weather", preferences.Enabled, Toggle);
        Children.Add(PanelLayout.Group("Weather", _enabled, "Current conditions and wind from Open-Meteo."));
        Children.Add(_status);

        var actions = new WrapPanel();
        _refresh = IntegrationControls.Action(actions, "Refresh weather", () => Run(() => _service.RefreshAsync(force: true)));
        _clear = IntegrationControls.Action(actions, "Clear saved weather", () => { _service.Clear(); Refresh(); });
        var current = new StackPanel();
        current.Children.Add(_forecast);
        current.Children.Add(actions);
        _forecastCard = PanelLayout.Group("Current weather", current);
        Children.Add(_forecastCard);

        var setup = new StackPanel();
        var device = new WrapPanel();
        _location = openLocationSettings == null
            ? IntegrationControls.Action(device, "Use Windows location", () => Run(UseLocationAsync), primary: true)
            : IntegrationControls.Action(device, "Set up Windows location", openLocationSettings);
        if (openWindowsPrivacy != null)
            IntegrationControls.Action(device, "Windows location settings", openWindowsPrivacy);
        setup.Children.Add(device);
        if (openLocationSettings == null)
            setup.Children.Add(Design.Text("Crest asks Windows once and shares a rounded area with Open-Meteo for forecasts.", 12, "Muted"));

        var search = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        _search = Design.Button("Find city", () => Run(FindCitiesAsync));
        _search.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(_search, Dock.Right);
        search.Children.Add(_search);
        _city.Text = preferences.UseWindowsLocation ? "" : preferences.City;
        AutomationProperties.SetName(_city, "Weather city search");
        _city.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter || !_search.IsEnabled) return;
            args.Handled = true;
            Run(FindCitiesAsync);
        };
        search.Children.Add(_city);
        var label = Design.Text("Search for a city", 12, "Muted", true);
        label.Margin = new Thickness(0, 12, 0, 0);
        setup.Children.Add(label);
        setup.Children.Add(search);
        setup.Children.Add(_cities);
        var disconnect = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        _disconnect = IntegrationControls.Action(disconnect, "Disconnect weather", Disconnect);
        setup.Children.Add(disconnect);
        Children.Add(PanelLayout.Group("Forecast location", setup));
        Children.Add(Design.Text("Weather data by Open-Meteo · CC BY 4.0", 12, "Muted"));

        Loaded += (_, _) =>
        {
            if (!_listening) { _service.Changed += OnChanged; _listening = true; }
            if (_preferences.Enabled) Run(() => _service.RefreshAsync());
        };
        Unloaded += (_, _) =>
        {
            if (_listening) { _service.Changed -= OnChanged; _listening = false; }
            _locationRequest?.Cancel();
        };
        Refresh();
    }

    public void Refresh()
    {
        _updating = true;
        _enabled.IsChecked = _preferences.Enabled;
        _updating = false;
        IntegrationControls.UpdateStatus(_status, _service.Status);
        _forecast.Children.Clear();
        if (_service.Current is { } weather)
        {
            _forecast.Children.Add(Design.Text(weather.City, 14, "Muted", true));
            var conditions = Design.Text(weather.TemperatureCelsius.ToString("0.#", CultureInfo.CurrentCulture) + " °C · " + weather.Conditions, 28, "Text", true);
            conditions.Margin = new Thickness(0, 2, 0, 4);
            _forecast.Children.Add(conditions);
            _forecast.Children.Add(Design.Text("Wind " + weather.WindKilometresPerHour.ToString("0.#", CultureInfo.CurrentCulture)
                + " km/h" + (_service.IsStale ? " · saved data is stale" : ""), 12, "Muted"));
        }
        _forecastCard.Visibility = _preferences.Enabled && _preferences.Latitude.HasValue ? Visibility.Visible : Visibility.Collapsed;
        _location.IsEnabled = !_busy;
        _city.IsEnabled = _search.IsEnabled = _preferences.Enabled && !_busy;
        _refresh.IsEnabled = _preferences.Enabled && _preferences.Latitude.HasValue && !_busy;
        _clear.IsEnabled = _service.Current != null || _preferences.Enabled && _preferences.Latitude.HasValue && _busy;
        _disconnect.IsEnabled = _preferences.Enabled || _preferences.Latitude.HasValue;
    }

    private async Task FindCitiesAsync()
    {
        _cities.Children.Clear();
        var cities = await _service.FindCitiesAsync(_city.Text);
        if (!_preferences.Enabled) return;
        if (cities.Count == 0) _message("No cities were found. Check your connection or try a nearby city.");
        foreach (var city in cities)
        {
            string label = "Use " + city.Name;
            var button = Design.Button(label, () => Run(async () =>
            {
                _service.SelectCity(city);
                _city.Text = city.Name;
                _cities.Children.Clear();
                _save();
                await _service.RefreshAsync();
            }));
            button.Content = Design.Text(label);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 0, 0, 6);
            _cities.Children.Add(button);
        }
    }

    private async Task UseLocationAsync()
    {
        using var request = new CancellationTokenSource();
        _locationRequest = request;
        try
        {
            if (!await _service.UseWindowsLocationAsync(request.Token)) return;
            _city.Clear();
            _cities.Children.Clear();
            _save();
        }
        finally { _locationRequest = null; }
    }

    private void Disconnect()
    {
        _locationRequest?.Cancel();
        _service.Disconnect();
        _city.Clear();
        _cities.Children.Clear();
        _save();
        Refresh();
    }

    private void OnChanged(object? sender, EventArgs args)
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(Refresh));
    }

    private void Toggle(bool enabled)
    {
        if (_updating) return;
        _preferences.Enabled = enabled;
        _service.ApplyPreferences();
        if (!enabled)
        {
            _locationRequest?.Cancel();
            _cities.Children.Clear();
        }
        _save();
        Refresh();
        if (enabled && _preferences.Latitude.HasValue) Run(() => _service.RefreshAsync());
    }

    private async void Run(Func<Task> work)
    {
        if (_busy) return;
        _busy = true;
        Refresh();
        try { await work(); }
        catch (Exception error) when (error is InvalidOperationException or OperationCanceledException)
        {
            if (_preferences.Enabled) _message("Weather could not be refreshed. Try again.");
        }
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
    private readonly TextBlock _status = IntegrationControls.Status();
    private readonly TextBlock _source = Design.Text("", 14, "Text", true);
    private readonly TextBlock _feedError = IntegrationControls.Status();
    private readonly StackPanel _events = new(), _feedSetup = new() { Visibility = Visibility.Collapsed };
    private readonly PasswordBox _feed = new() { MaxLength = 4096, Padding = new Thickness(10, 7, 10, 7), MinWidth = 100 };
    private readonly CheckBox _enabled, _details;
    private readonly Button _refresh, _outlook, _link, _select, _connectFeed, _clear, _disconnect;
    private readonly Border _calendarCard, _upcomingCard;
    private bool _busy, _listening, _updating;

    public CalendarPanel(CalendarService service, CalendarPreferences preferences, Action save, Action<string> launch,
        Action<string> message, Action<Action<string>>? chooseSource = null)
    {
        _service = service;
        _preferences = preferences;
        _save = save;
        _launch = launch;
        _message = message;
        _chooseSource = chooseSource;

        _enabled = IntegrationControls.Toggle("Enable calendar", "Enable local calendar", preferences.Enabled, Toggle);
        Children.Add(PanelLayout.Group("Calendar", _enabled, "Read upcoming events and open supported meeting links."));
        Children.Add(_status);

        var connected = new StackPanel();
        _source.Margin = new Thickness(0, 0, 0, 8);
        connected.Children.Add(_source);
        var actions = new WrapPanel();
        _refresh = IntegrationControls.Action(actions, "Refresh calendar", () => Run(() => _service.RefreshAsync()));
        _clear = IntegrationControls.Action(actions, "Clear event previews", () => { _service.Clear(); Refresh(); });
        _disconnect = IntegrationControls.Action(actions, "Disconnect calendar", Disconnect);
        connected.Children.Add(actions);
        _calendarCard = PanelLayout.Group("Selected source", connected);
        Children.Add(_calendarCard);

        var setup = new StackPanel();
        var sources = new WrapPanel();
        _outlook = IntegrationControls.Action(sources, "Connect Outlook", () => Run(async () =>
        {
            ShowFeedSetup(false);
            Task reading = _service.ConnectOutlookAsync();
            _save();
            await reading;
        }), primary: true);
        _link = IntegrationControls.Action(sources, "Use subscription link", () => ShowFeedSetup(_feedSetup.Visibility != Visibility.Visible));
        _select = IntegrationControls.Action(sources, "Select local calendar", SelectSource);
        setup.Children.Add(sources);
        setup.Children.Add(Design.Text("Open classic Outlook with your profile before connecting. You can also use a provider’s iCalendar link or a local .ics file.", 12, "Muted"));
        _feedSetup.Margin = new Thickness(0, 10, 0, 0);
        _feedSetup.Children.Add(Design.Text("Paste the HTTPS or webcal subscription link from your provider. Crest protects it with Windows encryption.", 12, "Muted"));
        var feedRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        _connectFeed = Design.Button("Connect calendar link", () => Run(ConnectFeedAsync), primary: true);
        _connectFeed.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(_connectFeed, Dock.Right);
        feedRow.Children.Add(_connectFeed);
        _feed.SetResourceReference(Control.BackgroundProperty, "Inset");
        _feed.SetResourceReference(Control.ForegroundProperty, "Text");
        _feed.SetResourceReference(Control.BorderBrushProperty, "Line");
        _feed.SetResourceReference(PasswordBox.CaretBrushProperty, "Text");
        AutomationProperties.SetName(_feed, "Private calendar subscription link");
        _feed.PasswordChanged += (_, _) => IntegrationControls.UpdateStatus(_feedError, "");
        _feed.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter || !_connectFeed.IsEnabled) return;
            args.Handled = true;
            Run(ConnectFeedAsync);
        };
        feedRow.Children.Add(_feed);
        _feedSetup.Children.Add(feedRow);
        _feedError.Margin = new Thickness(0, 6, 0, 0);
        _feedError.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        _feedSetup.Children.Add(_feedError);
        setup.Children.Add(_feedSetup);
        Children.Add(PanelLayout.Group("Connect a calendar", setup));

        var upcoming = new StackPanel();
        _details = IntegrationControls.Toggle("Show event titles and details", "Show calendar event titles and details", preferences.ShowDetails, SetDetails);
        _details.Margin = new Thickness(0, 0, 0, 8);
        upcoming.Children.Add(_details);
        upcoming.Children.Add(_events);
        _upcomingCard = PanelLayout.Group("Upcoming events", upcoming, "Up to 200 events over the next 90 days.");
        Children.Add(_upcomingCard);

        Loaded += (_, _) =>
        {
            if (!_listening) { _service.Changed += OnChanged; _listening = true; }
            if (_preferences.Enabled) Run(() => _service.RefreshAsync(force: false));
        };
        Unloaded += (_, _) =>
        {
            if (_listening) { _service.Changed -= OnChanged; _listening = false; }
            _feed.Clear();
        };
        Refresh();
    }

    public void Refresh()
    {
        _updating = true;
        _enabled.IsChecked = _preferences.Enabled;
        _details.IsChecked = _preferences.ShowDetails;
        _updating = false;
        var source = _preferences.Source;
        bool configured = source != CalendarSource.None;
        _source.Text = source switch
        {
            CalendarSource.Outlook => "Classic Outlook · Default calendar",
            CalendarSource.Feed => "Online iCalendar subscription",
            CalendarSource.File => Path.GetFileName(_preferences.SourcePath),
            _ => ""
        };
        _calendarCard.Visibility = configured ? Visibility.Visible : Visibility.Collapsed;
        _upcomingCard.Visibility = _preferences.Enabled && configured ? Visibility.Visible : Visibility.Collapsed;
        IntegrationControls.UpdateStatus(_status, _service.Status);
        _outlook.IsEnabled = _link.IsEnabled = _select.IsEnabled = _feed.IsEnabled = _connectFeed.IsEnabled = !_busy;
        _details.IsEnabled = _preferences.Enabled && !_busy;
        _refresh.IsEnabled = _preferences.Enabled && configured && !_busy;
        _disconnect.IsEnabled = _preferences.Enabled || configured;
        var entries = _service.Events;
        _clear.IsEnabled = entries.Count != 0 || _preferences.Enabled && configured && _busy;
        _events.Children.Clear();
        if (entries.Count == 0)
            _events.Children.Add(Design.Text("No event previews to show.", 12, "Muted"));
        foreach (var entry in entries)
        {
            var row = new StackPanel();
            string title = _preferences.ShowDetails ? entry.Title : "Busy";
            row.Children.Add(Design.Text(title, 13, "Text", true));
            string time = entry.AllDay
                ? (entry.CalendarDate?.ToString("D", CultureInfo.CurrentCulture) ?? entry.StartUtc.ToLocalTime().ToString("D")) + " · all day"
                : entry.StartUtc.ToLocalTime().ToString("g") + " – " + entry.EndUtc.ToLocalTime().ToString("t");
            row.Children.Add(Design.Text(time, 12, "Muted"));
            if (_preferences.ShowDetails)
            {
                if (!string.IsNullOrEmpty(entry.Location)) row.Children.Add(Design.Text(entry.Location, 12, "Muted"));
                if (!string.IsNullOrEmpty(entry.Description)) row.Children.Add(Design.Text(entry.Description.Length > 500 ? entry.Description[..500] + "…" : entry.Description, 12, "Muted"));
                if (entry.MeetingUrl is { } link && MeetingLinks.Validate(link) is { } validated)
                {
                    var join = Design.Button("Open " + validated.Provider + " meeting", () => _launch(validated.Url));
                    join.HorizontalAlignment = HorizontalAlignment.Left;
                    join.Margin = new Thickness(0, 8, 0, 0);
                    AutomationProperties.SetName(join, "Open " + validated.Provider + " meeting for " + title);
                    row.Children.Add(join);
                }
            }
            var card = Design.Card(row, new Thickness(12));
            card.SetResourceReference(Border.BackgroundProperty, "Inset");
            card.Margin = new Thickness(0, 4, 0, 4);
            _events.Children.Add(card);
        }
    }

    private void ShowFeedSetup(bool visible)
    {
        _feedSetup.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetItemStatus(_link, visible ? "Expanded" : "");
        if (visible) _feed.Focus();
        else
        {
            _feed.Clear();
            IntegrationControls.UpdateStatus(_feedError, "");
        }
    }

    private async Task ConnectFeedAsync()
    {
        string previousFeed = _preferences.ProtectedFeed;
        var connecting = _service.ConnectFeedAsync(_feed.Password);
        if (_preferences.ProtectedFeed != previousFeed) _save();
        string? error = await connecting;
        if (error != null)
        {
            IntegrationControls.UpdateStatus(_feedError, error);
            return;
        }
        ShowFeedSetup(false);
        _link.Focus();
    }

    private void SelectSource()
    {
        void Update(string path) => Run(async () =>
        {
            ShowFeedSetup(false);
            Task reading = _service.SelectSourceAsync(path);
            _save();
            await reading;
        });
        if (_chooseSource != null)
        {
            _chooseSource(Update);
            return;
        }
        var dialog = new OpenFileDialog { Title = "Select a local calendar", Filter = "iCalendar files|*.ics", CheckFileExists = true };
        if (dialog.ShowDialog() == true) Update(dialog.FileName);
    }

    private void Disconnect()
    {
        ShowFeedSetup(false);
        _service.Disconnect();
        _save();
        Refresh();
    }

    private void OnChanged(object? sender, EventArgs args)
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(Refresh));
    }

    private void Toggle(bool enabled)
    {
        if (_updating) return;
        _preferences.Enabled = enabled;
        if (!enabled) ShowFeedSetup(false);
        _service.ApplyPreferences();
        _save();
        Refresh();
        if (enabled && _preferences.Source != CalendarSource.None) Run(() => _service.RefreshAsync(force: false));
    }

    private void SetDetails(bool details)
    {
        if (_updating) return;
        _preferences.ShowDetails = details;
        _save();
        Refresh();
    }

    private async void Run(Func<Task> work)
    {
        if (_busy) return;
        _busy = true;
        Refresh();
        try { await work(); }
        catch (Exception error) when (error is InvalidOperationException or OperationCanceledException)
        {
            if (_preferences.Enabled) _message("Calendar could not be refreshed. Check your selected source.");
        }
        finally { _busy = false; Refresh(); }
    }
}

internal static class IntegrationControls
{
    public static CheckBox Toggle(string label, string automationName, bool enabled, Action<bool> change)
    {
        var box = new CheckBox { Content = label, IsChecked = enabled };
        AutomationProperties.SetName(box, automationName);
        box.Checked += (_, _) => change(true);
        box.Unchecked += (_, _) => change(false);
        return box;
    }

    public static Button Action(Panel parent, string label, Action action, bool primary = false)
    {
        var button = Design.Button(label, action, primary);
        button.Margin = new Thickness(0, 0, 8, 8);
        parent.Children.Add(button);
        return button;
    }

    public static TextBlock Status()
    {
        var status = Design.Text("", 12, "Muted");
        status.Margin = new Thickness(0, 0, 0, 12);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        return status;
    }

    public static void UpdateStatus(TextBlock status, string text)
    {
        status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        if (status.Text == text) return;
        status.Text = text;
        System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(status)?.RaiseAutomationEvent(
            System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }
}
