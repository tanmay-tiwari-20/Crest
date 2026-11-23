using System;
using System.Collections.Generic;
using System.Linq;

namespace Crest.Features;

public sealed class WindowsIntegrationPreferences
{
    public bool NotificationsEnabled { get; set; }
    public bool ShowNotificationDetails { get; set; }
    public bool AccessoryBatteryEnabled { get; set; }
    public bool PhoneEnabled { get; set; }
}

public sealed record NotificationPreview(uint Id, string App, string Title, string Body, DateTimeOffset CreatedUtc);
public sealed record AccessoryBattery(string Id, string Name, int? Percent, string Status, DateTimeOffset UpdatedUtc);

/// <summary>Memory-only previews. Acknowledgement never dismisses the original Windows notification.</summary>
public sealed class NotificationInbox
{
    private readonly object _gate = new();
    private readonly List<NotificationPreview> _items = new();
    private readonly Dictionary<uint, DateTimeOffset> _dismissed = new();
    public IReadOnlyList<NotificationPreview> Items { get { lock (_gate) return _items.ToArray(); } }
    public void Update(IEnumerable<NotificationPreview> incoming, DateTimeOffset now)
    {
        lock (_gate)
        {
            foreach (var id in _dismissed.Where(x => now - x.Value >= TimeSpan.FromHours(24)).Select(x => x.Key).ToArray()) _dismissed.Remove(id);
            _items.Clear();
            int payload = 0;
            foreach (var item in incoming.OrderByDescending(x => x.CreatedUtc).Take(1000))
            {
                if (_items.Count == 50) break;
                if (item.CreatedUtc > now.AddMinutes(5) || now - item.CreatedUtc >= TimeSpan.FromHours(24)
                    || _dismissed.TryGetValue(item.Id, out var dismissed) && dismissed >= item.CreatedUtc || _items.Any(x => x.Id == item.Id)) continue;
                var bounded = item with { App = Bound(item.App, 100), Title = Bound(item.Title, 512), Body = Bound(item.Body, 8192) };
                int size = bounded.App.Length + bounded.Title.Length + bounded.Body.Length;
                if (payload + size > 120000) continue;
                payload += size; _items.Add(bounded);
            }
        }
    }
    public void Dismiss(uint id, DateTimeOffset now)
    {
        lock (_gate)
        {
            _items.RemoveAll(x => x.Id == id); _dismissed[id] = now;
            if (_dismissed.Count > 1000) _dismissed.Remove(_dismissed.OrderBy(x => x.Value).First().Key);
        }
    }
    public void Clear(DateTimeOffset now)
    {
        lock (_gate) foreach (var item in _items.ToArray()) Dismiss(item.Id, now);
    }
    public void Reset() { lock (_gate) { _items.Clear(); _dismissed.Clear(); } }
    public void Expire(DateTimeOffset now)
    {
        lock (_gate)
        {
            _items.RemoveAll(x => now - x.CreatedUtc >= TimeSpan.FromHours(24));
            foreach (var id in _dismissed.Where(x => now - x.Value >= TimeSpan.FromHours(24)).Select(x => x.Key).ToArray()) _dismissed.Remove(id);
        }
    }
    private static string Bound(string? value, int limit) => value == null ? "" : value[..Math.Min(value.Length, limit)];
}
