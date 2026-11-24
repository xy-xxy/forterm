using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Forterm.Core;

namespace Forterm.Desktop;

/// <summary>Font-independent line icons used by the workspace controls.</summary>
public sealed class WorkspaceIconTemplate : IDataTemplate
{
    public bool Match(object? data) => data is string;
    public Control Build(object? data) => WorkspaceChrome.Icon(data?.ToString() ?? "");
}

public static class WorkspaceChrome
{
    public static IDataTemplate FileEntries { get; } = new FuncDataTemplate<RemoteEntry>((entry, _) =>
    {
        if (entry == null) return new Border();
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*,Auto"), Height = 26, Margin = new Thickness(4, 0) };
        row.Children.Add(Icon(entry.Directory ? "文件" : "在线编辑"));
        var name = new TextBlock { Text = entry.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 0) };
        Grid.SetColumn(name, 1); row.Children.Add(name);
        var size = new TextBlock { Text = entry.Directory ? "" : entry.Size >= 1024 ? $"{entry.Size / 1024d:0.#} K" : $"{entry.Size} B", FontSize = 10, Classes = { "note" }, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(size, 2); row.Children.Add(size);
        ToolTip.SetTip(row, entry.Path); return row;
    });

    public static Control Icon(string name)
    {
        string path = name switch
        {
            "文件" or "文件工作区" => "M2,6 L9,6 L11,9 L22,9 L22,21 L2,21 Z M2,11 L22,11",
            "在线编辑" => "M3,3 L21,3 L21,21 L3,21 Z M9,8 L6,12 L9,16 M15,8 L18,12 L15,16 M13,7 L11,17",
            "资源监控" or "监控" => "M3,19 A10,10 0 1 1 21,19 Z M12,14 L17,7 M5,14 L7,14 M12,4 L12,6 M17,14 L19,14",
            "终端" => "M2,4 L22,4 L22,20 L2,20 Z M6,9 L9,12 L6,15 M12,15 L17,15",
            "最大化" => "M3,9 L3,3 L9,3 M15,3 L21,3 L21,9 M3,15 L3,21 L9,21 M15,21 L21,21 L21,15",
            "关闭" => "M6,6 L18,18 M18,6 L6,18",
            "添加" => "M12,4 L12,20 M4,12 L20,12",
            "刷新" => "M20,10 A8,8 0 1 0 20,16 M20,3 L20,10 L13,10",
            "上传" => "M12,17 L12,3 M7,8 L12,3 L17,8 M3,16 L3,21 L21,21 L21,16",
            "下载" => "M12,3 L12,17 M7,12 L12,17 L17,12 M3,16 L3,21 L21,21 L21,16",
            "编辑" => "M4,17 L4,21 L8,21 L21,8 L17,4 Z M14,7 L18,11",
            "收藏" => "M12,2 L15,8 L22,9 L17,14 L18,21 L12,18 L6,21 L7,14 L2,9 L9,8 Z",
            _ => "M5,12 L5,12.1 M12,12 L12,12.1 M19,12 L19,12.1"
        };
        var icon = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(path), StrokeThickness = 1.5, Stretch = Stretch.Uniform, Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        icon.Bind(Shape.StrokeProperty, icon.GetResourceObservable("EditorTextBrush"));
        return icon;
    }

    public static Button IconButton(string icon, string name, Action action)
    {
        var button = UI.Button(icon, action);
        button.ContentTemplate = new WorkspaceIconTemplate();
        button.Classes.Add("pane-action");
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
        return button;
    }

    public static Border Header(string title, Action close, Action maximize, Action? add = null)
    {
        var row = new DockPanel { LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        if (add != null) actions.Children.Add(IconButton("添加", title + " · 添加", add));
        actions.Children.Add(IconButton("最大化", title + " · 最大化或还原", maximize));
        actions.Children.Add(IconButton("关闭", title + " · 收起", close));
        DockPanel.SetDock(actions, Dock.Right);
        row.Children.Add(actions);
        row.Children.Add(new TextBlock { Text = title, Classes = { "pane-title" }, VerticalAlignment = VerticalAlignment.Center });
        return new Border { Classes = { "pane-header" }, Child = row };
    }
}
