package main

import (
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"testing"
)

func TestCancelledOrPartialStageDoesNotLaunchNextWindow(t *testing.T) {
	for _, input := range []string{`{}`, `{"completed":false,"phases":[{},{},{},{}]}`, `{"completed":true,"phases":[{}]}`, `{"completed":true,"phases":null}`, `[]`} {
		var v any
		if e := json.Unmarshal([]byte(input), &v); e != nil {
			t.Fatal(e)
		}
		if completedStage(v) {
			t.Fatal("partial or cancelled report accepted", input)
		}
	}
	var v any
	json.Unmarshal([]byte(`{"completed":true,"phases":[{},{},{},{}]}`), &v)
	if !completedStage(v) {
		t.Fatal("complete stage rejected")
	}
}

func TestTruncatedOrMalformedReportStopsNextStage(t *testing.T) {
	for _, raw := range []string{"", `{"completed":true,`, "not JSON"} {
		if _, complete := decodeStage([]byte(raw)); complete {
			t.Fatal("malformed report accepted")
		}
	}
}

func TestOnlyCleanExitAndCompleteDecodableReportCanContinue(t *testing.T) {
	good := []byte(`{"completed":true,"phases":[{},{},{},{}]}`)
	cases := []struct {
		name            string
		runErr, readErr error
		raw             []byte
		want            bool
	}{
		{"normal complete", nil, nil, good, true},
		{"child failure despite complete JSON", errors.New("exit1"), nil, good, false},
		{"missing report", nil, os.ErrNotExist, nil, false},
		{"malformed report", nil, nil, []byte(`{"completed":`), false},
		{"cancelled report", nil, nil, []byte(`{"completed":false,"phases":[{}]}`), false},
		{"incomplete final write", nil, nil, []byte(`{"completed":false,"phases":[{},{},{},{}]}`), false},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			_, got := stageOutcome(c.runErr, c.readErr, c.raw)
			if got != c.want {
				t.Fatal("incorrect continuation decision")
			}
		})
	}
}
func TestReportCommitFailuresDoNotPublishCompletion(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "stage.json")
	if !save(path, map[string]any{"completed": false}) {
		t.Fatal("initial report failed")
	}
	if save(path, map[string]any{"completed": true, "invalid": make(chan int)}) {
		t.Fatal("invalid JSON reported written")
	}
	raw, _ := os.ReadFile(path)
	var value map[string]any
	if json.Unmarshal(raw, &value) != nil || value["completed"] != false {
		t.Fatal("failed write replaced prior incomplete report")
	}
	if save(filepath.Join(dir, "missing", "stage.json"), map[string]any{"completed": true}) {
		t.Fatal("missing directory write reported successful")
	}
	targetDir := filepath.Join(dir, "directory")
	os.Mkdir(targetDir, 0700)
	if save(targetDir, map[string]any{"completed": true}) {
		t.Fatal("rename failure reported successful")
	}
	if !save(path, map[string]any{"completed": true}) {
		t.Fatal("final atomic commit failed")
	}
	raw, _ = os.ReadFile(path)
	json.Unmarshal(raw, &value)
	if value["completed"] != true {
		t.Fatal("successful final commit missing")
	}
	matches, _ := filepath.Glob(filepath.Join(dir, ".forterm-report-*.tmp"))
	if len(matches) != 0 {
		t.Fatal("temporary report files leaked")
	}
}
