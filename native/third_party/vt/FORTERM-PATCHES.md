Base: github.com/charmbracelet/x/vt v0.1.0. MIT license retained.

Local native integration fixes: retain grapheme identity across io.Write boundaries by joining a continuing cluster back into the previous cell; reset that opportunity on VT controls; wrap wide cells at the right margin before inserting; set delayed-wrap when a wide cell fills the final two columns. No browser renderer is included.
