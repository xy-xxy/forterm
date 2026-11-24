using Avalonia.Controls;
using Avalonia.Platform.Storage;
namespace Forterm.Desktop;

public sealed record PickedLocalFile(string Path, string Name);
public interface IFilePickers
{
    Task<PickedLocalFile?> UploadAsync(Window owner);
    Task<string?> DownloadAsync(Window owner, string name);
}
public sealed class NativeFilePickers(string uploadTitle = "上传文件", string downloadTitle = "下载文件") : IFilePickers
{
    public async Task<PickedLocalFile?> UploadAsync(Window owner)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = uploadTitle, AllowMultiple = false }); if (files.Count == 0) return null;
        try { return new(files[0].TryGetLocalPath() ?? throw new IOException("安全上传需要本机普通文件路径"), files[0].Name); } finally { foreach (var file in files) file.Dispose(); }
    }
    public async Task<string?> DownloadAsync(Window owner, string name)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = downloadTitle, SuggestedFileName = name, ShowOverwritePrompt = true });
        if (file == null) return null; try { return file.TryGetLocalPath() ?? throw new IOException("安全下载需要本机磁盘路径，不直接截断提供器流"); } finally { file.Dispose(); }
    }
}
