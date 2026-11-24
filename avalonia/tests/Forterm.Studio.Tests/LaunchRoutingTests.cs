using Forterm.Core;
using Forterm.Studio.Launch;
using Xunit;

namespace Forterm.Studio.Tests;

public sealed class LaunchRoutingTests
{
    static LaunchRoute Route(params string[] args)
    {
        var plan = LaunchPlan.Parse(args);
        Assert.Equal("", plan.Error);
        return LaunchRouter.Route(plan.Connection!);
    }

    [Fact]
    public void XshellSshOpensTerminalWorkspaceNamedByNewTab()
    {
        var route = Route("--entry=xshell", "-newtab", "开发环境", "-url", "ssh://ticket:p%40ss@bastion.example:12024");
        Assert.Equal(WorkspaceKind.Terminal, route.Kind);
        Assert.Equal("开发环境", route.Title);
    }

    [Fact]
    public void BareUrlAndCaseInsensitiveOptionsStillRoute()
    {
        Assert.Equal(WorkspaceKind.Terminal, Route("--entry=xshell", "ssh://ticket:pw@bastion.example:22").Kind);
        Assert.Equal("bastion.example", Route("--entry=xshell", "-URL", "ssh://ticket:pw@bastion.example:22").Title);
    }

    [Fact]
    public void XftpTurnsSshUrlIntoSftpWorkspace()
    {
        Assert.Equal(WorkspaceKind.Sftp, Route("--entry=xftp", "-url", "ssh://ticket:pw@bastion.example:12024").Kind);
        Assert.Equal(WorkspaceKind.Sftp, Route("--entry=xftp", "-url", "sftp://ticket:pw@bastion.example:12024").Kind);
    }

    [Fact]
    public void XftpKeepsRealFtp()
    {
        var plan = LaunchPlan.Parse(["--entry=xftp", "-url", "ftp://user:pw@files.example"]);
        Assert.Equal("ftp", plan.Connection!.Protocol);
        Assert.Equal(21, plan.Connection.Port);
        Assert.Equal(WorkspaceKind.Ftp, LaunchRouter.Route(plan.Connection).Kind);
    }

    [Fact]
    public void ControllerTitlesAreBoundedAndPrintable()
    {
        Assert.Equal("ab", LaunchRouter.CleanTitle("  a\u0007b\r\n "));
        Assert.Equal(80, LaunchRouter.CleanTitle(new string('名', 200)).Length);
    }

    [Fact]
    public void UnknownArgumentsAreRejectedNotIgnored()
    {
        var plan = LaunchPlan.Parse(["--entry=xshell", "-url", "ssh://t:p@h:22", "-unknown"]);
        Assert.NotEqual("", plan.Error);
        Assert.Null(plan.Connection);
    }
}
