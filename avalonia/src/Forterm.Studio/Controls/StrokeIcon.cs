using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Forterm.Studio.Controls;

/// <summary>Draws a 24×24 line icon from <see cref="Data"/>, scaled to the control size.
/// The colour follows the inherited text foreground unless <see cref="Brush"/> is set.
/// Icons are Forterm's own simple geometry, not copied from any product.</summary>
public sealed class StrokeIcon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty = AvaloniaProperty.Register<StrokeIcon, Geometry?>(nameof(Data));
    public static readonly StyledProperty<IBrush?> BrushProperty = AvaloniaProperty.Register<StrokeIcon, IBrush?>(nameof(Brush));
    public static readonly StyledProperty<bool> FilledProperty = AvaloniaProperty.Register<StrokeIcon, bool>(nameof(Filled));
    public static readonly StyledProperty<double> ThicknessProperty = AvaloniaProperty.Register<StrokeIcon, double>(nameof(Thickness), 2.0);

    static StrokeIcon()
    {
        AffectsRender<StrokeIcon>(DataProperty, BrushProperty, FilledProperty, ThicknessProperty, TextElement.ForegroundProperty);
        WidthProperty.OverrideDefaultValue<StrokeIcon>(16);
        HeightProperty.OverrideDefaultValue<StrokeIcon>(16);
    }

    public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public IBrush? Brush { get => GetValue(BrushProperty); set => SetValue(BrushProperty, value); }
    public bool Filled { get => GetValue(FilledProperty); set => SetValue(FilledProperty, value); }
    public double Thickness { get => GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Data is not { } geometry) return;
        var brush = Brush ?? TextElement.GetForeground(this) ?? Brushes.Gray;
        double scale = Math.Min(Bounds.Width, Bounds.Height) / 24.0;
        if (scale <= 0) return;
        var transform = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation((Bounds.Width - 24 * scale) / 2, (Bounds.Height - 24 * scale) / 2);
        using (context.PushTransform(transform))
        {
            if (Filled) context.DrawGeometry(brush, null, geometry);
            else context.DrawGeometry(null, new Pen(brush, Thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
        }
    }
}
