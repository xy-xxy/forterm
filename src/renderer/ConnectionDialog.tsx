import { useState } from "react";
import { X, KeyRound } from "lucide-react";
import type { Profile, Connection } from "../shared/types";
export function ConnectionDialog({
  profiles,
  profile,
  onClose,
  onSaved,
  onConnected,
  onError,
  initialProtocol,
  fileOnly = false,
}: {
  profiles: Profile[];
  profile?: Profile;
  initialProtocol?: "sftp" | "ftp";
  fileOnly?: boolean;
  onClose: () => void;
  onSaved: (p: Profile[]) => void;
  onConnected: (id: string) => void;
  onError: (e: unknown) => void;
}) {
  const [form, setForm] = useState({
    title: profile?.title || "",
    host: profile?.host || "",
    port: profile?.port || (initialProtocol === "ftp" ? 21 : 22),
    username: profile?.username || "",
    password: "",
    protocol: profile?.protocol || initialProtocol || "ssh",
    group: profile?.group || "我的连接",
    keyPath: profile?.keyPath || "",
    passphrase: "",
    jumpId: profile?.jumpId || "",
  });
  const [save, setSave] = useState(!!profile);
  const [remember, setRemember] = useState(false);
  const [busy, setBusy] = useState(false);
  const update = (key: string, value: string | number) =>
    setForm((f) => ({ ...f, [key]: value }));
  const submit = async (connect: boolean) => {
    setBusy(true);
    try {
      if (!form.host.trim() || !form.username.trim())
        throw new Error("请填写地址和用户名");
      const connection: Connection = {
        ...form,
        host: form.host.trim(),
        protocol: form.protocol as Connection["protocol"],
        password: form.password || undefined,
        keyPath: form.keyPath || undefined,
        jumpId: form.jumpId || undefined,
        temporary: false,
      };
      if (save || !connect)
        onSaved(
          await window.forterm.saveProfile({
            ...connection,
            id: profile?.id,
            group: form.group,
            password: remember ? form.password : undefined,
          }),
        );
      if (connect) {
        if (profile && !form.password && !form.keyPath && profile.hasPassword)
          onConnected(await window.forterm.connectProfile(profile.id));
        else onConnected(await window.forterm.connect(connection));
      }
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
        className="modal connection-modal"
        onSubmit={(e) => {
          e.preventDefault();
          void submit(true);
        }}
      >
        <header>
          <div>
            <div className="eyebrow">CONNECTION</div>
            <h2>{profile ? "编辑连接" : "新建连接"}</h2>
          </div>
          <button
            type="button"
            className="icon-button"
            onClick={onClose}
            aria-label="关闭"
          >
            <X size={20} />
          </button>
        </header>
        {form.protocol === "ftp" && (
          <p className="editor-warning">
            FTP 为明文被动模式，需独立地址与认证；不使用 SSH
            私钥/跳板/TOTP。取消 FTP 传输会关闭该文件连接。
          </p>
        )}
        <div className="form-grid">
          <label>
            连接名称
            <input
              value={form.title}
              onChange={(e) => update("title", e.target.value)}
              placeholder="例如：开发环境"
              maxLength={256}
            />
          </label>
          <label>
            协议
            <select
              value={form.protocol}
              onChange={(e) =>
                setForm((old) => ({
                  ...old,
                  protocol: e.target.value as typeof old.protocol,
                  port: e.target.value === "ftp" ? 21 : 22,
                  keyPath: e.target.value === "ftp" ? "" : old.keyPath,
                  jumpId: e.target.value === "ftp" ? "" : old.jumpId,
                }))
              }
            >
              {!fileOnly && <option value="ssh">SSH 终端</option>}
              <option value="sftp">SFTP 文件管理</option>
              <option value="ftp">FTP 文件管理（明文 / 被动模式）</option>
            </select>
          </label>
          <label>
            地址
            <input
              required
              autoFocus
              value={form.host}
              onChange={(e) => update("host", e.target.value)}
              placeholder="主机或堡垒机入口"
            />
          </label>
          <label>
            端口
            <input
              required
              type="number"
              min={1}
              max={65535}
              value={form.port}
              onChange={(e) => update("port", Number(e.target.value))}
            />
          </label>
          <label>
            用户名
            <input
              required
              value={form.username}
              onChange={(e) => update("username", e.target.value)}
              autoComplete="off"
            />
          </label>
          <label>
            密码
            <input
              type="password"
              value={form.password}
              onChange={(e) => update("password", e.target.value)}
              autoComplete="new-password"
              placeholder={
                profile?.hasPassword
                  ? "已保存，留空使用原密码"
                  : "支持密码及交互认证"
              }
            />
          </label>
          <label>
            分组
            <input
              value={form.group}
              onChange={(e) => update("group", e.target.value)}
            />
          </label>
          <label>
            跳板机
            <select
              value={form.jumpId}
              onChange={(e) => update("jumpId", e.target.value)}
            >
              <option value="">直接连接</option>
              {profiles
                .filter((p) => p.id !== profile?.id && p.protocol === "ssh")
                .map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.title || p.host}
                  </option>
                ))}
            </select>
          </label>
          <label className="span-two">
            SSH 私钥
            <div className="input-row">
              <input
                readOnly
                value={form.keyPath}
                placeholder="可选：选择本机私钥"
              />
              <button
                type="button"
                className="secondary"
                onClick={() =>
                  void window.forterm
                    .pickKey()
                    .then((p) => p && update("keyPath", p))
                    .catch(onError)
                }
              >
                <KeyRound size={15} />
                选择
              </button>
              {form.keyPath && (
                <button
                  type="button"
                  className="secondary"
                  onClick={() => update("keyPath", "")}
                >
                  清除
                </button>
              )}
            </div>
          </label>
          {form.keyPath && (
            <label className="span-two">
              私钥口令
              <input
                type="password"
                value={form.passphrase}
                onChange={(e) => update("passphrase", e.target.value)}
                placeholder="仅用于本次连接"
              />
            </label>
          )}
        </div>
        <label className="check">
          <input
            type="checkbox"
            checked={save}
            disabled={!!profile}
            onChange={(e) => setSave(e.target.checked)}
          />
          保存到连接列表
        </label>
        {save && (
          <label className="check">
            <input
              type="checkbox"
              checked={remember}
              onChange={(e) => setRemember(e.target.checked)}
            />
            使用系统加密保存密码
          </label>
        )}
        <footer>
          <span className="subtle">服务器指纹将在首次连接时确认</span>
          <button
            disabled={busy}
            type="button"
            className="secondary"
            onClick={() => void submit(false)}
          >
            仅保存
          </button>
          <button disabled={busy} className="primary" type="submit">
            {busy ? "处理中…" : "连接"}
          </button>
        </footer>
      </form>
    </div>
  );
}
