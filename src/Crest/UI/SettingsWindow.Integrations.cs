namespace Crest.UI;

public sealed partial class SettingsWindow
{
    private IntegrationsPanel? _integrations;

    private void Integrations()
    {
        Heading("Integrations", "Connect weather, calendars, and other optional services. Choose what Crest can access for each source.");
        _integrations = new IntegrationsPanel(_app, SetStatus, true);
        _page.Children.Add(_integrations);
    }

    public void ShowIntegrationSource(string source)
    {
        ShowSection("Integrations");
        _integrations?.ShowSection(source);
        _pageScroll.ScrollToTop();
    }
}
