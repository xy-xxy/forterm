import { useEffect, useRef, useState } from "react";
import type { BastionAccount } from "../shared/bastion";
import type {
  BastionAccessSnapshot,
  BastionAssetPage,
  BastionProtocol,
} from "../shared/bastion-access";
import { CompactToolbar } from "./CompactToolbar";
export function BastionAccessPanel({
  account,
  onError,
}: {
  account: BastionAccount;
  onError: (e: unknown) => void;
}) {
  const [snapshot, setSnapshot] = useState<BastionAccessSnapshot>(),
    [password, setPassword] = useState(""),
    [code, setCode] = useState(""),
    [search, setSearch] = useState(""),
    [page, setPage] = useState<BastionAssetPage>(),
    [pageSearch, setPageSearch] = useState(""),
    [busy, setBusy] = useState(false);
  const [choices, setChoices] = useState<
    Record<string, { account: string; protocol: BastionProtocol }>
  >({});
  const live = useRef(true),
    sequence = useRef(0);
  const state = snapshot?.accounts.find((a) => a.accountId === account.id);
  const operations =
    snapshot?.operations.filter((o) => o.accountId === account.id) ?? [];
  useEffect(() => {
    live.current = true;
    let changed = false;
    const off = window.forterm.onEvent((e) => {
      if (e.type === "bastion-access") {
        changed = true;
        setSnapshot(e.snapshot);
      }
      if (e.type === "window-visibility" && !e.visible) {
        sequence.current++;
        setPassword("");
        setCode("");
      }
    });
    void window.forterm
      .bastionAccess()
      .then((s) => {
        if (live.current && !changed) setSnapshot(s);
      })
      .catch(onError);
    return () => {
      live.current = false;
      sequence.current++;
      off();
    };
  }, [account.id]);
  useEffect(() => {
    if (state?.status !== "authenticated") {
      setPage(undefined);
      setChoices({});
    }
  }, [state?.status]);
  const perform = async (action: () => Promise<unknown>) => {
    if (busy) return;
    setBusy(true);
    try {
      await action();
    } catch (e) {
      if (live.current) onError(e);
    } finally {
      if (live.current) setBusy(false);
    }
  };
  const query = (cursor?: string) =>
    perform(async () => {
      const generation = sequence.current,
        q = cursor ? pageSearch : search;
      const result = await window.forterm.bastionAssets(account.id, q, cursor);
      if (live.current && sequence.current === generation) {
        setPage(result);
        setPageSearch(q);
        setChoices({});
      }
    });
  return (
    <section
      className="account-card bastion-access-panel"
      aria-label={`堡垒机接入 ${account.name}`}
    >
      <CompactToolbar label="堡垒机接入状态">
        <strong className="toolbar-title">内置登录与授权资产</strong>
        <span>{state?.available ? "提供器已适配" : "待适配"}</span>
      </CompactToolbar>
      <p role="status">{state?.message ?? "正在读取接入能力"}</p>
      {!state?.available && (
        <p className="subtle">
          本地密码/验证码配置已可用。登录、资产和最终票据兑换尚缺厂商契约；这里不会访问入口或展示合成资产。可继续使用原控制器入口。
        </p>
      )}
      <div className="bastion-access-actions">
        {state?.available &&
          !["authenticated", "mfa"].includes(state.status) && (
            <input
              type="password"
              aria-label="本次堡垒机登录密码"
              autoComplete="off"
              placeholder={
                account.hasPassword
                  ? "留空使用已加密密码"
                  : "仅用于本次登录，不自动保存"
              }
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          )}
        <button
          className="secondary"
          disabled={
            busy ||
            !state?.available ||
            ["authenticated", "mfa", "authenticating", "unknown"].includes(
              state?.status ?? "",
            )
          }
          onClick={() =>
            void perform(async () => {
              try {
                await window.forterm.bastionLogin(
                  account.id,
                  password ? { password } : {},
                );
              } finally {
                setPassword("");
              }
            })
          }
        >
          登录堡垒机
        </button>
        {state?.available && state.status !== "signed-out" && (
          <button
            className="secondary"
            disabled={busy}
            onClick={() =>
              void perform(() => window.forterm.bastionLogout(account.id))
            }
          >
            注销门户身份
          </button>
        )}
      </div>
      {state?.challenge?.kind === "otp" && (
        <div className="bastion-access-actions">
          <input
            type="password"
            inputMode="numeric"
            maxLength={8}
            autoComplete="one-time-code"
            aria-label="本次登录验证码"
            value={code}
            onChange={(e) => setCode(e.target.value)}
          />
          <button
            disabled={busy || !code}
            onClick={() =>
              void perform(async () => {
                try {
                  await window.forterm.bastionRespond(
                    account.id,
                    state.challenge!.ref,
                    { code },
                  );
                } finally {
                  setCode("");
                }
              })
            }
          >
            提交验证码
          </button>
          <button
            disabled={busy || !account.totpEnabled || !account.hasTotpSecret}
            onClick={() =>
              void perform(() =>
                window.forterm.bastionRespond(
                  account.id,
                  state.challenge!.ref,
                  { useStoredTotp: true },
                ),
              )
            }
          >
            主动使用已配置验证器
          </button>
        </div>
      )}
      {state?.status === "authenticated" && (
        <>
          <form
            className="bastion-access-actions"
            onSubmit={(e) => {
              e.preventDefault();
              void query();
            }}
          >
            <input
              aria-label="搜索授权资产"
              maxLength={256}
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
            <button disabled={busy}>查询授权资产</button>
            {page?.nextCursor && (
              <button
                type="button"
                disabled={busy}
                onClick={() => void query(page.nextCursor)}
              >
                下一页资产
              </button>
            )}
          </form>
          {page && !page.items.length && <p>当前查询没有授权资产</p>}
          {page?.items.map((asset) => {
            const selected = choices[asset.ref] ?? {
              account: asset.accounts[0]?.ref ?? "",
              protocol: asset.accounts[0]?.protocols[0] ?? "ssh",
            };
            const selectedAccount = asset.accounts.find(
              (a) => a.ref === selected.account,
            );
            return (
              <article className="account-card" key={asset.ref}>
                <strong>{asset.name}</strong>
                <div className="bastion-access-actions">
                  <select
                    aria-label={`${asset.name} 授权账号`}
                    value={selected.account}
                    onChange={(e) => {
                      const a = asset.accounts.find(
                        (a) => a.ref === e.target.value,
                      );
                      if (a)
                        setChoices((old) => ({
                          ...old,
                          [asset.ref]: {
                            account: a.ref,
                            protocol: a.protocols[0],
                          },
                        }));
                    }}
                  >
                    {asset.accounts.map((a) => (
                      <option key={a.ref} value={a.ref}>
                        {a.name}
                      </option>
                    ))}
                  </select>
                  <select
                    aria-label={`${asset.name} 协议`}
                    value={selected.protocol}
                    onChange={(e) =>
                      setChoices((old) => ({
                        ...old,
                        [asset.ref]: {
                          ...selected,
                          protocol: e.target.value as BastionProtocol,
                        },
                      }))
                    }
                  >
                    {selectedAccount?.protocols.map((protocol) => (
                      <option key={protocol} value={protocol}>
                        {protocol.toUpperCase()}
                      </option>
                    ))}
                  </select>
                  <button
                    disabled={busy || !selectedAccount}
                    onClick={() =>
                      void perform(() =>
                        window.forterm.bastionOpen(
                          account.id,
                          asset.ref,
                          selected.account,
                          selected.protocol,
                        ),
                      )
                    }
                  >
                    {selected.protocol === "ssh"
                      ? "打开授权终端"
                      : "打开授权文件会话"}
                  </button>
                </div>
              </article>
            );
          })}
        </>
      )}
      {operations.map((op) => (
        <article key={op.id} className="account-card">
          <p>
            {op.protocol.toUpperCase()} · {op.message}
          </p>
          <div className="bastion-access-actions">
            {op.status === "pending-approval" && (
              <button
                disabled={busy}
                onClick={() =>
                  void perform(() =>
                    window.forterm.bastionRefreshOperation(op.id),
                  )
                }
              >
                刷新审批
              </button>
            )}
            {[
              "requesting",
              "pending-approval",
              "exchanging",
              "connecting",
            ].includes(op.status) && (
              <button
                disabled={busy}
                onClick={() =>
                  void perform(() =>
                    window.forterm.bastionCancelOperation(op.id),
                  )
                }
              >
                取消本次申请
              </button>
            )}
          </div>
        </article>
      ))}
    </section>
  );
}
