package sshconn

import (
	"context"
	"crypto/rand"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"fmt"
	"github.com/pkg/sftp"
	"io"
	"os"
	"path"
	"strings"
	"time"
	"unicode/utf8"
)

const TextLimit = 1 << 20

// TextSnapshot is bound to the originating Connection by its workspace. Text
// includes its original BOM and line endings; the editor never trims it.
type TextSnapshot struct {
	Path, Text, Revision string
	Mode                 os.FileMode
}

func validateText(data []byte) error {
	if len(data) > TextLimit {
		return errors.New("在线编辑限1MiB，请下载后编辑")
	}
	if !utf8.Valid(data) {
		return errors.New("文件不是有效UTF-8，请下载后使用对应编码编辑")
	}
	for _, b := range data {
		if b < 32 && b != 9 && b != 10 && b != 13 {
			return errors.New("检测到二进制内容，不能在线编辑")
		}
	}
	return nil
}
func revision(data []byte, info os.FileInfo) string {
	h := sha256.New()
	h.Write(data)
	fmt.Fprintf(h, "\x00%d:%d:%d", info.Size(), info.ModTime().UnixNano(), info.Mode())
	return hex.EncodeToString(h.Sum(nil))
}
func readText(f *sftp.Client, file string) (TextSnapshot, error) {
	before, e := f.Lstat(file)
	if e != nil {
		return TextSnapshot{}, errors.New("无法读取文件信息，请检查连接与权限")
	}
	if !before.Mode().IsRegular() {
		return TextSnapshot{}, errors.New("只能编辑普通文件，不支持目录或符号链接")
	}
	if before.Size() > TextLimit {
		return TextSnapshot{}, errors.New("在线编辑限1MiB，请下载后编辑")
	}
	stream, e := f.Open(file)
	if e != nil {
		return TextSnapshot{}, errors.New("远程文件无法读取")
	}
	data, e := io.ReadAll(io.LimitReader(stream, TextLimit+1))
	ce := stream.Close()
	if e != nil || ce != nil {
		return TextSnapshot{}, errors.New("文件读取未完成，未修改内容")
	}
	if e = validateText(data); e != nil {
		return TextSnapshot{}, e
	}
	after, e := f.Lstat(file)
	if e != nil || !after.Mode().IsRegular() || before.Size() != after.Size() || before.ModTime() != after.ModTime() || before.Mode() != after.Mode() {
		return TextSnapshot{}, errors.New("读取期间文件已变化，请重新打开")
	}
	return TextSnapshot{Path: file, Text: string(data), Mode: after.Mode(), Revision: revision(data, after)}, nil
}
func (c *Connection) ReadText(parent context.Context, file string) (TextSnapshot, error) {
	ctx, cancel := context.WithTimeout(parent, 15*time.Second)
	defer cancel()
	var doc TextSnapshot
	e := c.fileCall(ctx, func(f *sftp.Client) error { var e error; doc, e = readText(f, path.Clean(file)); return e })
	return doc, e
}
func (c *Connection) SaveText(parent context.Context, file, text, expected string) (TextSnapshot, error) {
	if e := validateText([]byte(text)); e != nil {
		return TextSnapshot{}, e
	}
	ctx, cancel := context.WithTimeout(parent, 15*time.Second)
	defer cancel()
	file = path.Clean(file)
	var saved TextSnapshot
	e := c.fileCall(ctx, func(f *sftp.Client) error {
		original, e := readText(f, file)
		if e != nil {
			return e
		}
		if original.Revision != expected {
			return errors.New("保存冲突：远程文件已变化，草稿保留，未覆盖远程文件")
		}
		var nonce [16]byte
		if _, e = rand.Read(nonce[:]); e != nil {
			return errors.New("无法建立安全临时文件")
		}
		temporary := path.Join(path.Dir(file), ".forterm-edit-"+hex.EncodeToString(nonce[:])+".tmp")
		created := false
		defer func() {
			if created {
				_ = f.Remove(temporary)
			}
		}()
		out, e := f.OpenFileWithMode(temporary, os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0600)
		if e != nil {
			return errors.New("无法排他创建私有临时文件，原文件未改动")
		}
		created = true
		_, e = io.Copy(out, strings.NewReader(text))
		ce := out.Close()
		if e != nil || ce != nil {
			return errors.New("临时写入未完成，草稿保留，原文件未改动")
		}
		if e = f.Chmod(temporary, original.Mode.Perm()); e != nil {
			return errors.New("无法恢复原文件普通权限，未替换原文件")
		}
		latest, e := readText(f, file)
		if e != nil {
			return e
		}
		if latest.Revision != expected {
			return errors.New("保存冲突：写入临时文件期间远程已变化，未覆盖原文件")
		}
		if _, ok := f.HasExtension("posix-rename@openssh.com"); ok {
			e = f.PosixRename(temporary, file)
		} else {
			e = f.Rename(temporary, file)
		}
		if e != nil {
			return errors.New("原子替换未获确认，未主动删除原文件；请保留草稿并重新读取核对")
		}
		created = false
		info, e := f.Lstat(file)
		if e != nil {
			return errors.New("保存结果未确认，请保留草稿并重新读取核对")
		}
		saved = TextSnapshot{Path: file, Text: text, Mode: info.Mode(), Revision: revision([]byte(text), info)}
		return nil
	})
	return saved, e
}
func (c *Connection) CreateText(parent context.Context, file, text string) (TextSnapshot, error) {
	if e := validateText([]byte(text)); e != nil {
		return TextSnapshot{}, e
	}
	ctx, cancel := context.WithTimeout(parent, 15*time.Second)
	defer cancel()
	file = path.Clean(file)
	var saved TextSnapshot
	e := c.fileCall(ctx, func(f *sftp.Client) error {
		if _, e := f.Lstat(file); e == nil {
			return errors.New("同名远程文件已存在，未覆盖")
		} else if !os.IsNotExist(e) {
			return errors.New("无法确认文件名与权限，未创建文件")
		}
		out, e := f.OpenFileWithMode(file, os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0600)
		if e != nil {
			return errors.New("新文件排他创建失败，未覆盖同名文件")
		}
		_, e = io.Copy(out, strings.NewReader(text))
		ce := out.Close()
		if e != nil || ce != nil {
			return errors.New("首次写入中断，可能留下部分新文件；请保留草稿并核对")
		}
		saved, e = readText(f, file)
		return e
	})
	return saved, e
}
