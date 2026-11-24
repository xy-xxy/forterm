package fileops

import (
	"bytes"
	"context"
	"crypto/rand"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"fmt"
	"forterm/native/internal/remotefs"
	"forterm/native/internal/transfers"
	"io"
	"os"
	"path"
	"strings"
	"time"
)

type Identity struct {
	Size      int64
	Mode      os.FileMode
	Modified  time.Time
	ModeKnown bool
}

func IdentityOf(info os.FileInfo) Identity {
	known := true
	if value, ok := info.(interface{ ModeKnown() bool }); ok {
		known = value.ModeKnown()
	}
	return Identity{info.Size(), info.Mode(), info.ModTime(), known}
}
func (v Identity) Matches(info os.FileInfo) bool {
	return info != nil && info.Mode().IsRegular() && info.Size() == v.Size && info.ModTime().Equal(v.Modified) && (!v.ModeKnown || info.Mode() == v.Mode)
}
func CheckParents(ctx context.Context, fs remotefs.Connection, file string) error {
	if _, err := SafePath(file); err != nil {
		return err
	}
	directory := path.Dir(file)
	if directory == "/" {
		return nil
	}
	prefix := ""
	for _, part := range strings.Split(strings.TrimPrefix(directory, "/"), "/") {
		prefix += "/" + part
		info, err := fs.Stat(ctx, prefix)
		if err != nil {
			return err
		}
		if !info.IsDir() || info.Mode()&os.ModeSymlink != 0 {
			return errors.New("父目录含链接或不是目录，已拒绝")
		}
	}
	return nil
}

type CopyRequest struct {
	Source, Destination         remotefs.Streams
	SourcePath, DestinationPath string
	SourceIdentity              Identity
	DestinationIdentity         *Identity
	Overwrite, Move             bool
}

func CopyFile(parent context.Context, request CopyRequest, progress func(int64, int64)) error {
	if _, err := SafePath(request.SourcePath); err != nil {
		return err
	}
	if _, err := SafePath(request.DestinationPath); err != nil {
		return err
	}
	srcKey, dstKey := transfers.ResourceKey(request.Source), transfers.ResourceKey(request.Destination)
	same := srcKey == dstKey
	if same && strings.EqualFold(request.SourcePath, request.DestinationPath) {
		return errors.New("源与目标相同，拒绝复制/移动")
	}
	if !request.SourceIdentity.Mode.IsRegular() || request.SourceIdentity.Size < 0 {
		return errors.New("只允许普通文件；目录和链接不递归复制或移动")
	}
	ctx, release, err := transfers.AcquireResources(parent, srcKey, dstKey)
	if err != nil {
		return err
	}
	defer release()
	if err = CheckParents(ctx, request.Source, request.SourcePath); err != nil {
		return err
	}
	if err = CheckParents(ctx, request.Destination, request.DestinationPath); err != nil {
		return err
	}
	sourceInfo, err := request.Source.Stat(ctx, request.SourcePath)
	if err != nil || !request.SourceIdentity.Matches(sourceInfo) {
		return errors.New("源文件在选择后发生变化，未开始")
	}
	if err = checkDestination(ctx, request); err != nil {
		return err
	}
	if same && request.Move {
		if request.Overwrite && request.DestinationIdentity != nil {
			if !supportsReplace(request.Destination) {
				return errors.New("此协议不支持已验证的覆盖移动，请改名或跳过")
			}
		}
		kind := "rename"
		if request.Overwrite && request.DestinationIdentity != nil {
			kind = "replace"
		}
		if err = request.Source.Mutate(ctx, kind, request.SourcePath, request.DestinationPath, 0); err != nil {
			return err
		}
		if _, err = request.Source.Stat(ctx, request.SourcePath); !os.IsNotExist(err) {
			return errors.New("移动后源路径状态未确认，请核对两端；未再次删除")
		}
		return nil
	}
	if request.Overwrite && request.DestinationIdentity != nil && !supportsReplace(request.Destination) {
		return errors.New("此协议不支持已验证的安全覆盖，请选择新名称或跳过")
	}
	watch := transfers.NewWatchdog(ctx, 15*time.Second, 30*time.Second)
	defer watch.Stop()
	ctx = watch.Context()
	watch.Data()
	token := make([]byte, 16)
	if _, err = rand.Read(token); err != nil {
		return err
	}
	temporary := path.Join(path.Dir(request.DestinationPath), ".forterm-copy-"+hex.EncodeToString(token))
	committed := false
	ownedTemporary := false
	defer func() {
		if !committed && ownedTemporary {
			cleanup, cancel := context.WithTimeout(context.Background(), 2*time.Second)
			defer cancel()
			if CheckParents(cleanup, request.Destination, temporary) == nil {
				_ = request.Destination.Mutate(cleanup, "remove", temporary, "", 0)
			}
		}
	}()
	firstHash := sha256.New()
	var copied int64
	writer := func(out io.Writer, reader io.Reader) error {
		ownedTemporary = true
		transfers.ReportPhase(ctx, transfers.PhaseData)
		if progress != nil {
			progress(0, request.SourceIdentity.Size)
		}
		p := &copyProgress{writer: io.MultiWriter(out, firstHash), total: request.SourceIdentity.Size, notify: progress, watch: watch}
		n, e := io.CopyBuffer(p, io.LimitReader(reader, request.SourceIdentity.Size+1), make([]byte, 32768))
		copied = n
		if e != nil {
			return e
		}
		if n != request.SourceIdentity.Size {
			return errors.New("复制期间源大小改变，未提交目标")
		}
		return nil
	}
	// A single FTP control connection cannot RETR and STOR concurrently. Keep
	// only small same-connection FTP copies in a bounded, cleared RAM buffer.
	_, isSSH := request.Source.(interface{ ProbeSFTP(context.Context) error })
	if same && !isSSH {
		if request.SourceIdentity.Size > 16<<20 {
			return errors.New("此FTP同连接复制限16MiB内存中转；较大文件请选择另一条已认证文件来源，不会自动登录")
		}
		var data bytes.Buffer
		err = request.Source.ReadStream(ctx, request.SourcePath, func(reader io.Reader, info os.FileInfo) error {
			if !request.SourceIdentity.Matches(info) {
				return errors.New("源已变化")
			}
			_, e := io.CopyBuffer(&data, io.LimitReader(reader, (16<<20)+1), make([]byte, 32768))
			return e
		})
		defer clear(data.Bytes())
		if err == nil && int64(data.Len()) != request.SourceIdentity.Size {
			err = errors.New("源大小已变化")
		}
		if err == nil {
			err = request.Destination.WriteStream(ctx, temporary, func(out io.Writer) error { return writer(out, bytes.NewReader(data.Bytes())) })
		}
	} else {
		err = request.Source.ReadStream(ctx, request.SourcePath, func(reader io.Reader, info os.FileInfo) error {
			if !request.SourceIdentity.Matches(info) {
				return errors.New("源已变化")
			}
			return request.Destination.WriteStream(ctx, temporary, func(out io.Writer) error { return writer(out, reader) })
		})
	}
	if err != nil {
		return fmt.Errorf("复制未提交，源保留，可能残留临时目标：%w", err)
	}
	if copied != request.SourceIdentity.Size {
		return errors.New("源大小未确认，未提交")
	}
	sourceHash := firstHash.Sum(nil)
	verify := func(fs remotefs.Streams, file string) ([]byte, error) {
		hash := sha256.New()
		e := fs.ReadStream(ctx, file, func(reader io.Reader, info os.FileInfo) error {
			if !info.Mode().IsRegular() || info.Size() != copied {
				return errors.New("校验大小变化")
			}
			_, e := io.CopyBuffer(&hashProgress{writer: hash, watch: watch}, io.LimitReader(reader, copied+1), make([]byte, 32768))
			return e
		})
		return hash.Sum(nil), e
	}
	transfers.ReportPhase(ctx, transfers.PhaseVerify)
	check, err := verify(request.Destination, temporary)
	if err != nil || !bytes.Equal(check, sourceHash) {
		return errors.New("目标关闭后字节校验失败，源保留")
	}
	current, err := request.Source.Stat(ctx, request.SourcePath)
	if err != nil || !request.SourceIdentity.Matches(current) {
		return errors.New("复制后源已改变，源保留且目标未提交")
	}
	currentHash, err := verify(request.Source, request.SourcePath)
	if err != nil || !bytes.Equal(currentHash, sourceHash) {
		return errors.New("源内容并发变化，未提交或删除源")
	}
	if err = ctx.Err(); err != nil {
		return err
	}
	if err = checkDestination(ctx, request); err != nil {
		return err
	}
	kind := "rename"
	if request.Overwrite && request.DestinationIdentity != nil {
		kind = "replace"
	}
	if err = request.Destination.Mutate(ctx, kind, temporary, request.DestinationPath, 0); err != nil {
		return err
	}
	committed = true
	finalHash, err := verify(request.Destination, request.DestinationPath)
	if err != nil || !bytes.Equal(finalHash, sourceHash) {
		return errors.New("目标提交后的校验未确认；源未删除，请核对目标")
	}
	if !request.Move {
		return nil
	}
	// Cross-source movement is intentionally not claimed as atomic. Only delete
	// after a fresh source content/metadata check and acknowledged target close.
	currentHash, err = verify(request.Source, request.SourcePath)
	if err != nil || !bytes.Equal(currentHash, sourceHash) {
		return errors.New("目标已复制，但源内容变化，保留源")
	}
	current, err = request.Source.Stat(ctx, request.SourcePath)
	if err != nil || !request.SourceIdentity.Matches(current) {
		return errors.New("目标已复制，但源元数据变化，保留源")
	}
	if err = ctx.Err(); err != nil {
		return errors.New("目标已复制，删除前取消，源保留")
	}
	if err = request.Source.Mutate(ctx, "remove", request.SourcePath, "", 0); err != nil {
		return errors.New("目标已验证，但源删除未获确认；请核对两端，跨源移动非原子")
	}
	return nil
}
func checkDestination(ctx context.Context, r CopyRequest) error {
	info, e := r.Destination.Stat(ctx, r.DestinationPath)
	if os.IsNotExist(e) {
		if r.DestinationIdentity != nil {
			return errors.New("已确认的目标在执行前变化")
		}
		return nil
	}
	if e != nil {
		return e
	}
	if !info.Mode().IsRegular() {
		return errors.New("目标不是普通文件，不覆盖目录或链接")
	}
	if !r.Overwrite || r.DestinationIdentity == nil {
		return errors.New("同名目标已存在，须明确跳过、改名或覆盖")
	}
	if !r.DestinationIdentity.Matches(info) {
		return errors.New("目标在覆盖确认后变化，已停止")
	}
	return nil
}
func supportsReplace(fs remotefs.Streams) bool {
	capability, ok := fs.(interface{ SupportsSafeReplace() bool })
	return ok && capability.SupportsSafeReplace()
}

type copyProgress struct {
	writer      io.Writer
	done, total int64
	notify      func(int64, int64)
	watch       *transfers.Watchdog
}

func (p *copyProgress) Write(data []byte) (int, error) {
	n, e := p.writer.Write(data)
	if n > 0 {
		p.done += int64(n)
		p.watch.Progress()
		if p.notify != nil {
			p.notify(p.done, p.total)
		}
	}
	return n, e
}

type hashProgress struct {
	writer io.Writer
	watch  *transfers.Watchdog
}

func (p *hashProgress) Write(data []byte) (int, error) {
	n, e := p.writer.Write(data)
	p.watch.Progress()
	return n, e
}
