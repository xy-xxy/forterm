import { BastionAccessPanel } from "./BastionAccessPanel";
import { useEffect, useRef, useState } from "react";
import {
  Plus,
  X,
  ShieldCheck,
  Pencil,
  Trash2,
  Copy,
  KeyRound,
} from "lucide-react";
import type {
  BastionAccount,
  BastionAccountInput,
  TotpReading,
  TotpParameters,
} from "../shared/bastion";
import { DEFAULT_TOTP } from "../shared/bastion";
import { CompactToolbar } from "./CompactToolbar";
const message = (error: unknown) =>
  String(error instanceof Error ? error.message : error).replace(
    /^Error invoking remote method '[^']+': Error: /,
    "",
  );
function CodeView({
  account,
  onError,
}: {
  account: BastionAccount;
  onError: (e: unknown) => void;
}) {
  const [reveal, setReveal] = useState(false),
    [reading, setReading] = useState<TotpReading | null>(null),
    [now, setNow] = useState(Date.now()),
    [manual, setManual] = useState(""),
    [notice, setNotice] = useState("");
  const current = useRef<TotpReading | null>(null),
    busy = useRef(false),
    epoch = useRef(0);
  useEffect(() => {
    const clear = () => {
      epoch.current++;
      current.current = null;
      setReading(null);
      setManual("");
      setReveal(false);
    };
    const visibility = () => {
      if (document.visibilityState !== "visible") clear();
    };
    document.addEventListener("visibilitychange", visibility);
    const off = window.forterm.onEvent((event) => {
      if (event.type === "window-visibility" && !event.visible) clear();
    });
    return () => {
      off();
      epoch.current++;
      document.removeEventListener("visibilitychange", visibility);
    };
  }, []);
  useEffect(() => {
    if (!reveal || document.visibilityState !== "visible") return;
    const generation = ++epoch.current;
    const tick = async () => {
      const time = Date.now();
      setNow(time);
      if (
        busy.current ||
        (current.current &&
          time < current.current.expiresAt &&
          time >= current.current.generatedAt - 1000)
      )
        return;
      busy.current = true;
      try {
        const result = await window.forterm.bastionCode(account.id);
        if (
          epoch.current === generation &&
          document.visibilityState === "visible"
        ) {
          current.current = result;
          setReading(result);
        }
      } catch (e) {
        if (epoch.current === generation) {
          current.current = null;
          setReading(null);
          setReveal(false);
          onError(e);
        }
      } finally {
        busy.current = false;
      }
    };
    void tick();
    const timer = setInterval(() => void tick(), 250);
    return () => {
      clearInterval(timer);
      epoch.current++;
      current.current = null;
    };
  }, [
    reveal,
    account.id,
    account.totpEnabled,
    account.hasTotpSecret,
    account.totp.period,
    account.totp.algorithm,
    account.totp.digits,
  ]);
  const hide = () => {
    epoch.current++;
    setReveal(false);
    current.current = null;
    setReading(null);
  };
  const remaining = reading
    ? Math.max(0, Math.ceil((reading.expiresAt - now) / 1000))
    : 0;
  return (
    <section className="account-code" aria-label="本地验证码">
      <div className="code-controls">
        <button
          className="secondary"
          disabled={!account.totpEnabled || !account.hasTotpSecret}
          onClick={() => {
            setNotice("");
            if (reveal) hide();
            else setReveal(true);
          }}
        >
          {reveal ? "隐藏验证码" : "查看当前验证码"}
        </button>
        {reveal && (
          <>
            <output aria-label="当前验证码" className="totp-code">
              {reading && remaining > 0 ? reading.code : "…"}
            </output>
            <span aria-label="验证码倒计时">{remaining} 秒</span>
            <button
              className="secondary"
              disabled={!reading || remaining === 0}
              onClick={() => {
                const generation = epoch.current;
                void window.forterm
                  .copyBastionCode(account.id)
                  .then((result) => {
                    if (
                      epoch.current !== generation ||
                      document.visibilityState !== "visible"
                    )
                      return;
                    current.current = result;
                    setReading(result);
                    setNow(Date.now());
                    setNotice("已复制到系统剪贴板");
                  })
                  .catch(onError);
              }}
            >
              <Copy size={14} />
              复制当前码
            </button>
          </>
        )}
      </div>
      <div className="manual-code">
        <label>
          手动验证码
          <input
            aria-label="手动验证码"
            type="password"
            autoComplete="one-time-code"
            inputMode="numeric"
            maxLength={8}
            value={manual}
            onChange={(e) => setManual(e.target.value.replace(/\D/g, ""))}
          />
        </label>
        <button
          className="secondary"
          disabled={!/^(?:\d{6}|\d{8})$/.test(manual)}
          onClick={() =>
            void window.forterm
              .copyManualCode(manual)
              .then(() => {
                setManual("");
                setNotice("已复制到系统剪贴板");
              })
              .catch(onError)
          }
        >
          复制手动码
        </button>
      </div>
      {notice && (
        <p className="subtle" role="status">
          {notice}
        </p>
      )}
      <p className="subtle">
        仅在主动查看时生成；隐藏页面或关闭后停止并清空。复制会写入系统剪贴板。手动码只在本地输入，不发送到
        SSH 或服务器。
      </p>
    </section>
  );
}
function AccountForm({
  account,
  onSaved,
  onClose,
  onError,
}: {
  account?: BastionAccount;
  onSaved: () => void;
  onClose: () => void;
  onError: (e: unknown) => void;
}) {
  const [name, setName] = useState(account?.name ?? ""),
    [url, setUrl] = useState(account?.portalUrl ?? ""),
    [username, setUsername] = useState(account?.username ?? "");
  const [savePassword, setSavePassword] = useState(!!account?.hasPassword),
    [password, setPassword] = useState("");
  const [enabled, setEnabled] = useState(!!account?.totpEnabled),
    [seed, setSeed] = useState(""),
    [clearSeed, setClearSeed] = useState(false),
    [parameters, setParameters] = useState<TotpParameters>({
      ...(account?.totp ?? DEFAULT_TOTP),
    });
  const [dirty, setDirty] = useState(false),
    [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const uri = /^otpauth:/i.test(seed.trim());
  const change = (action: () => void) => {
    setDirty(true);
    setError("");
    action();
  };
  useEffect(() => {
    void window.forterm
      .setDirty("bastion-configuration", dirty || busy)
      .catch(() => {});
    return () => {
      void window.forterm
        .setDirty("bastion-configuration", false)
        .catch(() => {});
    };
  }, [dirty, busy]);
  useEffect(() => {
    const guard = (e: BeforeUnloadEvent) => {
      if (dirty || busy) {
        e.preventDefault();
        e.returnValue = "";
      }
    };
    window.addEventListener("beforeunload", guard);
    return () => window.removeEventListener("beforeunload", guard);
  }, [dirty, busy]);
  const close = () => {
    if (busy) return;
    if (dirty && !confirm("账户配置尚未保存，丢弃修改？")) return;
    onClose();
  };
  const inspect = async () => {
    if (!seed.trim()) return;
    try {
      setParameters(await window.forterm.inspectTotp(seed));
      setError("");
    } catch (e) {
      setError(message(e));
    }
  };
  const save = async () => {
    if (busy) return;
    setBusy(true);
    setError("");
    const input: BastionAccountInput = {
      id: account?.id,
      name,
      portalUrl: url,
      username,
      passwordAction: savePassword ? (password ? "replace" : "keep") : "clear",
      password: savePassword && password ? password : undefined,
      totpEnabled: enabled,
      seedAction: clearSeed ? "clear" : seed.trim() ? "replace" : "keep",
      seedInput: seed.trim() || undefined,
      totp: parameters,
    };
    try {
      await window.forterm.saveBastionAccount(input);
      setPassword("");
      setSeed("");
      setDirty(false);
      onSaved();
    } catch (e) {
      setError(message(e));
    } finally {
      setBusy(false);
    }
  };
  return (
    <form
      className="account-form"
      onSubmit={(e) => {
        e.preventDefault();
        void save();
      }}
      autoComplete="off"
    >
      <CompactToolbar label="堡垒机账户配置">
        <strong className="toolbar-title">
          {account ? "编辑堡垒机账户" : "新增堡垒机账户"}
        </strong>
        <button
          type="button"
          aria-label="关闭账户编辑"
          disabled={busy}
          onClick={close}
        >
          <X size={18} />
        </button>
      </CompactToolbar>
      <div className="account-form-body">
        <div className="form-grid">
          <label>
            配置名称
            <input
              aria-label="堡垒机配置名称"
              required
              maxLength={128}
              value={name}
              onChange={(e) => change(() => setName(e.target.value))}
            />
          </label>
          <label>
            登录用户名
            <input
              aria-label="堡垒机登录用户名"
              required
              maxLength={256}
              autoComplete="off"
              value={username}
              onChange={(e) => change(() => setUsername(e.target.value))}
            />
          </label>
          <label className="span-two">
            入口地址
            <input
              aria-label="堡垒机入口地址"
              required
              placeholder="https://bastion.example/"
              type="url"
              value={url}
              onChange={(e) => change(() => setUrl(e.target.value))}
            />
            <small>
              仅填非秘密入口地址，不含凭据、Token、查询参数。本功能不会访问此地址。
            </small>
          </label>
        </div>
        <label className="check">
          <input
            type="checkbox"
            checked={savePassword}
            onChange={(e) =>
              change(() => {
                setSavePassword(e.target.checked);
                if (!e.target.checked) setPassword("");
              })
            }
          />
          保存堡垒机登录密码
        </label>
        {savePassword && (
          <label>
            堡垒机登录密码
            <input
              aria-label="堡垒机登录密码"
              type="password"
              autoComplete="new-password"
              value={password}
              required={!account?.hasPassword}
              placeholder={
                account?.hasPassword
                  ? "已保存；留空保留，填写替换"
                  : "填写后由系统加密保存"
              }
              onChange={(e) => change(() => setPassword(e.target.value))}
            />
          </label>
        )}
        {account?.hasPassword && (
          <button
            type="button"
            className="secondary"
            onClick={() =>
              change(() => {
                setSavePassword(false);
                setPassword("");
              })
            }
          >
            清除已保存密码
          </button>
        )}
        <p className="subtle">
          取消保存或点击清除后，需保存配置才会删除长期密码。与 SSH
          目标密码及控制器临时票据分开管理。
        </p>
        <label className="check">
          <input
            type="checkbox"
            checked={enabled}
            onChange={(e) => change(() => setEnabled(e.target.checked))}
          />
          启用手机验证器（TOTP）
        </label>
        {enabled && (
          <>
            <label>
              验证器 key / 配置 URI
              <input
                aria-label="验证器密钥"
                type="password"
                autoComplete="new-password"
                value={seed}
                required={!account?.hasTotpSecret || clearSeed}
                placeholder={
                  account?.hasTotpSecret && !clearSeed
                    ? "Key 已保存；留空保留，填写替换"
                    : "Base32 key 或 otpauth://totp/…"
                }
                onBlur={() => {
                  if (uri) void inspect();
                }}
                onChange={(e) =>
                  change(() => {
                    setSeed(e.target.value);
                    setClearSeed(false);
                  })
                }
              />
            </label>
            <div className="totp-parameters">
              <label>
                算法
                <select
                  aria-label="验证器算法"
                  disabled={uri}
                  value={parameters.algorithm}
                  onChange={(e) =>
                    change(() =>
                      setParameters((p) => ({
                        ...p,
                        algorithm: e.target
                          .value as TotpParameters["algorithm"],
                      })),
                    )
                  }
                >
                  <option value="SHA1">SHA-1</option>
                  <option value="SHA256">SHA-256</option>
                  <option value="SHA512">SHA-512</option>
                </select>
              </label>
              <label>
                位数
                <select
                  aria-label="验证码位数"
                  disabled={uri}
                  value={parameters.digits}
                  onChange={(e) =>
                    change(() =>
                      setParameters((p) => ({
                        ...p,
                        digits: Number(e.target.value) as 6 | 8,
                      })),
                    )
                  }
                >
                  <option value={6}>6 位</option>
                  <option value={8}>8 位</option>
                </select>
              </label>
              <label>
                周期（秒）
                <input
                  aria-label="验证码周期"
                  type="number"
                  required
                  min={5}
                  max={300}
                  disabled={uri}
                  value={parameters.period}
                  onChange={(e) =>
                    change(() =>
                      setParameters((p) => ({
                        ...p,
                        period: Number(e.target.value),
                      })),
                    )
                  }
                />
              </label>
            </div>
            <small>
              {uri
                ? "URI 中支持的参数优先，保存时由主进程校验。"
                : "默认六位、30 秒、SHA-1；高级参数应与验证器服务配置一致。"}
            </small>
          </>
        )}
        {account?.hasTotpSecret && (
          <button
            className="secondary"
            type="button"
            onClick={() =>
              change(() => {
                setClearSeed(true);
                setSeed("");
                setEnabled(false);
              })
            }
          >
            清除已保存 key
          </button>
        )}
        {clearSeed && <p className="subtle">保存后清除 key 并停用验证器。</p>}
        <p className="subtle">
          停用验证器会保留加密 key；清除需单独操作。未知厂商算法未验证；不支持
          HOTP，不会自动登录或向 SSH 输入验证码。
        </p>
        {error && (
          <p className="editor-warning" role="alert">
            {error}
          </p>
        )}
        <footer>
          <button
            className="secondary"
            type="button"
            disabled={busy}
            onClick={close}
          >
            取消
          </button>
          <button className="primary" type="submit" disabled={busy}>
            {busy ? "保存中…" : "保存账户配置"}
          </button>
        </footer>
      </div>
    </form>
  );
}
export function BastionAccountsDialog({
  accounts,
  onClose,
  onError,
  initial,
}: {
  accounts: BastionAccount[];
  onClose: () => void;
  onError: (e: unknown) => void;
  initial?: { mode: "list" | "new" | "edit"; id?: string };
}) {
  const [form, setForm] = useState<{ account?: BastionAccount } | null>(() =>
      initial?.mode === "new"
        ? {}
        : initial?.mode === "edit"
          ? { account: accounts.find((a) => a.id === initial.id) }
          : null,
    ),
    [selected, setSelected] = useState(""),
    [access, setAccess] = useState("");
  const current = accounts.find((a) => a.id === selected);
  return (
    <div className="overlay">
      <section className="modal account-modal" aria-label="堡垒机账户管理">
        {form ? (
          <AccountForm
            account={form.account}
            onSaved={() => setForm(null)}
            onClose={() => setForm(null)}
            onError={onError}
          />
        ) : (
          <>
            <CompactToolbar label="堡垒机账户管理操作">
              <ShieldCheck size={18} />
              <strong className="toolbar-title">堡垒机账户</strong>
              <button
                className="primary"
                onClick={() => {
                  setSelected("");
                  setForm({});
                }}
              >
                <Plus size={16} />
                新增账户
              </button>
              <button aria-label="关闭堡垒机账户" onClick={onClose}>
                <X size={18} />
              </button>
            </CompactToolbar>
            <div className="account-list">
              <p className="account-boundary">
                本地配置与验证码已可用；内置登录 /
                资产列表尚未接通。请在本机填写真实
                key，无需发送到聊天。入口不会被自动访问。
              </p>
              {!accounts.length && (
                <p className="subtle">
                  还没有堡垒机账户配置。与左侧 SSH 保存连接独立。
                </p>
              )}
              {accounts.map((a) => (
                <article key={a.id} className="account-card">
                  <CompactToolbar label={`账户 ${a.name} 操作`}>
                    <strong
                      className="toolbar-title"
                      title={`${a.name} · ${a.username} · ${a.portalUrl}`}
                    >
                      {a.name}
                    </strong>
                    <button
                      aria-label={`编辑账户 ${a.name}`}
                      onClick={() => {
                        setSelected("");
                        setForm({ account: a });
                      }}
                    >
                      <Pencil size={16} />
                    </button>
                    <button
                      aria-label={`删除账户 ${a.name}`}
                      onClick={() => {
                        if (confirm("删除此堡垒机配置及保存的密码/key？"))
                          void window.forterm
                            .deleteBastionAccount(a.id)
                            .catch(onError);
                      }}
                    >
                      <Trash2 size={16} />
                    </button>
                    <button
                      className="secondary"
                      onClick={() => setSelected(selected === a.id ? "" : a.id)}
                    >
                      <KeyRound size={15} />
                      {selected === a.id ? "收起验证码" : "验证码"}
                    </button>
                    <button
                      className="secondary"
                      onClick={() => setAccess(access === a.id ? "" : a.id)}
                      aria-label={`查看接入状态 ${a.name}`}
                    >
                      接入状态
                    </button>
                  </CompactToolbar>
                  {access === a.id && (
                    <BastionAccessPanel
                      key={`${a.id}:${a.revision}`}
                      account={a}
                      onError={onError}
                    />
                  )}
                  <p className="account-metadata" title={a.portalUrl}>
                    {a.username} · {a.portalUrl}
                  </p>
                  <p className="account-metadata">
                    {a.hasPassword ? "登录密码已加密保存" : "未保存登录密码"} ·{" "}
                    {a.hasTotpSecret ? "Key 已加密保存" : "未配置 Key"} ·{" "}
                    {a.totpEnabled ? "验证器启用" : "验证器停用"} ·{" "}
                    {a.totp.digits} 位 / {a.totp.period} 秒 / {a.totp.algorithm}
                  </p>
                </article>
              ))}
              {current && (
                <CodeView
                  key={`${current.id}:${current.revision}:${current.totpEnabled}:${current.hasTotpSecret}:${JSON.stringify(current.totp)}`}
                  account={current}
                  onError={onError}
                />
              )}
            </div>
          </>
        )}
      </section>
    </div>
  );
}
