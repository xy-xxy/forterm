// Package vault stores only explicitly saved manual credentials using the OS
// credential service. There is no plaintext-file or command-line fallback.
package vault

import (
	"errors"
	"strings"
)

var ErrNotFound = errors.New("未保存该凭据")

type Vault interface {
	Get(string) ([]byte, error)
	Set(string, []byte) error
	Delete(string) error
}

func validKey(key string) bool {
	return len(key) > 0 && len(key) < 240 && !strings.ContainsAny(key, "\x00\r\n")
}

const service = "Forterm Native"
