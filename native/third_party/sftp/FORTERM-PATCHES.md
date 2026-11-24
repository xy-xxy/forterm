Base: github.com/pkg/sftp v1.13.12. Original LICENSE retained.

Adds OpenFileWithMode so private new text/temporary files send 0600 in the
initial SSH_FXP_OPEN packet. Chmod after an unprotected create is insufficient.
Existing OpenFile API and its wire behavior are unchanged.
