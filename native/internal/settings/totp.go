package settings

import (
	"crypto/hmac"
	"crypto/sha1"
	"crypto/sha256"
	"crypto/sha512"
	"encoding/base32"
	"encoding/binary"
	"errors"
	"fmt"
	"hash"
	"net/url"
	"strconv"
	"strings"
	"time"
)

func textUnits(value string) int {
	n := 0
	for _, r := range value {
		if r > 0xffff {
			n += 2
		} else {
			n++
		}
	}
	return n
}
func validTOTP(p TotpParameters) bool {
	return (p.Algorithm == "SHA1" || p.Algorithm == "SHA256" || p.Algorithm == "SHA512") && (p.Digits == 6 || p.Digits == 8) && p.Period >= 5 && p.Period <= 300
}
func DecodeBase32(input string) ([]byte, error) {
	if textUnits(input) > 1024 {
		return nil, errors.New("验证器key格式无效")
	}
	clean := strings.ToUpper(strings.NewReplacer(" ", "", "\t", "", "\r", "", "\n", "", "-", "").Replace(input))
	raw := strings.TrimRight(clean, "=")
	padding := len(clean) - len(raw)
	if raw == "" || padding > 6 {
		return nil, errors.New("验证器key必须是Base32字符")
	}
	for _, c := range raw {
		if !(c >= 'A' && c <= 'Z' || c >= '2' && c <= '7') {
			return nil, errors.New("验证器key必须是Base32字符")
		}
	}
	expected, ok := map[int]int{0: 0, 2: 6, 4: 4, 5: 3, 7: 1}[len(raw)%8]
	if !ok || (padding > 0 && padding != expected) {
		return nil, errors.New("验证器key长度或填充无效")
	}
	decoded, e := base32.StdEncoding.WithPadding(base32.NoPadding).DecodeString(raw)
	if e != nil || len(decoded) < 10 || len(decoded) > 128 || base32.StdEncoding.WithPadding(base32.NoPadding).EncodeToString(decoded) != raw {
		for i := range decoded {
			decoded[i] = 0
		}
		return nil, errors.New("验证器key必须为有效的10–128字节Base32密钥")
	}
	return decoded, nil
}
func NormalizeTOTP(input string, p TotpParameters) ([]byte, TotpParameters, error) {
	if textUnits(input) > 4096 {
		return nil, p, errors.New("验证器配置格式无效")
	}
	if p.Algorithm == "" && p.Digits == 0 && p.Period == 0 {
		p = TotpParameters{Algorithm: "SHA1", Digits: 6, Period: 30}
	}
	secret := strings.TrimSpace(input)
	if strings.HasPrefix(strings.ToLower(secret), "otpauth:") {
		u, e := url.Parse(secret)
		if e != nil || !strings.EqualFold(u.Scheme, "otpauth") || !strings.EqualFold(u.Hostname(), "totp") || u.User != nil || u.Port() != "" || u.Fragment != "" {
			return nil, p, errors.New("仅支持不含凭据或片段的otpauth TOTP")
		}
		label, e := url.PathUnescape(strings.TrimPrefix(u.EscapedPath(), "/"))
		if e != nil || label == "" || textUnits(label) > 256 {
			return nil, p, errors.New("验证器URI标签无效")
		}
		for _, r := range label {
			if r < 32 {
				return nil, p, errors.New("验证器URI标签无效")
			}
		}
		query, e := url.ParseQuery(u.RawQuery)
		if e != nil {
			return nil, p, errors.New("验证器URI参数无效")
		}
		allowed := map[string]bool{"secret": true, "issuer": true, "algorithm": true, "digits": true, "period": true}
		for key, values := range query {
			if !allowed[key] || len(values) != 1 {
				return nil, p, errors.New("验证器URI含不支持或重复参数")
			}
		}
		// A URI is a standalone configuration, never an override on prior UI fields.
		p = TotpParameters{Algorithm: "SHA1", Digits: 6, Period: 30}
		if value := query.Get("algorithm"); value != "" {
			p.Algorithm = value
		}
		if value := query.Get("digits"); value != "" {
			if value != "6" && value != "8" {
				return nil, p, errors.New("验证器位数无效")
			}
			p.Digits, _ = strconv.Atoi(value)
		}
		if value := query.Get("period"); value != "" {
			if len(value) > 3 {
				return nil, p, errors.New("验证器周期无效")
			}
			for _, r := range value {
				if r < '0' || r > '9' {
					return nil, p, errors.New("验证器周期无效")
				}
			}
			p.Period, _ = strconv.Atoi(value)
		}
		secret = query.Get("secret")
	}
	if !validTOTP(p) {
		return nil, p, errors.New("验证器周期须为5–300秒，位数为6或8，算法为SHA1/SHA256/SHA512")
	}
	decoded, e := DecodeBase32(secret)
	return decoded, p, e
}

func GenerateTOTP(seed []byte, p TotpParameters, at time.Time) (string, int, error) {
	var algorithm func() hash.Hash
	switch p.Algorithm {
	case "SHA1":
		algorithm = sha1.New
	case "SHA256":
		algorithm = sha256.New
	case "SHA512":
		algorithm = sha512.New
	default:
		return "", 0, errors.New("TOTP算法无效")
	}
	if !validTOTP(p) || len(seed) < 10 || len(seed) > 128 || at.Unix() < 0 {
		return "", 0, errors.New("TOTP参数或时间无效")
	}
	var counter [8]byte
	binary.BigEndian.PutUint64(counter[:], uint64(at.Unix()/int64(p.Period)))
	mac := hmac.New(algorithm, seed)
	mac.Write(counter[:])
	digest := mac.Sum(nil)
	defer func() {
		for i := range digest {
			digest[i] = 0
		}
		for i := range counter {
			counter[i] = 0
		}
	}()
	offset := int(digest[len(digest)-1] & 15)
	value := binary.BigEndian.Uint32(digest[offset:offset+4]) & 0x7fffffff
	modulo := uint32(1000000)
	if p.Digits == 8 {
		modulo = 100000000
	}
	return fmt.Sprintf("%0*d", p.Digits, value%modulo), p.Period - int(at.Unix()%int64(p.Period)), nil
}
