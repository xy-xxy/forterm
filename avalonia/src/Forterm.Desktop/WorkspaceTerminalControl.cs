using Avalonia.Controls.Primitives;
using Iciclecreek.Terminal;
namespace Forterm.Desktop;

/// <summary>A workspace owns the terminal's lifetime. Tabs, library navigation and
/// source associations detach/reparent the visual control without ending the session.</summary>
public sealed class WorkspaceTerminalControl : TerminalControl
{
    protected override Type StyleKeyOverride => typeof(TerminalControl);
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        // The upstream detach hook otherwise cancels the external PTY reader and unbinds input.
        // Keep the reparent guard for the workspace lifetime; SessionPane explicitly disposes it.
        BeginReparent();
    }
}
