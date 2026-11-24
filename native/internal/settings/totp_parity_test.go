package settings

import (
	"strings"
	"testing"
	"time"
)

const publicSeed = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"

func TestTOTPPeriodBoundariesAndStandaloneURIDefaults(t *testing.T) {
	for _, period := range []int{5, 300} {
		seed, p, e := NormalizeTOTP(publicSeed, TotpParameters{Algorithm: "SHA1", Digits: 6, Period: period})
		if e != nil {
			t.Fatal("legacy-valid boundary rejected", period, e)
		}
		if _, _, e = GenerateTOTP(seed, p, time.Unix(59, 0)); e != nil {
			t.Fatal(e)
		}
	}
	_, p, e := NormalizeTOTP("otpauth://totp/test?secret="+publicSeed, TotpParameters{Algorithm: "SHA512", Digits: 8, Period: 60})
	if e != nil || p.Algorithm != "SHA1" || p.Digits != 6 || p.Period != 30 {
		t.Fatal("URI inherited unrelated prior fields", p, e)
	}
}
func TestTOTPStrictURIAndCanonicalBase32(t *testing.T) {
	for _, input := range []string{"otpauth://totp/test?secret=" + publicSeed + "&secret=" + publicSeed, "otpauth://totp/test?secret=" + publicSeed + "&counter=1", "otpauth://user:pass@totp/test?secret=" + publicSeed, "otpauth://totp/test?secret=" + publicSeed + "#fragment", "otpauth://totp/?secret=" + publicSeed, "otpauth://totp/bad%00label?secret=" + publicSeed, publicSeed + "=", strings.Repeat("A", 1025)} {
		if _, _, e := NormalizeTOTP(input, TotpParameters{}); e == nil {
			t.Fatal("legacy-invalid configuration accepted")
		}
	}
	// 11 bytes encoded: the final symbol has unused low bits, which must be zero.
	for _, key := range []string{"AAAAAAAAAAAAAAAAAB", "AAAAAAAAAAAAAAAAAA====="} {
		if _, e := DecodeBase32(key); e == nil {
			t.Fatal("noncanonical trailing bits/padding accepted")
		}
	}
	for _, period := range []int{4, 301} {
		if _, _, e := NormalizeTOTP(publicSeed, TotpParameters{Algorithm: "SHA1", Digits: 6, Period: period}); e == nil {
			t.Fatal("invalid period accepted")
		}
	}
}
func TestLeadingZeroAndOtherRFCAlgorithms(t *testing.T) {
	cases := []struct{ seed, algorithm, want string }{{"12345678901234567890123456789012", "SHA256", "46119246"}, {"1234567890123456789012345678901234567890123456789012345678901234", "SHA512", "90693936"}}
	for _, c := range cases {
		got, _, e := GenerateTOTP([]byte(c.seed), TotpParameters{Algorithm: c.algorithm, Digits: 8, Period: 30}, time.Unix(59, 0))
		if e != nil || got != c.want {
			t.Fatal("RFC algorithm vector mismatch", got, e)
		}
	}
	code, _, e := GenerateTOTP([]byte("12345678901234567890"), TotpParameters{Algorithm: "SHA1", Digits: 8, Period: 30}, time.Unix(1111111109, 0))
	if e != nil || code != "07081804" {
		t.Fatal("leading zero lost")
	}
}
