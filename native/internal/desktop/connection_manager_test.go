package desktop

import (
	"bytes"
	"encoding/json"
	"fmt"
	"image/png"
	"os"
	"path/filepath"
	"strings"
	"sync/atomic"
	"testing"
	"time"

	"forterm/native/internal/launch"
	"forterm/native/internal/settings"
	"forterm/native/internal/workspace"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/canvas"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/driver/desktop"
	ftest "fyne.io/fyne/v2/test"
	"fyne.io/fyne/v2/widget"
)

type managerSpyVault struct{ reads atomic.Int64 }

func (v *managerSpyVault) Get(string) ([]byte, error) {
	v.reads.Add(1)
	return nil, fmt.Errorf("synthetic secret disabled")
}
func (v *managerSpyVault) Set(string, []byte) error { return fmt.Errorf("disabled") }
func (v *managerSpyVault) Delete(string) error      { return nil }
func managerFixture(t *testing.T, n int) (*App, *managerSpyVault) {
	t.Helper()
	ui, _ := uploadRouteUI(t)
	v := &managerSpyVault{}
	data := settings.Data{Version: 1, KnownHosts: map[string]string{}}
	groups := []string{"开发环境", "测试环境", "生产环境", "归档"}
	protocols := []string{"ssh", "sftp", "ftp"}
	for i := 0; i < n; i++ {
		data.Profiles = append(data.Profiles, settings.Profile{ID: fmt.Sprintf("synthetic-%04d", i), Title: fmt.Sprintf("演示连接 %04d", i), Group: groups[i%4], Protocol: protocols[i%3], Host: fmt.Sprintf("node-%04d.example.invalid", i), Port: 22, Username: "fixture", Auth: "password"})
	}
	for i := 0; i < 3; i++ {
		data.Accounts = append(data.Accounts, settings.Account{ID: fmt.Sprint(i), Name: fmt.Sprintf("合成入口 %d", i+1), Username: "fixture", PortalURL: fmt.Sprintf("https://portal-%d.example.invalid", i+1)})
	}
	dir := t.TempDir()
	raw, _ := json.Marshal(data)
	if err := os.WriteFile(filepath.Join(dir, "config.json"), raw, 0600); err != nil {
		t.Fatal(err)
	}
	a := &App{diagnostic: true, UI: ui, Window: ui.NewWindow("Forterm synthetic layout"), Store: settings.Open(dir, v), model: workspace.New(), Status: widget.NewLabel(""), gate: launch.NewGate(), trusted: map[string]string{}}
	a.Tabs = container.NewDocTabs(container.NewTabItem("连接管理", a.startPage()))
	a.connectionsTab = a.Tabs.Items[0]
	a.miscTabs = append(a.miscTabs, a.connectionsTab)
	a.initChrome()
	a.Window.Resize(fyne.NewSize(1440, 900))
	t.Cleanup(a.Close)
	return a, v
}
func TestConnectionManagerVirtualRowsFilterAndNoAutomaticSecretReads(t *testing.T) {
	a, v := managerFixture(t, 1000)
	m := a.connections
	if !a.profileTree.IsBranch("") || len(a.profileTree.ChildUIDs("")) != 4 {
		t.Fatal("group tree has no traversable root")
	}
	if len(m.rows) != 1000 {
		t.Fatal("metadata missing")
	}
	// Force full software traversal, then jump to the last row and recycle.
	a.Window.Canvas().Capture()
	m.table.ScrollTo(widget.TableCellID{Row: 999, Col: 1})
	a.Window.Canvas().Capture()
	if findPickerObject(a.navigation, func(o fyne.CanvasObject) bool { l, ok := o.(*widget.Label); return ok && l.Text == "开发环境" }) == nil {
		t.Fatal("rendered group missing from navigation")
	}
	if m.createdCells > 600 {
		t.Fatalf("table built entire dataset: %d templates", m.createdCells)
	}
	m.selectGroup("生产环境")
	if len(m.rows) != 250 {
		t.Fatal(len(m.rows))
	}
	m.protocol = "sftp"
	m.filter()
	for _, p := range m.rows {
		if p.Protocol != "sftp" || p.Group != "生产环境" {
			t.Fatal("filter crossed source group/protocol")
		}
	}
	m.search.SetText("NO-MATCH")
	if len(m.rows) != 0 || !m.empty.Visible() {
		t.Fatal("empty search not visible")
	}
	m.selectProfile("synthetic-0999")
	if m.selectedID != "synthetic-0999" || len(m.rows) != 1000 {
		t.Fatal("tree selection did not reveal correct row")
	}
	if len(a.sessions) != 0 || v.reads.Load() != 0 {
		t.Fatal("layout/filter triggered connection or credential read")
	}
}
func TestConnectionManagerRecycledActionsUseCurrentProfile(t *testing.T) {
	a, v := managerFixture(t, 2)
	c := newConnectionCell(a.connections)
	c.set(a.connections.profiles[0], 5)
	c.set(a.connections.profiles[1], 5)
	expected := c.profile.Host
	ftest.Tap(c.actions.Objects[1].(*widget.Button))
	popup := a.Window.Canvas().Overlays().Top()
	if popup == nil {
		t.Fatal("edit not shown")
	}
	found := findPickerObject(popup, func(o fyne.CanvasObject) bool { e, ok := o.(*formEntry); return ok && e.Text == expected })
	if found == nil {
		t.Fatal("recycled edit retained earlier profile")
	}
	if v.reads.Load() != 0 {
		t.Fatal("editing metadata read vault")
	}
}
func TestConnectionManagerThemesSmallAndScaledSoftwareSnapshots(t *testing.T) {
	a, v := managerFixture(t, 1000)
	output := os.Getenv("FORTERM_UI_EVIDENCE")
	cases := []struct {
		name, mode string
		size       fyne.Size
		scale      float32
	}{{"manager-dark-1440", "dark", fyne.NewSize(1440, 900), 1}, {"manager-light-1440", "light", fyne.NewSize(1440, 900), 1}, {"manager-dark-1050", "dark", fyne.NewSize(1050, 650), 1}, {"manager-dark-150percent", "dark", fyne.NewSize(700, 433), 1.5}}
	for _, tc := range cases {
		a.SetAppearance(tc.mode)
		a.Window.Canvas().(interface{ SetScale(float32) }).SetScale(tc.scale)
		a.Window.Resize(tc.size)
		m := a.connections
		m.table.ScrollToTop()
		m.resizeColumns(m.Size().Width)
		picture := a.Window.Canvas().Capture()
		if picture.Bounds().Dx() != int(tc.size.Width*tc.scale) {
			t.Fatalf("unexpected scaled raster: %v", picture.Bounds())
		}
		if a.Tabs.Size().Width < 320 || a.navigation.Size().Width > 420 || a.Tabs.Position().X+a.Tabs.Size().Width > a.chromeBody.Size().Width+1 {
			t.Fatalf("columns not reachable in %s", tc.name)
		}
		if m.table.Size().Height < 150 || m.tools.Size().Width <= 0 || m.more.Position().X+m.more.Size().Width > m.Size().Width+1 {
			t.Fatalf("table/actions missing in %s", tc.name)
		}
		if output != "" {
			if err := os.MkdirAll(output, 0700); err != nil {
				t.Fatal(err)
			}
			f, err := os.Create(filepath.Join(output, tc.name+".png"))
			if err != nil {
				t.Fatal(err)
			}
			err = png.Encode(f, picture)
			f.Close()
			if err != nil {
				t.Fatal(err)
			}
			report, _ := json.MarshalIndent(map[string]any{"version": Version, "softwareDriverOnly": true, "syntheticMetadataOnly": true, "physicalScenarioScale": tc.scale, "logicalWidth": tc.size.Width, "logicalHeight": tc.size.Height, "tableRows": len(m.rows), "createdCellTemplates": m.createdCells, "leftWidth": a.navigation.Size().Width, "tableWidth": m.table.Size().Width, "tableHeight": m.table.Size().Height, "secretReads": v.reads.Load(), "networkConnections": len(a.sessions), "fonts": SystemUIFontSnapshot()}, "", "  ")
			os.WriteFile(filepath.Join(output, tc.name+".json"), report, 0600)
		}
	}

	// Horizontal scroll must expose the row actions at the 150% logical width.
	a.connections.table.ScrollTo(widget.TableCellID{Row: 0, Col: 5})
	actions := findPickerObject(a.connections.table, func(o fyne.CanvasObject) bool {
		c, ok := o.(*connectionCell)
		return ok && c.column == 5 && c.actions != nil && c.actions.Visible()
	})
	if actions == nil {
		t.Fatal("row actions unreachable at scaled narrow width")
	}
	if output != "" {
		f, err := os.Create(filepath.Join(output, "manager-150percent-actions.png"))
		if err != nil {
			t.Fatal(err)
		}
		err = png.Encode(f, a.Window.Canvas().Capture())
		f.Close()
		if err != nil {
			t.Fatal(err)
		}
	}
	a.ShowAccounts()
	if a.Tabs.Selected() != a.accountsTab || a.Window.Canvas().Overlays().Top() != nil {
		t.Fatal("accounts did not open in main workbench")
	}
	if output != "" {
		f, _ := os.Create(filepath.Join(output, "accounts-small.png"))
		if f != nil {
			png.Encode(f, a.Window.Canvas().Capture())
			f.Close()
		}
	}
	a.ShowLibrary()
	a.ToggleNavigation()
	if a.navigationVisible {
		t.Fatal("navigation did not collapse")
	}
	a.ToggleNavigation()
	if !a.navigationVisible {
		t.Fatal("navigation did not restore")
	}
	if v.reads.Load() != 0 {
		t.Fatal("appearance or accounts page read secrets")
	}
	if strings.Contains(a.connections.summary.Text, "password") {
		t.Fatal("unexpected secret in summary")
	}
}

func TestUnifiedWorkspaceVisualsKeepDockStateAndUnknownMetrics(t *testing.T) {
	a, v := managerFixture(t, 20)
	w := a.AddSyntheticWorkspacePreview()
	if w == nil {
		t.Fatal("offline fixture not created")
	}
	pump := func(check func() bool) {}
	// Retrieve the serialized software dispatcher installed by managerFixture.
	driver := a.UI.Driver().(*uploadRouteDriver)
	pump = func(check func() bool) {
		until := time.After(4 * time.Second)
		for !check() {
			select {
			case fn := <-driver.calls:
				fn()
			case <-until:
				t.Fatal("preview output not rendered")
			case <-time.After(time.Millisecond):
			}
		}
	}
	pump(func() bool { _, rendered, _ := w.Term.OutputStats(); return rendered > 0 })
	originalTerminal, originalEditor := w.Term, w.documents[0].Editor
	output := os.Getenv("FORTERM_UI_EVIDENCE")
	for _, tc := range []struct {
		name, mode, dock string
		size             fyne.Size
		scale            float32
	}{{"workspace-dark-bottom", "dark", "bottom", fyne.NewSize(1440, 900), 1}, {"workspace-light-right", "light", "right", fyne.NewSize(1440, 900), 1}, {"workspace-dark-left-1050", "dark", "left", fyne.NewSize(1050, 650), 1}, {"workspace-dark-150percent", "dark", "bottom", fyne.NewSize(700, 433), 1.5}} {
		a.SetAppearance(tc.mode)
		a.Window.Canvas().(interface{ SetScale(float32) }).SetScale(tc.scale)
		a.Window.Resize(tc.size)
		if tc.scale > 1 && a.navigationVisible {
			a.ToggleNavigation()
		}
		w.editorDock = tc.dock
		w.editorDockRight = tc.dock != "bottom"
		w.rebuildEditorDock()
		w.MonitorToggle.SetChecked(true)
		image := a.Window.Canvas().Capture()
		if originalTerminal != w.Term || originalEditor != w.documents[0].Editor || !w.editorHost.Visible() || !w.fileSourcesRoot.Visible() || !w.Monitor.Content.Visible() {
			t.Fatalf("workspace capability lost in %s", tc.name)
		}
		if w.Monitor.cpuValue.Text != "—" || !strings.Contains(w.Monitor.sourceLabel.Text, "未获取") || w.Monitor.auto.Checked {
			t.Fatal("preview fabricated or automatically sampled metrics")
		}
		if output != "" {
			os.MkdirAll(output, 0700)
			f, err := os.Create(filepath.Join(output, tc.name+".png"))
			if err != nil {
				t.Fatal(err)
			}
			err = png.Encode(f, image)
			f.Close()
			if err != nil {
				t.Fatal(err)
			}
		}
	}
	if v.reads.Load() != 0 || w.Conn != nil || w.FileConn != nil {
		t.Fatal("visual preview connected or read credentials")
	}
}

func TestConnectionManagerTreeSelectionAndEmbeddedAccountsDoNotConnect(t *testing.T) {
	a, v := managerFixture(t, 1000)
	a.profileTree.OpenBranch("g:开发环境")
	a.profileTree.Select("g:开发环境")
	if a.connections.group != "开发环境" || len(a.connections.rows) != 250 {
		t.Fatal("tree did not filter table")
	}
	a.connections.table.Select(widget.TableCellID{Row: 1, Col: 1})
	a.connections.table.OnHighlighted(widget.TableCellID{Row: 2, Col: 1})
	output := os.Getenv("FORTERM_UI_EVIDENCE")
	if output != "" {
		os.MkdirAll(output, 0700)
		f, err := os.Create(filepath.Join(output, "manager-selected-tree.png"))
		if err != nil {
			t.Fatal(err)
		}
		err = png.Encode(f, a.Window.Canvas().Capture())
		f.Close()
		if err != nil {
			t.Fatal(err)
		}
	}
	a.ShowAccounts()
	if a.Tabs.Selected() != a.accountsTab {
		t.Fatal("accounts page not selected")
	}
	if v.reads.Load() != 0 || len(a.sessions) != 0 {
		t.Fatal("navigation opened network or credentials")
	}
}

func TestManagerNavigationKeyboardPersistenceAndAccessibleThemeIcons(t *testing.T) {
	a, v := managerFixture(t, 2)
	if !strings.Contains(a.navButton.AccessibilityLabel(), "连接导航") {
		t.Fatal("navigation icon has no useful accessible name")
	}
	dark := append([]byte(nil), a.navButton.Icon.Content()...)
	a.SetAppearance("light")
	light := a.navButton.Icon.Content()
	if bytes.Equal(dark, light) {
		t.Fatal("named icon stopped adapting to theme")
	}
	a.navDivider.TypedKey(&fyne.KeyEvent{Name: fyne.KeyRight})
	if a.navigationWidth != 288 || a.UI.Preferences().Float("navigationWidth") != 288 {
		t.Fatal("keyboard resize did not persist")
	}
	a.navDivider.TypedKey(&fyne.KeyEvent{Name: fyne.KeyHome})
	if a.navigationWidth != 160 {
		t.Fatal("minimum width inaccessible")
	}
	a.navDivider.TypedKey(&fyne.KeyEvent{Name: fyne.KeyEnd})
	if a.navigationWidth != 420 {
		t.Fatal("maximum width inaccessible")
	}
	a.navDivider.TypedKey(&fyne.KeyEvent{Name: fyne.KeyReturn})
	if a.navigationWidth != 280 {
		t.Fatal("reset width differs from connection page design")
	}
	if v.reads.Load() != 0 {
		t.Fatal("navigation read secrets")
	}
}

func TestVisualPreviewMetadataRejectsSecretPersistenceWithoutPanic(t *testing.T) {
	a, _ := managerFixture(t, 2)
	dir := t.TempDir()
	data := settings.Data{Version: 1, Profiles: []settings.Profile{{ID: "safe-fixture", Title: "Synthetic", Host: "fixture.invalid", Port: 22, Protocol: "ssh", Username: "fixture", Auth: "password"}}}
	raw, _ := json.Marshal(data)
	path := filepath.Join(dir, "config.json")
	if err := os.WriteFile(path, raw, 0600); err != nil {
		t.Fatal(err)
	}
	if !a.LoadSyntheticPreviewMetadata(dir) {
		t.Fatal("offline preview metadata rejected")
	}
	p := a.Store.Snapshot().Profiles[0]
	p.Title = "attempted preview edit"
	if _, err := a.Store.UpsertProfile(p, settings.SecretUpdate{Action: "clear"}, settings.SecretUpdate{Action: "clear"}, false); err == nil {
		t.Fatal("preview secret service unexpectedly accepted save")
	}
	got, _ := os.ReadFile(path)
	if string(got) != string(raw) {
		t.Fatal("rejected preview save rewrote fixture")
	}
	before := a.Store
	a.diagnostic = false
	if a.LoadSyntheticPreviewMetadata(dir) || a.Store != before {
		t.Fatal("normal app accepted preview-only metadata path")
	}
}

func TestNarrowConnectionToolbarMoreAlwaysOffersRefresh(t *testing.T) {
	a, _ := managerFixture(t, 2)
	a.Window.Resize(fyne.NewSize(700, 433))
	m := a.connections
	ftest.Tap(m.more)
	menu := findPickerObject(a.Window.Canvas().Overlays().Top(), func(o fyne.CanvasObject) bool {
		label, ok := o.(*canvas.Text)
		return ok && label.Text == "刷新连接列表"
	})
	if menu == nil {
		t.Fatal("narrow toolbar has no discoverable refresh action")
	}
	if m.more.Position().X < 0 || m.more.Position().X+m.more.Size().Width > m.Size().Width+1 {
		t.Fatal("more action is clipped")
	}
}

func TestConnectionRowPointerDownSelectsWithoutWaitingOrAuthentication(t *testing.T) {
	a, v := managerFixture(t, 2)
	m := a.connections
	c := newConnectionCell(m)
	c.set(m.rows[1], 1)
	c.MouseDown(&desktop.MouseEvent{Button: desktop.MouseButtonPrimary})
	if m.selectedID != m.rows[1].ID || a.Window.Canvas().Focused() != m.table {
		t.Fatal("row feedback waits for double-tap timeout")
	}
	if v.reads.Load() != 0 || len(a.sessions) != 0 {
		t.Fatal("selection initiated authentication")
	}
}
