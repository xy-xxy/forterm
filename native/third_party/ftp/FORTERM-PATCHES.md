Base: github.com/jlaffaye/ftp v0.2.4. Original LICENSE retained.

Preserves unix.mode metadata (including special bits), recognizes Unix symlink
MLST facts, and exposes a guarded Command method for HELP SITE/SITE CHMOD.
Control characters in raw commands are rejected; no debug output is enabled.
