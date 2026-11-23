using System.Net;
using System.Net.Http;
using System.Text;
using Crest.Core;
using Crest.Features;

internal static class WeatherCalendarTests
{
    public static int Run()
    {
        int count = 0;
        string root = Path.Combine(Path.GetTempPath(), "CrestWeatherCalendarTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Test("Disabled weather makes no requests and normalizes invalid city coordinates", () =>
            {
                var options = new WeatherPreferences { Latitude = double.NaN, Longitude = 181, City = new string('a', 200) };
                WeatherPreferences.Normalize(options); Check(options.City.Length == 160 && options.Latitude == null && options.Longitude == null);
                using var handler = new FixtureHandler(_ => WeatherJson); using var client = new HttpClient(handler);
                using var service = new WeatherService(new SettingsStore(Path.Combine(root, "disabled")), () => options, client);
                service.RefreshAsync().GetAwaiter().GetResult(); service.FindCitiesAsync("Pune").GetAwaiter().GetResult();
                Check(handler.Requests.Count == 0 && service.Current == null);
            });
            Test("Weather city search uses bounded explicit HTTPS results without device location", () =>
            {
                var options = new WeatherPreferences { Enabled = true };
                using var handler = new FixtureHandler(_ => "{\"results\":[{\"name\":\"Pune\",\"admin1\":\"Maharashtra\",\"country\":\"India\",\"latitude\":18.52,\"longitude\":73.85}]}"); using var client = new HttpClient(handler);
                using var service = new WeatherService(new SettingsStore(Path.Combine(root, "cities")), () => options, client);
                var cities = service.FindCitiesAsync("Pune & nearby").GetAwaiter().GetResult();
                Check(cities.Count == 1 && cities[0].Name == "Pune, Maharashtra, India" && handler.Requests.Single().Scheme == "https");
                Check(handler.Requests[0].Query.Contains("Pune%20%26%20nearby"));
            });
            Test("Weather caches real values, bounds refreshes, restores cache and reports stale offline data", () =>
            {
                var options = new WeatherPreferences { Enabled = true, City = "Pune", Latitude = 18.52, Longitude = 73.85 };
                var store = new SettingsStore(Path.Combine(root, "cache")); var now = new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero); bool offline = false;
                using var handler = new FixtureHandler(_ => offline ? throw new HttpRequestException() : WeatherJson); using var client = new HttpClient(handler);
                using (var service = new WeatherService(store, () => options, client, () => now))
                {
                    service.RefreshAsync().GetAwaiter().GetResult(); Check(service.Current?.TemperatureCelsius == 24.5 && service.Current.Conditions == "Partly cloudy");
                    service.RefreshAsync().GetAwaiter().GetResult(); Check(handler.Requests.Count == 1);
                    now = now.AddMinutes(31); offline = true; service.RefreshAsync().GetAwaiter().GetResult();
                    Check(service.IsStale && service.Current?.TemperatureCelsius == 24.5 && service.Status.Contains("saved weather"));
                }
                using var recovered = new WeatherService(store, () => options, client, () => now); Check(recovered.Current?.City == "Pune");
                recovered.RefreshAsync(force: true).GetAwaiter().GetResult(); string beforeApply = recovered.Status; recovered.ApplyPreferences();
                Check(recovered.Status == beforeApply && recovered.Current?.City == "Pune");
                recovered.Disconnect(); Check(!options.Enabled && options.City == "" && recovered.Current == null && !File.Exists(Path.Combine(store.DirectoryPath, "weather.json")));
            });
            Test("Weather rejects malformed and oversized provider responses without replacing valid cache", () =>
            {
                var options = new WeatherPreferences { Enabled = true, City = "Pune", Latitude = 18.52, Longitude = 73.85 }; string response = WeatherJson;
                using var handler = new FixtureHandler(_ => response); using var client = new HttpClient(handler);
                using var service = new WeatherService(new SettingsStore(Path.Combine(root, "bad")), () => options, client);
                service.RefreshAsync(force: true).GetAwaiter().GetResult(); response = "{}"; service.RefreshAsync(force: true).GetAwaiter().GetResult(); Check(service.Current?.TemperatureCelsius == 24.5);
                response = new string('x', 129 * 1024); service.RefreshAsync(force: true).GetAwaiter().GetResult(); Check(service.Current?.TemperatureCelsius == 24.5);
                service.Clear(); Check(service.Current == null);
            });
            Test("Clearing weather while a response is in flight prevents saved content returning", () =>
            {
                var options = new WeatherPreferences { Enabled = true, City = "Pune", Latitude = 18.52, Longitude = 73.85 };
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var handler = new DelayedHandler(async () => { ready.SetResult(); await finish.Task; return WeatherJson; }); using var client = new HttpClient(handler);
                var store = new SettingsStore(Path.Combine(root, "inflight")); using var service = new WeatherService(store, () => options, client);
                var task = service.RefreshAsync(force: true); ready.Task.GetAwaiter().GetResult(); service.Clear(); finish.SetResult(); task.GetAwaiter().GetResult();
                Check(service.Current == null && !File.Exists(Path.Combine(store.DirectoryPath, "weather.json")));
            });
            Test("Calendar handles folded text, UTC dates, all-day events, and explicit meeting links", () =>
            {
                var result = CalendarService.Parse(Calendar("BEGIN:VEVENT\r\nUID:a\r\nDTSTART:20261125T100000Z\r\nDTEND:20261125T110000Z\r\nSUMMARY:Planning\\,\r\n session\r\nDESCRIPTION:Join\\nhttps://teams.microsoft.com/l/meetup-join/example\r\nEND:VEVENT\r\nBEGIN:VEVENT\r\nUID:b\r\nDTSTART;VALUE=DATE:20261126\r\nSUMMARY:Holiday\r\nEND:VEVENT"), Now);
                Check(result.Events.Count == 2 && result.Events[0].Title == "Planning,session" && result.Events[0].MeetingProvider == "Microsoft Teams" && result.Events[1].AllDay);
                Check(result.Events[0].Description.Contains('\n') && result.Events[0].EndUtc - result.Events[0].StartUtc == TimeSpan.FromHours(1));
            });
            Test("Calendar supports named timezones and keeps daily recurrence at the local wall time", () =>
            {
                var result = CalendarService.Parse(Calendar("BEGIN:VEVENT\nUID:tz\nDTSTART;TZID=Asia/Kolkata:20261125T100000\nDURATION:PT30M\nRRULE:FREQ=DAILY;COUNT=2\nSUMMARY:Standup\nEND:VEVENT"), Now);
                Check(result.Events.Count == 2 && result.Events.All(item => item.StartUtc.Hour == 4 && item.StartUtc.Minute == 30));
            });
            Test("Weekly recurrence applies weekday, count, exclusions and additional dates", () =>
            {
                var result = CalendarService.Parse(Calendar("BEGIN:VEVENT\nUID:weekly\nDTSTART:20261123T100000Z\nDURATION:PT1H\nRRULE:FREQ=WEEKLY;BYDAY=MO,WE;COUNT=4\nEXDATE:20261125T100000Z\nRDATE:20261127T100000Z\nSUMMARY:Review\nEND:VEVENT"), Now);
                Check(result.Events.Select(item => item.StartUtc.Day).SequenceEqual(new[] { 27, 30, 2 }));
            });
            Test("Recurrence overrides replace one occurrence and cancelled instances stay absent", () =>
            {
                string body = "BEGIN:VEVENT\nUID:series\nDTSTART:20261125T100000Z\nDURATION:PT1H\nRRULE:FREQ=DAILY;COUNT=3\nSUMMARY:Original\nEND:VEVENT\nBEGIN:VEVENT\nUID:series\nRECURRENCE-ID:20261126T100000Z\nDTSTART:20261126T120000Z\nDURATION:PT1H\nSUMMARY:Moved\nEND:VEVENT\nBEGIN:VEVENT\nUID:series\nRECURRENCE-ID:20261127T100000Z\nDTSTART:20261127T100000Z\nSTATUS:CANCELLED\nEND:VEVENT";
                var result = CalendarService.Parse(Calendar(body), Now);
                Check(result.Events.Count == 2 && result.Events[1].Title == "Moved" && result.Events[1].StartUtc.Hour == 12);
            });
            Test("Monthly recurrence skips invalid month dates and returns to the selected day", () =>
            {
                var result = CalendarService.Parse(Calendar("BEGIN:VEVENT\nUID:month\nDTSTART:20270131T100000Z\nDURATION:PT1H\nRRULE:FREQ=MONTHLY;COUNT=3\nSUMMARY:Month end\nEND:VEVENT"), new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
                Check(result.Events.Select(item => item.StartUtc.Month).SequenceEqual(new[] { 1, 3 }) && result.Events.All(item => item.StartUtc.Day == 31));
            });
            Test("All-day recurrence preserves calendar-day boundaries across daylight saving", () =>
            {
                var result = CalendarService.Parse(Calendar("BEGIN:VEVENT\nUID:day\nDTSTART;VALUE=DATE;TZID=America/New_York:20270313\nDTEND;VALUE=DATE;TZID=America/New_York:20270314\nRRULE:FREQ=DAILY;COUNT=3\nSUMMARY:All day\nEND:VEVENT"), new DateTimeOffset(2027, 3, 12, 0, 0, 0, TimeSpan.Zero));
                Check(result.Events.Count == 3 && result.Events[1].EndUtc - result.Events[1].StartUtc == TimeSpan.FromHours(23));
            });
            Test("Calendar reports unsupported recurrence and rejects invalid dates without inventing events", () =>
            {
                var result = CalendarService.Parse(Calendar("BEGIN:VEVENT\nDTSTART:20261125T100000Z\nRRULE:FREQ=MONTHLY;BYDAY=1MO\nEND:VEVENT\nBEGIN:VEVENT\nDTSTART:invalid\nEND:VEVENT\nBEGIN:VEVENT\nDTSTART:20261125T100000Z\nDTEND:20261125T090000Z\nEND:VEVENT"), Now);
                Check(result.Events.Count == 0 && result.UnsupportedEvents == 1 && result.InvalidEvents == 2);
            });
            Test("Calendar bounds upcoming events and excludes expired and distant content", () =>
            {
                string body = string.Join('\n', Enumerable.Range(0, 250).Select(index => $"BEGIN:VEVENT\nUID:{index}\nDTSTART:20261125T100000Z\nSUMMARY:Event {index}\nEND:VEVENT"));
                var result = CalendarService.Parse(Calendar(body + "\nBEGIN:VEVENT\nDTSTART:20270101T100000Z\nSUMMARY:Upcoming\nEND:VEVENT\nBEGIN:VEVENT\nDTSTART:20260101T100000Z\nEND:VEVENT\nBEGIN:VEVENT\nDTSTART:20270101T100000Z\nSTATUS:CANCELLED\nEND:VEVENT"), Now);
                Check(result.Events.Count == CalendarService.MaxEvents);
                bool refused = false; try { CalendarService.Parse(Calendar(new string('x', CalendarService.MaxFileBytes)), Now); } catch (IOException) { refused = true; } Check(refused);
            });
            Test("Calendar explicit selection retains only a reference, survives missing sources and clears memory on disconnect", () =>
            {
                string file = Path.Combine(root, "selected.ics"); string body = Calendar("BEGIN:VEVENT\nUID:a\nDTSTART:20261125T100000Z\nSUMMARY:Private event\nEND:VEVENT"); File.WriteAllText(file, body);
                var options = new CalendarPreferences { Enabled = true }; var store = new SettingsStore(Path.Combine(root, "calendar"));
                using var service = new CalendarService(store, () => options, () => Now); service.SelectSourceAsync(file).GetAwaiter().GetResult(); Check(service.Events.Count == 1 && options.SourcePath == file);
                File.Move(file, file + ".moved"); service.RefreshAsync().GetAwaiter().GetResult(); Check(service.Events.Count == 1 && service.Status.Contains("last read"));
                service.Disconnect(); Check(service.Events.Count == 0 && !options.Enabled && options.SourcePath == "" && File.ReadAllText(file + ".moved") == body);
                Check(!Directory.Exists(store.DirectoryPath) || !Directory.EnumerateFiles(store.DirectoryPath).Any());
            });
            Test("Unchanged calendar preference application preserves loaded events and parser warnings", () =>
            {
                string path = Path.Combine(root, "warnings.ics");
                File.WriteAllText(path, Calendar("BEGIN:VEVENT\nUID:ok\nDTSTART:20261125T100000Z\nSUMMARY:Supported\nEND:VEVENT\nBEGIN:VEVENT\nUID:unsupported\nDTSTART:20261125T100000Z\nRRULE:FREQ=MONTHLY;BYDAY=1MO\nEND:VEVENT"));
                var preferences = new CalendarPreferences { Enabled = true, SourcePath = path }; using var service = new CalendarService(new SettingsStore(Path.Combine(root, "noop")), () => preferences, () => Now);
                service.RefreshAsync().GetAwaiter().GetResult(); string status = service.Status; var updated = service.LastUpdatedUtc; int events = 0; service.Changed += (_, _) => events++;
                preferences.ShowDetails = true; service.ApplyPreferences(); service.ApplyPreferences();
                Check(service.Events.Count == 1 && service.Status == status && status.Contains("unsupported") && service.LastUpdatedUtc == updated && events == 0);
            });
            Test("Meeting joins allow named HTTPS providers and reject lookalike hosts, credentials and unrelated actions", () =>
            {
                Check(MeetingLinks.Validate("https://teams.microsoft.com/l/meetup-join/example")?.Provider == "Microsoft Teams");
                Check(MeetingLinks.Validate("https://us02web.zoom.us/j/123456789")?.Provider == "Zoom");
                Check(MeetingLinks.Validate("https://meet.google.com/abc-defg-hij")?.Provider == "Google Meet");
                foreach (string invalid in new[] { "http://zoom.us/j/123", "https://zoom.us.evil.test/j/123", "https://evilzoom.us/j/123", "https://person:secret@zoom.us/j/123", "https://zoom.us:444/j/123", "javascript:alert(1)", "https://teams.microsoft.com/files", "https://meet.google.com/arbitrary" }) Check(MeetingLinks.Validate(invalid) == null);
            });
            return count;
        }
        finally { string full = Path.GetFullPath(root); if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("CrestWeatherCalendarTests-", StringComparison.Ordinal)) Directory.Delete(full, true); }
        void Test(string label, Action action) { action(); count++; Console.WriteLine("PASS " + label); }
        static void Check(bool condition) { if (!condition) throw new Exception("Weather/calendar outcome failed."); }
    }
    private static DateTimeOffset Now => new(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);
    private static string Calendar(string body) => "BEGIN:VCALENDAR\nVERSION:2.0\n" + body + "\nEND:VCALENDAR";
    private const string WeatherJson = "{\"current\":{\"temperature_2m\":24.5,\"weather_code\":2,\"wind_speed_10m\":8.2}}";
    private sealed class FixtureHandler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!); cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(request), Encoding.UTF8, "application/json") });
        }
    }
    private sealed class DelayedHandler(Func<Task<string>> response) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => new(HttpStatusCode.OK) { Content = new StringContent(await response(), Encoding.UTF8, "application/json") };
    }
}
