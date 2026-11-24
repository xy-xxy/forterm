using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Forterm.Studio.Controls;

/// <summary>A workspace column: a header with the pane's tab chip and actions, then content.</summary>
public sealed class StudioPane : HeaderedContentControl
{
    public static readonly StyledProperty<ICommand?> CloseCommandProperty = AvaloniaProperty.Register<StudioPane, ICommand?>(nameof(CloseCommand));
    public static readonly StyledProperty<ICommand?> MaximizeCommandProperty = AvaloniaProperty.Register<StudioPane, ICommand?>(nameof(MaximizeCommand));
    public static readonly StyledProperty<bool> IsMaximizedProperty = AvaloniaProperty.Register<StudioPane, bool>(nameof(IsMaximized));

    public ICommand? CloseCommand { get => GetValue(CloseCommandProperty); set => SetValue(CloseCommandProperty, value); }
    public ICommand? MaximizeCommand { get => GetValue(MaximizeCommandProperty); set => SetValue(MaximizeCommandProperty, value); }
    public bool IsMaximized { get => GetValue(IsMaximizedProperty); set => SetValue(IsMaximizedProperty, value); }
}
