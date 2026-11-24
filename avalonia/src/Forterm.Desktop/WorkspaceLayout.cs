using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
namespace Forterm.Desktop;
public sealed record WorkspaceLayout
{
    public string Placement { get; init; } = "editorBottom";
    public bool TerminalVisible { get; init; } = true;
    public bool FilesVisible { get; init; } = true;
    public bool EditorVisible { get; init; }
    public bool MonitorVisible { get; init; }
    public double TerminalWeight { get; init; } = 1;
    public double FilesWeight { get; init; } = 1;
    public double CenterWeight { get; init; } = 4;
    public double EditorWeight { get; init; } = 1;
    public double MonitorRatio { get; init; } = .18;
    public void Validate()
    {
        if (Placement is not ("columns" or "editorLeft" or "editorBottom") || new[] { TerminalWeight, FilesWeight, CenterWeight, EditorWeight }.Any(w => !double.IsFinite(w) || w < .1 || w > 20) || !double.IsFinite(MonitorRatio) || MonitorRatio < .1 || MonitorRatio > .8) throw new IOException("工作区布局或分隔比例无效");
    }
}
/// <summary>Files stay left, terminal/editor stay central, monitor stays right.
/// Only the central editor placement changes; no control or session is recreated.</summary>
internal sealed class CapabilityLayout
{
    readonly Grid body, strip; readonly Control terminal, files, editor, center;
    readonly GridSplitter first, second, monitor; readonly Action<WorkspaceLayout> changed; double viewportWidth;
    public WorkspaceLayout Value { get; private set; }
    public CapabilityLayout(Grid body, Grid strip, Control terminal, Control files, Control editor, GridSplitter first, GridSplitter second, GridSplitter monitor, WorkspaceLayout initial, Action<WorkspaceLayout> changed)
    {
        this.body=body;this.strip=strip;this.terminal=terminal;this.files=files;this.editor=editor;center=body;this.first=first;this.second=second;this.monitor=monitor;this.changed=changed;initial.Validate();Value=initial;
        body.SizeChanged += (_, _) => MinimumCenterHeight();
        foreach(var (split,name) in new[]{(first,"终端与文件分隔"),(second,"编辑器分隔"),(monitor,"资源监控分隔")})
        {
            split.Focusable=true;AutomationProperties.SetName(split,name);ToolTip.SetTip(split,"拖动或方向键调整；双击重置比例");split.DragCompleted+=(_,_)=>Capture();
            split.KeyUp+=(_,e)=>{if(e.Key is Key.Left or Key.Right or Key.Up or Key.Down)Avalonia.Threading.Dispatcher.UIThread.Post(Capture,Avalonia.Threading.DispatcherPriority.Background);};split.DoubleTapped+=(_,_)=>Reset();
        }
    }
    public void ResizeTo(double width){viewportWidth=width;strip.Width=Math.Max(strip.MinWidth,width);}
    public void Set(WorkspaceLayout next,bool persist=true)
    {
        next.Validate();Value=next;bool t=next.TerminalVisible,f=next.FilesVisible,e=next.EditorVisible,m=next.MonitorVisible,c=t||e,bottom=next.Placement=="editorBottom",left=next.Placement=="editorLeft";
        terminal.IsVisible=t;files.IsVisible=f;editor.IsVisible=e;center.IsVisible=c;first.IsVisible=f&&c;second.IsVisible=t&&e;monitor.IsVisible=m&&(f||c);
        strip.ColumnDefinitions=new("*,5,*,5,*");
        double centerWeight=c?next.CenterWeight:0,fileWeight=f?next.FilesWeight:0,monitorWeight=m?(fileWeight+centerWeight>0?(fileWeight+centerWeight)*next.MonitorRatio/(1-next.MonitorRatio):1):0;
        static GridLength W(bool visible,double weight)=>new(visible?weight:0,GridUnitType.Star);
        strip.ColumnDefinitions[0].Width=W(f,fileWeight);strip.ColumnDefinitions[0].MinWidth=f?170:0;
        strip.ColumnDefinitions[1].Width=new(first.IsVisible?5:0);
        strip.ColumnDefinitions[2].Width=W(c,centerWeight);strip.ColumnDefinitions[2].MinWidth=c?170:0;
        strip.ColumnDefinitions[3].Width=new(monitor.IsVisible?5:0);
        strip.ColumnDefinitions[4].Width=W(m,monitorWeight);strip.ColumnDefinitions[4].MinWidth=m?170:0;
        Grid.SetColumn(monitor,3);Grid.SetColumnSpan(monitor,1);
        if(!c&&f&&m){Grid.SetColumn(monitor,1);Grid.SetColumnSpan(monitor,3);strip.ColumnDefinitions[1].Width=new(5);strip.ColumnDefinitions[3].Width=new(0);}
        strip.MinWidth=(f?170:0)+(c?170:0)+(m?170:0)+(first.IsVisible?5:0)+(monitor.IsVisible?5:0);ResizeTo(viewportWidth);
        foreach(var control in new Control[]{terminal,editor,second}){Grid.SetColumn(control,0);Grid.SetRow(control,0);Grid.SetColumnSpan(control,1);Grid.SetRowSpan(control,1);}
        if(bottom)
        {
            body.ColumnDefinitions=new("*");body.RowDefinitions=new("*,5,*");body.RowDefinitions[0].Height=W(t,next.TerminalWeight);body.RowDefinitions[1].Height=new(second.IsVisible?5:0);body.RowDefinitions[2].Height=W(e,next.EditorWeight);Grid.SetRow(second,1);Grid.SetRow(editor,2);
        }
        else
        {
            body.RowDefinitions=new("*");body.ColumnDefinitions=new("*,5,*");body.ColumnDefinitions[0].Width=W(left?e:t,left?next.EditorWeight:next.TerminalWeight);body.ColumnDefinitions[1].Width=new(second.IsVisible?5:0);body.ColumnDefinitions[2].Width=W(left?t:e,left?next.TerminalWeight:next.EditorWeight);
            Grid.SetColumn(left?editor:terminal,0);Grid.SetColumn(second,1);Grid.SetColumn(left?terminal:editor,2);
        }
        first.ResizeDirection=monitor.ResizeDirection=GridResizeDirection.Columns;second.ResizeDirection=bottom?GridResizeDirection.Rows:GridResizeDirection.Columns;
        MinimumCenterHeight(); if(persist)changed(next);
    }
    void MinimumCenterHeight()
    {
        if(body.RowDefinitions.Count!=3)return;
        bool both=Value.TerminalVisible&&Value.EditorVisible;double height=body.Bounds.Height;
        double top=both?Math.Min(60,Math.Max(0,height*.25)):0;
        body.RowDefinitions[0].MinHeight=top;
        body.RowDefinitions[2].MinHeight=both?Math.Min(210,Math.Max(0,height-top-5)):0;
    }
    static double Bound(double weight)=>Math.Clamp(weight,.1,20);
    void Capture()
    {
        var next=Value;double width=files.IsVisible?files.Bounds.Width:0,centerWidth=center.IsVisible?center.Bounds.Width:0,total=width+centerWidth;
        if(files.IsVisible&&center.IsVisible&&total>0)next=next with{FilesWeight=Bound(3*width/total),CenterWeight=Bound(3*centerWidth/total)};
        if(terminal.IsVisible&&editor.IsVisible)
        {
            bool bottom=next.Placement=="editorBottom";double a=bottom?terminal.Bounds.Height:terminal.Bounds.Width,b=bottom?editor.Bounds.Height:editor.Bounds.Width;
            if(a+b>0)next=next with{TerminalWeight=Bound(2*a/(a+b)),EditorWeight=Bound(2*b/(a+b))};
        }
        if(next.MonitorVisible&&strip.ColumnDefinitions[4].ActualWidth>0&&total>0)next=next with{MonitorRatio=Math.Clamp(strip.ColumnDefinitions[4].ActualWidth/(total+strip.ColumnDefinitions[4].ActualWidth),.1,.8)};
        Value=next;changed(next);
    }
    public void Reset()=>Set(Value with{TerminalWeight=1,FilesWeight=1,CenterWeight=2,EditorWeight=1,MonitorRatio=.28});
}
