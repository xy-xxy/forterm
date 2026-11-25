using System.Diagnostics;
using System.Reflection;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Media;
using Forterm.Core;

namespace Forterm.Studio;

internal static class Program
{
    internal static readonly Channel<LaunchPlan> LaunchInbox = Channel.CreateBounded<LaunchPlan>(128);
    static readonly LaunchGate Gate = new();
    static readonly Stopwatch Clock = Stopwatch.StartNew();

    /// <summary>Accepts one launch (initial argv or one forwarded by a later entry).
    /// Identical controller launches within three seconds are acknowledged but not queued.</summary>
    internal static bool Accept(string[] args, bool initial = false)
    {
        var plan = LaunchPlan.Parse(args);
        if (!Gate.Accept(plan, Clock.Elapsed))
        {
            if (plan.Connection != null) plan.Connection.Password = "";
            return true;
        }
        if (initial) plan.NewWindow = false;
        if (LaunchInbox.Writer.TryWrite(plan)) return true;
        if (plan.Connection != null) plan.Connection.Password = "";
        return false;
    }

    internal static (InstanceBroker? Primary, bool Forwarded) StartBroker(string dataDirectory, string[] args, Func<string[], bool> accept) =>
        InstanceBroker.StartAsync(InstanceBroker.ForDataDirectory(dataDirectory), args, accept, CancellationToken.None, acrossSessions: true, queue: true).GetAwaiter().GetResult();

    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--runtime-check"])
        {
            using var bitmap = new SkiaSharp.SKBitmap(32, 32);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                Version = StudioPaths.Version,
                Edition = "Studio",
                Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                Skia = bitmap.Width,
                TerminalEnginePatch = PatchId(typeof(XTerm.Terminal).Assembly),
                TerminalControlPatch = PatchId(typeof(Iciclecreek.Terminal.TerminalControl).Assembly),
                VisibleWindowsCreated = 0,
            }));
            return 0;
        }
        var initial = LaunchPlan.Parse(args);
        if (initial.Connection != null) initial.Connection.Password = "";
        if (initial.Diagnose)
        {
            Console.WriteLine(initial.ToString());
            Array.Clear(args);
            return initial.Error.Length == 0 ? 0 : 2;
        }
        try
        {
            (InstanceBroker? Primary, bool Forwarded) result;
            try
            {
                result = StartBroker(StudioPaths.DataDirectory, args, envelope => Accept(envelope));
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or TimeoutException or UnauthorizedAccessException)
            {
                // The running instance did not acknowledge. Report failure to the entry
                // (and so to the controller) instead of opening a second, competing primary.
                Console.Error.WriteLine("Forterm: 现有实例没有确认转交，请重新从控制器发起连接。");
                return 4;
            }
            if (result.Forwarded) return 0;
            using var broker = result.Primary!;
            Accept(args, true);
            Array.Clear(args);
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(Array.Empty<string>());
            return 0;
        }
        finally
        {
            Array.Clear(args);
            Gate.Dispose();
        }
    }

    static string PatchId(Assembly assembly) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>().SingleOrDefault(a => a.Key == "FortermPatchId")?.Value ?? "";

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().With(Fonts(OperatingSystem.IsWindows())).LogToTrace();

    /// <summary>Windows: Chinese text that the font in use does not have comes from the font for simplified Chinese. Left
    /// to the system it may be one for traditional Chinese or Japanese, with other forms and punctuation.</summary>
    internal static FontManagerOptions Fonts(bool windows) => new()
    {
        FontFallbacks = !windows ? null : new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimSun" }
            .Select(f => new FontFallback { FontFamily = new FontFamily(f), UnicodeRange = UnicodeRange.Parse("U+2E80-2FDF, U+3000-303F, U+31C0-31EF, U+3400-4DBF, U+4E00-9FFF, U+F900-FAFF, U+FF00-FFEF") }).ToArray(),
    };
}
