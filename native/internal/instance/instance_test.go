package instance

import (
	"os"
	"os/exec"
	"reflect"
	"runtime"
	"sync"
	"testing"
	"time"
)

func TestIPCChild(t *testing.T) {
	dir := os.Getenv("FORTERM_IPC_CHILD")
	if dir == "" {
		return
	}
	_, primary, err := Start(dir, []string{"-newtab", "名称 空格", "-url", "ssh://synthetic%40user:synthetic%2525@127.0.0.1:2222"}, func([]string) {})
	if err != nil || primary {
		os.Exit(9)
	}
}
func TestElevenProcessesTransferExactArguments(t *testing.T) {
	base := ""
	if runtime.GOOS != "windows" {
		base = "/tmp"
	}
	dir, err := os.MkdirTemp(base, "ft-native-ipc-")
	if err != nil {
		t.Fatal(err)
	}
	defer os.RemoveAll(dir)
	incoming := make(chan []string, 11)
	server, primary, err := Start(dir, nil, func(args []string) { incoming <- args })
	if err != nil || !primary {
		t.Fatalf("primary start failed: %v", err)
	}
	defer server.Close()
	var wg sync.WaitGroup
	errs := make(chan error, 11)
	for i := 0; i < 11; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			cmd := exec.Command(os.Args[0], "-test.run=^TestIPCChild$")
			cmd.Env = append(os.Environ(), "FORTERM_IPC_CHILD="+dir)
			errs <- cmd.Run()
		}()
	}
	wg.Wait()
	close(errs)
	for err := range errs {
		if err != nil {
			t.Fatal("child transfer failed", err)
		}
	}
	expected := []string{"-newtab", "名称 空格", "-url", "ssh://synthetic%40user:synthetic%2525@127.0.0.1:2222"}
	for i := 0; i < 11; i++ {
		select {
		case args := <-incoming:
			if !reflect.DeepEqual(args, expected) {
				t.Fatal("argv changed")
			}
		case <-time.After(time.Second):
			t.Fatal("missing transfer")
		}
	}
}
