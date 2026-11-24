package uiresponse

import "testing"

func TestCheckModeCannotConsumeControllerArguments(t *testing.T) {
	if !Requested([]string{"--ui-check"}) {
		t.Fatal("explicit offline mode missing")
	}
	for _, args := range [][]string{nil, {"-newtab", "--ui-check", "-url", "ssh://fixture.invalid"}, {"--ui-check", "-url", "ssh://fixture.invalid"}, {"--entry=xftp", "--ui-check"}, {"--ui-check=1"}} {
		if Requested(args) {
			t.Fatal("normal controller arguments entered offline mode")
		}
	}
}
