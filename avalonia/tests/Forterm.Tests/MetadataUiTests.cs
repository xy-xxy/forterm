using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Desktop;
using Xunit;
namespace Forterm.Tests;
public sealed class MetadataUiTests
{
    sealed class Picker(string path) : IFilePickers
    {
        public Task<PickedLocalFile?> UploadAsync(Window owner) => Task.FromResult<PickedLocalFile?>(new(path, "profiles.json"));
        public Task<string?> DownloadAsync(Window owner, string name) => Task.FromResult<string?>(null);
    }
    static void Click(Control root, string label) => root.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static async Task Until(Func<bool> check) { using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10)); while (!check()) { await Task.Delay(10, ct.Token); Dispatcher.UIThread.RunJobs(); } }
    [AvaloniaFact] public async Task ExplicitImportCancelPreservesExistingAndConfirmAppendsWithoutVaultAccess()
    {
        string dir = Path.Combine(Path.GetTempPath(), "forterm-import-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); string input = Path.Combine(dir, "input.json");
        var store = new ProfileStore(dir); var original = new ConnectionProfile("existing", "g", "existing.invalid", 22, "user") { HasPassword = true }; store.Save([original]);
        File.WriteAllText(input, MetadataExchange.ExportProfiles([new("imported", "g", "import.invalid", 22, "user")])); var vault = new CredentialStoreTests.MemoryVault(); int reloads = 0;
        var owner = new Window(); owner.Show(); var window = new MetadataWindow(store, new(store, vault), new(dir, vault), () => reloads++, new Picker(input)); var showing = window.ShowDialog(owner);
        try
        {
            Click(window, "导入连接配置"); await Until(() => window.OwnedWindows.Count > 0); Click(window.OwnedWindows.Single(), "取消"); await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "已取消导入")); Assert.Equal(original, Assert.Single(store.Load()));
            Click(window, "导入连接配置"); await Until(() => window.OwnedWindows.Count > 0); window.OwnedWindows.Single().UpdateLayout(); window.OwnedWindows.Single().CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "metadata-import-preview.png")); Click(window.OwnedWindows.Single(), "确认");
            await Until(() => reloads == 1); Assert.Equal(2, store.Load().Length); Assert.Equal(original, store.Load().Single(p => p.Id == original.Id)); Assert.Equal(0, vault.Reads + vault.Writes + vault.Deletes);
        }
        finally { window.Close(); owner.Close(); await showing; Directory.Delete(dir, true); }
    }
}
