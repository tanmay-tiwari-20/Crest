using System;
using System.Windows;
using System.Windows.Media;

namespace Crest.UI;

/// <summary>Paints the same geometry used for masking and native hit testing, without changing layout per frame.</summary>
internal sealed class IslandEnvelope : FrameworkElement, IDisposable
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(IslandEnvelope), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty OutlineProperty = DependencyProperty.Register(nameof(Outline), typeof(Brush), typeof(IslandEnvelope), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    private readonly RectangleGeometry _geometry;
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Outline { get => (Brush)GetValue(OutlineProperty); set => SetValue(OutlineProperty, value); }
    public IslandEnvelope(RectangleGeometry geometry)
    {
        _geometry = geometry;
        IsHitTestVisible = false;
        geometry.Changed += GeometryChanged;
        SetResourceReference(FillProperty, "IslandShell");
        SetResourceReference(OutlineProperty, "IslandOutline");
    }
    private void GeometryChanged(object? sender, EventArgs e) => InvalidateVisual();
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        context.DrawRoundedRectangle(Fill, new Pen(Outline, 1), _geometry.Rect, _geometry.RadiusX, _geometry.RadiusY);
    }
    public void Dispose() => _geometry.Changed -= GeometryChanged;
}
