// The portable entry extracts the bundled native runtime to a content-addressed
// cache and forwards argv as a slice; it never uses a shell or embeds tickets.
package main

import (
	"archive/zip"
	"bytes"
	"crypto/sha256"
	_ "embed"
	"fmt"
	"github.com/gofrs/flock"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
)

//go:embed payload.zip
var payload []byte

func main() {
	if e := run(); e != nil {
		fmt.Fprintln(os.Stderr, "原生便携版未能启动，请使用完整四入口ZIP；没有自动连接或重用凭据")
		os.Exit(1)
	}
}
func run() error {
	cache, e := os.UserCacheDir()
	if e != nil {
		return e
	}
	sum := sha256.Sum256(payload)
	root := filepath.Join(cache, "FortermNativePortable", fmt.Sprintf("%x", sum[:12]))
	if e = os.MkdirAll(root, 0700); e != nil {
		return e
	}
	lock := flock.New(filepath.Join(root, ".extract.lock"), flock.SetPermissions(0600))
	if e = lock.Lock(); e != nil {
		return e
	}
	executable := filepath.Join(root, "forterm-native.exe")
	if _, e = os.Stat(filepath.Join(root, ".complete")); e != nil {
		archive, e := zip.NewReader(bytes.NewReader(payload), int64(len(payload)))
		if e != nil {
			lock.Close()
			return e
		}
		for _, file := range archive.File {
			parts := strings.SplitN(file.Name, "/", 2)
			if len(parts) < 2 || parts[1] == "" {
				continue
			}
			relative := parts[1]
			clean := filepath.Clean(filepath.FromSlash(relative))
			if filepath.IsAbs(clean) || clean == ".." || strings.HasPrefix(clean, ".."+string(os.PathSeparator)) {
				lock.Close()
				return fmt.Errorf("invalid embedded path")
			}
			target := filepath.Join(root, clean)
			if file.FileInfo().IsDir() {
				e = os.MkdirAll(target, 0700)
			} else {
				e = os.MkdirAll(filepath.Dir(target), 0700)
				if e == nil {
					var source io.ReadCloser
					source, e = file.Open()
					if e == nil {
						var destination *os.File
						destination, e = os.OpenFile(target, os.O_WRONLY|os.O_CREATE|os.O_TRUNC, 0600)
						if e == nil {
							_, e = io.Copy(destination, source)
							ce := destination.Close()
							if e == nil {
								e = ce
							}
						}
						source.Close()
					}
				}
			}
			if e != nil {
				lock.Close()
				return e
			}
		}
		if e = os.WriteFile(filepath.Join(root, ".complete"), sum[:], 0600); e != nil {
			lock.Close()
			return e
		}
	}
	lock.Close()
	command := exec.Command(executable, os.Args[1:]...)
	command.Dir = root
	hide(command)
	return command.Run()
}
