package main

import (
	"reflect"
	"testing"
)

func TestOnlyExplicitDiagnoseFlagEntersOfflineUI(t *testing.T) {
	if got := entryArguments("forterm-diagnose", []string{"--ui-check"}); !reflect.DeepEqual(got, []string{"--ui-check"}) {
		t.Fatal(got)
	}
	raw := []string{"-newtab", "--ui-check", "-url", "ssh://fixture-user:synthetic%2520@fixture.invalid:22"}
	for name, mode := range map[string]string{"xshell": "xshell", "xftp": "xftp", "forterm-diagnose": "diagnose"} {
		got := entryArguments(name, raw)
		if !reflect.DeepEqual(got, append([]string{"--entry=" + mode}, raw...)) {
			t.Fatal("controller arguments changed")
		}
	}
	if got := entryArguments("xshell", []string{"--ui-check"}); got[0] != "--entry=xshell" {
		t.Fatal("SSH entry unexpectedly changed modes")
	}
}
