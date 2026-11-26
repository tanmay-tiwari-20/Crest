using System.IO;
using System.Net;
using System.Net.Http;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Crest.Core;
using Crest.Features;
using Crest.UI;

internal static class WeatherCalendarChecks
{
    public static async Task Run(Crest.App app, Action<bool, string> check, string outputDirectory)
    {
        app.Dispatcher.VerifyAccess();
        string directory = Path.Combine(Path.GetTempPath(), "CrestWeatherCalendarUI-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); Directory.CreateDirectory(outputDirectory);
        var originalPreferences = app.EffectivePreferences;
        var store = new SettingsStore(Path.Combine(directory, "state"));
        var weatherPreferences = new WeatherPreferences(); var calendarPreferences = new CalendarPreferences();
        int locationRequests = 0;
        using var handler = new ProviderFixture(); using var client = new HttpClient(handler);
        using var weather = new WeatherService(store, () => weatherPreferences, client, locate: _ =>
        {
            locationRequests++;
            return Task.FromResult<(double Latitude, double Longitude)?>((18.5179, 73.8412));
        });
        var now = new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);
        using var calendar = new CalendarService(store, () => calendarPreferences, () => now);
        var messages = new List<string>(); var launches = new List<string>(); int saves = 0;
        try
        {
            var weatherPanel = new WeatherPanel(weather, weatherPreferences, () => saves++, messages.Add);
            Layout(weatherPanel);
            check(FindButton(weatherPanel, "Use Windows location").IsEnabled && !FindButton(weatherPanel, "Find city").IsEnabled
                && !FindButton(weatherPanel, "Refresh weather").IsEnabled && weather.Current == null && handler.Requests == 0 && locationRequests == 0,
                "Weather starts off but offers explicit Windows setup without requesting location or forecasts on open");
            FindCheckBox(weatherPanel, "Enable city weather").IsChecked = true;
            check(weatherPreferences.Enabled && saves == 1 && handler.Requests == 0,
                "Weather enablement stores consent and waits for an explicitly chosen city");
            weather.SelectCity(new WeatherCity("Pune", 18.52, 73.85)); await weather.RefreshAsync(); weatherPanel.Refresh(); Layout(weatherPanel);
            check(Texts(weatherPanel).Any(text => text.Contains(24.5.ToString("0.#", CultureInfo.CurrentCulture)) && text.Contains("Partly cloudy"))
                && Texts(weatherPanel).Any(text => text.Contains("Last refreshed")),
                "Weather UI displays provider temperature, conditions and refresh time without inferred values");
            RenderInThemes(weatherPanel, "weather", outputDirectory);
            FindButton(weatherPanel, "Clear saved weather").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(weather.Current == null && !File.Exists(Path.Combine(store.DirectoryPath, "weather.json")),
                "Weather Clear removes visible forecast and durable cached data");
            FindButton(weatherPanel, "Disconnect weather").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!weatherPreferences.Enabled && weatherPreferences.City == "" && weatherPreferences.Latitude == null
                && !FindButton(weatherPanel, "Find city").IsEnabled,
                "Weather Disconnect clears location selection and disables further requests");
            FindButton(weatherPanel, "Use Windows location").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => FindButton(weatherPanel, "Use Windows location").IsEnabled);
            check(locationRequests == 1 && weatherPreferences.Enabled && weatherPreferences.UseWindowsLocation
                && weatherPreferences.Latitude == 18.5 && weatherPreferences.Longitude == 73.8 && weather.Current != null,
                "Use Windows location activates weather in one step and displays a forecast for rounded coordinates");

            string path = Path.Combine(directory, "Selected calendar.ics");
            string original = "BEGIN:VCALENDAR\nVERSION:2.0\nBEGIN:VEVENT\nUID:private\nDTSTART:20261125T100000Z\nDTEND:20261125T110000Z\nSUMMARY:Private planning title\nDESCRIPTION:Private event details\\nhttps://meet.google.com/abc-defg-hij\nLOCATION:Private meeting room\nEND:VEVENT\nEND:VCALENDAR";
            File.WriteAllText(path, original);
            var calendarPanel = new CalendarPanel(calendar, calendarPreferences, () => saves++, launches.Add, messages.Add, selected => selected(path));
            Layout(calendarPanel);
            check(FindButton(calendarPanel, "Select local calendar").IsEnabled && FindButton(calendarPanel, "Connect Outlook").IsEnabled
                && !FindButton(calendarPanel, "Refresh calendar").IsEnabled && calendar.Events.Count == 0,
                "Calendar starts off and offers a source choice without reading a calendar on open");
            FindButton(calendarPanel, "Select local calendar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => FindButton(calendarPanel, "Select local calendar").IsEnabled);
            check(calendarPreferences.Enabled && calendarPreferences.Source == CalendarSource.File && calendar.Events.Count == 1,
                "Selecting an iCalendar file activates calendar previews without a separate enable step");
            Layout(calendarPanel);
            check(Texts(calendarPanel).Contains("Busy") && !Texts(calendarPanel).Any(text => text.Contains("Private planning title") || text.Contains("Private meeting room") || text.Contains("Private event details"))
                && !Descendants<Button>(calendarPanel).Any(button => AutomationProperties.GetName(button).StartsWith("Open Google Meet meeting", StringComparison.Ordinal)),
                "Calendar defaults conceal event titles, locations, details and join actions until detail visibility is enabled");
            FindCheckBox(calendarPanel, "Show calendar event titles and details").IsChecked = true; Layout(calendarPanel);
            check(Texts(calendarPanel).Contains("Private planning title") && Texts(calendarPanel).Contains("Private meeting room"),
                "Calendar detail setting explicitly reveals selected event title and location");
            Descendants<Button>(calendarPanel).Single(button => AutomationProperties.GetName(button) == "Open Google Meet meeting for Private planning title").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(launches.SequenceEqual(new[] { "https://meet.google.com/abc-defg-hij" }),
                "Calendar meeting action delegates only the validated provider HTTPS join link");
            RenderInThemes(calendarPanel, "calendar", outputDirectory);
            check(Descendants<Button>(calendarPanel).All(button => !string.IsNullOrEmpty(AutomationProperties.GetName(button)))
                && Descendants<CheckBox>(calendarPanel).All(box => !string.IsNullOrEmpty(AutomationProperties.GetName(box))),
                "Calendar and privacy controls expose meaningful accessible names");
            FindButton(calendarPanel, "Clear event previews").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(calendar.Events.Count == 0 && !Texts(calendarPanel).Contains("Private planning title") && File.ReadAllText(path) == original,
                "Calendar Clear removes event previews and preserves the original selected file");
            FindButton(calendarPanel, "Disconnect calendar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!calendarPreferences.Enabled && calendarPreferences.SourcePath == "" && calendar.Events.Count == 0 && !FindButton(calendarPanel, "Refresh calendar").IsEnabled,
                "Calendar Disconnect clears the selected path and disables future source reads");
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        }
        finally
        {
            weather.Dispose(); calendar.Dispose(); Design.Apply(originalPreferences);
            string resolved = Path.GetFullPath(directory);
            if (resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(resolved).StartsWith("CrestWeatherCalendarUI-", StringComparison.Ordinal) && Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }
    private static Button FindButton(DependencyObject parent, string name) => Descendants<Button>(parent).Single(button => AutomationProperties.GetName(button) == name);
    private static CheckBox FindCheckBox(DependencyObject parent, string name) => Descendants<CheckBox>(parent).Single(box => AutomationProperties.GetName(box) == name);
    private static string[] Texts(DependencyObject parent) => Descendants<TextBlock>(parent).Select(text => text.Text).ToArray();
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Layout(FrameworkElement panel) { panel.Measure(new Size(388, double.PositiveInfinity)); panel.Arrange(new Rect(0, 0, 388, panel.DesiredSize.Height)); panel.UpdateLayout(); }
    private static async Task Until(Func<bool> ready)
    {
        for (int attempt = 0; attempt < 200 && !ready(); attempt++) await Task.Delay(10);
    }
    private static void RenderInThemes(FrameworkElement panel, string name, string output)
    {
        foreach (string theme in new[] { "Dark", "Light" })
        {
            Design.Apply(new AppSettings { Theme = theme });
            var host = new Border { Width = 420, Height = 316, Padding = new Thickness(16), Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
            host.SetResourceReference(Border.BackgroundProperty, "Surface"); host.Measure(new Size(420, 316)); host.Arrange(new Rect(0, 0, 420, 316)); host.UpdateLayout();
            var bitmap = new RenderTargetBitmap(420, 316, 96, 96, PixelFormats.Pbgra32); bitmap.Render(host);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(output, "p3-" + name + "-" + theme.ToLowerInvariant() + ".png"))) encoder.Save(stream);
            ((ScrollViewer)host.Child).Content = null; host.Child = null; Layout(panel);
        }
    }
    private sealed class ProviderFixture : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"current\":{\"temperature_2m\":24.5,\"weather_code\":2,\"wind_speed_10m\":8.2}}") });
        }
    }
}
