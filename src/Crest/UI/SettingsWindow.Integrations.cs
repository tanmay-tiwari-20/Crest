namespace Crest.UI;

public sealed partial class SettingsWindow
{
    private void Integrations()
    {
        Heading("Connect only what you choose.", "Optional sources start off. Review each source, enable it explicitly, and disconnect or clear its data whenever you want.");
        _page.Children.Add(new IntegrationsPanel(_app, message => _message.Text = message, true));
    }
}
