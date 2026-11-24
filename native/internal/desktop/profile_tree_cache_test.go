package desktop

import (
	"encoding/json"
	"forterm/native/internal/settings"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	ftest "fyne.io/fyne/v2/test"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
	"os"
	"path/filepath"
	"testing"
)

func TestProfileIndexFilterOrderAndIndependentInput(t *testing.T) {
	input := []settings.Profile{{ID: "b", Title: "Second", Group: "z", Host: "second.invalid"}, {ID: "a", Title: "First", Group: "a", Host: "first.invalid", Username: "fixture"}, {ID: "c", Host: "third.invalid"}}
	nodes := buildProfileNodes(input, "")
	if len(nodes.children[""]) != 3 || nodes.children[""][0] != "g:a" || nodes.children["g:z"][0] != "p:b" {
		t.Fatal("group order or leaf identity changed")
	}
	input[0].Title = "mutated"
	if nodes.profiles["p:b"].Title != "Second" {
		t.Fatal("cached row aliases input metadata")
	}
	filtered := buildProfileNodes(input, "FIXTURE")
	if len(filtered.profiles) != 1 || filtered.profiles["p:a"].Host != "first.invalid" {
		t.Fatal("search fields or case folding changed")
	}
}
func TestSearchOpeningDoesNotOverwriteSavedCollapsedGroups(t *testing.T) {
	ui := ftest.NewApp()
	defer ui.Quit()
	ui.Settings().SetTheme(theme.DefaultTheme())
	dir := t.TempDir()
	data := settings.Data{Version: 1, Profiles: []settings.Profile{{ID: "a", Title: "fixture", Group: "z", Host: "fixture.invalid"}}}
	raw, _ := json.Marshal(data)
	os.WriteFile(filepath.Join(dir, "config.json"), raw, 0600)
	a := &App{UI: ui, Window: ui.NewWindow("profile cache"), Store: settings.Open(dir, diagnosticVault{})}
	ui.Preferences().SetBool("group-open-g:z", false)
	a.Window.SetContent(a.profileSection())
	a.Window.Resize(fyne.NewSize(300, 500))
	if a.profileTree.IsBranchOpen("g:z") {
		t.Fatal("saved collapsed state not restored")
	}
	a.navigationQuery = "fixture"
	a.refreshProfileTree()
	if !a.profileTree.IsBranchOpen("g:z") || ui.Preferences().Bool("group-open-g:z") {
		t.Fatal("search must open matches without persisting user preference")
	}
	a.navigationQuery = ""
	a.refreshProfileTree()
	if a.profileTree.IsBranchOpen("g:z") {
		t.Fatal("clearing search did not restore collapsed preference")
	}
	a.profileTree.OpenBranch("g:z")
	if !ui.Preferences().Bool("group-open-g:z") {
		t.Fatal("actual user group open no longer persists")
	}
}

func TestVirtualLibraryRowsStayBoundAfterRecycling(t *testing.T) {
	ui := ftest.NewApp()
	defer ui.Quit()
	ui.Settings().SetTheme(theme.DefaultTheme())
	a := &App{UI: ui, Window: ui.NewWindow("virtual library"), Store: settings.Open(t.TempDir(), diagnosticVault{})}
	row := newLibraryRow(a, func() {})
	row.setEntry(libraryEntry{header: true, group: "fixture group"})
	if !row.group.Visible() || row.controls.Visible() {
		t.Fatal("header row not switched")
	}
	row.setEntry(libraryEntry{profile: settings.Profile{ID: "one", Title: "first", Host: "first.invalid"}})
	row.setEntry(libraryEntry{profile: settings.Profile{ID: "two", Title: "second", Host: "second.invalid"}})
	if row.entry.profile.ID != "two" || row.connect.Text != "second" || row.group.Visible() || !row.controls.Visible() {
		t.Fatal("recycled row retained the previous profile")
	}
}
func TestConnectionPageAndPreferencesFitShortSoftwareWindow(t *testing.T) {
	ui := ftest.NewApp()
	defer ui.Quit()
	ui.Settings().SetTheme(theme.DefaultTheme())
	a := &App{UI: ui, Window: ui.NewWindow("small dialogs"), Store: settings.Open(t.TempDir(), diagnosticVault{}), Tabs: container.NewDocTabs(), Status: widget.NewLabel("")}
	a.Window.SetContent(a.Tabs)
	a.Window.Resize(fyne.NewSize(700, 412))
	a.ShowLibrary()
	if a.connectionsTab == nil || a.Tabs.Selected() != a.connectionsTab || a.Window.Canvas().Overlays().Top() != nil {
		t.Fatal("connection manager must use main workspace tab")
	}
	for _, show := range []func(){a.ShowPreferences} {
		show()
		popup := a.Window.Canvas().Overlays().Top()
		if popup == nil {
			t.Fatal("dialog not shown")
		}
		inner := findPickerObject(popup, func(o fyne.CanvasObject) bool { _, ok := o.(*widget.PopUp); return ok })
		if inner == nil {
			t.Fatal("modal popup not found inside overlay")
		}
		body := inner.(*widget.PopUp).Content
		position := ui.Driver().AbsolutePositionForObject(body)
		size := body.Size()
		canvas := a.Window.Canvas().Size()
		if position.X < 0 || position.Y < 0 || position.X+size.Width > canvas.Width+1 || position.Y+size.Height > canvas.Height+1 {
			t.Fatalf("dialog outside short window: %v %v canvas%v", position, size, canvas)
		}
		popup.Hide()
	}
}
