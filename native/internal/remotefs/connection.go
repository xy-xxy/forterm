package remotefs

import (
	"context"
	"forterm/native/internal/transfers"
	"io"
	"os"
	"time"
)

type Entry struct {
	ModeKnown bool
	Name      string
	Size      int64
	Directory bool
	Mode      os.FileMode
	Modified  time.Time
}
type Connection interface {
	List(context.Context, string) ([]Entry, error)
	Stat(context.Context, string) (os.FileInfo, error)
	RunTransfer(context.Context, transfers.Spec, func(int64, int64)) error
	Close()
	Closed() bool
}

// Operations are separate from terminal/exec capabilities. A denied file
// operation must not disconnect another channel or silently follow links.
type Operations interface {
	Connection
	RealPath(context.Context, string) (string, error)
	Mutate(context.Context, string, string, string, os.FileMode) error
	ChmodSupported() bool
}

// Streams are bounded by the operation context and never authenticate again.
type Streams interface {
	Operations
	ReadStream(context.Context, string, func(io.Reader, os.FileInfo) error) error
	WriteStream(context.Context, string, func(io.Writer) error) error
}
