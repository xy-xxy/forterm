package sshconn

import (
	"encoding/json"
	"forterm/native/internal/fileops"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

func TestPermissionHelperInspectsWithoutFollowingLinksAndCommandQuotes(t *testing.T) {
	python, e := exec.LookPath("python3")
	if e != nil {
		t.Skip("Python fixture unavailable")
	}
	root, e := filepath.EvalSymlinks(t.TempDir())
	if e != nil {
		t.Fatal(e)
	}
	file := filepath.Join(root, "a ' ; $(echo injected) %#.txt")
	os.WriteFile(file, []byte("fixture"), 0600)
	info, _ := os.Lstat(file)
	item := fileops.PermissionItem{Path: file, Before: fileops.IdentityOf(info), Mode: 0644}
	script, e := permissionScript(item, "inspect", nil)
	if e != nil {
		t.Fatal(e)
	}
	if !strings.Contains(script, "'\\''") || strings.Contains(script, "sudo chmod -R") {
		t.Fatal("path was not safely quoted")
	}
	raw, _ := json.Marshal(privilegePayload{Action: "inspect", Path: file})
	out, e := exec.Command(python, "-c", permissionHelper, string(raw)).Output()
	if e != nil {
		t.Fatal(e)
	}
	var value fileops.PrivilegeStat
	if json.Unmarshal(out, &value) != nil || value.Size != info.Size() || value.Mode != 0600 {
		t.Fatal("safe helper metadata mismatch")
	}
	link := filepath.Join(root, "link")
	os.Symlink(file, link)
	raw, _ = json.Marshal(privilegePayload{Action: "inspect", Path: link})
	if exec.Command(python, "-c", permissionHelper, string(raw)).Run() == nil {
		t.Fatal("helper followed symlink")
	}
	for _, p := range []string{"/", "/a/../b", "/bad\npath"} {
		item.Path = p
		if _, e = permissionScript(item, "apply", nil); e == nil {
			t.Fatal("unsafe sudo path accepted")
		}
	}
}
func TestSudoPasswordWriterRequiresExactFreshPrompt(t *testing.T) {
	calls := make(chan struct{}, 2)
	writer := &permissionWriter{marker: "[unique-prompt]", prompt: func() { calls <- struct{}{} }}
	writer.Write([]byte("not the password prompt"))
	select {
	case <-calls:
		t.Fatal("unexpected authentication")
	default:
	}
	writer.Write([]byte("[unique-"))
	writer.Write([]byte("prompt]"))
	writer.Write([]byte("[unique-prompt]"))
	select {
	case <-calls:
	case <-time.After(time.Second):
		t.Fatal("explicit sudo prompt not detected")
	}
	select {
	case <-calls:
		t.Fatal("password requested again")
	default:
	}
}
