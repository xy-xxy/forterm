using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;

namespace Forterm.Studio.Services;

/// <summary>"自定义背景图片": the picture is copied into the data directory, made no wider than a 4K screen and written
/// anew as PNG (which also leaves out what a photo carries besides its pixels, such as where it was taken). The original
/// is never read again, and the copy is not part of a backup.</summary>
public static partial class Backdrop
{
    public const long MostBytes = 32L * 1024 * 1024;
    /// <summary>What may be decoded at all: a picture of more pixels takes too much memory to read.</summary>
    public const int MostPixels = 40_000_000, MostSide = 16_384;
    /// <summary>What is kept, either way: no screen shows more.</summary>
    public const int Widest = 3840;

    [GeneratedRegex("^background-[0-9a-f]{8}\\.png\\z")] private static partial Regex Name();
    public static bool IsName(string? name) => name != null && Name().IsMatch(name);

    static (string Path, Bitmap? Image)? cache;

    /// <summary>The picture of these preferences, or null (none, or it cannot be read: then there is none).</summary>
    public static Bitmap? Load(string? directory, StudioPreferences preferences)
    {
        if (directory == null || preferences.BackgroundImage.Length == 0) return null;
        string path = Path.Combine(directory, preferences.BackgroundImage);
        if (cache is { } kept && kept.Path == path) return kept.Image;
        Bitmap? image = null;
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length <= MostBytes) image = new Bitmap(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException) { image = null; }
        cache = (path, image);
        return image;
    }

    /// <summary>Copies a picture the user chose; returns the name of the copy. Throws <see cref="IOException"/> with the
    /// reason when it cannot be used.</summary>
    public static Task<string> ImportAsync(string source, string directory) => Task.Run(() => Import(source, directory));

    static string Import(string source, string directory)
    {
        var file = new FileInfo(source);
        if (!file.Exists) throw new IOException("找不到这个文件");
        if (file.Length > MostBytes) throw new IOException($"图片超过 {MostBytes / 1024 / 1024} MiB");
        // Read once: what is checked is what is decoded, even if the file is replaced meanwhile.
        byte[] bytes;
        using (var input = File.OpenRead(source))
        {
            if (input.Length > MostBytes) throw new IOException($"图片超过 {MostBytes / 1024 / 1024} MiB");
            bytes = new byte[input.Length];
            input.ReadExactly(bytes);
        }
        int width, height;
        using (var stream = new MemoryStream(bytes, writable: false))
        using (var codec = SkiaSharp.SKCodec.Create(stream))
        {
            if (codec == null) throw new IOException("不是能识别的图片（可以用 PNG、JPEG、BMP、GIF、WebP）");
            (width, height) = (codec.Info.Width, codec.Info.Height);
        }
        if (width <= 0 || height <= 0) throw new IOException("图片没有内容");
        if (width > MostSide || height > MostSide || (long)width * height > MostPixels)
            throw new IOException($"图片太大（{width}×{height}），最多 {MostPixels / 1_000_000} 百万像素");
        // No side longer than Widest.
        int kept = (int)Math.Min(width, Math.Min(Widest, (long)Widest * width / height));
        Bitmap decoded;
        using (var stream = new MemoryStream(bytes, writable: false))
            decoded = kept < width ? Bitmap.DecodeToWidth(stream, Math.Max(1, kept), BitmapInterpolationMode.HighQuality) : new Bitmap(stream);
        using (decoded)
        {
            Directory.CreateDirectory(directory);
            string name = "background-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant() + ".png";
            string path = Path.Combine(directory, name), temp = path + ".tmp";
            try
            {
                using (var output = File.Create(temp)) decoded.Save(output);
                File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return name;
        }
    }

    /// <summary>Removes the copies that are no longer chosen.</summary>
    public static void Forget(string? directory, string keep)
    {
        if (directory == null || !Directory.Exists(directory)) return;
        foreach (string path in Directory.EnumerateFiles(directory, "background-*.png"))
        {
            string name = Path.GetFileName(path);
            if (name == keep || !IsName(name)) continue;
            try
            {
                // Not disposed: a window may still be drawing it.
                if (cache is { } kept && kept.Path == path) cache = null;
                File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
