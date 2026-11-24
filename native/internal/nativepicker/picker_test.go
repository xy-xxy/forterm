package nativepicker

import (
	"context"
	"fyne.io/fyne/v2/test"
	"sync/atomic"
	"testing"
)

func TestNativeResultPreservesUnicodeAndFinishesOnce(t *testing.T) {
	app := test.NewApp()
	defer app.Quit()
	id := sequence.Add(1)
	var calls atomic.Int32
	expected := []string{"/tmp/中文 空格%20#文件.txt", "/tmp/second.txt"}
	requests.Store(id, &pending{callback: func(result Result) {
		calls.Add(1)
		if !result.Native || result.Err != nil || len(result.Paths) != 2 || result.Paths[0] != expected[0] {
			t.Fatal("native paths were decoded/truncated")
		}
	}})
	for _, path := range expected {
		addPath(id, path)
	}
	finish(id, 0)
	finish(id, 0)
	if calls.Load() != 1 {
		t.Fatal("callback delivered twice")
	}
	window := app.NewWindow("software owner")
	if Show(context.Background(), window, Request{}, func(Result) { t.Fatal("unsupported window called native UI") }) != ErrUnavailable {
		t.Fatal("unsupported native parent not identified")
	}
}
