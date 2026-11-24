//go:build windows

package vault

import (
	"errors"
	"github.com/danieljoos/wincred"
	"syscall"
)

type nativeVault struct{}

func New() Vault { return nativeVault{} }
func (nativeVault) Get(key string) ([]byte, error) {
	if !validKey(key) {
		return nil, errors.New("凭据标识无效")
	}
	credential, err := wincred.GetGenericCredential(service + "/" + key)
	if errors.Is(err, syscall.Errno(1168)) {
		return nil, ErrNotFound
	}
	if err != nil {
		return nil, errors.New("Windows凭据管理器不可用")
	}
	return append([]byte(nil), credential.CredentialBlob...), nil
}
func (nativeVault) Set(key string, data []byte) error {
	if !validKey(key) || len(data) > 2560 {
		return errors.New("凭据超出Windows安全存储限制")
	}
	credential := wincred.NewGenericCredential(service + "/" + key)
	credential.CredentialBlob = append([]byte(nil), data...)
	if err := credential.Write(); err != nil {
		return errors.New("Windows凭据保存失败，未回退明文")
	}
	return nil
}
func (nativeVault) Delete(key string) error {
	if !validKey(key) {
		return errors.New("凭据标识无效")
	}
	credential, err := wincred.GetGenericCredential(service + "/" + key)
	if errors.Is(err, syscall.Errno(1168)) {
		return nil
	}
	if err != nil {
		return errors.New("Windows凭据不可用")
	}
	if err = credential.Delete(); err != nil {
		return errors.New("Windows凭据删除未获确认")
	}
	return nil
}
