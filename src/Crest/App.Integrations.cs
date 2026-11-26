using System;
using System.Threading.Tasks;
using Crest.Features;

namespace Crest;

public partial class App
{
    public WeatherService Weather { get; private set; } = null!;
    public CalendarService Calendar { get; private set; } = null!;
    public DownloadService Downloads { get; private set; } = null!;
    public AiAssistantService Ai { get; private set; } = null!;
    public WindowsIntegrationService WindowsIntegrations { get; private set; } = null!;
    private bool _hadIntegrationEnabled;
    private void InitializeIntegrations()
    {
        Weather = new(Store, () => Preferences.Weather, locate: WindowsWeatherLocation.ReadAsync);
        Calendar = new(Store, () => Preferences.Calendar, readOutlook: new OutlookCalendarReader().ReadAsync);
        Downloads = new(() => Preferences.Downloads);
        Ai = new(() => Preferences.Ai);
        WindowsIntegrations = new(() => Preferences.WindowsIntegrations);
        _hadIntegrationEnabled = AnyIntegrationEnabled();
    }
    private void ApplyIntegrationPreferences()
    {
        Weather?.ApplyPreferences(); Calendar?.ApplyPreferences(); Downloads?.ApplyPreferences(); Ai?.ApplyPreferences(); WindowsIntegrations?.ApplyPreferences();
        bool enabled = AnyIntegrationEnabled();
        if (enabled && !_hadIntegrationEnabled && !Preferences.EnabledModules.Contains("Integrations")) Preferences.EnabledModules.Add("Integrations");
        _hadIntegrationEnabled = enabled;
    }
    private bool AnyIntegrationEnabled() => Preferences.Weather.Enabled || Preferences.Calendar.Enabled || Preferences.Downloads.Enabled || Preferences.Ai.Enabled
        || Preferences.WindowsIntegrations.NotificationsEnabled || Preferences.WindowsIntegrations.AccessoryBatteryEnabled || Preferences.WindowsIntegrations.PhoneEnabled;
    public void RefreshVisibleIntegrations()
    {
        if (Preferences.WindowsIntegrations.NotificationsEnabled) _ = WindowsIntegrations.RefreshNotificationsAsync();
    }
    public void OpenIntegrationSettings(string source)
    {
        OpenSettings();
        _settings?.ShowIntegrationSource(source);
    }
    public void OpenIntegrationLink(string url)
    {
        Controller.Dismiss(); _island.Suppress();
        _ = OpenTarget(() => Local.Launch(new Core.LauncherEntry { Target = url, Kind = "Website" }));
    }
    private void ClearIntegrations()
    {
        Weather?.Disconnect(); Calendar?.Disconnect(); Downloads?.Clear(); Ai?.Clear(); WindowsIntegrations?.Clear();
    }
    private void DisposeIntegrations()
    {
        foreach (IDisposable? service in new IDisposable?[] { Weather, Calendar, Downloads, Ai, WindowsIntegrations })
        {
            try { service?.Dispose(); }
            catch (Exception error) { WriteDiagnostic("integration-cleanup:" + error.GetType().Name); }
        }
    }
}
