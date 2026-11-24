import { useEffect, useState } from "react";
import { X } from "lucide-react";
import type { FilePermissions } from "../shared/types";
export function PermissionsDialog({
  sessionId,
  path,
  onClose,
  onDone,
  onError,
}: {
  sessionId: string;
  path: string;
  onClose: () => void;
  onDone: () => void;
  onError: (e: unknown) => void;
}) {
  const [value, setValue] = useState<FilePermissions | null>(null),
    [mode, setMode] = useState(""),
    [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const load = async () => {
    setBusy(true);
    try {
      const p = await window.forterm.filePermissions(sessionId, path);
      setValue(p);
      setMode(
        p.mode === null ? "" : (p.mode & 0o777).toString(8).padStart(3, "0"),
      );
      setError("");
    } catch (e) {
      setError(
        String(e).replace(/^Error invoking remote method '[^']+': Error: /, ""),
      );
    } finally {
      setBusy(false);
    }
  };
  useEffect(() => {
    void load();
  }, [sessionId, path]);
  const number = /^[0-7]{3}$/.test(mode) ? parseInt(mode, 8) : null;
  return (
    <div className="overlay">
      <section className="modal permission-modal" aria-label="远程权限">
        <header>
          <h2>权限</h2>
          <button aria-label="关闭权限" disabled={busy} onClick={onClose}>
            <X size={18} />
          </button>
        </header>
        <p className="permission-target" title={path}>
          {path}
        </p>
        <p className="subtle">
          固定文件连接：{sessionId}。只修改此项，不递归，不改所有者。
        </p>
        {value?.mode !== null && value?.mode !== undefined ? (
          <>
            <p>
              当前权限：{value.mode.toString(8).padStart(3, "0")}；特殊位保留：
              {(value.mode & 0o7000).toString(8).padStart(4, "0")}（
              {[
                value.mode & 0o4000 ? "setuid" : "",
                value.mode & 0o2000 ? "setgid" : "",
                value.mode & 0o1000 ? "sticky" : "",
              ]
                .filter(Boolean)
                .join(" / ") || "无额外特殊位"}
              ）
            </p>
            <div className="permission-grid">
              {["所有者", "所属组", "其他用户"].map((group, i) => (
                <fieldset key={group} disabled={busy || !value.supported}>
                  <legend>{group}</legend>
                  {["读", "写", "执行"].map((label, j) => {
                    const bit = 1 << (8 - i * 3 - j);
                    return (
                      <label className="check" key={label}>
                        <input
                          type="checkbox"
                          aria-label={`${group}${label}`}
                          checked={number !== null && !!(number & bit)}
                          onChange={(e) =>
                            setMode(
                              ((number ?? 0) ^ bit)
                                .toString(8)
                                .padStart(3, "0"),
                            )
                          }
                        />
                        {label}
                      </label>
                    );
                  })}
                </fieldset>
              ))}
            </div>
            <label>
              普通权限八进制
              <input
                aria-label="普通权限八进制"
                value={mode}
                maxLength={3}
                disabled={busy || !value.supported}
                onChange={(e) => setMode(e.target.value)}
              />
            </label>
          </>
        ) : (
          <p>权限未知，未显示为 000。</p>
        )}
        {value?.message && <p className="editor-warning">{value.message}</p>}
        {error && (
          <p className="editor-warning" role="alert">
            {error}
          </p>
        )}
        <footer>
          <button
            className="secondary"
            disabled={busy}
            onClick={() => void load()}
          >
            重新读取权限
          </button>
          <button className="secondary" disabled={busy} onClick={onClose}>
            取消
          </button>
          <button
            className="primary"
            disabled={busy || !value?.supported || number === null}
            onClick={async () => {
              if (!value || number === null) return;
              setBusy(true);
              try {
                const result = await window.forterm.setFilePermissions(
                  sessionId,
                  path,
                  number,
                  value.mode!,
                );
                setValue(result);
                onDone();
                onClose();
              } catch (e) {
                setError(
                  String(e).replace(
                    /^Error invoking remote method '[^']+': Error: /,
                    "",
                  ),
                );
              } finally {
                setBusy(false);
              }
            }}
          >
            应用权限
          </button>
        </footer>
      </section>
    </div>
  );
}
