using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crest.Core;

namespace Crest.Features;

public sealed class WeatherPreferences
{
    public bool Enabled { get; set; }
    public string City { get; set; } = "";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public static void Normalize(WeatherPreferences value)
    {
        value.City = (value.City ?? "").Trim();
        if (value.City.Length > 160) value.City = value.City[..160];
        if (!value.Latitude.HasValue || !value.Longitude.HasValue || !double.IsFinite(value.Latitude.Value)
            || !double.IsFinite(value.Longitude.Value) || value.Latitude.Value is < -90 or > 90 || value.Longitude.Value is < -180 or > 180)
            value.Latitude = value.Longitude = null;
    }
}

public sealed record WeatherCity(string Name, double Latitude, double Longitude);
public sealed record WeatherSnapshot(string City, double Latitude, double Longitude, double TemperatureCelsius, int WeatherCode, double WindKilometresPerHour, DateTimeOffset UpdatedUtc)
{
    public string Conditions => WeatherService.Conditions(WeatherCode);
}

/// <summary>Explicit city-based Open-Meteo requests. No location access, account, or periodic hidden polling.</summary>
public sealed class WeatherService : IDisposable
{
    private readonly SettingsStore _store;
    private readonly Func<WeatherPreferences> _preferences;
    private readonly Func<DateTimeOffset> _now;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private WeatherSnapshot? _current;
    private long _generation;
    private bool _disposed;
    private bool _hasApplied, _appliedEnabled;
    private string _appliedCity = "";
    private double? _appliedLatitude, _appliedLongitude;
    private string _status = "Weather is off. Enable it to request weather for a city you choose.";
    public event EventHandler? Changed;
    public WeatherSnapshot? Current { get { lock (_gate) return _preferences().Enabled ? _current : null; } }
    public string Status { get { lock (_gate) return _status; } }
    public bool IsStale => Current is { } current && _now() - current.UpdatedUtc > TimeSpan.FromMinutes(30);

    public WeatherService(SettingsStore store, Func<WeatherPreferences> preferences, HttpClient? client = null, Func<DateTimeOffset>? now = null)
    {
        _store = store; _preferences = preferences; _now = now ?? (() => DateTimeOffset.UtcNow);
        _ownsClient = client == null; _client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var saved = store.ReadState<WeatherSnapshot>("weather.json");
        if (saved != null && ValidSnapshot(saved) && Matches(saved, preferences()) && saved.UpdatedUtc <= _now().AddMinutes(5)) _current = saved;
        ApplyPreferences();
    }

    public void ApplyPreferences()
    {
        var options = _preferences(); WeatherPreferences.Normalize(options);
        lock (_gate)
        {
            if (_hasApplied && _appliedEnabled == options.Enabled && _appliedCity == options.City && _appliedLatitude == options.Latitude && _appliedLongitude == options.Longitude) return;
            _hasApplied = true; _appliedEnabled = options.Enabled; _appliedCity = options.City; _appliedLatitude = options.Latitude; _appliedLongitude = options.Longitude;
            if (!options.Enabled || (_current != null && !Matches(_current, options))) { _current = null; _generation++; }
            _status = !options.Enabled ? "Weather is off. No weather requests are made."
                : !options.Latitude.HasValue ? "Choose a city to load weather."
                : _current == null ? "Weather has not been refreshed yet."
                : "Last refreshed " + _current.UpdatedUtc.ToLocalTime().ToString("g");
        }
        if (!options.Enabled) _store.DeleteState("weather.json");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyList<WeatherCity>> FindCitiesAsync(string city, CancellationToken cancellationToken = default)
    {
        if (_disposed || !_preferences().Enabled || string.IsNullOrWhiteSpace(city)) return Array.Empty<WeatherCity>();
        city = city.Trim();
        if (city.Length > 100) city = city[..100];
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        linked.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            string address = "https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(city) + "&count=5&language=en&format=json";
            using var data = await GetJsonAsync(address, linked.Token).ConfigureAwait(false);
            if (!data.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return Array.Empty<WeatherCity>();
            return results.EnumerateArray().Take(5).Where(item => item.TryGetProperty("latitude", out _) && item.TryGetProperty("longitude", out _)).Select(item =>
            {
                string name = String(item, "name"), region = String(item, "admin1"), country = String(item, "country");
                string label = string.Join(", ", new[] { name, region, country }.Where(part => !string.IsNullOrWhiteSpace(part)).Distinct());
                if (label.Length > 160) label = label[..160];
                return new WeatherCity(label, item.GetProperty("latitude").GetDouble(), item.GetProperty("longitude").GetDouble());
            }).Where(item => double.IsFinite(item.Latitude) && double.IsFinite(item.Longitude) && item.Latitude is >= -90 and <= 90 && item.Longitude is >= -180 and <= 180).ToArray();
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or InvalidOperationException or FormatException or OperationCanceledException)
        {
            if (!_disposed && _preferences().Enabled) SetStatus(cancellationToken.IsCancellationRequested ? "City search cancelled." : "City search is unavailable. Check your connection and try again.");
            return Array.Empty<WeatherCity>();
        }
    }

    public void SelectCity(WeatherCity city)
    {
        var options = _preferences(); options.City = city.Name; options.Latitude = city.Latitude; options.Longitude = city.Longitude;
        WeatherPreferences.Normalize(options); Clear(); ApplyPreferences();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default, bool force = false)
    {
        if (_disposed) return;
        var options = _preferences(); WeatherPreferences.Normalize(options);
        if (!options.Enabled || !options.Latitude.HasValue || !options.Longitude.HasValue) { ApplyPreferences(); return; }
        string city = options.City; double latitude = options.Latitude.Value, longitude = options.Longitude.Value;
        long generation; lock (_gate) generation = _generation;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        linked.CancelAfter(TimeSpan.FromSeconds(15));
        bool acquired = false;
        try
        {
            await _requests.WaitAsync(linked.Token).ConfigureAwait(false); acquired = true;
            if (!force && Current is { } cached && Matches(cached, options) && _now() - cached.UpdatedUtc < TimeSpan.FromMinutes(30)) return;
            SetStatus("Refreshing weather…");
            string address = string.Format(CultureInfo.InvariantCulture, "https://api.open-meteo.com/v1/forecast?latitude={0:R}&longitude={1:R}&current=temperature_2m,weather_code,wind_speed_10m&timezone=auto", latitude, longitude);
            using var json = await GetJsonAsync(address, linked.Token).ConfigureAwait(false);
            var current = json.RootElement.GetProperty("current");
            var snapshot = new WeatherSnapshot(city, latitude, longitude, current.GetProperty("temperature_2m").GetDouble(), current.GetProperty("weather_code").GetInt32(), current.GetProperty("wind_speed_10m").GetDouble(), _now());
            if (!ValidSnapshot(snapshot)) throw new JsonException("Invalid weather values.");
            lock (_gate)
            {
                if (_disposed || generation != _generation || !Matches(snapshot, _preferences()) || !_preferences().Enabled) return;
                _current = snapshot; _status = "Last refreshed " + snapshot.UpdatedUtc.ToLocalTime().ToString("g");
                _store.WriteState("weather.json", snapshot);
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or InvalidOperationException or FormatException or OperationCanceledException or KeyNotFoundException)
        {
            var cached = Current;
            if (!_disposed && generation == _generation && _preferences().Enabled) SetStatus(cached == null ? "Weather is unavailable. Check your connection and refresh." : "Refresh unavailable. Showing saved weather from " + cached.UpdatedUtc.ToLocalTime().ToString("g") + ".");
        }
        finally { if (acquired) _requests.Release(); }
    }

    public void Clear()
    {
        lock (_gate) { _current = null; _generation++; }
        _store.DeleteState("weather.json");
        SetStatus(_preferences().Enabled ? "Saved weather cleared. Refresh to request current weather." : "Weather is off. No weather requests are made.");
    }
    public void Disconnect()
    {
        var options = _preferences(); options.Enabled = false; options.City = ""; options.Latitude = options.Longitude = null;
        Clear(); ApplyPreferences();
    }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; } _lifetime.Cancel(); if (_ownsClient) _client.Dispose(); }
    private void SetStatus(string value) { lock (_gate) _status = value; Changed?.Invoke(this, EventArgs.Empty); }
    private static bool Matches(WeatherSnapshot value, WeatherPreferences options) => options.Enabled && options.City == value.City && options.Latitude == value.Latitude && options.Longitude == value.Longitude;
    private static bool ValidSnapshot(WeatherSnapshot value) => !string.IsNullOrWhiteSpace(value.City) && value.City.Length <= 160 && double.IsFinite(value.Latitude) && value.Latitude is >= -90 and <= 90 && double.IsFinite(value.Longitude) && value.Longitude is >= -180 and <= 180 && double.IsFinite(value.TemperatureCelsius) && value.TemperatureCelsius is >= -150 and <= 100 && double.IsFinite(value.WindKilometresPerHour) && value.WindKilometresPerHour is >= 0 and <= 500;
    private static string String(JsonElement value, string name) => value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
    private async Task<JsonDocument> GetJsonAsync(string uri, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 128 * 1024) throw new IOException("Response exceeds limit.");
        using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream(); var buffer = new byte[4096]; int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > 128 * 1024) throw new IOException("Response exceeds limit.");
            output.Write(buffer, 0, read);
        }
        return JsonDocument.Parse(output.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
    }
    public static string Conditions(int code) => code switch
    {
        0 => "Clear sky", 1 => "Mainly clear", 2 => "Partly cloudy", 3 => "Overcast", 45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle", 56 or 57 => "Freezing drizzle", 61 or 63 or 65 => "Rain", 66 or 67 => "Freezing rain",
        71 or 73 or 75 or 77 => "Snow", 80 or 81 or 82 => "Rain showers", 85 or 86 => "Snow showers", 95 or 96 or 99 => "Thunderstorm", _ => "Conditions unavailable"
    };
}
