using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Crest.Core;
using Crest.UI;

/// <summary>Presentation fixtures follow the current single envelope rather than old dashboard coordinates.</summary>
internal static class IslandProbe
{
    public static T Field<T>(object instance, string name)
        => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    public static Rect Target(IslandWindow island) => Field<Rect>(island, "_targetRect");
    public static Rect Current(IslandWindow island) => Field<RectangleGeometry>(island, "_clip").Rect;
    public static string Presentation(IslandWindow island) => Field<object>(island, "_presentation").ToString()!;
    public static IslandActivity? Selected(IslandWindow island) => Field<IslandActivity?>(island, "_selectedActivity");
    public static void Collapse(IslandWindow island)
        => typeof(IslandWindow).GetMethod("Collapse", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!.Invoke(island, null);
    public static Point Interior(IslandWindow island)
    {
        Rect rect = Current(island);
        return new Point(rect.X + rect.Width / 2, rect.Y + Math.Min(22, rect.Height / 2));
    }
    public static VisualBrush VisualBrush(FrameworkElement visual)
    {
        Vector offset = VisualTreeHelper.GetOffset(visual);
        return new(visual) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(offset.X, offset.Y, visual.ActualWidth, visual.ActualHeight) };
    }
    public static bool NoMotionClocks(IslandWindow island)
    {
        var motion = Field<SpringMotion>(island, "_motion");
        return Field<IDictionary>(motion, "_tracks").Count == 0 && !island.HasAnimatedProperties
            && !Field<RectangleGeometry>(island, "_clip").HasAnimatedProperties
            && !Field<Border>(island, "_detail").HasAnimatedProperties
            && !Field<Grid>(island, "_compact").HasAnimatedProperties;
    }
    public static void ApplyIslandPalette(FrameworkElement scope)
    {
        foreach (var (key, islandKey) in new[] {
            ("Surface", "IslandShell"), ("Canvas", "IslandShell"), ("Raised", "IslandCard"), ("Inset", "IslandInput"),
            ("Text", "IslandText"), ("Muted", "IslandMuted"), ("Subtle", "IslandMuted"), ("Line", "IslandLine"),
            ("Divider", "IslandOutline"), ("Accent", "IslandAccent"), ("AccentFill", "IslandAccent"), ("AccentText", "IslandActionText"),
            ("Hover", "IslandCard"), ("Selected", "IslandCard") })
            scope.Resources[key] = Design.Brush(islandKey);
    }
}
