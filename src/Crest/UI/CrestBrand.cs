using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Crest.UI;

/// <summary>CREST's original open summit: one silhouette, at every display scale.</summary>
public static class CrestBrand
{
    public const string GeometryData = "M 29,4 Q 26,3 23,6 L 6,23 Q 3,26 6,29 L 23,46 Q 26,49 29,48 L 43,43 L 35,35 L 27,38 L 15,26 L 27,14 L 35,17 L 43,9 Z";
    private static readonly Geometry Symbol = CreateGeometry();

    private static Geometry CreateGeometry()
    {
        var geometry = Geometry.Parse(GeometryData);
        geometry.Freeze();
        return geometry;
    }

    public static FrameworkElement Mark(double size, string resourceKey = "Text")
    {
        var path = new Path { Data = Symbol, Stretch = Stretch.Uniform, Width = size, Height = size, IsHitTestVisible = false };
        path.SetResourceReference(Shape.FillProperty, resourceKey);
        AutomationProperties.SetName(path, "Crest");
        return path;
    }

    public static FrameworkElement Wordmark(double size = 28, string resourceKey = "Text")
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var symbol = Mark(size, resourceKey);
        symbol.Margin = new Thickness(0, 0, 10, 0);
        stack.Children.Add(symbol);
        var text = Design.Text("crest", size * 0.82, resourceKey, true);
        text.VerticalAlignment = VerticalAlignment.Center;
        stack.Children.Add(text);
        AutomationProperties.SetName(stack, "Crest");
        return stack;
    }
}
