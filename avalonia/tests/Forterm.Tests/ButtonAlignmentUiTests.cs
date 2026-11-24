using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Automation;
using Forterm.Core;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Desktop;
using System.Text.Json;
using Xunit;
namespace Forterm.Tests;
public sealed class ButtonAlignmentUiTests
{
 static string Folder => UiEvidence.DirectoryPath;
 static void Settle(Window w){w.UpdateLayout();Dispatcher.UIThread.RunJobs();w.UpdateLayout();AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);}
 static object Rect(Control c,Window w){var p=c.TranslatePoint(default,w)??default;return new{x=p.X,y=p.Y,width=c.Bounds.Width,height=c.Bounds.Height};}
 static void Save(Window w,string name){Settle(w);Assert.Equal(w.RequestedThemeVariant,w.ActualThemeVariant);AssertCentered(w);w.CaptureRenderedFrame()!.Save(Path.Combine(Folder,name+".png"));var data=w.GetVisualDescendants().OfType<Button>().Where(b=>b.IsEffectivelyVisible && b.GetType()==typeof(Button)).Select(b=>{var p=b.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault();return new{content=b.Content?.ToString(),classes=b.Classes.ToString(),font=b.FontSize,horizontal=b.HorizontalContentAlignment.ToString(),vertical=b.VerticalContentAlignment.ToString(),padding=b.Padding.ToString(),bounds=Rect(b,w),presenter=p==null?null:new{horizontal=p.HorizontalContentAlignment.ToString(),vertical=p.VerticalContentAlignment.ToString(),bounds=Rect(p,w),child=p.Child==null?null:Rect(p.Child,w)}};});File.WriteAllText(Path.Combine(Folder,name+".json"),JsonSerializer.Serialize(new{renderScaling=w.RenderScaling,buttons=data},new JsonSerializerOptions{WriteIndented=true}));}
 static void AssertCentered(Window w)
 {
  foreach(var b in w.GetVisualDescendants().OfType<Button>().Where(b=>b.GetType()==typeof(Button)&&b.IsEffectivelyVisible))
  {
   var p=b.GetVisualDescendants().OfType<ContentPresenter>().First();
   Assert.Equal(HorizontalAlignment.Center,b.HorizontalContentAlignment);Assert.Equal(VerticalAlignment.Center,b.VerticalContentAlignment);
   Assert.Equal(HorizontalAlignment.Center,p.HorizontalContentAlignment);Assert.Equal(VerticalAlignment.Center,p.VerticalContentAlignment);
   Assert.Equal(b.Padding.Left,b.Padding.Right);Assert.Equal(b.Padding.Top,b.Padding.Bottom);
   var child=p.Child!;var pos=child.TranslatePoint(default,b)!.Value;double dx=pos.X+child.Bounds.Width/2-b.Bounds.Width/2,dy=pos.Y+child.Bounds.Height/2-b.Bounds.Height/2;
   Assert.True(Math.Abs(dx)<=1/w.RenderScaling+.01 && Math.Abs(dy)<=1/w.RenderScaling+.01,$"{b.Content}: center offset {dx},{dy}, scale {w.RenderScaling}, button {b.Bounds}, child {child.Bounds}");
  }
 }
 [AvaloniaFact] public void ActualButtonStylesAndNormalWindowsCenterAtThreeFontSizesAndRenderScales()
 {
 Directory.CreateDirectory(Folder);
 foreach(var theme in new[]{ThemeVariant.Dark,ThemeVariant.Light})foreach(double scale in new[]{1d,1.25,1.5}){
 Application.Current!.RequestedThemeVariant=theme;
 var body=new StackPanel{Margin=new Thickness(20),Spacing=16};body.Children.Add(new TextBlock{Text="按钮对齐 · 中文 / English · 34dp / 30dp / 26dp",FontSize=16});
 foreach(double size in new[]{13d,14d,16d}){
 var row=new WrapPanel{ItemSpacing=10,LineSpacing=8};body.Children.Add(new TextBlock{Text="字号 "+size,FontSize=13});
 foreach(var (text,cls) in new[]{("保存并连接","primary"),("Cancel",""),("本地终端","toolbar-link"),("私钥设置","section-nav"),("✓  测试连接",""),("⌄","icon"),("ⓘ","queue"),("Disabled","disabled")}){
 var b=UI.Button(text,()=>{});b.FontSize=size;if(cls=="queue"){b.Classes.Add("icon");b.Width=b.Height=b.MinWidth=b.MinHeight=26;}else if(cls.Length>0&&cls!="disabled")b.Classes.Add(cls);if(cls=="disabled")b.IsEnabled=false;row.Children.Add(b);}
 body.Children.Add(row);
 }
 var mixed=new Button{Content=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6,Children={new TextBlock{Text="＋",VerticalAlignment=VerticalAlignment.Center},new TextBlock{Text="新建连接 New",VerticalAlignment=VerticalAlignment.Center}}}};body.Children.Add(mixed);
 var w=new Window{RequestedThemeVariant=theme,Content=body,Width=1100,Height=460};w.SetRenderScaling(scale);w.Show();try{Save(w,$"matrix-{theme}-{scale:0.00}");var primary=w.GetVisualDescendants().OfType<Button>().First(b=>b.Classes.Contains("primary"));w.MouseMove(primary.TranslatePoint(new Point(primary.Bounds.Width/2,17),w)!.Value);Assert.True(primary.IsPointerOver);Save(w,$"matrix-hover-{theme}-{scale:0.00}");primary.Focus(NavigationMethod.Tab);Settle(w);Assert.True(primary.IsFocused);Assert.Equal(2,primary.BorderThickness.Top);Save(w,$"matrix-focus-{theme}-{scale:0.00}");}finally{w.Close();}
 var main=new MainWindow(null){RequestedThemeVariant=theme,Width=1150,Height=760};main.SetRenderScaling(scale);main.Show();try{Save(main,$"main-{theme}-{scale:0.00}");}finally{main.Close();}
 var connection=new ConnectionWindow{RequestedThemeVariant=theme,Width=900,Height=700};connection.SetRenderScaling(scale);connection.Show();try{Save(connection,$"connection-{theme}-{scale:0.00}");}finally{connection.Close();}
 }
 Application.Current!.RequestedThemeVariant=ThemeVariant.Dark;
 }
 [AvaloniaFact] public async Task RealQueueAndConfirmationAndKeyboardStatesKeepTheirHitAreas()
 {
 foreach(var theme in new[]{ThemeVariant.Dark,ThemeVariant.Light})foreach(double scale in new[]{1d,1.25,1.5})
 {
  Application.Current!.RequestedThemeVariant=theme;
  var owner=new Window{RequestedThemeVariant=theme,Width=640,Height=400};owner.SetRenderScaling(scale);
  using var queue=new TransferQueue();using var panel=new TransferPanel(owner,queue);owner.Content=panel;owner.Show();
  try
  {
   var ticket=queue.Add("alignment-owned.bin","合成来源",async(p,ct)=>{p.Report(new(1,10,"传输中"));await Task.Delay(Timeout.Infinite,ct);});
   using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
   while(!owner.GetVisualDescendants().OfType<Button>().Any(b=>AutomationProperties.GetName(b)?.StartsWith("取消任务 ")==true)){await Task.Delay(10,timeout.Token);Settle(owner);}
   Save(owner,$"queue-{theme}-{scale:0.00}");
   foreach(var b in owner.GetVisualDescendants().OfType<Button>().Where(b=>AutomationProperties.GetName(b)?.StartsWith("任务详情 ")==true||AutomationProperties.GetName(b)?.StartsWith("取消任务 ")==true)){Assert.Equal(26,b.Width);Assert.Equal(26,b.Height);Assert.True(Math.Abs(b.Bounds.Width-26)<=1/scale);Assert.True(Math.Abs(b.Bounds.Height-26)<=1/scale);}
   var cancel=owner.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)?.StartsWith("取消任务 ")==true);var pos=cancel.TranslatePoint(new Point(13,13),owner)!.Value;owner.MouseDown(pos,MouseButton.Left);owner.MouseUp(pos,MouseButton.Left);Assert.Equal(TransferState.Cancelled,(await ticket.Completion.WaitAsync(TimeSpan.FromSeconds(3))).State);
   var confirmation=UI.Confirm(owner,"确认操作","按钮水平和垂直居中，正文仍按原布局。");var dialog=owner.OwnedWindows.Single();dialog.RequestedThemeVariant=theme;dialog.SetRenderScaling(scale);Save(dialog,$"confirm-{theme}-{scale:0.00}");
   var confirm=dialog.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"确认"));Assert.Equal(34,confirm.Height);Assert.True(Math.Abs(confirm.Bounds.Height-34)<=1/scale);confirm.Focus(NavigationMethod.Tab);Settle(dialog);Assert.True(confirm.IsFocused);Save(dialog,$"confirm-focus-{theme}-{scale:0.00}");
   dialog.KeyPress(Key.Space,RawInputModifiers.None,PhysicalKey.Space,null);Assert.True(confirm.IsPressed);Save(dialog,$"confirm-pressed-{theme}-{scale:0.00}");dialog.KeyRelease(Key.Space,RawInputModifiers.None,PhysicalKey.Space,null);Assert.True(await confirmation);
  }
  finally{owner.Close();}
 }
 Application.Current!.RequestedThemeVariant=ThemeVariant.Dark;
 }

}
