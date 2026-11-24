package ftpconn

import (
	"context"
	"errors"
	"forterm/native/internal/localfile"
	"forterm/native/internal/transfers"
	"io"
	"os"
	"path/filepath"
	"time"
)

type localFailure struct{ message string }

func (e localFailure) Error() string { return e.message }

type uploadReader struct {
	io.Reader
	watch       *transfers.Watchdog
	done, total int64
	progress    func(int64, int64)
	started     bool
}

func (r *uploadReader) Read(p []byte) (int, error) {
	if !r.started {
		r.started = true
		r.watch.Data()
		transfers.ReportPhase(r.watch.Context(), transfers.PhaseData)
	}
	n, e := r.Reader.Read(p)
	if n > 0 {
		r.done += int64(n)
		r.watch.Progress()
		if r.progress != nil {
			r.progress(r.done, r.total)
		}
	}
	if e == io.EOF {
		r.watch.Metadata()
		transfers.ReportPhase(r.watch.Context(), transfers.PhaseClose)
	}
	return n, e
}

type downloadWriter struct {
	io.Writer
	watch       *transfers.Watchdog
	done, total int64
	progress    func(int64, int64)
}

func (w *downloadWriter) Write(p []byte) (int, error) {
	n, e := w.Writer.Write(p)
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
	remote, e := c.resolve(spec.Remote)
	if e != nil {
		return e
	}
	watch := transfers.NewWatchdog(parent, 10*time.Second, 30*time.Second)
	defer watch.Stop()
	started := false
	transfers.ReportPhase(parent, transfers.PhaseFileLane)
	e = c.run(watch.Context(), func() (result error) {
		defer func() {
			if result != nil && started {
				c.Close()
			}
		}()
		if spec.Direction == "upload" {
			transfers.ReportPhase(parent, transfers.PhaseLocalOpen)
			in, e := localfile.OpenContext(watch.Context(), spec.Local)
			if e != nil {
				return localFailure{"本地文件无法读取"}
			}
			defer in.Close()
			info, e := in.Stat()
			if e != nil || !info.Mode().IsRegular() {
				return localFailure{"只能上传普通文件"}
			}
			if !spec.Overwrite {
				if _, e := c.stat(remote); e == nil {
					return localFailure{"同名FTP目标已存在，未主动覆盖"}
				} else if !os.IsNotExist(e) {
					return e
				}
			}
			if progress != nil {
				progress(0, info.Size())
			}
			reader := &uploadReader{Reader: in, watch: watch, total: info.Size(), progress: progress}
			started = true
			transfers.ReportPhase(parent, transfers.PhaseRemoteOpen)
			return c.client.Stor(remote, reader)
		}
		if spec.Direction != "download" {
			return localFailure{"无效的传输方向"}
		}
		info, e := c.stat(remote)
		if e != nil {
			return e
		}
		if !info.Mode().IsRegular() {
			return localFailure{"只能下载普通文件，目录与链接不直接下载"}
		}
		if progress != nil {
			progress(0, info.Size())
		}
		response, e := c.client.Retr(remote)
		if e != nil {
			return e
		}
		defer response.Close()
		out, e := os.CreateTemp(filepath.Dir(spec.Local), ".forterm-native-*.part")
		if e != nil {
			return localFailure{"本地目录无法写入"}
		}
		temporary := out.Name()
		defer os.Remove(temporary)
		started = true
		watch.Data()
		writer := &downloadWriter{Writer: out, watch: watch, total: info.Size(), progress: progress}
		_, e = io.CopyBuffer(writer, response, make([]byte, 32768))
		watch.Metadata()
		oe := out.Close()
		re := response.Close()
		if e != nil || oe != nil || re != nil {
			return localFailure{"FTP下载未完成，本地原文件保持不变"}
		}
		if watch.Context().Err() != nil {
			return localFailure{"FTP下载已取消，未提交本地文件"}
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
			return localFailure{"本地目标已存在或无法原子提交，未主动删除目标"}
		}
		return nil
	})
	if cause := watch.Cause(); cause != nil && parent.Err() == nil {
		return cause
	}
	if e != nil && started && spec.Direction == "upload" {
		return errors.New(e.Error() + "；远端可能保留部分文件")
	}
	return e
}
