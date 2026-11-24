using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Desktop;
using Xunit;
namespace Forterm.Tests;
public sealed class ConnectionPolishUiTests
{
    static void Click(Control root,string label)=>root.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static TextBox Field(Control root,string name)=>root.GetVisualDescendants().OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)==name);
    static void Layout(Window w){w.UpdateLayout();Dispatcher.UIThread.RunJobs();w.UpdateLayout();}
    static void Save(Window w,string name){Layout(w);w.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath,name+".png"));}
    [AvaloniaFact] public void PrivateKeyNavigationOnlyRevealsSectionAndPreservesAuthenticationInputsAndVaultFlags()
    {
        foreach(var (mode,protocol) in new[]{("password","ssh"),("key","ssh"),("keyboard","ssh"),("agent","ssh"),("password","ftp")})
        {
            var vault=new CredentialStoreTests.MemoryVault();var p=new ConnectionProfile("synthetic","test","example.invalid",protocol=="ftp"?21:22,"user"){Protocol=protocol,AuthMode=mode,KeyPath=mode=="key"?"/synthetic/key":"",HasPassword=mode is "password" or "keyboard",HasPassphrase=mode=="key"};
            var w=new ConnectionWindow(p,new ProfileRepository(new ProfileStore(null),vault));w.Show();
            try
            {
                Field(w,"登录密码").Text="synthetic-password";Field(w,"私钥口令").Text="synthetic-phrase";
                var auth=w.GetVisualDescendants().OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="认证方式");int selected=auth.SelectedIndex;
                var passwordFlag=w.GetVisualDescendants().OfType<CheckBox>().Single(c=>Equals(c.Content,"保存到系统凭据保管库"));var phraseFlag=w.GetVisualDescendants().OfType<CheckBox>().Single(c=>Equals(c.Content,"保存私钥口令到系统保管库"));bool? beforePassword=passwordFlag.IsChecked,beforePhrase=phraseFlag.IsChecked;bool passwordEnabled=Field(w,"登录密码").IsEnabled;
                Click(w,"私钥设置");Layout(w);
                Assert.Equal(selected,auth.SelectedIndex);Assert.Equal(passwordEnabled,Field(w,"登录密码").IsEnabled);Assert.Equal(beforePassword,passwordFlag.IsChecked);Assert.Equal(beforePhrase,phraseFlag.IsChecked);Assert.Equal("synthetic-password",Field(w,"登录密码").Text);Assert.Equal("synthetic-phrase",Field(w,"私钥口令").Text);
                Assert.True(Field(w,"私钥文件").IsEffectivelyVisible);Assert.Equal(mode=="key",Field(w,"私钥文件").IsEnabled);Assert.Equal(mode=="key",w.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"选择文件")).IsEnabled);
                Assert.Equal(0,vault.Reads+vault.Writes+vault.Deletes);
                if(mode=="password"&&protocol=="ssh")
                {
                    Save(w,"key-navigation-preserves-password");auth.SelectedIndex=1;Layout(w);Assert.True(Field(w,"私钥文件").IsEnabled);Assert.False(Field(w,"登录密码").IsEnabled);Assert.Equal(0,vault.Reads+vault.Writes+vault.Deletes);
                    Save(w,"key-explicit-auth-selection");auth.SelectedIndex=0;Assert.Equal("synthetic-password",Field(w,"登录密码").Text);Assert.Equal(beforePassword,passwordFlag.IsChecked);
                }
            }
            finally{w.Close();}
        }
    }
    [AvaloniaFact] public void FormGutterSeparatesHelpAndControlsAndBottomContainsOnlyConnectionSettings()
    {
        var w=new ConnectionWindow();w.Show();
        try
        {
            foreach(var theme in new[]{Avalonia.Styling.ThemeVariant.Dark,Avalonia.Styling.ThemeVariant.Light})
            {
                Application.Current!.RequestedThemeVariant=theme;
                foreach(double width in new[]{900d,760d,700d,480d})
                {
                    w.Width=width;w.Height=700;Layout(w);
                    foreach(var row in w.GetVisualDescendants().OfType<Grid>().Where(g=>g.IsEffectivelyVisible&&AutomationProperties.GetName(g)?.StartsWith("连接字段 ")==true))
                    {
                        var label=row.Children[0];var value=row.Children[1];
                        if(width>=700)Assert.True(value.Bounds.Left-label.Bounds.Right>=24,$"{AutomationProperties.GetName(row)} gap {value.Bounds.Left-label.Bounds.Right}");
                        else Assert.True(value.Bounds.Top-label.Bounds.Bottom>=10);
                        Assert.True(value.Bounds.Width>0);
                    }
                    var scroll=w.GetVisualDescendants().OfType<ScrollViewer>().First();scroll.Offset=default;Save(w,$"connection-polish-{theme}-{width}-top");scroll.Offset=new Vector(0,double.MaxValue);Layout(w);
                    Assert.DoesNotContain(w.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="迁移状态");var last=w.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Text=="心跳与字号在偏好设置中配置。");var point=last.TranslatePoint(default,w)!.Value;Assert.True(point.Y>=0&&point.Y+last.Bounds.Height<w.Bounds.Height);Save(w,$"connection-polish-{theme}-{width}-bottom");
                }
            }
        }
        finally{Application.Current!.RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;w.Close();}
    }
    [AvaloniaFact] public void LibraryHeadingHasItsOwnRowAndValidationDetailsLiveInAbout()
    {
        var vault=new CredentialStoreTests.MemoryVault();var w=new MainWindow(null,vault);w.Show();
        try
        {
            foreach(double width in new[]{1150d,700d})
            {
                w.Width=width;Layout(w);var title=w.GetVisualDescendants().OfType<TextBlock>().Single(t=>AutomationProperties.GetName(t)=="连接库标题");var tools=w.GetVisualDescendants().OfType<WrapPanel>().Single(t=>AutomationProperties.GetName(t)=="连接库操作");var point=tools.TranslatePoint(default,w)!.Value;var heading=title.TranslatePoint(default,w)!.Value;Assert.True(point.Y-heading.Y-title.Bounds.Height>=8);
                var buttons=tools.Children.OfType<Button>().ToArray();for(int i=1;i<buttons.Length;i++){var a=buttons[i-1].TranslatePoint(default,w)!.Value;var b=buttons[i].TranslatePoint(default,w)!.Value;Assert.True(b.Y>a.Y||b.X-a.X-buttons[i-1].Bounds.Width>=10);}
                Assert.DoesNotContain(w.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text?.Contains("C# + Avalonia")==true||t.Text?.Contains("现场验证项见清单")==true||t.Text=="迁移状态");Save(w,"library-polish-"+width);
            }
            var about=w.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="关于与验证说明");about.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var dialog=w.OwnedWindows.Single();Layout(dialog);Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="验证说明");Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text=="0.3.0-alpha.36");Save(dialog,"about-verification-details");dialog.Close();Assert.Equal(0,vault.Reads+vault.Writes+vault.Deletes);
        }
        finally{w.Close();}
    }
}
