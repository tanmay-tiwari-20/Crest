namespace Crest.UI;

public sealed partial class IslandWindow
{
    private void BuildIntegrations()
    {
        _body.Children.Add(new IntegrationsPanel(_app, ShowMessage, false));
    }
}
