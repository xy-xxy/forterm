namespace Forterm.Tests;
internal static class UiEvidence
{
    public static string DirectoryPath
    {
        get
        {
            string version = typeof(Forterm.Desktop.App).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion.Split('+')[0];
            string path = Path.Combine(TransportTests.FindRoot(), "evidence", "ui-" + version); Directory.CreateDirectory(path); return path;
        }
    }
}
