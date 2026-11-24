package nativepicker

import (
	"context"
	"errors"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/driver"
	"sync"
	"sync/atomic"
)

var ErrUnavailable = errors.New("当前窗口或平台不支持系统原生文件对话框")

type Request struct {
	Save, Directory, Multiple     bool
	Title, InitialDirectory, Name string
}
type Result struct {
	Paths             []string
	Cancelled, Native bool
	Err               error
}
type Provider func(context.Context, fyne.Window, Request, func(Result)) error
type pending struct {
	mu       sync.Mutex
	paths    []string
	callback func(Result)
	stop     func() bool
}

var sequence atomic.Uint64
var requests sync.Map

func Show(ctx context.Context, window fyne.Window, request Request, callback func(Result)) error {
	if ctx.Err() != nil {
		return ctx.Err()
	}
	native, ok := window.(driver.NativeWindow)
	if !ok {
		return ErrUnavailable
	}
	id := sequence.Add(1)
	p := &pending{callback: callback}
	requests.Store(id, p)
	var err error
	native.RunNative(func(owner any) { err = startPlatform(id, owner, request) })
	if err != nil {
		requests.Delete(id)
		return err
	}
	stop := context.AfterFunc(ctx, func() { cancelPlatform(id) })
	p.mu.Lock()
	p.stop = stop
	p.mu.Unlock()
	if _, exists := requests.Load(id); !exists {
		stop()
	}
	return nil
}
func addPath(id uint64, path string) {
	if value, ok := requests.Load(id); ok {
		p := value.(*pending)
		p.mu.Lock()
		if len(p.paths) < 1001 {
			p.paths = append(p.paths, path)
		}
		p.mu.Unlock()
	}
}
func finish(id uint64, code int) {
	value, ok := requests.LoadAndDelete(id)
	if !ok {
		return
	}
	p := value.(*pending)
	p.mu.Lock()
	paths := append([]string(nil), p.paths...)
	stop := p.stop
	p.mu.Unlock()
	if stop != nil {
		stop()
	}
	result := Result{Paths: paths, Native: true, Cancelled: code == 1}
	if code == 0 && len(paths) == 0 {
		result.Err = errors.New("系统对话框未返回有效文件路径")
	}
	if code == 2 {
		result.Err = errors.New("系统文件对话框未能完成")
	}
	if len(paths) > 1000 {
		result.Err = errors.New("一次最多选择1000个文件")
		result.Paths = nil
	}
	fyne.Do(func() { p.callback(result) })
}
