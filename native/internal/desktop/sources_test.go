package desktop

import (
	"context"
	"forterm/native/internal/workspace"
	"testing"

	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	ftest "fyne.io/fyne/v2/test"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
)

func TestAttachFileSessionReopensPaneAndKeepsTerminal(t *testing.T) {
	ui := ftest.NewApp()
	defer ui.Quit()
	ui.Settings().SetTheme(theme.DefaultTheme())
	a := &App{UI: ui, Window: ui.NewWindow("sources"), Tabs: container.NewDocTabs(), model: workspace.New(), Status: widget.NewLabel("")}
	ui.Preferences().SetBool("filesVisible", false)
	owner := NewWorkspace(a, "terminal", "ssh")
	source := NewWorkspace(a, "files", "sftp")
	if !source.filesPaneEnabled {
		t.Fatal("explicit file session inherited collapsed file pane")
	}
	defer owner.cancel()
	defer source.cancel()
	a.Tabs.Append(owner.Tab)
	a.Tabs.Append(source.Tab)
	a.Tabs.Select(owner.Tab)
	a.Window.SetContent(a.Tabs)
	a.Window.Resize(fyne.NewSize(1000, 700))
	ctx, cancel := context.WithCancel(owner.Context)
	defer cancel()
	// Cached view avoids network traffic; association is purely a UI action.
	view := &Workspace{Context: ctx, cancel: cancel, filePane: container.NewStack(widget.NewLabel("files"))}
	owner.sourceViews[source.ID] = view
	terminal := owner.Term
	if err := owner.AttachFileSource(source.ID); err != nil {
		t.Fatal(err)
	}
	if !owner.filesPaneEnabled || !view.filesVisible || !owner.fileSourcesRoot.Visible() {
		t.Fatal("selected file session did not appear alongside terminal")
	}
	if owner.Term != terminal || a.Tabs.Selected() != owner.Tab || owner.Context.Err() != nil || source.Context.Err() != nil {
		t.Fatal("association changed terminal or closed a session")
	}
	for _, tab := range a.Tabs.Items {
		if tab == source.Tab {
			t.Fatal("selected file session still has a duplicate top tab")
		}
	}
	owner.DetachFileSource(source.ID)
	found := false
	for _, tab := range a.Tabs.Items {
		found = found || tab == source.Tab
	}
	if !found || source.Context.Err() != nil {
		t.Fatal("detaching did not restore the live file session tab")
	}
}
