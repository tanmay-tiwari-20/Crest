namespace Crest.Core;

/// <summary>Applies the saved access preference after native environment checks succeed.</summary>
public readonly record struct VisibilityContext(
    bool EnvironmentReady,
    bool ActualDesktop,
    bool RegisteredInputSurface,
    bool OtherAppForeground,
    bool CoveringApp);

public static class VisibilityRules
{
    public static bool Allows(AppSettings settings, VisibilityContext context)
    {
        if (!context.EnvironmentReady) return false;
        if (settings.AllowOverApps)
            return context.ActualDesktop || context.RegisteredInputSurface || context.OtherAppForeground;
        return (context.ActualDesktop || context.RegisteredInputSurface)
            && (!settings.StrictDesktop || !context.CoveringApp);
    }
}
