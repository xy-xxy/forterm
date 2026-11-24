//go:build !windows && !darwin

package vault

import "errors"

type unavailable struct{}

func New() Vault { return unavailable{} }
func (unavailable) Get(string) ([]byte, error) {
	return nil, errors.New("此平台的系统凭据存储尚未接入")
}
func (unavailable) Set(string, []byte) error {
	return errors.New("系统安全存储不可用，未回退明文")
}
func (unavailable) Delete(string) error { return errors.New("系统安全存储不可用") }
