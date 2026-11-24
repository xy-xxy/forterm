import { useState } from "react";
import { Activity, X } from "lucide-react";
import type { KeepAliveSettings } from "../shared/types";
export function KeepAliveDialog({
  settings,
  onClose,
  onSave,
  onError,
}: {
  settings: KeepAliveSettings;
  onClose: () => void;
  onSave: (value: KeepAliveSettings) => void;
  onError: (e: unknown) => void;
}) {
  const [value, setValue] = useState({ ...settings }),
    [busy, setBusy] = useState(false);
  const save = async () => {
    setBusy(true);
    try {
      onSave(await window.forterm.setKeepAlive(value));
      onClose();
    } catch (e) {
      onError(e);
    } finally {
      setBusy(false);
    }
  };
  return (
    <div className="overlay">
      <form
        className="modal heartbeat-modal"
        onSubmit={(e) => {
          e.preventDefault();
          void save();
        }}
      >
        <header>
          <div>
            <div className="eyebrow">KEEP ALIVE</div>
            <h2>会话心跳</h2>
          </div>
          <button
            className="icon-button"
            type="button"
            aria-label="关闭心跳设置"
            onClick={onClose}
          >
            <X size={20} />
          </button>
        </header>
        <div className="heartbeat-intro">
          <Activity size={24} />
          <p>
            定时检测连接响应，减少网络空闲造成的断线。适用于终端、文件传输和跳板连接。
          </p>
        </div>
        <label className="check">
          <input
            type="checkbox"
            checked={value.enabled}
            onChange={(e) =>
              setValue((v) => ({ ...v, enabled: e.target.checked }))
            }
          />
          启用会话心跳
        </label>
        <label className="heartbeat-interval">
          心跳间隔（秒）
          <input
            type="number"
            required
            min={5}
            max={600}
            step={1}
            disabled={!value.enabled}
            value={value.intervalSeconds}
            onChange={(e) =>
              setValue((v) => ({
                ...v,
                intervalSeconds: Number(e.target.value),
              }))
            }
          />
          <small>默认 15 秒，可设置为 5–600 秒。</small>
        </label>
        <label className="check">
          <input
            type="checkbox"
            disabled={!value.enabled}
            checked={value.sftpProbe}
            onChange={(e) =>
              setValue((v) => ({ ...v, sftpProbe: e.target.checked }))
            }
          />
          保持 SFTP 文件通道活跃
        </label>
        <p className="heartbeat-help">
          终端使用 SSH 协议保活；已打开的 SFTP
          通道定时进行只读路径查询。服务器强制设置的会话时限仍然有效。
        </p>
        <footer>
          <span className="subtle">
            保存后对新会话生效，已有连接保持原设置。
          </span>
          <button className="secondary" type="button" onClick={onClose}>
            取消
          </button>
          <button className="primary" disabled={busy} type="submit">
            {busy ? "保存中…" : "保存设置"}
          </button>
        </footer>
      </form>
    </div>
  );
}
