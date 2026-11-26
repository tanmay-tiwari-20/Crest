namespace Crest.UI;

public sealed partial class IslandWindow
{
    private void BuildIntegrations()
    {
        _integrationTools = new IntegrationsPanel(_app, ShowMessage, false);
        _body.Children.Add(_integrationTools);
    }
}
