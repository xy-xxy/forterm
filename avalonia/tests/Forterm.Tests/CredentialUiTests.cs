using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Desktop;
using Xunit;
namespace Forterm.Tests;
public sealed class CredentialUiTests
{
 static async Task Until(Func<bool> condition){using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(8));while(!condition()){ct.Token.ThrowIfCancellationRequested();await Task.Delay(5,ct.Token);}}
 static TextBox Entry(Window w,string name)=>w.GetVisualDescendants().OfType<TextBox>().Single(e=>AutomationProperties.GetName(e)==name);
 [AvaloniaFact]public async Task FailedSystemSaveRetainsFormAndInputWithoutMetadataOrPlaintextFallback()
 {
  string dir=Path.Combine(Path.GetTempPath(),"forterm-form-save-"+Guid.NewGuid().ToString("N"));var owner=new Window{Width=1050,Height=650};owner.Show();var vault=new CredentialStoreTests.MemoryVault{FailWrite=true};var repo=new ProfileRepository(new ProfileStore(dir),vault);var form=new ConnectionWindow(null,repo,new KnownHosts(dir));var result=form.ShowDialog<ConnectionRequest?>(owner);
  try{Entry(form,"主机地址").Text="synthetic.invalid";Entry(form,"登录用户").Text="synthetic";Entry(form,"登录密码").Text="synthetic-secret";form.GetVisualDescendants().OfType<CheckBox>().Single(c=>Equals(c.Content,"保存到系统凭据保管库")).IsChecked=true;
   form.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"保存并连接")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>form.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text?.Contains("当前输入仍保留")==true));
   Assert.False(result.IsCompleted);Assert.Equal("synthetic-secret",Entry(form,"登录密码").Text);Assert.Empty(new ProfileStore(dir).Load());Assert.Empty(vault.Values);
   var evidence=UiEvidence.DirectoryPath;Directory.CreateDirectory(evidence);form.CaptureRenderedFrame()?.Save(Path.Combine(evidence,"system-save-failure-keeps-input.png"));
  }finally{form.Close(null);owner.Close();if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }
 [AvaloniaFact]public async Task ExplicitSystemSaveFlagsReflectActualVaultAndKeyPageRemainsReachable()
 {
  string dir=Path.Combine(Path.GetTempPath(),"forterm-form-save-"+Guid.NewGuid().ToString("N"));var owner=new Window{Width=1050,Height=650};owner.Show();var vault=new CredentialStoreTests.MemoryVault();var repo=new ProfileRepository(new ProfileStore(dir),vault);var form=new ConnectionWindow(null,repo,new KnownHosts(dir));var result=form.ShowDialog<ConnectionRequest?>(owner);
  try{
   Entry(form,"主机地址").Text="synthetic.invalid";Entry(form,"登录用户").Text="synthetic";var passwordEntry=Entry(form,"登录密码");passwordEntry.Text="synthetic-secret";
   var mode=form.GetVisualDescendants().OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="认证方式");mode.SelectedIndex=1;form.Width=480;form.Height=500;form.UpdateLayout();Avalonia.Threading.Dispatcher.UIThread.RunJobs();form.UpdateLayout();Entry(form,"私钥口令").Focus();Entry(form,"私钥口令").BringIntoView();Avalonia.Threading.Dispatcher.UIThread.RunJobs();form.UpdateLayout();var point=Entry(form,"私钥口令").TranslatePoint(default,form)!.Value;Assert.True(point.Y>=0&&point.Y+Entry(form,"私钥口令").Bounds.Height<form.Height,$"key field {point} size {Entry(form,"私钥口令").Bounds} window {form.Bounds}");
   var folder=UiEvidence.DirectoryPath;Directory.CreateDirectory(folder);form.CaptureRenderedFrame()?.Save(Path.Combine(folder,"key-page-small.png"));mode.SelectedIndex=0;
   form.GetVisualDescendants().OfType<CheckBox>().Single(c=>Equals(c.Content,"保存到系统凭据保管库")).IsChecked=true;form.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"保存并连接")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var answer=await result;Assert.NotNull(answer);Assert.True(answer.Profile.HasPassword);Assert.Equal("synthetic-secret",await repo.PasswordAsync(answer.Profile,CancellationToken.None));Assert.Equal("",passwordEntry.Text);Assert.DoesNotContain("synthetic-secret",File.ReadAllText(Path.Combine(dir,"profiles.json")));
  }finally{if(!result.IsCompleted)form.Close(null);owner.Close();if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }
}
