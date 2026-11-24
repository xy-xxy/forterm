using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Forterm.Studio.Services;

namespace Forterm.Studio.Controls;

/// <summary>One value at one time; null is unknown and is never drawn as zero.</summary>
public readonly record struct TrendValue(DateTime Time, double? Value);

public enum TrendUnit { Percent, BytesPerSecond }

/// <summary>A line over the last minutes (newest on the right), or two lines of the same unit on one scale.
/// Time is real time: a pause in the readings is a gap in the line. Pointing at the chart — or the arrow keys
/// when it has the focus — reads the values at that moment; <see cref="HoverTime"/> is shared, so every chart
/// of the column shows the same moment.</summary>
public sealed class TrendChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<TrendValue>?> ValuesProperty = AvaloniaProperty.Register<TrendChart, IReadOnlyList<TrendValue>?>(nameof(Values));
    public static readonly StyledProperty<IReadOnlyList<TrendValue>?> SecondaryProperty = AvaloniaProperty.Register<TrendChart, IReadOnlyList<TrendValue>?>(nameof(Secondary));
    public static readonly StyledProperty<string> LabelProperty = AvaloniaProperty.Register<TrendChart, string>(nameof(Label), "");
    public static readonly StyledProperty<string> SecondaryLabelProperty = AvaloniaProperty.Register<TrendChart, string>(nameof(SecondaryLabel), "");
    public static readonly StyledProperty<TrendUnit> UnitProperty = AvaloniaProperty.Register<TrendChart, TrendUnit>(nameof(Unit));
    public static readonly StyledProperty<TimeSpan> WindowProperty = AvaloniaProperty.Register<TrendChart, TimeSpan>(nameof(Window), TimeSpan.FromMinutes(3));
    public static readonly StyledProperty<TimeSpan> GapProperty = AvaloniaProperty.Register<TrendChart, TimeSpan>(nameof(Gap), TimeSpan.FromSeconds(12));
    public static readonly StyledProperty<DateTime?> HoverTimeProperty = AvaloniaProperty.Register<TrendChart, DateTime?>(nameof(HoverTime), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<TrendChart, IBrush?>(nameof(Stroke));
    public static readonly StyledProperty<IBrush?> SecondaryStrokeProperty = AvaloniaProperty.Register<TrendChart, IBrush?>(nameof(SecondaryStroke));
    public static readonly StyledProperty<IBrush?> GridProperty = AvaloniaProperty.Register<TrendChart, IBrush?>(nameof(Grid));
    public static readonly StyledProperty<IBrush?> SurfaceProperty = AvaloniaProperty.Register<TrendChart, IBrush?>(nameof(Surface));
    public static readonly StyledProperty<IBrush?> InkProperty = AvaloniaProperty.Register<TrendChart, IBrush?>(nameof(Ink));
    public static readonly StyledProperty<IBrush?> DimProperty = AvaloniaProperty.Register<TrendChart, IBrush?>(nameof(Dim));
    public static readonly StyledProperty<IBrush?> ReadoutProperty = AvaloniaProperty.Register<TrendChart, IBrush?>(nameof(Readout));

    static TrendChart()
    {
        AffectsRender<TrendChart>(ValuesProperty, SecondaryProperty, LabelProperty, SecondaryLabelProperty, UnitProperty, WindowProperty, GapProperty, HoverTimeProperty,
            StrokeProperty, SecondaryStrokeProperty, GridProperty, SurfaceProperty, InkProperty, DimProperty, ReadoutProperty);
        FocusableProperty.OverrideDefaultValue<TrendChart>(true);
    }

    public IReadOnlyList<TrendValue>? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public IReadOnlyList<TrendValue>? Secondary { get => GetValue(SecondaryProperty); set => SetValue(SecondaryProperty, value); }
    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string SecondaryLabel { get => GetValue(SecondaryLabelProperty); set => SetValue(SecondaryLabelProperty, value); }
    public TrendUnit Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    /// <summary>How much time the width shows.</summary>
    public TimeSpan Window { get => GetValue(WindowProperty); set => SetValue(WindowProperty, value); }
    /// <summary>Readings further apart than this are not joined.</summary>
    public TimeSpan Gap { get => GetValue(GapProperty); set => SetValue(GapProperty, value); }
    public DateTime? HoverTime { get => GetValue(HoverTimeProperty); set => SetValue(HoverTimeProperty, value); }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public IBrush? SecondaryStroke { get => GetValue(SecondaryStrokeProperty); set => SetValue(SecondaryStrokeProperty, value); }
    public IBrush? Grid { get => GetValue(GridProperty); set => SetValue(GridProperty, value); }
    public IBrush? Surface { get => GetValue(SurfaceProperty); set => SetValue(SurfaceProperty, value); }
    public IBrush? Ink { get => GetValue(InkProperty); set => SetValue(InkProperty, value); }
    public IBrush? Dim { get => GetValue(DimProperty); set => SetValue(DimProperty, value); }
    public IBrush? Readout { get => GetValue(ReadoutProperty); set => SetValue(ReadoutProperty, value); }

    const double Top = 13, Bottom = 1, Right = 6;

    DateTime? End => Values is { Count: > 0 } v ? v[^1].Time : null;

    double X(DateTime time, DateTime end) => (Bounds.Width - Right) * (1 - (end - time) / Window);

    /// <summary>The scale ends on a round number: 100%, or 1, 2, 5 × a power of ten of K, M, G per second.</summary>
    public static double RoundUp(double value)
    {
        double unit = 1024;
        while (value > unit * 1000) unit *= 1024;
        double v = Math.Max(1, value / unit), power = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (double step in new[] { 1d, 2, 5, 10 })
            if (v <= step * power * 1.0001) return step * power * unit;
        return 10 * power * unit;
    }

    double Scale()
    {
        if (Unit == TrendUnit.Percent || End is not { } end) return Unit == TrendUnit.Percent ? 100 : RoundUp(0);
        var since = end - Window;
        double Highest(IReadOnlyList<TrendValue>? values) => values?.Where(v => v.Time >= since).Max(v => v.Value) ?? 0;
        return RoundUp(Math.Max(Highest(Values), Highest(Secondary)));
    }

    public string Format(double? value) => Unit == TrendUnit.Percent
        ? value is { } v ? v.ToString("0", CultureInfo.InvariantCulture) + "%" : "—"
        : MonitorProbe.Bytes(value, "/s");

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 20 || h < 24) return;
        context.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h)); // pointer anywhere, not only on the line
        double plot = h - Top - Bottom, scale = Scale();
        var grid = new Pen(Grid ?? Brushes.Gray, 1);
        foreach (double share in new[] { 0, 0.5, 1 })
        {
            double y = Math.Round(Top + plot * share) + 0.5;
            context.DrawLine(grid, new Point(0, y), new Point(w, y));
        }
        var top = Text(Unit == TrendUnit.Percent ? "100%" : AxisText(scale), 10, Dim);
        context.DrawText(top, new Point(0, 0));
        if (End is not { } end) return;

        double Y(double value) => Top + plot * (1 - Math.Clamp(value / scale, 0, 1));
        using (context.PushClip(new Rect(0, 0, w, h)))
        {
            DrawSeries(context, Secondary, SecondaryStroke ?? Brushes.Orange, end, Y, h - Bottom, area: false);
            DrawSeries(context, Values, Stroke ?? Brushes.SteelBlue, end, Y, h - Bottom, area: Secondary == null);
        }
        if (HoverTime is { } hover) DrawReadout(context, hover, end, Y, w, h);
    }

    /// <summary>"200 K/s": the scale is a round number, so no decimals.</summary>
    static string AxisText(double scale) => MonitorProbe.Bytes(scale, "/s").Replace(".0 ", " ");

    void DrawSeries(DrawingContext context, IReadOnlyList<TrendValue>? values, IBrush brush, DateTime end, Func<double, double> y, double floor, bool area)
    {
        if (values == null) return;
        var since = end - Window - Gap;
        var run = new List<Point>();
        DateTime last = default;
        var pen = new Pen(brush, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        void Flush()
        {
            if (run.Count == 1) context.DrawEllipse(brush, null, run[0], 1.5, 1.5);
            else if (run.Count > 1)
            {
                var line = new StreamGeometry();
                using (var g = line.Open()) { g.BeginFigure(run[0], false); foreach (var p in run.Skip(1)) g.LineTo(p); g.EndFigure(false); }
                if (area && brush is ISolidColorBrush solid)
                {
                    var fill = new StreamGeometry();
                    using (var g = fill.Open()) { g.BeginFigure(new Point(run[0].X, floor), true); foreach (var p in run) g.LineTo(p); g.LineTo(new Point(run[^1].X, floor)); g.EndFigure(true); }
                    context.DrawGeometry(new SolidColorBrush(solid.Color, 0.10), null, fill);
                }
                context.DrawGeometry(null, pen, line);
            }
            run.Clear();
        }
        Point? newest = null;
        foreach (var v in values)
        {
            if (v.Time < since) continue;
            if (v.Value is not { } value) { Flush(); continue; }
            if (run.Count > 0 && v.Time - last > Gap) Flush();
            newest = new Point(X(v.Time, end), y(value));
            run.Add(newest.Value);
            last = v.Time;
        }
        Flush();
        if (newest is { } dot && last == end) Dot(context, dot, brush);
    }

    /// <summary>A dot with a ring of the surface colour, so it stays apart from the lines it sits on.</summary>
    void Dot(DrawingContext context, Point at, IBrush brush)
    {
        context.DrawEllipse(Surface ?? Brushes.Transparent, null, at, 5.5, 5.5);
        context.DrawEllipse(brush, null, at, 3.5, 3.5);
    }

    static double? At(IReadOnlyList<TrendValue>? values, DateTime time) => values?.FirstOrDefault(v => v.Time == time) is { Time.Ticks: > 0 } v ? v.Value : null;

    void DrawReadout(DrawingContext context, DateTime hover, DateTime end, Func<double, double> y, double w, double h)
    {
        double x = X(hover, end);
        if (x < -1 || x > w) return;
        context.DrawLine(new Pen(Dim ?? Brushes.Gray, 1), new Point(Math.Round(x) + 0.5, Top), new Point(Math.Round(x) + 0.5, h - Bottom));
        var rows = new List<(IBrush Brush, double? Value, string Label)> { (Stroke ?? Brushes.SteelBlue, At(Values, hover), Label) };
        if (Secondary != null) rows.Add((SecondaryStroke ?? Brushes.Orange, At(Secondary, hover), SecondaryLabel));
        foreach (var (brush, value, _) in rows) if (value is { } v) Dot(context, new Point(x, y(v)), brush);

        var texts = rows.Select(r => (r.Brush, Value: Text(Format(r.Value), 11, Ink, bold: true), Label: Text(r.Label, 10, Dim))).ToList();
        var time = Text(hover.ToString("HH:mm:ss", CultureInfo.InvariantCulture), 10, Dim);
        const double pad = 6, key = 10, line = 14;
        double width = Math.Max(time.Width, texts.Max(t => key + t.Value.Width + 5 + t.Label.Width)) + 2 * pad, height = 12 + line * texts.Count + 6;
        double left = x + 10 + width <= w ? x + 10 : Math.Max(0, x - 10 - width);
        var box = new Rect(left, Math.Max(0, (h - height) / 2), width, height);
        context.DrawRectangle(Readout ?? Surface, new Pen(Grid ?? Brushes.Gray, 1), box, 4, 4);
        context.DrawText(time, new Point(box.X + pad, box.Y + 2));
        for (int i = 0; i < texts.Count; i++)
        {
            double top = box.Y + 14 + line * i;
            context.DrawLine(new Pen(texts[i].Brush, 2, lineCap: PenLineCap.Round), new Point(box.X + pad, top + 7), new Point(box.X + pad + 6, top + 7));
            context.DrawText(texts[i].Value, new Point(box.X + pad + key, top));
            context.DrawText(texts[i].Label, new Point(box.X + pad + key + texts[i].Value.Width + 5, top + 1));
        }
    }

    FormattedText Text(string text, double size, IBrush? brush, bool bold = false) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, weight: bold ? FontWeight.SemiBold : FontWeight.Normal), size, brush ?? Brushes.Gray);

    // ---------- reading a moment ----------

    IReadOnlyList<TrendValue> Shown => End is { } end && Values != null ? Values.Where(v => v.Time >= end - Window).ToList() : [];

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (End is not { } end) return;
        double x = e.GetPosition(this).X;
        HoverTime = Shown.MinBy(v => Math.Abs(X(v.Time, end) - x)) is { Time.Ticks: > 0 } nearest ? nearest.Time : null;
    }

    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); HoverTime = null; }
    protected override void OnLostFocus(FocusChangedEventArgs e) { base.OnLostFocus(e); HoverTime = null; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && HoverTime != null) { HoverTime = null; e.Handled = true; return; }
        if (e.Key is not (Key.Left or Key.Right)) return;
        var shown = Shown;
        if (shown.Count == 0) return;
        int at = HoverTime is { } t ? shown.ToList().FindIndex(v => v.Time == t) : -1;
        at = at < 0 ? shown.Count - 1 : Math.Clamp(at + (e.Key == Key.Left ? -1 : 1), 0, shown.Count - 1);
        HoverTime = shown[at].Time;
        e.Handled = true;
    }
}

/// <summary>A share of a limit: filled from the left, with an optional lighter second part (cached memory).
/// The fill says how serious it is; the track is the same colour, faint.</summary>
public sealed class Meter : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<Meter, double>(nameof(Value), coerce: Finite);
    public static readonly StyledProperty<double> SecondaryProperty = AvaloniaProperty.Register<Meter, double>(nameof(Secondary), coerce: Finite);
    public static readonly StyledProperty<MonitorLevel> LevelProperty = AvaloniaProperty.Register<Meter, MonitorLevel>(nameof(Level));
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<Meter, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<IBrush?> WarningProperty = AvaloniaProperty.Register<Meter, IBrush?>(nameof(Warning));
    public static readonly StyledProperty<IBrush?> DangerProperty = AvaloniaProperty.Register<Meter, IBrush?>(nameof(Danger));

    /// <summary>An animation cannot get away from a value that is not a number.</summary>
    static double Finite(AvaloniaObject _, double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;

    static Meter()
    {
        AffectsRender<Meter>(ValueProperty, SecondaryProperty, LevelProperty, FillProperty, WarningProperty, DangerProperty);
        HeightProperty.OverrideDefaultValue<Meter>(6);
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Secondary { get => GetValue(SecondaryProperty); set => SetValue(SecondaryProperty, value); }
    public MonitorLevel Level { get => GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? Warning { get => GetValue(WarningProperty); set => SetValue(WarningProperty, value); }
    public IBrush? Danger { get => GetValue(DangerProperty); set => SetValue(DangerProperty, value); }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 4 || h < 2) return;
        var color = ((Level switch { MonitorLevel.Danger => Danger, MonitorLevel.Warning => Warning, _ => Fill }) as ISolidColorBrush)?.Color ?? Colors.SteelBlue;
        double r = h / 2, first = w * Math.Clamp(Value, 0, 100) / 100, second = w * Math.Clamp(Secondary, 0, 100) / 100;
        context.DrawRectangle(new SolidColorBrush(color, 0.18), null, new Rect(0, 0, w, h), r, r);
        using (context.PushClip(new RoundedRect(new Rect(0, 0, w, h), r)))
        {
            if (second >= 1) context.DrawRectangle(new SolidColorBrush(color, 0.42), null, new Rect(first > 0 ? first + 2 : 0, 0, Math.Max(0, Math.Min(second - 2, w - first - 2)), h), 1, 1);
            if (Value > 0) context.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, Math.Max(first, 3), h), r, r);
        }
    }
}

public sealed record CoreRow(string Index, double? Percent)
{
    public string Text => Percent is { } p ? p.ToString("0", CultureInfo.InvariantCulture) + "%" : "—";
}

/// <summary>Every core at once: a bar each while there is room, squares (darker is busier) on large machines.
/// <see cref="Hovered"/> is the core under the pointer.</summary>
public sealed class CoreBars : Control
{
    public static readonly StyledProperty<IReadOnlyList<CoreRow>?> CoresProperty = AvaloniaProperty.Register<CoreBars, IReadOnlyList<CoreRow>?>(nameof(Cores));
    public static readonly StyledProperty<CoreRow?> HoveredProperty = AvaloniaProperty.Register<CoreBars, CoreRow?>(nameof(Hovered), defaultBindingMode: Avalonia.Data.BindingMode.OneWayToSource);
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<CoreBars, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<IBrush?> InkProperty = AvaloniaProperty.Register<CoreBars, IBrush?>(nameof(Ink));

    static CoreBars()
    {
        AffectsRender<CoreBars>(CoresProperty, FillProperty, InkProperty, HoveredProperty);
        AffectsMeasure<CoreBars>(CoresProperty);
    }

    public IReadOnlyList<CoreRow>? Cores { get => GetValue(CoresProperty); set => SetValue(CoresProperty, value); }
    public CoreRow? Hovered { get => GetValue(HoveredProperty); set => SetValue(HoveredProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? Ink { get => GetValue(InkProperty); set => SetValue(InkProperty, value); }

    const double Gap = 2, BarHeight = 26, Square = 12, LeastBar = 4, MostBar = 22;

    /// <summary>The cell of every core at this width, and whether they are bars.</summary>
    public static (Rect[] Cells, bool Bars, double Height) Arrange(int count, double width)
    {
        if (count == 0 || width < Square) return ([], true, 0);
        double bar = (width - Gap * (count - 1)) / count;
        if (bar >= LeastBar)
        {
            bar = Math.Min(MostBar, Math.Floor(bar));
            return (Enumerable.Range(0, count).Select(i => new Rect(i * (bar + Gap), 0, bar, BarHeight)).ToArray(), true, BarHeight);
        }
        int columns = Math.Max(1, (int)((width + Gap) / (Square + Gap)));
        var cells = Enumerable.Range(0, count).Select(i => new Rect(i % columns * (Square + Gap), i / columns * (Square + Gap), Square, Square)).ToArray();
        return (cells, false, (count + columns - 1) / columns * (Square + Gap) - Gap);
    }

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsFinite(available.Width) ? available.Width : 240;
        return new Size(0, Arrange(Cores?.Count ?? 0, width).Height);
    }

    public override void Render(DrawingContext context)
    {
        if (Cores is not { Count: > 0 } cores) return;
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var color = (Fill as ISolidColorBrush)?.Color ?? Colors.SteelBlue;
        var (cells, bars, _) = Arrange(cores.Count, Bounds.Width);
        for (int i = 0; i < cells.Length; i++)
        {
            double share = Math.Clamp(cores[i].Percent ?? 0, 0, 100) / 100;
            if (bars)
            {
                context.DrawRectangle(new SolidColorBrush(color, 0.18), null, cells[i], 2, 2);
                double filled = cores[i].Percent is > 0 ? Math.Max(2, cells[i].Height * share) : 0;
                if (filled > 0) context.DrawRectangle(new SolidColorBrush(color), null, new Rect(cells[i].X, cells[i].Bottom - filled, cells[i].Width, filled), 2, 2);
            }
            else context.DrawRectangle(new SolidColorBrush(color, 0.14 + 0.86 * share), null, cells[i], 2, 2);
            if (Hovered is { } h && h.Index == cores[i].Index) context.DrawRectangle(null, new Pen(Ink ?? Brushes.White, 1), cells[i].Inflate(0.5), 2, 2);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Cores is not { Count: > 0 } cores) return;
        var at = e.GetPosition(this);
        var cells = Arrange(cores.Count, Bounds.Width).Cells;
        int index = Array.FindIndex(cells, c => c.Inflate(Gap / 2).Contains(at));
        SetCurrentValue(HoveredProperty, index >= 0 ? cores[index] : null);
    }

    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); SetCurrentValue(HoveredProperty, null); }
}

/// <summary>Cards in as many columns as fit, each put where there is the most room: one under the other in the
/// side column, side by side when the column is maximised.</summary>
public sealed class ColumnsPanel : Panel
{
    public static readonly StyledProperty<double> MinColumnWidthProperty = AvaloniaProperty.Register<ColumnsPanel, double>(nameof(MinColumnWidth), 320);
    public static readonly StyledProperty<int> MaxColumnsProperty = AvaloniaProperty.Register<ColumnsPanel, int>(nameof(MaxColumns), 3);
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<ColumnsPanel, double>(nameof(Spacing), 8);
    public static readonly StyledProperty<double> ColumnSpacingProperty = AvaloniaProperty.Register<ColumnsPanel, double>(nameof(ColumnSpacing), double.NaN);

    static ColumnsPanel() => AffectsMeasure<ColumnsPanel>(MinColumnWidthProperty, MaxColumnsProperty, SpacingProperty, ColumnSpacingProperty);

    public double MinColumnWidth { get => GetValue(MinColumnWidthProperty); set => SetValue(MinColumnWidthProperty, value); }
    public int MaxColumns { get => GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }
    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }
    /// <summary>Between columns; the same as <see cref="Spacing"/> unless set.</summary>
    public double ColumnSpacing { get => GetValue(ColumnSpacingProperty); set => SetValue(ColumnSpacingProperty, value); }
    /// <summary>One, two or four columns, never three with one left over.</summary>
    public bool Even { get; set; }
    double Between => double.IsNaN(ColumnSpacing) ? Spacing : ColumnSpacing;

    (int Columns, double Width) Split(double width)
    {
        int columns = double.IsFinite(width) ? Math.Clamp((int)((width + Between) / (MinColumnWidth + Between)), 1, Math.Max(1, MaxColumns)) : 1;
        if (Even && columns == 3) columns = 2;
        double each = double.IsFinite(width) ? Math.Max(0, (width - Between * (columns - 1)) / columns) : MinColumnWidth;
        return (columns, each);
    }

    protected override Size MeasureOverride(Size available)
    {
        var (columns, each) = Split(available.Width);
        foreach (var child in Children) child.Measure(new Size(each, double.PositiveInfinity));
        return new Size(columns * each + Between * (columns - 1), Place(columns, null));
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (columns, each) = Split(final.Width);
        Place(columns, (child, column, y) => child.Arrange(new Rect(column * (each + Between), y, each, child.DesiredSize.Height)));
        return final;
    }

    double Place(int columns, Action<Control, int, double>? put)
    {
        var heights = new double[columns];
        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            int column = Array.IndexOf(heights, heights.Min());
            double y = heights[column] > 0 ? heights[column] + Spacing : 0;
            put?.Invoke(child, column, y);
            heights[column] = y + child.DesiredSize.Height;
        }
        return heights.Max();
    }
}

/// <summary>Rows under each other, all exactly as wide as the panel. (A StackPanel gives a row that asks for
/// more — a grid with trimmed text does — more, and it runs out of its card.)</summary>
public sealed class RowsPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<RowsPanel, double>(nameof(Spacing));
    static RowsPanel() => AffectsMeasure<RowsPanel>(SpacingProperty);
    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    IEnumerable<Control> Shown => Children.Where(c => c.IsVisible);

    protected override Size MeasureOverride(Size available)
    {
        double width = 0, height = 0;
        foreach (var child in Children) child.Measure(new Size(available.Width, double.PositiveInfinity));
        foreach (var child in Shown) { width = Math.Max(width, child.DesiredSize.Width); height += child.DesiredSize.Height + (height > 0 ? Spacing : 0); }
        return new Size(Math.Min(width, available.Width), height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double y = 0;
        foreach (var child in Shown)
        {
            if (y > 0) y += Spacing;
            child.Arrange(new Rect(0, y, final.Width, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
        }
        return final;
    }
}

/// <summary>An arc that turns while something is under way (the turning itself belongs to Themes/Motion.axaml;
/// without animations it stands still and still reads as "busy").</summary>
public sealed class BusyRing : Control
{
    public static readonly StyledProperty<double> AngleProperty = AvaloniaProperty.Register<BusyRing, double>(nameof(Angle));
    public static readonly StyledProperty<IBrush?> BrushProperty = AvaloniaProperty.Register<BusyRing, IBrush?>(nameof(Brush));
    /// <summary>Turns only while this is true: an animation that nobody sees still costs a frame each time.</summary>
    public static readonly StyledProperty<bool> IsActiveProperty = AvaloniaProperty.Register<BusyRing, bool>(nameof(IsActive));

    static BusyRing()
    {
        AffectsRender<BusyRing>(AngleProperty, BrushProperty);
        WidthProperty.OverrideDefaultValue<BusyRing>(20);
        HeightProperty.OverrideDefaultValue<BusyRing>(20);
    }

    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public double Angle { get => GetValue(AngleProperty); set => SetValue(AngleProperty, value); }
    public IBrush? Brush { get => GetValue(BrushProperty); set => SetValue(BrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height), thickness = Math.Max(2, size / 10), r = (size - thickness) / 2;
        if (r < 2) return;
        var color = (Brush as ISolidColorBrush)?.Color ?? Colors.Gray;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        context.DrawEllipse(null, new Pen(new SolidColorBrush(color, 0.2), thickness), center, r, r);
        double start = (Angle - 90) * Math.PI / 180, end = start + Math.PI * 0.55;
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            g.BeginFigure(new Point(center.X + r * Math.Cos(start), center.Y + r * Math.Sin(start)), false);
            g.ArcTo(new Point(center.X + r * Math.Cos(end), center.Y + r * Math.Sin(end)), new Size(r, r), 0, false, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(new SolidColorBrush(color), thickness, lineCap: PenLineCap.Round), arc);
    }
}
