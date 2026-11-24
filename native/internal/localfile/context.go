package localfile

import (
	"context"
	"errors"
	"os"
	"sync"
)

var localOpenGate sync.Map

// OpenContext does blocking OS work off the UI and bounds the caller's wait.
// A late handle is closed; retries do not accumulate blocked opens for one path.
func OpenContext(ctx context.Context, path string) (*os.File, error) {
	return openContext(ctx, path, os.Open)
}
func openContext(ctx context.Context, path string, open func(string) (*os.File, error)) (*os.File, error) {
	if ctx.Err() != nil {
		return nil, ctx.Err()
	}
	if _, busy := localOpenGate.LoadOrStore(path, true); busy {
		return nil, errors.New("此本地路径仍有未释放的打开请求；未叠加重试")
	}
	type result struct {
		file *os.File
		err  error
	}
	answer := make(chan result)
	go func() {
		defer localOpenGate.Delete(path)
		before, e := os.Lstat(path)
		if e == nil && (!before.Mode().IsRegular() || before.Mode()&os.ModeSymlink != 0) {
			e = errors.New("只上传普通本地文件，不跟随链接")
		}
		var file *os.File
		if e == nil {
			file, e = open(path)
		}
		if e == nil {
			after, err := file.Stat()
			if err != nil || !os.SameFile(before, after) {
				file.Close()
				file = nil
				e = errors.New("本地文件在选择后变化")
			}
		}
		select {
		case answer <- result{file, e}:
		case <-ctx.Done():
			if file != nil {
				file.Close()
			}
		}
	}()
	select {
	case r := <-answer:
		return r.file, r.err
	case <-ctx.Done():
		return nil, ctx.Err()
	}
}

type TargetVersion struct {
	Path   string
	Info   os.FileInfo
	Exists bool
}

func CaptureTarget(path string) (TargetVersion, error) {
	info, err := os.Lstat(path)
	if os.IsNotExist(err) {
		return TargetVersion{Path: path}, nil
	}
	if err != nil {
		return TargetVersion{}, err
	}
	if !info.Mode().IsRegular() || info.Mode()&os.ModeSymlink != 0 {
		return TargetVersion{}, errors.New("保存目标不是普通文件，不能覆盖目录或链接")
	}
	return TargetVersion{path, info, true}, nil
}
func (v TargetVersion) Check() error {
	info, err := os.Lstat(v.Path)
	if !v.Exists {
		if os.IsNotExist(err) {
			return nil
		}
		if err == nil {
			return errors.New("目标在系统选择后被创建，未覆盖")
		}
		return err
	}
	if err != nil {
		return errors.New("目标在确认后消失或不可读取")
	}
	if !info.Mode().IsRegular() || !os.SameFile(v.Info, info) || info.Size() != v.Info.Size() || !info.ModTime().Equal(v.Info.ModTime()) || info.Mode() != v.Info.Mode() {
		return errors.New("目标在覆盖确认后发生变化，下载未提交")
	}
	return nil
}

func CaptureTargetContext(ctx context.Context, path string) (TargetVersion, error) {
	key := "target:" + path
	if _, busy := localOpenGate.LoadOrStore(key, true); busy {
		return TargetVersion{}, errors.New("该保存路径仍在检查中，未叠加")
	}
	type result struct {
		value TargetVersion
		err   error
	}
	answer := make(chan result)
	go func() {
		defer localOpenGate.Delete(key)
		value, err := CaptureTarget(path)
		select {
		case answer <- result{value, err}:
		case <-ctx.Done():
		}
	}()
	select {
	case value := <-answer:
		return value.value, value.err
	case <-ctx.Done():
		return TargetVersion{}, ctx.Err()
	}
}
