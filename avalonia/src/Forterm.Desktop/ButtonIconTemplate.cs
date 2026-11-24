using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;

namespace Forterm.Desktop;

/// <summary>Keep the menu chevron independent of the font's low U+2304 baseline.
/// The standard presenter centers the whole icon; no per-button offsets.</summary>
public sealed class ButtonIconTemplate : IDataTemplate
{
    static readonly Geometry Chevron = Geometry.Parse("M 1,4 L 7,10 L 13,4 L 14,5 L 7,12 L 0,5 Z");
    public bool Match(object? data) => data is string;
    public Control Build(object? data) => data is "⌄"
        ? new PathIcon { Data = Chevron, Width = 14, Height = 14 }
        : new TextBlock { Text = data?.ToString() ?? "" };
}

/// <summary>Center icon and label as one group with a fixed gap, while retaining
/// the original string Content for automation and command lookup.</summary>
public sealed class ButtonLabelTemplate : IDataTemplate
{
    public bool Match(object? data) => data is string text && (text.StartsWith("✓  ") || text.StartsWith("＋ "));
    public Control Build(object? data)
    {
        string text = (string)data!;
        return new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = text[..1], VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                new TextBlock { Text = text[1..].TrimStart(), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
            }
        };
    }
}
