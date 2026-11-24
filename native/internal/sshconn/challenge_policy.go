package sshconn

import "strings"

func singlePasswordQuestion(questions []string, echo []bool) bool {
	if len(questions) != 1 || len(echo) != 1 || echo[0] {
		return false
	}
	q := strings.ToLower(strings.TrimSpace(questions[0]))
	q = strings.TrimRight(q, ":： ")
	return q == "password" || q == "密码" || q == "登录密码" || q == "login password"
}
func limitChallengeText(s string, n int) string {
	r := []rune(s)
	if len(r) > n {
		return string(r[:n]) + "…"
	}
	return s
}
