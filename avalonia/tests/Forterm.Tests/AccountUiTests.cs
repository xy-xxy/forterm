using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Desktop;
using Xunit;
namespace Forterm.Tests;
public sealed class AccountUiTests
{
    static void Click(Control root, string label) => root.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static TextBox Field(Control root, string label) => root.GetVisualDescendants().OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == label);
    static async Task Until(Func<bool> check) { using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10)); while (!check()) { await Task.Delay(10, ct.Token); Dispatcher.UIThread.RunJobs(); } }
    [AvaloniaFact] public async Task VaultFailureRetainsMaskedInputsAndSuccessClearsWithoutMetadataSecrets()
    {
        string dir = Path.Combine(Path.GetTempPath(), "forterm-account-ui-" + Guid.NewGuid().ToString("N"));
        var vault = new CredentialStoreTests.MemoryVault { FailWrite = true }; var repository = new AccountRepository(dir, vault); var owner = new Window(); owner.Show(); var window = new AccountEditWindow(repository); var showing = window.ShowDialog<BastionAccount?>(owner);
        try
        {
            Field(window, "账户名称").Text = "合成账户"; Field(window, "入口地址").Text = "https://example.invalid"; Field(window, "账户用户").Text = "synthetic";
            Field(window, "账户密码").Text = "synthetic-password"; Field(window, "验证器key").Text = "otpauth://totp/public?secret=" + TotpTests.PublicSeed;
            window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "启用本地TOTP验证码")).IsChecked = true;
            Click(window, "保存"); await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("保存失败") == true));
            Assert.Equal("synthetic-password", Field(window, "账户密码").Text); Assert.NotEqual('\0', Field(window, "账户密码").PasswordChar); Assert.NotEqual('\0', Field(window, "验证器key").PasswordChar); Assert.Empty(repository.Load());
            window.Height = 380; window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "account-small-failure.png"));
            var save = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "保存")); Assert.True(save.Bounds.Bottom <= window.Bounds.Height);
            vault.FailWrite = false; Click(window, "保存"); var saved = await showing; Assert.NotNull(saved); Assert.True(saved.TotpEnabled); Assert.Equal(new TotpParameters(), saved.Totp);
            Assert.Empty(FieldFromLogical(window, "账户密码")?.Text ?? "");
            string metadata = File.ReadAllText(Path.Combine(dir, "bastion-accounts.json")); Assert.DoesNotContain(TotpTests.PublicSeed, metadata); Assert.DoesNotContain("synthetic-password", metadata);
        }
        finally { window.Close(); owner.Close(); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    static TextBox? FieldFromLogical(Control root, string label) => root.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(b => AutomationProperties.GetName(b) == label);
    [AvaloniaFact] public async Task CodeHiddenByDefaultCopyUsesCurrentPeriodAndClosingWipesOwnedKey()
    {
        var configuration = Totp.Normalize(TotpTests.PublicSeed); var now = DateTimeOffset.FromUnixTimeSeconds(59); var window = new TotpWindow(new() { Name = "RFC公开测试账户", Username = "synthetic" }, configuration, () => now); window.Show();
        try
        {
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "••••••"); Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "287082");
            window.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = true; Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "287082");
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "totp-public-vector.png"));
            now = DateTimeOffset.FromUnixTimeSeconds(60); Click(window, "复制验证码"); string expected = Totp.Generate(configuration.Secret, configuration.Parameters, now).Code; await Until(() => window.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult() == expected);
            await window.Clipboard!.SetTextAsync("user-new-content"); await TotpWindow.ClearClipboard(window.Clipboard!, expected, TimeSpan.Zero); Assert.Equal("user-new-content", await window.Clipboard!.TryGetTextAsync());
            await window.Clipboard!.SetTextAsync(expected); await TotpWindow.ClearClipboard(window.Clipboard!, expected, TimeSpan.Zero); Assert.Equal("", await window.Clipboard!.TryGetTextAsync());
        }
        finally { window.Close(); }
        Assert.All(configuration.Secret, b => Assert.Equal((byte)0, b));
    }
}
