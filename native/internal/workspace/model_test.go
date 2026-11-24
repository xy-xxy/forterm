package workspace

import "testing"

func TestExplicitFileSourcesHideOnlyReferencedFileTabs(t *testing.T) {
	m := New()
	for _, s := range []Session{{ID: "ssh", Protocol: "ssh", Open: true}, {ID: "sftp", Protocol: "sftp", Open: true}, {ID: "ftp", Protocol: "ftp", Open: true}, {ID: "other", Protocol: "ssh", Open: true}} {
		m.Add(s)
	}
	if e := m.Attach("ssh", "sftp"); e != nil {
		t.Fatal(e)
	}
	m.Attach("ssh", "ftp")
	m.Select("ssh", "sftp")
	visible := m.Visible()
	if len(visible) != 2 || visible[0].ID != "ssh" || visible[1].ID != "other" {
		t.Fatal("inactive referenced source was not hidden")
	}
	if m.OwnerFor("ftp") != "ssh" {
		t.Fatal("external focus did not resolve to owner")
	}
	m.Attach("other", "sftp")
	m.Detach("ssh", "sftp")
	if len(m.Referencing("sftp")) != 1 {
		t.Fatal("shared reference lost")
	}
	m.Close("other")
	if len(m.Visible()) != 2 {
		t.Fatal("last reference removal did not restore source tab")
	}
}
func TestCycleRetainsReachableWorkspaceAndSSHNeverHides(t *testing.T) {
	m := New()
	m.Add(Session{ID: "a", Protocol: "sftp", Open: true})
	m.Add(Session{ID: "b", Protocol: "ftp", Open: true})
	m.Attach("a", "b")
	m.Attach("b", "a")
	if len(m.Visible()) == 0 || m.OwnerFor("a") == "" || m.OwnerFor("b") == "" {
		t.Fatal("cycle removed every reachable workspace")
	}
	m.Add(Session{ID: "ssh", Protocol: "ssh", Open: true})
	m.Attach("a", "ssh")
	found := false
	for _, s := range m.Visible() {
		if s.ID == "ssh" {
			found = true
		}
	}
	if !found {
		t.Fatal("SSH tab hidden by file association")
	}
}
