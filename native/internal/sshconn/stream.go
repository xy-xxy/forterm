package sshconn

import (
	"context"
	"errors"
	"forterm/native/internal/localfile"
	"forterm/native/internal/transfers"
	"github.com/pkg/sftp"
	"io"
	"os"
	"path/filepath"
	"time"
)

type progressWriter struct {
	out         io.Writer
	done, total int64
	progress    func(int64, int64)
	watch       *transfers.Watchdog
}

func (w *progressWriter) Write(data []byte) (int, error) {
	n, e := w.out.Write(data)
	if n > 0 {
		w.done += int64(n)
		w.watch.Progress()
		if w.progress != nil {
			w.progress(w.done, w.total)
		}
	}
	return n, e
}
func (c *Connection) RunTransfer(parent context.Context, spec transfers.Spec, progress func(int64, int64)) error {
	return c.transferWithBounds(parent, spec, progress, 10*time.Second, 30*time.Second)
}
func (c *Connection) transferWithBounds(parent context.Context, spec transfers.Spec, progress func(int64, int64), metadata, stall time.Duration) error {
	watch := transfers.NewWatchdog(parent, metadata, stall)
	defer watch.Stop()
	started := false
	transfers.ReportPhase(parent, transfers.PhaseFileLane)
	e := c.fileCall(watch.Context(), func(f *sftp.Client) (result error) {
		defer func() {
			if result != nil && started {
				c.fileMu.Lock()
				owned := c.file == f
				if owned {
					c.file = nil
				}
				c.fileMu.Unlock()
				if owned {
					_ = f.Close()
				}
			}
		}()

		if spec.Direction == "upload" {
			transfers.ReportPhase(parent, transfers.PhaseLocalOpen)
			in, e := localfile.OpenContext(watch.Context(), spec.Local)
			if e != nil {
				return errors.New("本地文件无法读取")
			}
			defer in.Close()
			info, e := in.Stat()
			if e != nil || !info.Mode().IsRegular() {
				return errors.New("只能上传普通文件")
			}
			if progress != nil {
				progress(0, info.Size())
			}
			flags := os.O_WRONLY | os.O_CREATE | os.O_EXCL
			if spec.Overwrite {
				flags = os.O_WRONLY | os.O_CREATE | os.O_TRUNC
			}
			transfers.ReportPhase(parent, transfers.PhaseRemoteOpen)
			out, e := f.OpenFileWithMode(spec.Remote, flags, 0666)
			if e != nil {
				return errors.New("上传目标已存在、没有权限或服务器拒绝文件流")
			}
			started = true
			watch.Data()
			transfers.ReportPhase(parent, transfers.PhaseData)
			writer := &progressWriter{out: out, total: info.Size(), progress: progress, watch: watch}
			_, e = io.CopyBuffer(writer, in, make([]byte, 32768))
			watch.Metadata()
			transfers.ReportPhase(parent, transfers.PhaseClose)
			ce := out.Close()
			if e != nil || ce != nil {
				return errors.New("上传未完成，远端可能保留部分文件，未自动重传")
			}
			return nil
		}
		if spec.Direction != "download" {
			return errors.New("无效的传输方向")
		}
		info, e := f.Lstat(spec.Remote)
		if e != nil || !info.Mode().IsRegular() || info.Size() < 0 {
			return errors.New("只能下载普通文件，请检查权限；目录和链接不能直接下载")
		}
		if progress != nil {
			progress(0, info.Size())
		}
		in, e := f.Open(spec.Remote)
		if e != nil {
			return errors.New("远程文件无法读取")
		}
		defer in.Close()
		out, e := os.CreateTemp(filepath.Dir(spec.Local), ".forterm-native-*.part")
		if e != nil {
			return errors.New("本地目录无法写入")
		}
		temporary := out.Name()
		defer os.Remove(temporary)
		started = true
		watch.Data()
		writer := &progressWriter{out: out, total: info.Size(), progress: progress, watch: watch}
		_, e = io.CopyBuffer(writer, in, make([]byte, 32768))
		watch.Metadata()
		ce := out.Close()
		if e != nil || ce != nil {
			return errors.New("下载未完成，原本地文件保持不变")
		}
		if watch.Context().Err() != nil {
			return errors.New("下载已取消，未提交本地文件")
		}
		if spec.CommitGuard != nil {
			if e = spec.CommitGuard(); e != nil {
				return e
			}
		}
		if spec.Overwrite {
			e = os.Rename(temporary, spec.Local)
		} else {
			e = localfile.CommitExclusive(temporary, spec.Local)
		}
		if e != nil {
			return errors.New("本地目标已存在或无法原子提交，未删除目标")
		}
		return nil
	})
	if cause := watch.Cause(); cause != nil && parent.Err() == nil {
		if started && spec.Direction == "upload" {
			return errors.New(cause.Error() + "；远程可能保留部分文件")
		}
		return cause
	}
	return e
}

func (c *Connection) Stat(parent context.Context, remote string) (os.FileInfo, error) {
	ctx, cancel := context.WithTimeout(parent, 10*time.Second)
	defer cancel()
	var info os.FileInfo
	err := c.fileCall(ctx, func(f *sftp.Client) (result error) { var err error; info, err = f.Lstat(remote); return err })
	return info, err
}
