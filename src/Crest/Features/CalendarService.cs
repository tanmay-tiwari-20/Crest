using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Crest.Core;

namespace Crest.Features;

public sealed class CalendarPreferences
{
    public bool Enabled { get; set; }
    public string SourcePath { get; set; } = "";
    public bool ShowDetails { get; set; }
    public static void Normalize(CalendarPreferences value)
    {
        value.SourcePath ??= "";
        if (value.SourcePath.Length > 32768 || (!string.IsNullOrEmpty(value.SourcePath) && (!Path.IsPathFullyQualified(value.SourcePath) || !string.Equals(Path.GetExtension(value.SourcePath), ".ics", StringComparison.OrdinalIgnoreCase)))) value.SourcePath = "";
    }
}

public sealed record CalendarEvent(string Id, string Title, DateTimeOffset StartUtc, DateTimeOffset EndUtc, bool AllDay, string Location, string Description, string? MeetingUrl, string? MeetingProvider, DateOnly? CalendarDate = null);
public sealed record CalendarParseResult(IReadOnlyList<CalendarEvent> Events, int UnsupportedEvents, int InvalidEvents);

/// <summary>Read-only, user-selected iCalendar source. Event contents stay in memory and never leave the device.</summary>
public sealed class CalendarService : IDisposable
{
    public const int MaxEvents = 200;
    public const int MaxFileBytes = 2 * 1024 * 1024;
    public const int PreviewDays = 90;
    private readonly Func<CalendarPreferences> _preferences;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private CalendarEvent[] _events = Array.Empty<CalendarEvent>();
    private string _loadedPath = "", _status = "Calendar is off. Choose a local .ics file to read events.";
    private bool _disposed;
    private bool _hasApplied, _appliedEnabled;
    private string _appliedPath = "";
    private long _generation;
    private long _refreshVersion;
    public event EventHandler? Changed;
    public IReadOnlyList<CalendarEvent> Events { get { lock (_gate) return _preferences().Enabled ? _events.Where(item => item.EndUtc > _now()).ToArray() : Array.Empty<CalendarEvent>(); } }
    public string Status { get { lock (_gate) return _status; } }
    public DateTimeOffset? LastUpdatedUtc { get; private set; }
    public CalendarService(SettingsStore store, Func<CalendarPreferences> preferences, Func<DateTimeOffset>? now = null)
    {
        _preferences = preferences; _now = now ?? (() => DateTimeOffset.UtcNow); ApplyPreferences();
    }
    public void ApplyPreferences()
    {
        var options = _preferences(); CalendarPreferences.Normalize(options);
        lock (_gate)
        {
            if (_hasApplied && _appliedEnabled == options.Enabled && _appliedPath == options.SourcePath) return;
            _hasApplied = true; _appliedEnabled = options.Enabled; _appliedPath = options.SourcePath;
            if (!options.Enabled || _loadedPath != options.SourcePath) { _events = Array.Empty<CalendarEvent>(); LastUpdatedUtc = null; _generation++; }
            _status = !options.Enabled ? "Calendar is off. Event contents are not read."
                : string.IsNullOrEmpty(options.SourcePath) ? "Choose a local .ics calendar file."
                : LastUpdatedUtc == null ? "Refresh the selected calendar to read upcoming events." : "Last read " + LastUpdatedUtc.Value.ToLocalTime().ToString("g");
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public async Task SelectSourceAsync(string path, CancellationToken cancellationToken = default)
    {
        var options = _preferences(); options.SourcePath = path; CalendarPreferences.Normalize(options); Clear(); ApplyPreferences(); await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        var options = _preferences(); CalendarPreferences.Normalize(options);
        if (!options.Enabled || string.IsNullOrEmpty(options.SourcePath)) { ApplyPreferences(); return; }
        string path = options.SourcePath; long generation, refreshVersion;
        lock (_gate) { generation = _generation; refreshVersion = ++_refreshVersion; _status = "Reading local calendar…"; }
        Changed?.Invoke(this, EventArgs.Empty);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            var parsed = await Task.Run(async () =>
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (stream.Length > MaxFileBytes) throw new IOException("Calendar exceeds size limit.");
                using var output = new MemoryStream(); var buffer = new byte[8192]; int read;
                while ((read = await stream.ReadAsync(buffer, linked.Token).ConfigureAwait(false)) > 0)
                {
                    if (output.Length + read > MaxFileBytes) throw new IOException("Calendar exceeds size limit.");
                    output.Write(buffer, 0, read);
                }
                return Parse(Encoding.UTF8.GetString(output.ToArray()), _now(), linked.Token);
            }, linked.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (_disposed || generation != _generation || refreshVersion != _refreshVersion || !_preferences().Enabled || path != _preferences().SourcePath) return;
                _events = parsed.Events.ToArray(); _loadedPath = path; LastUpdatedUtc = _now();
                _status = $"{_events.Length} upcoming events · last read {LastUpdatedUtc.Value.ToLocalTime():g}.";
                if (parsed.UnsupportedEvents > 0) _status += $" {parsed.UnsupportedEvents} event(s) use unsupported recurrence or timezone rules and were skipped.";
                if (parsed.InvalidEvents > 0) _status += $" {parsed.InvalidEvents} invalid event(s) were skipped.";
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or OperationCanceledException)
        {
            lock (_gate)
            {
                if (_disposed || generation != _generation || refreshVersion != _refreshVersion) return;
                _status = _events.Length == 0 ? "Calendar could not be read. Select an available .ics file of at most 2 MiB."
                    : "Calendar refresh unavailable. Showing events last read " + LastUpdatedUtc?.ToLocalTime().ToString("g") + ".";
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Clear()
    {
        lock (_gate) { _events = Array.Empty<CalendarEvent>(); LastUpdatedUtc = null; _loadedPath = ""; _generation++; _status = "Calendar event previews cleared."; }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Disconnect()
    {
        var options = _preferences(); options.Enabled = false; options.SourcePath = ""; Clear(); ApplyPreferences();
    }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _events = Array.Empty<CalendarEvent>(); _generation++; } _lifetime.Cancel(); }

    public static CalendarParseResult Parse(string text, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxFileBytes) throw new IOException("Calendar exceeds size limit.");
        if (!text.TrimStart('\uFEFF', ' ', '\r', '\n').StartsWith("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase)) throw new IOException("Not an iCalendar file.");
        var entries = new List<Dictionary<string, List<Property>>>(); Dictionary<string, List<Property>>? current = null;
        int unsupported = 0, invalid = 0; bool inAlarm = false;
        foreach (string line in Unfold(text))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase)) { if (entries.Count >= 1000) break; current = new(StringComparer.OrdinalIgnoreCase); continue; }
            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase)) { if (current != null) entries.Add(current); current = null; continue; }
            if (line.Equals("BEGIN:VALARM", StringComparison.OrdinalIgnoreCase)) { inAlarm = true; continue; }
            if (line.Equals("END:VALARM", StringComparison.OrdinalIgnoreCase)) { inAlarm = false; continue; }
            if (current == null || inAlarm) continue;
            var property = ReadProperty(line); if (property == null) continue;
            if (!current.TryGetValue(property.Name, out var values)) current[property.Name] = values = new();
            if (values.Count < 100) values.Add(property);
        }
        var events = new List<CalendarEvent>(); var overridden = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries.Where(item => item.ContainsKey("RECURRENCE-ID")))
        {
            if (TryDate(First(entry, "RECURRENCE-ID"), out var occurrence)) overridden.Add(Unescape(Value(entry, "UID")) + ":" + occurrence.Utc.UtcTicks);
        }
        DateTimeOffset through = nowUtc.AddDays(PreviewDays);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Value(entry, "STATUS").Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)) continue;
            if (!TryDate(First(entry, "DTSTART"), out var start)) { invalid++; continue; }
            DateTimeOffset end;
            try { end = start.Utc.Add(start.AllDay ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1)); }
            catch (ArgumentOutOfRangeException) { invalid++; continue; }
            if (First(entry, "DTEND") is { } ending)
            {
                if (!TryDate(ending, out var endDate) || endDate.Utc < start.Utc) { invalid++; continue; }
                end = endDate.Utc;
            }
            else if (Value(entry, "DURATION") is { Length: > 0 } duration)
            {
                if (!TryDuration(duration, out var parsedDuration)) { invalid++; continue; }
                try { end = start.Utc.Add(parsedDuration); }
                catch (ArgumentOutOfRangeException) { invalid++; continue; }
            }
            string id = Unescape(Value(entry, "UID")); if (id.Length == 0) id = "event-" + entries.IndexOf(entry).ToString(CultureInfo.InvariantCulture);
            string title = Bounded(Unescape(Value(entry, "SUMMARY")), 300); if (title.Length == 0) title = "Untitled event";
            string description = Bounded(Unescape(Value(entry, "DESCRIPTION")), 4096), location = Bounded(Unescape(Value(entry, "LOCATION")), 500);
            var meeting = MeetingLinks.Find(Value(entry, "URL"), location, description);
            var exclusions = new HashSet<long>();
            if (entry.TryGetValue("EXDATE", out var exdates)) foreach (var property in exdates) foreach (string date in property.Value.Split(',')) if (TryDate(property with { Value = date }, out var excluded)) exclusions.Add(excluded.Utc.UtcTicks);
            string rule = Value(entry, "RRULE");
            if (entry.ContainsKey("RECURRENCE-ID")) rule = "";
            var instances = Expand(start, rule, nowUtc, through, cancellationToken, out bool supported);
            if (!supported) { unsupported++; continue; }
            if (entry.TryGetValue("RDATE", out var addedDates))
                foreach (var property in addedDates) foreach (string date in property.Value.Split(',')) if (TryDate(property with { Value = date }, out var added)) instances.Add(added.Utc);
            TimeSpan eventDuration = end - start.Utc;
            foreach (var occurrence in instances.Distinct())
            {
                DateTimeOffset occurrenceEnd;
                try { occurrenceEnd = occurrence.Add(eventDuration); }
                catch (ArgumentOutOfRangeException) { continue; }
                if (start.AllDay)
                {
                    var localOccurrence = TimeZoneInfo.ConvertTime(occurrence, start.Zone).Date;
                    int calendarDays = Math.Max(1, (int)Math.Round((TimeZoneInfo.ConvertTime(end, start.Zone).Date - start.Local.Date).TotalDays));
                    try { occurrenceEnd = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localOccurrence.AddDays(calendarDays), start.Zone), TimeSpan.Zero); }
                    catch (ArgumentException) { continue; }
                }
                if (occurrence > through || occurrenceEnd <= nowUtc || exclusions.Contains(occurrence.UtcTicks)) continue;
                if (!entry.ContainsKey("RECURRENCE-ID") && overridden.Contains(id + ":" + occurrence.UtcTicks)) continue;
                events.Add(new(id + ":" + occurrence.UtcTicks, title, occurrence, occurrenceEnd, start.AllDay, location, description, meeting?.Url, meeting?.Provider,
                    start.AllDay ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(occurrence, start.Zone).Date) : null));
            }
        }
        return new(events.OrderBy(item => item.StartUtc).ThenBy(item => item.Title).Take(MaxEvents).ToArray(), unsupported, invalid);
    }

    private sealed record Property(string Name, string Parameters, string Value);
    private sealed record CalendarDate(DateTime Local, TimeZoneInfo Zone, DateTimeOffset Utc, bool AllDay);
    private static Property? First(Dictionary<string, List<Property>> entry, string name) => entry.TryGetValue(name, out var values) ? values.FirstOrDefault() : null;
    private static string Value(Dictionary<string, List<Property>> entry, string name) => First(entry, name)?.Value ?? "";
    private static string Bounded(string value, int limit) => value.Length <= limit ? value : value[..limit] + "…";
    private static IEnumerable<string> Unfold(string text)
    {
        string? previous = null;
        foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if ((raw.StartsWith(' ') || raw.StartsWith('\t')) && previous != null) { if (previous.Length + raw.Length <= 32768) previous += raw[1..]; continue; }
            if (previous != null) yield return previous;
            previous = raw.TrimStart('\uFEFF');
        }
        if (previous != null) yield return previous;
    }
    private static Property? ReadProperty(string line)
    {
        bool quoted = false; int colon = -1;
        for (int index = 0; index < line.Length; index++) { if (line[index] == '"') quoted = !quoted; if (line[index] == ':' && !quoted) { colon = index; break; } }
        if (colon <= 0) return null;
        string heading = line[..colon]; int semicolon = heading.IndexOf(';');
        return new(semicolon < 0 ? heading : heading[..semicolon], semicolon < 0 ? "" : heading[(semicolon + 1)..], line[(colon + 1)..]);
    }
    private static bool TryDate(Property? property, out CalendarDate result)
    {
        result = null!; if (property == null) return false;
        string value = property.Value; bool utc = value.EndsWith('Z'), allDay = value.Length == 8;
        string[] formats = allDay ? new[] { "yyyyMMdd" } : utc ? new[] { "yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmm'Z'" } : new[] { "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm" };
        if (!DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return false;
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var zone = utc ? TimeZoneInfo.Utc : TimeZoneInfo.Local;
        var tzid = property.Parameters.Split(';').FirstOrDefault(part => part.StartsWith("TZID=", StringComparison.OrdinalIgnoreCase));
        if (tzid != null && !utc)
        {
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(tzid[5..].Trim('"')); }
            catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { return false; }
        }
        if (zone.IsInvalidTime(local)) return false;
        try { result = new(local, zone, new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero), allDay); return true; }
        catch (ArgumentException) { return false; }
    }
    private static List<DateTimeOffset> Expand(CalendarDate start, string rawRule, DateTimeOffset now, DateTimeOffset through, CancellationToken cancellationToken, out bool supported)
    {
        supported = true; var output = new List<DateTimeOffset>();
        if (string.IsNullOrEmpty(rawRule)) { output.Add(start.Utc); return output; }
        var rule = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string part in rawRule.Split(';')) { var pair = part.Split('=', 2); if (pair.Length != 2 || !rule.TryAdd(pair[0].ToUpperInvariant(), pair[1].ToUpperInvariant())) { supported = false; return output; } }
        if (rule.Keys.Any(key => key is not ("FREQ" or "INTERVAL" or "COUNT" or "UNTIL" or "BYDAY" or "WKST"))) { supported = false; return output; }
        if (!rule.TryGetValue("FREQ", out var frequency) || frequency is not ("DAILY" or "WEEKLY" or "MONTHLY" or "YEARLY")) { supported = false; return output; }
        int interval = 1, count = int.MaxValue;
        if (rule.TryGetValue("INTERVAL", out var rawInterval) && (!int.TryParse(rawInterval, out interval) || interval is < 1 or > 366)) { supported = false; return output; }
        if (rule.TryGetValue("COUNT", out var rawCount) && (!int.TryParse(rawCount, out count) || count < 1)) { supported = false; return output; }
        DateTimeOffset until = through;
        if (rule.TryGetValue("UNTIL", out var rawUntil))
        {
            if (!TryDate(new("UNTIL", "", rawUntil), out var parsedUntil)) { supported = false; return output; }
            until = parsedUntil.AllDay ? parsedUntil.Utc.AddDays(1).AddTicks(-1) : parsedUntil.Utc;
            if (until > through) until = through;
        }
        var days = new HashSet<DayOfWeek>();
        if (rule.TryGetValue("BYDAY", out var rawDays))
        {
            if (frequency != "WEEKLY") { supported = false; return output; }
            foreach (string day in rawDays.Split(',')) { int index = Array.IndexOf(DayNames, day); if (index < 0) { supported = false; return output; } days.Add((DayOfWeek)index); }
        }
        if (frequency == "WEEKLY" && days.Count == 0) days.Add(start.Local.DayOfWeek);
        DayOfWeek weekStart = DayOfWeek.Monday;
        if (rule.TryGetValue("WKST", out var rawWeekStart)) { int index = Array.IndexOf(DayNames, rawWeekStart); if (index < 0) { supported = false; return output; } weekStart = (DayOfWeek)index; }
        // Bound expansion by the input horizon and iteration budget. Old unlimited rules jump close to now.
        DateTime cursor = start.Local; int emitted = 0, iterations = 0, period = 0;
        if (count == int.MaxValue && frequency is "DAILY" or "WEEKLY")
        {
            double elapsedDays = (TimeZoneInfo.ConvertTime(now.AddDays(-7), start.Zone).Date - start.Local.Date).TotalDays;
            int stride = frequency == "DAILY" ? interval : interval * 7;
            if (elapsedDays > stride) cursor = start.Local.AddDays(Math.Floor(elapsedDays / stride) * stride);
        }
        DateTime firstWeek = start.Local.Date.AddDays(-(((int)start.Local.DayOfWeek - (int)weekStart + 7) % 7));
        while (iterations++ < 100000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool include = (frequency != "WEEKLY" || days.Contains(cursor.DayOfWeek) && ((int)(cursor.Date - firstWeek).TotalDays / 7) % interval == 0)
                && (frequency is not ("MONTHLY" or "YEARLY") || cursor.Day == start.Local.Day && cursor.Month == (frequency == "YEARLY" ? start.Local.Month : cursor.Month));
            if (!start.Zone.IsInvalidTime(cursor))
            {
                var utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(cursor, start.Zone), TimeSpan.Zero);
                if (utc > until || emitted >= count) break;
                if (include) { emitted++; if (utc >= now.AddDays(-7)) output.Add(utc); }
            }
            try
            {
                period++;
                cursor = frequency switch { "DAILY" => cursor.AddDays(interval), "WEEKLY" => cursor.AddDays(1), "MONTHLY" => start.Local.AddMonths(checked(period * interval)), _ => start.Local.AddYears(checked(period * interval)) };
            }
            catch (Exception error) when (error is ArgumentOutOfRangeException or OverflowException) { break; }
        }
        if (iterations >= 100000) supported = false;
        return output;
    }
    private static readonly string[] DayNames = { "SU", "MO", "TU", "WE", "TH", "FR", "SA" };
    private static bool TryDuration(string value, out TimeSpan duration)
    {
        duration = default;
        try
        {
            var match = Regex.Match(value, "^P(?:(\\d+)W)?(?:(\\d+)D)?(?:T(?:(\\d+)H)?(?:(\\d+)M)?(?:(\\d+)S)?)?$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (!match.Success) return false;
            double Number(int index) => match.Groups[index].Success ? double.Parse(match.Groups[index].Value, CultureInfo.InvariantCulture) : 0;
            duration = TimeSpan.FromDays(Number(1) * 7 + Number(2)) + TimeSpan.FromHours(Number(3)) + TimeSpan.FromMinutes(Number(4)) + TimeSpan.FromSeconds(Number(5));
            return duration > TimeSpan.Zero && duration <= TimeSpan.FromDays(366);
        }
        catch (Exception error) when (error is OverflowException or FormatException or RegexMatchTimeoutException) { return false; }
    }
    public static string Unescape(string value)
    {
        var output = new StringBuilder();
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] == '\\' && index + 1 < value.Length) { char next = value[++index]; output.Append(next is 'n' or 'N' ? '\n' : next); }
            else output.Append(value[index]);
        }
        return output.ToString();
    }
}

public sealed record MeetingLink(string Provider, string Url);
public static class MeetingLinks
{
    public static MeetingLink? Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || uri.Port != 443) return null;
        string host = uri.IdnHost.ToLowerInvariant(), path = uri.AbsolutePath;
        if ((host == "teams.microsoft.com" || host == "teams.live.com") && path.StartsWith("/meet/", StringComparison.OrdinalIgnoreCase) && path.Length > 6) return new("Microsoft Teams", uri.AbsoluteUri);
        if (host == "teams.microsoft.com" && path.StartsWith("/l/meetup-join/", StringComparison.OrdinalIgnoreCase) && path.Length > 15) return new("Microsoft Teams", uri.AbsoluteUri);
        if ((host == "zoom.us" || host.EndsWith(".zoom.us", StringComparison.Ordinal)) &&
            (Regex.IsMatch(path, "^/j/[0-9]{6,15}/?$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
            || path.StartsWith("/my/", StringComparison.OrdinalIgnoreCase) && path.Length is > 4 and <= 260)) return new("Zoom", uri.AbsoluteUri);
        if (host == "meet.google.com" && Regex.IsMatch(path, "^/[a-z]{3}-[a-z]{4}-[a-z]{3}/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) return new("Google Meet", uri.AbsoluteUri);
        return null;
    }
    public static MeetingLink? Find(params string[] values)
    {
        foreach (string value in values)
        {
            string bounded = value.Length > 8192 ? value[..8192] : value;
            if (Validate(bounded) is { } direct) return direct;
            foreach (Match match in Regex.Matches(bounded, "https://[^\\s<>\"']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) if (Validate(match.Value.TrimEnd('.', ',', ')', ';')) is { } link) return link;
        }
        return null;
    }
}
