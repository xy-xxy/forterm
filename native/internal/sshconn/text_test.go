package sshconn

import (
	"context"
	"forterm/native/internal/testserver"
	"strings"
	"sync/atomic"
	"testing"
)

func TestTextRoundtripBoundariesAndExclusiveCreate(t *testing.T) {
	server, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer server.Close()
	c := dialFixture(t, server)
	ctx := context.Background()
	original := "\ufefffirst  \r\n\t中文\r\n"
	doc, e := c.CreateText(ctx, "/code.txt", original)
	if e != nil {
		t.Fatal(e)
	}
	if doc.Text != original {
		t.Fatal("BOM, CRLF or trailing whitespace changed")
	}
	if _, e = c.CreateText(ctx, "/code.txt", "overwrite"); e == nil {
		t.Fatal("exclusive create overwrote")
	}
	next := original + "third  \r\n"
	saved, e := c.SaveText(ctx, doc.Path, next, doc.Revision)
	if e != nil {
		t.Fatal(e)
	}
	got, e := c.ReadText(ctx, doc.Path)
	if e != nil || got.Text != next || got.Revision != saved.Revision {
		t.Fatal("saved bytes/revision differ", e)
	}
	modes := server.CreationModes()
	if len(modes) < 2 {
		t.Fatal("no creation attributes recorded")
	}
	for _, mode := range modes {
		if mode != 0600 {
			t.Fatalf("private file created with mode %o", mode)
		}
	}
	for name, text := range map[string]string{"binary": "x\x00y", "invalid": "\xff", "large": strings.Repeat("x", TextLimit+1)} {
		if _, e = c.CreateText(ctx, "/"+name, text); e == nil {
			t.Fatal("invalid text created", name)
		}
	}
}
func TestTextFirstAndSecondConflictChecks(t *testing.T) {
	server, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer server.Close()
	c := dialFixture(t, server)
	ctx := context.Background()
	doc, e := c.CreateText(ctx, "/doc", "first")
	if e != nil {
		t.Fatal(e)
	}
	if e = server.ReplaceFixtureFile("/doc", []byte("other")); e != nil {
		t.Fatal(e)
	}
	if _, e = c.SaveText(ctx, "/doc", "draft", doc.Revision); e == nil {
		t.Fatal("first conflict ignored")
	}
	fresh, e := c.ReadText(ctx, "/doc")
	if e != nil {
		t.Fatal(e)
	}
	var changed atomic.Bool
	server.AfterTempChmod = func() {
		if changed.CompareAndSwap(false, true) {
			_ = server.ReplaceFixtureFile("/doc", []byte("remote"))
		}
	}
	if _, e = c.SaveText(ctx, "/doc", "draft", fresh.Revision); e == nil {
		t.Fatal("second conflict ignored")
	}
	got, e := c.ReadText(ctx, "/doc")
	if e != nil || got.Text != "remote" {
		t.Fatal("conflict overwrote remote", e)
	}
	entries, e := c.List(ctx, "/")
	if e != nil {
		t.Fatal(e)
	}
	for _, entry := range entries {
		if strings.HasPrefix(entry.Name, ".forterm-edit-") {
			t.Fatal("conflict left temporary text")
		}
	}
	if server.Auth.Load() != 1 {
		t.Fatal("text API reauthenticated")
	}
}
