using Crest.Core;
using Crest.Features;

internal static class WindowsIntegrationTests
{
    public static int Run()
    {
        int count = 0;
        var now = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
        Test("Notification previews cap count and payload without persisting content", () =>
        {
            var inbox = new NotificationInbox();
            inbox.Update(Enumerable.Range(0, 100).Select(i => new NotificationPreview((uint)i, "App", "Title", new string('x', 20000), now.AddSeconds(-i))), now);
            Check(inbox.Items.Count <= 50 && inbox.Items.Sum(x => x.App.Length + x.Title.Length + x.Body.Length) <= 120000 && inbox.Items.All(x => x.Body.Length <= 8192));
        });
        Test("Notification expiration and malformed future dates are excluded", () =>
        {
            var inbox = new NotificationInbox();
            inbox.Update(new[] { new NotificationPreview(1, "A", "old", "", now.AddDays(-1)), new NotificationPreview(2, "A", "future", "", now.AddDays(1)), new NotificationPreview(3, "A", "current", "", now) }, now);
            Check(inbox.Items.Count == 1 && inbox.Items[0].Id == 3);
        });
        Test("Acknowledged previews remain removed until a new notification with the same id", () =>
        {
            var inbox = new NotificationInbox(); var item = new NotificationPreview(1, "A", "Title", "", now);
            inbox.Update(new[] { item }, now); inbox.Dismiss(1, now); inbox.Update(new[] { item }, now.AddMinutes(1)); Check(inbox.Items.Count == 0);
            inbox.Update(new[] { item with { CreatedUtc = now.AddMinutes(2) } }, now.AddMinutes(3)); Check(inbox.Items.Count == 1);
        });
        Test("Clear removes all previews and reset drops acknowledgement history", () =>
        {
            var inbox = new NotificationInbox(); var item = new NotificationPreview(1, "A", "Title", "", now);
            inbox.Update(new[] { item, item }, now); Check(inbox.Items.Count == 1); inbox.Clear(now); inbox.Update(new[] { item }, now); Check(inbox.Items.Count == 0);
            inbox.Reset(); inbox.Update(new[] { item }, now); Check(inbox.Items.Count == 1);
        });
        Test("Notification retention removes expired content without any new notification", () =>
        {
            var inbox = new NotificationInbox(); inbox.Update(new[] { new NotificationPreview(1, "A", "Title", "Body", now) }, now);
            inbox.Expire(now.AddHours(24)); Check(inbox.Items.Count == 0);
        });
        Test("All optional integrations start disabled and survive safe settings migration", () =>
        {
            string path = Path.Combine(Path.GetTempPath(), "CrestIntegrationTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new SettingsStore(path); var settings = new AppSettings(); store.Save(settings); var loaded = store.Load();
                Check(!loaded.Weather.Enabled && !loaded.Calendar.Enabled && !loaded.Downloads.Enabled && !loaded.Ai.Enabled && !loaded.WindowsIntegrations.NotificationsEnabled && !loaded.WindowsIntegrations.AccessoryBatteryEnabled && !loaded.WindowsIntegrations.PhoneEnabled);
                Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "settings.json"), "{\"SchemaVersion\":1,\"Weather\":null,\"Calendar\":null,\"Ai\":null,\"Downloads\":null,\"WindowsIntegrations\":null}");
                loaded = store.Load(); Check(!store.RecoveredCorruptSettings && loaded.Weather != null && loaded.WindowsIntegrations != null);
                store.WriteState("weather.json", new { City = "A" }); store.WriteState("weather.json.tmp", new { City = "B" }); store.Clear(); Check(!Directory.GetFiles(path).Any());
            }
            finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
        });
        return count;
        void Test(string name, Action action) { action(); count++; Console.WriteLine("PASS " + name); }
        void Check(bool value) { if (!value) throw new InvalidOperationException("Windows integration outcome failed."); }
    }
}
