using System.Diagnostics;
using Forterm.Core;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>The controller → entry → running instance chain, without a GUI.</summary>
public sealed class EntryChainTests
{
    [Fact]
    public void IdenticalControllerLaunchesWithinThreeSecondsOpenOneSession()
    {
        while (Program.LaunchInbox.Reader.TryRead(out _)) { }
        string[] Args() => ["--entry=xshell", "-newtab", "第一次", "-url", "ssh://ticket:once@dedup.example:12024"];
        Assert.True(Program.Accept(Args()));
        Assert.True(Program.Accept(["--entry=xshell", "-newtab", "改了标签", "-url", "ssh://ticket:once@dedup.example:12024"]));
        Assert.True(Program.LaunchInbox.Reader.TryRead(out var first));
        Assert.Equal("第一次", first!.Connection!.Title);
        Assert.False(Program.LaunchInbox.Reader.TryRead(out _));
        // A different ticket is a different launch.
        Assert.True(Program.Accept(["--entry=xshell", "-url", "ssh://ticket:other@dedup.example:12024"]));
        Assert.True(Program.LaunchInbox.Reader.TryRead(out _));
    }

    /// <summary>Synchronous like Program.Main: the primary's mutex must be released by the
    /// thread that acquired it.</summary>
    [Fact]
    public void SecondInstanceForwardsArgumentsToThePrimary()
    {
        // A plain thread has no SynchronizationContext, like Program.Main before Avalonia starts;
        // the mutex is acquired and released on this same thread.
        Exception? failure = null;
        var thread = new Thread(() => { try { ForwardOnce(); } catch (Exception e) { failure = e; } });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "broker test did not finish within 30 seconds");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    static void ForwardOnce()
    {
        string name = "studio-test-" + Guid.NewGuid().ToString("N");
        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (primary, forwarded) = InstanceBroker.StartAsync(name, ["--entry=xshell"], a => received.TrySetResult(a.ToArray()), CancellationToken.None).GetAwaiter().GetResult();
        Assert.False(forwarded);
        using (primary)
        {
            string[] second = ["--entry=xftp", "-url", "sftp://ticket:pw@bastion.example:12024"];
            var (none, forwardedSecond) = Task.Run(() => InstanceBroker.StartAsync(name, second, _ => true, CancellationToken.None)).GetAwaiter().GetResult();
            Assert.Null(none);
            Assert.True(forwardedSecond);
            Assert.True(received.Task.Wait(TimeSpan.FromSeconds(10)));
            Assert.Equal(second, received.Task.Result);
        }
    }

    /// <summary>A launch from Finder and one from a terminal, or from two terminals, are different POSIX
    /// sessions, which .NET's named mutexes do not cross by themselves. They still meet one instance.</summary>
    [Fact]
    public void ALaunchFromAnotherPosixSessionReachesTheRunningInstance()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "POSIX sessions; on Windows all launches of a logon session share one mutex.");
        string data = Paths.TempDirectory();
        Exception? failure = null;
        var thread = new Thread(() => { try { ForwardFromAnotherSession(data); } catch (Exception e) { failure = e; } });
        thread.Start();
        bool finished = thread.Join(TimeSpan.FromSeconds(60));
        Directory.Delete(data, true);
        Assert.True(finished, "broker test did not finish within 60 seconds");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    static void ForwardFromAnotherSession(string data)
    {
        string configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug";
        string app = Path.Combine(Paths.Root, "src", "Forterm.Studio", "bin", configuration, "net10.0", "Forterm");
        Assert.True(File.Exists(app), app);
        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (primary, _) = Program.StartBroker(data, [], a => received.TrySetResult(a.ToArray()));
        Assert.NotNull(primary);
        using (primary)
        {
            string[] args = ["--entry=xshell", "-newtab", "另一个会话", "-url", "ssh://ticket:pw@bastion.example:12024"];
            var start = new ProcessStartInfo(Paths.Python) { UseShellExecute = false };
            foreach (string arg in new[] { "-I", "-c", "import os,sys; os.setsid(); os.execv(sys.argv[1],sys.argv[1:])", app }.Concat(args)) start.ArgumentList.Add(arg);
            start.Environment["FORTERM_STUDIO_DATA"] = data;
            start.Environment["DOTNET_ROOT"] = Path.GetDirectoryName(Environment.ProcessPath)!;
            if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root) start.Environment["DOTNET_ROOT"] = root;
            using var second = Process.Start(start)!;
            try
            {
                Assert.True(second.WaitForExit(20_000), "the second launch became an instance of its own");
                Assert.Equal(0, second.ExitCode);
                Assert.True(received.Task.Wait(TimeSpan.FromSeconds(5)));
                Assert.Equal(args, received.Task.Result);
            }
            finally { if (!second.HasExited) second.Kill(true); }
        }
    }

    /// <summary>xshell/xftp must wait for the main program and return its exit code (0.1.1 behaviour).</summary>
    [Fact]
    public async Task EntryWaitsForMainProgramAndForwardsExitCode()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses a POSIX shell script as the stand-in main program; the Windows run is covered by the packaged entry check.");
        string dir = PrepareEntry("xftp");
        try
        {
            File.WriteAllText(Path.Combine(dir, "Forterm"), "#!/bin/sh\nprintf '%s\\n' \"$@\" > \"$(dirname \"$0\")/args.txt\"\nsleep 1\nexit 7\n");
            File.SetUnixFileMode(Path.Combine(dir, "Forterm"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var clock = Stopwatch.StartNew();
            var (code, _) = await Run(Path.Combine(dir, "xftp"), "-url", "sftp://ticket:p%40ss@bastion.example:12024");
            Assert.Equal(7, code);
            Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(900), "entry returned before the main program finished");
            Assert.Equal(["--entry=xftp", "-url", "sftp://ticket:p%40ss@bastion.example:12024"], File.ReadAllLines(Path.Combine(dir, "args.txt")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task EntryWithoutMainProgramFailsInsteadOfPretendingSuccess()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "POSIX apphost name; see EntryWaitsForMainProgramAndForwardsExitCode.");
        string dir = PrepareEntry("xshell");
        try { Assert.Equal(2, (await Run(Path.Combine(dir, "xshell"), "-url", "ssh://t:p@h:22")).Code); }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>Copies the built launcher and renames its apphost, as packaging does for xshell/xftp.</summary>
    static string PrepareEntry(string name)
    {
        string configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug";
        string built = Path.Combine(Paths.Root, "src", "Forterm.Launcher", "bin", configuration, "net10.0");
        Assert.True(File.Exists(Path.Combine(built, "Forterm.Launcher.dll")), "launcher not built at " + built);
        string dir = Paths.TempDirectory();
        foreach (string file in Directory.GetFiles(built)) File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
        File.Copy(Path.Combine(built, "Forterm.Launcher"), Path.Combine(dir, name));
        File.SetUnixFileMode(Path.Combine(dir, name), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return dir;
    }

    static async Task<(int Code, string Output)> Run(string executable, params string[] args)
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        string output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, output);
    }
}
