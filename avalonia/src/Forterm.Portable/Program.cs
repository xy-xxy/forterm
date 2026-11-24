using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace Forterm.Portable;
internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")] static extern int MessageBox(IntPtr window, string text, string caption, uint flags);
    [STAThread] static int Main(string[] args)
    {
        string? directory = null;
        try
        {
            var assembly = Assembly.GetExecutingAssembly(); string expected = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "PayloadSHA256").Value!;
            using var payload = assembly.GetManifestResourceStream("Forterm.Payload.zip") ?? throw new IOException("missing payload");
            directory = PortableRuntime.CreateDirectory(); var hashes = PortableRuntime.Extract(payload, expected, directory);
            if (args.SequenceEqual(new[] { "--portable-runtime-check" }))
            {
                Console.WriteLine(JsonSerializer.Serialize(new { PayloadSHA256 = expected, Files = hashes, VisibleWindowsCreated = 0, ChildProcessesCreated = 0 })); return 0;
            }
            var start = PortableRuntime.StartInfo(Path.Combine(directory, "Forterm.exe"), args);
            using var child = Process.Start(start) ?? throw new IOException("child not started");
            start.ArgumentList.Clear(); Array.Clear(args); child.WaitForExit(); return child.ExitCode;
        }
        catch
        {
            const string message = "Forterm便携运行未完成，请使用完整四入口ZIP解压版检查。不会自动安装运行库或重新使用临时票据。";
            if (OperatingSystem.IsWindows()) MessageBox(IntPtr.Zero, message, "Forterm Avalonia", 0x10); else Console.Error.WriteLine(message); return 1;
        }
        finally
        {
            Array.Clear(args); if (directory != null) { try { Directory.Delete(directory, true); } catch { /* Keep only this owned runtime if the OS still holds a module. Never remove another directory. */ } }
        }
    }
}
