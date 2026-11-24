package fileops

import (
	"context"
	"errors"
	"fmt"
	"forterm/native/internal/remotefs"
	"forterm/native/internal/transfers"
	"os"
	"path"
	"strings"
	"sync"
	"sync/atomic"
	"time"
)

func SafePath(input string) (string, error) {
	if !strings.HasPrefix(input, "/") || len(input) > 8192 || len(strings.Split(input, "/")) > 34 || strings.ContainsAny(input, "\r\n\x00\\") {
		return "", errors.New("拒绝操作根目录、相对路径或不安全路径")
	}
	for _, part := range strings.Split(input, "/")[1:] {
		if part == "" || part == "." || part == ".." {
			return "", errors.New("拒绝操作根目录、空段或父目录跳转")
		}
	}
	return input, nil
}
func ChildName(name string) error {
	if name == "" || name == "." || name == ".." || strings.ContainsAny(name, "/\\\r\n\x00") {
		return errors.New("服务器返回异常目录项，已停止操作")
	}
	return nil
}
func RawMode(mode os.FileMode) uint32 {
	raw := uint32(mode.Perm())
	if mode&os.ModeSetuid != 0 {
		raw |= 04000
	}
	if mode&os.ModeSetgid != 0 {
		raw |= 02000
	}
	if mode&os.ModeSticky != 0 {
		raw |= 01000
	}
	return raw
}
func FileMode(raw uint32) os.FileMode {
	mode := os.FileMode(raw & 0777)
	if raw&04000 != 0 {
		mode |= os.ModeSetuid
	}
	if raw&02000 != 0 {
		mode |= os.ModeSetgid
	}
	if raw&01000 != 0 {
		mode |= os.ModeSticky
	}
	return mode
}

type Status struct {
	Running       bool
	Path, Message string
	Count         int
}
type Permissions struct {
	Path                   string
	Mode                   uint32
	Known, Supported, Link bool
	Message                string
}
type Service struct {
	resourceKey      string
	observers        map[uint64]func(Status)
	observerSequence uint64
	fs               remotefs.Operations
	ftp              bool
	busy             atomic.Bool
	mu               sync.Mutex
	cancel           context.CancelFunc
	status           Status
	onChange         func(Status)
	hasTransfer      func(string) bool
}

func New(fs remotefs.Operations, isFTP bool, hasTransfer func(string) bool, onChange func(Status)) *Service {
	return &Service{fs: fs, ftp: isFTP, hasTransfer: hasTransfer, onChange: onChange}
}
func (s *Service) Snapshot() Status { s.mu.Lock(); defer s.mu.Unlock(); return s.status }
func (s *Service) publish(state Status) {
	s.mu.Lock()
	s.status = state
	observers := make([]func(Status), 0, len(s.observers)+1)
	if s.onChange != nil {
		observers = append(observers, s.onChange)
	}
	for _, fn := range s.observers {
		observers = append(observers, fn)
	}
	s.mu.Unlock()
	for _, fn := range observers {
		fn(state)
	}
}
func (s *Service) Subscribe(fn func(Status)) func() {
	s.mu.Lock()
	if s.observers == nil {
		s.observers = map[uint64]func(Status){}
	}
	s.observerSequence++
	id := s.observerSequence
	s.observers[id] = fn
	current := s.status
	s.mu.Unlock()
	fn(current)
	return func() { s.mu.Lock(); delete(s.observers, id); s.mu.Unlock() }
}

func (s *Service) Cancel() {
	s.mu.Lock()
	cancel := s.cancel
	s.mu.Unlock()
	if cancel != nil {
		cancel()
	}
}
func (s *Service) parents(ctx context.Context, file string) error {
	parts := strings.Split(strings.TrimPrefix(file, "/"), "/")
	for i := 1; i < len(parts); i++ {
		if ctx.Err() != nil {
			return ctx.Err()
		}
		info, e := s.fs.Stat(ctx, "/"+strings.Join(parts[:i], "/"))
		if e != nil {
			return e
		}
		if !info.IsDir() || info.Mode()&os.ModeSymlink != 0 {
			return errors.New("不通过符号链接或非目录父路径执行文件操作")
		}
	}
	return nil
}
func (s *Service) available(file string) error {
	if s.busy.Load() {
		return errors.New("此文件连接已有目录操作，请等待或取消")
	}
	if s.hasTransfer != nil && s.hasTransfer(file) {
		return errors.New("目标涉及活动或排队传输，请先完成或取消")
	}
	return nil
}
func (s *Service) Permissions(ctx context.Context, input string) (Permissions, error) {
	file, e := SafePath(input)
	if e != nil {
		return Permissions{}, e
	}
	if e = s.parents(ctx, file); e != nil {
		return Permissions{}, e
	}
	info, e := s.fs.Stat(ctx, file)
	if e != nil {
		return Permissions{}, e
	}
	known := true
	if flag, ok := info.(interface{ ModeKnown() bool }); ok {
		known = flag.ModeKnown()
	}
	p := Permissions{Path: file, Mode: RawMode(info.Mode()), Known: known, Link: info.Mode()&os.ModeSymlink != 0}
	p.Supported = known && !p.Link && s.fs.ChmodSupported()
	if p.Link {
		p.Message = "符号链接不修改外部目标"
	} else if !known {
		p.Message = "服务器未提供可验证权限，不能显示为000"
	} else if !s.fs.ChmodSupported() {
		p.Message = "FTP服务器未确认SITE CHMOD能力"
	}
	return p, nil
}
func (s *Service) Chmod(ctx context.Context, input string, normal, expected uint32) (Permissions, error) {
	ctx, release, err := transfers.AcquireResources(ctx, s.resourceKey)
	if err != nil {
		return Permissions{}, err
	}
	defer release()
	if normal > 0777 {
		return Permissions{}, errors.New("普通权限须为000–777，特殊位单独保留")
	}
	if e := s.available(input); e != nil {
		return Permissions{}, e
	}
	before, e := s.Permissions(ctx, input)
	if e != nil {
		return Permissions{}, e
	}
	if !before.Supported {
		return before, errors.New(before.Message)
	}
	if before.Mode != expected {
		return before, errors.New("权限在打开对话框后已变化，请重新读取")
	}
	target := (before.Mode & 07000) | normal
	if e = s.fs.Mutate(ctx, "chmod", input, "", FileMode(target)); e != nil {
		return Permissions{}, e
	}
	after, e := s.Permissions(ctx, input)
	if e != nil {
		return Permissions{}, e
	}
	if after.Mode != target {
		return after, errors.New("服务器未按请求应用权限，请重新读取核对")
	}
	return after, nil
}
func (s *Service) Mkdir(ctx context.Context, input string) error {
	ctx, release, err := transfers.AcquireResources(ctx, s.resourceKey)
	if err != nil {
		return err
	}
	defer release()
	file, e := SafePath(input)
	if e != nil {
		return e
	}
	if e = s.available(file); e != nil {
		return e
	}
	if e = s.parents(ctx, file); e != nil {
		return e
	}
	if _, e = s.fs.Stat(ctx, file); e == nil {
		return errors.New("同名目标已存在，未覆盖")
	} else if !os.IsNotExist(e) {
		return e
	}
	return s.fs.Mutate(ctx, "mkdir", file, "", 0)
}
func (s *Service) Rename(ctx context.Context, input, destination string) error {
	ctx, release, err := transfers.AcquireResources(ctx, s.resourceKey)
	if err != nil {
		return err
	}
	defer release()
	file, e := SafePath(input)
	if e != nil {
		return e
	}
	target, e := SafePath(destination)
	if e != nil {
		return e
	}
	if target == file || strings.HasPrefix(target, file+"/") {
		return errors.New("不能重命名到自身或子目录")
	}
	for _, p := range []string{file, target} {
		if e = s.available(p); e != nil {
			return e
		}
		if e = s.parents(ctx, p); e != nil {
			return e
		}
	}
	if _, e = s.fs.Stat(ctx, target); e == nil {
		return errors.New("同名目标已存在，未覆盖")
	} else if !os.IsNotExist(e) {
		return e
	}
	return s.fs.Mutate(ctx, "rename", file, target, 0)
}
func (s *Service) Remove(parent context.Context, input string) error {
	parent, release, err := transfers.AcquireResources(parent, s.resourceKey)
	if err != nil {
		return err
	}
	defer release()
	file, e := SafePath(input)
	if e != nil {
		return e
	}
	if s.hasTransfer != nil && s.hasTransfer(file) {
		return errors.New("目标涉及活动或排队传输，请先完成或取消")
	}
	if !s.busy.CompareAndSwap(false, true) {
		return errors.New("已有目录操作，请等待或取消")
	}
	defer s.busy.Store(false)
	ctx, cancel := context.WithTimeout(parent, 120*time.Second)
	defer cancel()
	s.mu.Lock()
	s.cancel = cancel
	s.mu.Unlock()
	defer func() { s.mu.Lock(); s.cancel = nil; s.mu.Unlock() }()
	state := Status{Running: true, Path: file, Message: "检查目录边界与内容"}
	s.publish(state)
	finish := func(err error) error {
		state.Running = false
		if err != nil {
			state.Message = fmt.Sprintf("%s；已完成%d项不会回滚", err.Error(), state.Count)
		} else {
			state.Message = fmt.Sprintf("删除完成，共%d项", state.Count)
		}
		s.publish(state)
		return err
	}
	if e = s.parents(ctx, file); e != nil {
		return finish(e)
	}
	type entry struct {
		file string
		info os.FileInfo
	}
	plan := []entry{}
	seen := map[string]bool{}
	var walk func(string, int) error
	walk = func(target string, depth int) error {
		if ctx.Err() != nil {
			return errors.New("操作取消或超过两分钟上限")
		}
		if depth > 32 {
			return errors.New("目录超过32层深度上限")
		}
		if len(seen) >= 2000 {
			return errors.New("目录超过2000项上限，尚未开始删除")
		}
		if seen[target] {
			return errors.New("服务器返回重复目录项，尚未开始删除")
		}
		seen[target] = true
		info, e := s.fs.Stat(ctx, target)
		if e != nil {
			return e
		}
		link := info.Mode()&os.ModeSymlink != 0
		if link && s.ftp {
			return errors.New("FTP链接语义无法确认，拒绝递归删除，未跟随目标")
		}
		if info.IsDir() && !link {
			children, e := s.fs.List(ctx, target)
			if e != nil {
				return e
			}
			for _, child := range children {
				if e = ChildName(child.Name); e != nil {
					return e
				}
				if e = walk(path.Join(target, child.Name), depth+1); e != nil {
					return e
				}
			}
		}
		plan = append(plan, entry{target, info})
		return nil
	}
	if e = walk(file, 0); e != nil {
		return finish(e)
	}
	for _, item := range plan {
		if ctx.Err() != nil {
			return finish(errors.New("操作已取消或超时"))
		}
		if e = s.parents(ctx, item.file); e != nil {
			return finish(e)
		}
		current, e := s.fs.Stat(ctx, item.file)
		if e != nil {
			return finish(e)
		}
		if s.hasTransfer != nil && s.hasTransfer(item.file) {
			return finish(errors.New("检查后出现相关传输，停止删除"))
		}
		if !current.IsDir() && (current.Size() != item.info.Size() || !current.ModTime().Equal(item.info.ModTime())) {
			return finish(errors.New("文件在检查后已变化，停止删除"))
		}
		if current.Mode() != item.info.Mode() || current.IsDir() != item.info.IsDir() {
			return finish(errors.New("目录项类型或权限在检查后已变化，停止操作"))
		}
		kind := "remove"
		if current.IsDir() && current.Mode()&os.ModeSymlink == 0 {
			kind = "rmdir"
		}
		if e = s.fs.Mutate(ctx, kind, item.file, "", 0); e != nil {
			return finish(errors.New("删除失败：目录可能有并发新增内容或权限变化，未继续递归"))
		}
		state.Count++
		state.Message = "正在删除，已完成项不会回滚"
		s.publish(state)
	}
	return finish(nil)
}

func (s *Service) SetResourceKey(key string) { s.resourceKey = key }
