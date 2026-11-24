using Avalonia;
using Avalonia.Controls;
namespace Forterm.Desktop;
/// <summary>Fixed footer actions: two left slots and three right slots. It
/// reserves a second line when needed; its minimum never forces a wide window.</summary>
public sealed class ActionBar : Panel
{
    protected override Size MeasureOverride(Size available)
    {
        foreach (var c in Children) c.Measure(new Size(double.PositiveInfinity, 34));
        var (left, right) = Widths(); return new Size(Math.Min(available.Width, left + right + 32), left + right + 32 > available.Width ? 80 : 34);
    }
    (double left, double right) Widths() { double left = 0, right = 0; for (int i = 0; i < Children.Count; i++) { var c = Children[i]; if (!c.IsVisible) continue; if (i < 2) { if (left > 0) left += 12; left += c.DesiredSize.Width; } else { if (right > 0) right += 8; right += c.DesiredSize.Width; } } return (left, right); }
    protected override Size ArrangeOverride(Size size)
    {
        var (left, right) = Widths(); double y = left + right + 32 > size.Width ? 46 : 0, x = 0, end = size.Width;
        for (int i = 0; i < 2; i++) { var c = Children[i]; if (!c.IsVisible) continue; c.Arrange(new Rect(x, 0, c.DesiredSize.Width, 34)); x += c.DesiredSize.Width + 12; }
        for (int i = Children.Count - 1; i >= 2; i--) { var c = Children[i]; if (!c.IsVisible) continue; end -= c.DesiredSize.Width; c.Arrange(new Rect(end, y, c.DesiredSize.Width, 34)); end -= 8; }
        return size;
    }
}
