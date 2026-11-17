using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace SchoolDesk.Controls;

public sealed class SvgIcon : FrameworkElement
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly Lazy<XDocument> Sprite = new(LoadSprite);
    private Geometry? _geometry;

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon),
        typeof(string),
        typeof(SvgIcon),
        new FrameworkPropertyMetadata(null, OnIconChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke),
        typeof(Brush),
        typeof(SvgIcon),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(SvgIcon),
        new FrameworkPropertyMetadata(2d, FrameworkPropertyMetadataOptions.AffectsRender));

    public SvgIcon()
    {
        IsHitTestVisible = false;
        Focusable = false;
        SnapsToDevicePixels = true;
    }

    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (_geometry is null || ActualWidth <= 0 || ActualHeight <= 0) return;

        var bounds = _geometry.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var scale = Math.Min(ActualWidth / bounds.Width, ActualHeight / bounds.Height);
        var offsetX = (ActualWidth - bounds.Width * scale) / 2 - bounds.Left * scale;
        var offsetY = (ActualHeight - bounds.Height * scale) / 2 - bounds.Top * scale;
        var geometry = _geometry.Clone();
        geometry.Transform = new MatrixTransform(new Matrix(scale, 0, 0, scale, offsetX, offsetY));
        drawingContext.DrawGeometry(null, new Pen(Stroke, StrokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        }, geometry);
    }

    private static void OnIconChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not SvgIcon icon)
            return;
        if (e.NewValue is not string name || string.IsNullOrWhiteSpace(name))
        {
            icon._geometry = null;
            icon.InvalidateVisual();
            return;
        }

        var symbol = Sprite.Value.Root?.Elements(Svg + "symbol")
            .FirstOrDefault(element => (string?)element.Attribute("id") == name)
            ?? throw new InvalidDataException($"L’icône SVG « {name} » est introuvable.");

        var geometries = symbol.Elements(Svg + "path")
            .Select(element => (string?)element.Attribute("d"))
            .Where(data => !string.IsNullOrWhiteSpace(data))
            .Select(data => Geometry.Parse(data!))
            .ToList();

        if (geometries.Count == 0)
            throw new InvalidDataException($"L’icône SVG « {name} » ne contient aucun tracé.");

        var group = new GeometryGroup();
        foreach (var geometry in geometries) group.Children.Add(geometry);
        icon._geometry = group;
        icon.InvalidateVisual();
    }

    private static XDocument LoadSprite()
    {
        const string resourceName = "SchoolDesk.Assets.schooldesk-icons.svg";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"La bibliothèque d’icônes SVG intégrée est introuvable : {resourceName}.");
        return XDocument.Load(stream);
    }
}
