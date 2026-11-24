import { randomUUID } from "node:crypto";
import { z } from "zod";
import type { BastionAccount } from "../shared/bastion";
import type {
  BastionAccessState,
  BastionAccessSnapshot,
  BastionAssetPage,
  BastionOpenOperation,
  BastionProtocol,
} from "../shared/bastion-access";
import type { Connection, SessionInfo } from "../shared/types";
import type { Store } from "./store";
import {
  BastionProviderError,
  type BastionProvider,
  type LoginResult,
  type PortalIdentity,
  type PortalChallenge,
  type PortalGrant,
  type GrantResult,
  type ProviderAsset,
  type TemporaryEndpoint,
} from "./bastion-provider";

type StoreAccess = Pick<
  Store,
  "bastionAccounts" | "withBastionPassword" | "bastionCode"
>;
type SessionAccess = {
  connect(c: Connection): Promise<string>;
  list(): SessionInfo[];
  close(id: string): void;
  sftp(id: string): Promise<unknown>;
};
class Fault extends Error {
  constructor(readonly kind: "timeout" | "cancelled" | "invalid") {
    super("堡垒机操作未完成");
  }
}
type Entry = {
  account: BastionAccount;
  provider?: BastionProvider;
  state: BastionAccessState;
  epoch: number;
  control: AbortController;
  identity?: PortalIdentity;
  challenge?: PortalChallenge;
  login?: Promise<BastionAccessState>;
  listing?: Promise<BastionAssetPage>;
  timer?: ReturnType<typeof setTimeout>;
  assets: Map<
    string,
    {
      asset: ProviderAsset;
      accounts: Map<string, ProviderAsset["accounts"][number]>;
    }
  >;
  cursors: Map<string, { search: string; value: unknown }>;
};
type Operation = {
  view: BastionOpenOperation;
  entry: Entry;
  epoch: number;
  key: string;
  control: AbortController;
  grant?: PortalGrant;
  busy: boolean;
  exchanged: boolean;
  timer?: ReturnType<typeof setTimeout>;
  checkingFiles?: boolean;
};
const label = z
  .string()
  .min(1)
  .max(256)
  .regex(/^[^\x00-\x1f]+$/);
const endpointSchema = z
  .object({
    protocol: z.enum(["ssh", "sftp"]),
    host: z
      .string()
      .min(1)
      .max(253)
      .regex(/^[^\s/\\\x00-\x1f@]+$/),
    port: z.number().int().min(1).max(65535),
    username: z.string().min(1).max(4096),
    password: z.string().min(1).max(16384),
    expiresAt: z.number().finite(),
  })
  .strict();
const terminal = new Set([
  "ready",
  "denied",
  "cancelled",
  "expired",
  "unknown",
  "error",
]);

/** No HTTP client and no vendor endpoints here. Only trusted, main-owned adapters may be registered. */
export class BastionService {
  private entries = new Map<string, Entry>();
  private operations = new Map<string, Operation>();
  private inFlight = new Map<string, Operation>();
  private ended = false;
  constructor(
    private store: StoreAccess,
    private sessions: SessionAccess,
    private resolveProvider: (
      account: BastionAccount,
    ) => BastionProvider | undefined,
    private emit: (snapshot: BastionAccessSnapshot) => void,
    private options: {
      requestTimeoutMs?: number;
      connectTimeoutMs?: number;
    } = {},
  ) {}
  private dispose(e: Entry, handle: unknown) {
    try {
      e.provider?.dispose(handle);
    } catch {}
  }
  private entry(id: string): Entry {
    if (this.ended) throw new Error("堡垒机接入服务已关闭");
    const account = this.store.bastionAccounts().find((a) => a.id === id);
    if (!account) throw new Error("堡垒机账户已不存在");
    let e = this.entries.get(id);
    if (e && e.account.revision !== account.revision) {
      this.entries.delete(id);
      this.forget(e, "signed-out", "账户配置已改变，请重新登录");
      e = undefined;
    }
    if (!e) {
      const provider = this.resolveProvider(account);
      e = {
        account,
        provider,
        state: {
          accountId: id,
          available: !!provider,
          status: provider ? "signed-out" : "unavailable",
          message: provider
            ? "尚未登录"
            : "待适配：尚缺对应厂商的登录、资产和票据兑换契约",
        },
        epoch: 0,
        control: new AbortController(),
        assets: new Map(),
        cursors: new Map(),
      };
      this.entries.set(id, e);
    }
    this.checkExpiry(e);
    return e;
  }
  private checkExpiry(e: Entry) {
    if (
      (e.identity?.expiresAt ?? e.challenge?.expiresAt ?? Infinity) <=
      Date.now()
    )
      this.forget(
        e,
        "expired",
        "门户身份或验证挑战已过期，请重新登录；已有SSH会话不受影响",
      );
  }
  private armExpiry(e: Entry) {
    clearTimeout(e.timer);
    const expires = e.identity?.expiresAt ?? e.challenge?.expiresAt;
    if (expires)
      e.timer = setTimeout(
        () => {
          this.checkExpiry(e);
          if (e.identity || e.challenge) this.armExpiry(e);
          this.publish();
        },
        Math.min(2147483647, Math.max(1, expires - Date.now())),
      );
    e.timer?.unref();
  }
  private publish() {
    if (!this.ended) this.emit(this.snapshot());
  }
  state(id: string) {
    return structuredClone(this.entry(id).state);
  }
  snapshot(): BastionAccessSnapshot {
    return {
      accounts: this.store
        .bastionAccounts()
        .map((a) => structuredClone(this.entry(a.id).state)),
      operations: [...this.operations.values()].map((o) => ({ ...o.view })),
    };
  }
  reconcile() {
    for (const [id, e] of this.entries)
      if (!this.store.bastionAccounts().some((a) => a.id === id)) {
        this.entries.delete(id);
        this.forget(e, "signed-out", "账户已删除，本地门户身份已清除");
      }
    this.publish();
  }
  private usable(e: Entry, epoch = e.epoch) {
    this.checkExpiry(e);
    if (e.epoch !== epoch || e.control.signal.aborted)
      throw new Fault("cancelled");
    if (!e.provider) throw new Error("待适配：此账户还没有厂商接入提供器");
    return e.provider;
  }
  private identity(e: Entry) {
    this.usable(e);
    if (!e.identity) throw new Error("请先完成堡垒机登录与验证");
    return e.identity;
  }
  private validExpiry(expiresAt: number) {
    if (!Number.isFinite(expiresAt)) throw new Fault("invalid");
    if (expiresAt <= Date.now()) throw new BastionProviderError("expired");
  }
  private async call<T>(
    signal: AbortSignal,
    fn: (signal: AbortSignal) => Promise<T>,
    late?: (value: T) => void,
  ): Promise<T> {
    if (signal.aborted) throw new Fault("cancelled");
    return new Promise<T>((resolve, reject) => {
      const child = new AbortController();
      let settled = false;
      const finish = (error?: unknown, value?: T) => {
        if (settled) {
          if (!error && value !== undefined) {
            try {
              late?.(value);
            } catch {}
          }
          return;
        }
        settled = true;
        clearTimeout(timer);
        signal.removeEventListener("abort", abort);
        error ? reject(error) : resolve(value!);
      };
      const abort = () => {
        child.abort();
        finish(new Fault("cancelled"));
      };
      const timer = setTimeout(() => {
        child.abort();
        finish(new Fault("timeout"));
      }, this.options.requestTimeoutMs ?? 15000);
      signal.addEventListener("abort", abort, { once: true });
      Promise.resolve()
        .then(() => {
          if (child.signal.aborted) throw new Fault("cancelled");
          return fn(child.signal);
        })
        .then(
          (v) => finish(undefined, v),
          (e) => finish(e),
        );
    });
  }
  private disposeLogin(e: Entry, r: LoginResult) {
    if (r?.kind === "authenticated") this.dispose(e, r.identity?.handle);
    else if (r?.kind === "challenge") this.dispose(e, r.challenge?.handle);
  }
  private acceptLogin(e: Entry, epoch: number, r: LoginResult) {
    if (e.epoch !== epoch || e.control.signal.aborted) {
      this.disposeLogin(e, r);
      throw new Fault("cancelled");
    }
    if (r.kind === "authenticated") {
      try {
        this.validExpiry(r.identity.expiresAt);
      } catch (error) {
        this.disposeLogin(e, r);
        throw error;
      }
      e.identity = r.identity;
      e.state = {
        accountId: e.account.id,
        available: true,
        status: "authenticated",
        message: "门户身份已确认",
        expiresAt: r.identity.expiresAt,
      };
    } else if (
      r.kind === "challenge" &&
      ["otp", "browser"].includes(r.challenge.kind)
    ) {
      try {
        this.validExpiry(r.challenge.expiresAt);
      } catch (error) {
        this.disposeLogin(e, r);
        throw error;
      }
      e.challenge = r.challenge;
      e.state = {
        accountId: e.account.id,
        available: true,
        status: "mfa",
        message:
          r.challenge.kind === "otp"
            ? "请主动提交验证码"
            : "需要厂商支持的隔离网页登录；当前没有自动打开入口",
        challenge: {
          ref: randomUUID(),
          kind: r.challenge.kind,
          expiresAt: r.challenge.expiresAt,
        },
      };
    } else throw new Fault("invalid");
    this.armExpiry(e);
    this.publish();
    return this.state(e.account.id);
  }
  private loginFailure(
    e: Entry,
    epoch: number,
    error: unknown,
    attempted = false,
  ) {
    if (e.epoch !== epoch) return this.state(e.account.id);
    const unknown = attempted && !(error instanceof BastionProviderError);
    e.state = {
      accountId: e.account.id,
      available: true,
      status:
        error instanceof BastionProviderError && error.reason === "expired"
          ? "expired"
          : unknown
            ? "unknown"
            : "error",
      message: unknown
        ? "登录或验证响应未确认，结果未知；未自动重试，请核对门户状态后注销本地身份"
        : "登录或验证未完成，请核对配置与授权",
    };
    this.publish();
    return this.state(e.account.id);
  }
  login(
    id: string,
    input: { password?: string } = {},
  ): Promise<BastionAccessState> {
    const e = this.entry(id);
    this.usable(e);
    if (e.login) return e.login;
    if (["authenticated", "mfa", "unknown"].includes(e.state.status))
      return Promise.resolve(this.state(id));
    if (
      input.password !== undefined &&
      (!input.password || input.password.length > 16384)
    )
      throw new Error("登录密码格式无效");
    const epoch = e.epoch;
    e.state = {
      accountId: id,
      available: true,
      status: "authenticating",
      message: "正在确认门户身份",
    };
    let attempted = false;
    e.login = (async () => {
      try {
        const use = async (saved?: string) => {
          this.usable(e, epoch);
          let credentials = {
            username: e.account.username,
            password: input.password ?? saved,
          };
          try {
            attempted = true;
            const r = await this.call(
              e.control.signal,
              (s) => e.provider!.login(e.account, credentials, s),
              (r) => this.disposeLogin(e, r),
            );
            return this.acceptLogin(e, epoch, r);
          } finally {
            credentials.password = undefined;
          }
        };
        return input.password !== undefined
          ? await use()
          : await this.store.withBastionPassword(id, use);
      } catch (error) {
        return this.loginFailure(e, epoch, error, attempted);
      } finally {
        input.password = undefined;
        if (e.epoch === epoch) e.login = undefined;
      }
    })();
    this.publish();
    return e.login;
  }
  respond(
    id: string,
    ref: string,
    input: { code?: string; useStoredTotp?: boolean },
    requireActive: () => void = () => {},
  ): Promise<BastionAccessState> {
    const e = this.entry(id);
    this.usable(e);
    if (e.login) return e.login;
    if (
      e.state.challenge?.ref !== ref ||
      !e.challenge ||
      e.challenge.kind !== "otp"
    )
      throw new Error("验证挑战已改变、过期或不支持此提交方式");
    if (input.useStoredTotp && input.code !== undefined)
      throw new Error("请选择一种验证码来源");
    const epoch = e.epoch,
      challenge = e.challenge;
    e.login = (async () => {
      let code = "",
        attempted = false;
      try {
        code = input.useStoredTotp
          ? (await this.store.bastionCode(id)).code
          : (input.code ?? "");
        if (!/^(?:\d{6}|\d{8})$/.test(code))
          throw new Error("请输入6或8位验证码");
        this.usable(e, epoch);
        requireActive();
        attempted = true;
        const r = await this.call(
          e.control.signal,
          (s) => e.provider!.respond(challenge, code, s),
          (r) => this.disposeLogin(e, r),
        );
        const nextHandle =
          r.kind === "authenticated" ? r.identity.handle : r.challenge.handle;
        if (nextHandle !== challenge.handle) this.dispose(e, challenge.handle);
        e.challenge = undefined;
        return this.acceptLogin(e, epoch, r);
      } catch (error) {
        if (e.epoch === epoch) {
          this.dispose(e, challenge.handle);
          e.challenge = undefined;
          clearTimeout(e.timer);
        }
        return this.loginFailure(e, epoch, error, attempted);
      } finally {
        code = "";
        if (e.epoch === epoch) e.login = undefined;
      }
    })();
    return e.login;
  }
  assets(id: string, search = "", cursor?: string): Promise<BastionAssetPage> {
    const e = this.entry(id),
      identity = this.identity(e),
      epoch = e.epoch;
    if (search.length > 256) throw new Error("资产搜索条件过长");
    const page = cursor ? e.cursors.get(cursor) : undefined;
    if (cursor && (!page || page.search !== search))
      throw new Error("分页引用无效，请重新搜索");
    if (e.listing) throw new Error("资产查询进行中，请等待");
    e.listing = (async () => {
      try {
        const result = await this.call(e.control.signal, (s) =>
          e.provider!.assets(identity, { search, cursor: page?.value }, s),
        );
        this.usable(e, epoch);
        if (
          !Array.isArray(result.items) ||
          result.items.length > 200 ||
          e.assets.size + result.items.length > 2000 ||
          e.cursors.size >= 128
        )
          throw new Fault("invalid");
        for (const item of result.items) {
          label.parse(item.name);
          if (!Array.isArray(item.accounts) || item.accounts.length > 100)
            throw new Fault("invalid");
          for (const a of item.accounts) {
            label.parse(a.name);
            z.array(z.enum(["ssh", "sftp"]))
              .min(1)
              .max(2)
              .parse(a.protocols);
          }
        }
        const items = result.items.map((asset) => {
          const ref = randomUUID(),
            accounts = new Map<string, ProviderAsset["accounts"][number]>();
          const publicAccounts = asset.accounts.map((a) => {
            const ref = randomUUID();
            accounts.set(ref, a);
            return { ref, name: a.name, protocols: [...new Set(a.protocols)] };
          });
          e.assets.set(ref, { asset, accounts });
          return { ref, name: asset.name, accounts: publicAccounts };
        });
        let nextCursor: string | undefined;
        if (result.nextCursor !== undefined) {
          nextCursor = randomUUID();
          e.cursors.set(nextCursor, { search, value: result.nextCursor });
        }
        return { items, nextCursor };
      } catch (error) {
        if (
          error instanceof BastionProviderError &&
          error.reason === "expired"
        ) {
          this.forget(e, "expired", "门户身份已过期，请重新登录");
          this.publish();
        }
        throw new Error(
          "资产查询未完成：身份过期、超时或返回结构不受支持；未自动重试",
        );
      } finally {
        if (e.epoch === epoch) e.listing = undefined;
      }
    })();
    return e.listing;
  }
  open(
    id: string,
    assetRef: string,
    accountRef: string,
    protocol: BastionProtocol,
  ): BastionOpenOperation {
    const e = this.entry(id);
    this.identity(e);
    const asset = e.assets.get(assetRef),
      account = asset?.accounts.get(accountRef);
    if (!asset || !account || !account.protocols.includes(protocol))
      throw new Error("资产、账号或协议不在当前身份的授权引用中");
    const key = JSON.stringify([id, e.epoch, assetRef, accountRef, protocol]);
    const old = this.inFlight.get(key);
    if (old) return { ...old.view };
    if (this.operations.size >= 100) {
      for (const [id, o] of this.operations)
        if (terminal.has(o.view.status) && o.view.status !== "unknown") {
          this.operations.delete(id);
          break;
        }
      if (this.operations.size >= 100)
        throw new Error("会话申请记录过多，请先处理未完成申请");
    }
    const view: BastionOpenOperation = {
      id: randomUUID(),
      accountId: id,
      assetRef,
      accountRef,
      protocol,
      status: "requesting",
      message: "正在申请授权会话",
    };
    const op: Operation = {
      view,
      entry: e,
      epoch: e.epoch,
      key,
      control: new AbortController(),
      busy: true,
      exchanged: false,
    };
    this.operations.set(view.id, op);
    this.inFlight.set(key, op);
    this.publish();
    void (async () => {
      try {
        const r = await this.call(
          op.control.signal,
          (s) =>
            e.provider!.request(
              this.identity(e),
              {
                asset: asset.asset.handle,
                account: account.handle,
                protocol,
                requestId: view.id,
              },
              s,
            ),
          (r) => {
            if (r.kind !== "denied") this.dispose(e, r.grant.handle);
          },
        );
        await this.acceptGrant(op, r);
      } catch (error) {
        this.fail(op, error);
      } finally {
        op.busy = false;
      }
    })();
    return { ...view };
  }
  private active(op: Operation) {
    this.usable(op.entry, op.epoch);
    if (op.control.signal.aborted || terminal.has(op.view.status))
      throw new Fault("cancelled");
  }
  private setOperation(
    op: Operation,
    status: BastionOpenOperation["status"],
    message: string,
  ) {
    op.view.status = status;
    op.view.message = message;
    if (terminal.has(status)) {
      clearTimeout(op.timer);
      if (status !== "unknown") this.inFlight.delete(op.key);
      if (op.grant) {
        this.dispose(op.entry, op.grant.handle);
        op.grant = undefined;
      }
    }
    this.publish();
  }
  private armGrantExpiry(op: Operation, expiresAt: number) {
    clearTimeout(op.timer);
    op.timer = setTimeout(
      () => {
        if (op.view.status !== "pending-approval") return;
        if (expiresAt > Date.now()) {
          this.armGrantExpiry(op, expiresAt);
          return;
        }
        op.control.abort();
        this.setOperation(op, "expired", "授权申请已过期；没有兑换或重新申请");
      },
      Math.min(2147483647, Math.max(1, expiresAt - Date.now())),
    );
    op.timer.unref();
  }
  private async acceptGrant(op: Operation, r: GrantResult) {
    this.active(op);
    if (r.kind === "denied") {
      this.setOperation(op, "denied", "授权申请被拒绝，未建立SSH连接");
      return;
    }
    if (!["pending", "approved"].includes(r.kind)) throw new Fault("invalid");
    this.validExpiry(r.grant.expiresAt);
    if (op.grant && op.grant.handle !== r.grant.handle)
      this.dispose(op.entry, op.grant.handle);
    op.grant = r.grant;
    if (r.kind === "pending") {
      this.setOperation(
        op,
        "pending-approval",
        "等待审批；仅在主动刷新时检查，不重复申请",
      );
      this.armGrantExpiry(op, r.grant.expiresAt);
      return;
    }
    clearTimeout(op.timer);
    if (op.exchanged) throw new Fault("invalid");
    op.exchanged = true;
    this.setOperation(op, "exchanging", "授权已批准，正在一次性兑换连接信息");
    let endpoint: TemporaryEndpoint | undefined;
    try {
      endpoint = await this.call(
        op.control.signal,
        (s) => op.entry.provider!.exchange(this.identity(op.entry), r.grant, s),
        (value) => {
          value.password = "";
        },
      );
      this.active(op);
      const c = endpointSchema.parse(endpoint);
      this.validExpiry(c.expiresAt);
      if (c.protocol !== op.view.protocol) throw new Fault("invalid");
      // Explicit fields only; never pass portal passwords/sid or arbitrary profile/key paths.
      const sessionId = await this.sessions.connect({
        protocol: c.protocol,
        host: c.host,
        port: c.port,
        username: c.username,
        password: c.password,
        temporary: true,
        title: `${op.entry.account.name} · 授权资产会话`,
      });
      op.view.sessionId = sessionId;
      if (op.epoch !== op.entry.epoch || op.control.signal.aborted) {
        this.stopUnauthenticated(sessionId);
        return;
      }
      this.setOperation(op, "connecting", "正在建立连接，目标通道就绪后才可用");
      op.timer = setTimeout(() => {
        if (op.view.status === "connecting") {
          this.stopUnauthenticated(sessionId);
          this.setOperation(
            op,
            "error",
            "连接等待超时，未认证尝试已停止；已认证SSH会话保留，没有重新兑换票据",
          );
        }
      }, this.options.connectTimeoutMs ?? 60000);
      op.timer.unref();
      const info = this.sessions.list().find((s) => s.id === sessionId);
      if (info) this.observeSession(info);
    } finally {
      if (endpoint) endpoint.password = "";
    }
  }
  private fail(op: Operation, error: unknown) {
    if (terminal.has(op.view.status)) return;
    if (error instanceof BastionProviderError) {
      const status =
        error.reason === "expired"
          ? "expired"
          : error.reason === "denied"
            ? "denied"
            : "error";
      this.setOperation(
        op,
        status,
        status === "expired"
          ? "身份或票据已过期，请重新授权"
          : status === "denied"
            ? "服务器明确拒绝授权或兑换"
            : "提供器尚不支持此契约，未建立连接",
      );
      return;
    }
    if (op.view.status === "pending-approval") {
      this.setOperation(
        op,
        "pending-approval",
        "审批查询未完成，服务端申请可能仍有效；可主动刷新或取消，未重复申请",
      );
      return;
    }
    const uncertain = ["requesting", "exchanging"].includes(op.view.status);
    this.setOperation(
      op,
      uncertain ? "unknown" : "error",
      uncertain
        ? "申请或兑换结果未知；未自动重试，需核对服务端授权/消费状态"
        : "操作未完成，请检查身份、审批与通道权限",
    );
  }
  refresh(id: string) {
    const op = this.operations.get(id);
    if (!op) throw new Error("会话申请已不存在");
    if (op.busy || op.view.status !== "pending-approval") return { ...op.view };
    this.active(op);
    if (op.grant!.expiresAt <= Date.now()) {
      this.setOperation(op, "expired", "授权申请已过期，请重新授权");
      return { ...op.view };
    }
    op.busy = true;
    void (async () => {
      try {
        const r = await this.call(op.control.signal, (s) =>
          op.entry.provider!.approval(this.identity(op.entry), op.grant!, s),
        );
        await this.acceptGrant(op, r);
      } catch (error) {
        this.fail(op, error);
      } finally {
        op.busy = false;
      }
    })();
    return { ...op.view };
  }
  async cancel(id: string) {
    const op = this.operations.get(id);
    if (!op) throw new Error("会话申请已不存在");
    if (terminal.has(op.view.status)) return { ...op.view };
    const previous = op.view.status,
      grant = op.grant;
    op.control.abort();
    if (previous === "connecting" && op.view.sessionId)
      this.stopUnauthenticated(op.view.sessionId);
    if (["requesting", "exchanging"].includes(previous)) {
      this.setOperation(
        op,
        "unknown",
        "本地操作已取消，但服务端申请/票据消费结果未知；不会自动重试",
      );
      return { ...op.view };
    }
    op.grant = undefined;
    this.setOperation(
      op,
      "cancelled",
      "本地申请已取消；未确认服务端撤销，已有SSH会话不受影响",
    );
    if (grant) {
      try {
        if (op.entry.provider?.cancel && op.entry.identity) {
          const ack = await this.call(op.entry.control.signal, (s) =>
            op.entry.provider!.cancel!(op.entry.identity!, grant, s),
          );
          if (ack) op.view.message = "服务端已确认撤销申请";
        }
      } catch {
        op.view.message = "本地已取消，服务端撤销结果未知，请核对审批状态";
      } finally {
        this.dispose(op.entry, grant.handle);
      }
      this.publish();
    }
    return { ...op.view };
  }
  sessionRemoved(id: string) {
    for (const op of this.operations.values())
      if (op.view.sessionId === id && op.view.status === "connecting")
        this.setOperation(
          op,
          "cancelled",
          "连接已由用户关闭；未重新兑换或自动重用票据",
        );
  }
  private stopUnauthenticated(sessionId: string) {
    const info = this.sessions.list().find((s) => s.id === sessionId);
    if (info?.status !== "ready") this.sessions.close(sessionId);
  }
  observeSession(info: SessionInfo) {
    for (const op of this.operations.values())
      if (op.view.status === "connecting" && op.view.sessionId === info.id) {
        if (["closed", "error"].includes(info.status)) {
          this.setOperation(
            op,
            "error",
            "连接已结束或失败；没有自动重试或重放票据",
          );
          continue;
        }
        if (info.status !== "ready") continue;
        if (op.view.protocol === "ssh") {
          if (info.terminalStatus === "ready")
            this.setOperation(op, "ready", "终端通道已就绪");
          else if (["error", "closed"].includes(info.terminalStatus ?? ""))
            this.setOperation(
              op,
              "error",
              "终端通道未开放；会话其他已授权能力仍可使用",
            );
        } else if (!op.checkingFiles) {
          op.checkingFiles = true;
          void this.sessions.sftp(info.id).then(
            () => {
              if (op.view.status === "connecting")
                this.setOperation(op, "ready", "SFTP文件通道已就绪");
            },
            () => {
              if (op.view.status === "connecting")
                this.setOperation(
                  op,
                  "error",
                  "SFTP通道不可用；其他已授权能力仍可使用",
                );
            },
          );
        }
      }
  }
  private forget(
    e: Entry,
    status: BastionAccessState["status"],
    message: string,
    preserveIdentity = false,
  ) {
    const identity = e.identity,
      challenge = e.challenge;
    e.epoch++;
    e.control.abort();
    e.control = new AbortController();
    clearTimeout(e.timer);
    e.login = undefined;
    e.listing = undefined;
    e.identity = undefined;
    e.challenge = undefined;
    e.assets.clear();
    e.cursors.clear();
    e.state = {
      accountId: e.account.id,
      available: !!e.provider,
      status: e.provider ? status : "unavailable",
      message,
    };
    if (identity && !preserveIdentity) this.dispose(e, identity.handle);
    if (challenge) this.dispose(e, challenge.handle);
    for (const op of this.operations.values())
      if (op.entry === e && !terminal.has(op.view.status)) {
        const phase = op.view.status;
        op.control.abort();
        if (phase === "connecting" && op.view.sessionId)
          this.stopUnauthenticated(op.view.sessionId);
        this.setOperation(
          op,
          ["requesting", "exchanging"].includes(phase)
            ? "unknown"
            : "cancelled",
          "门户身份已清除；未完成申请已停止，服务端结果可能未知；已有认证SSH会话保留",
        );
      }
  }
  async logout(id: string) {
    const e = this.entry(id),
      identity = e.identity;
    this.forget(e, "signed-out", "本地门户身份已注销；已认证SSH会话保留", true);
    const epoch = e.epoch;
    this.publish();
    if (identity && e.provider) {
      try {
        await this.call(e.control.signal, (s) =>
          e.provider!.logout(identity, s),
        );
      } catch {
        if (e.epoch === epoch)
          e.state.message = "本地身份已清除，服务端注销结果未知；请在门户核对";
      } finally {
        this.dispose(e, identity.handle);
      }
    }
    this.publish();
    return this.state(id);
  }
  close() {
    this.ended = true;
    for (const e of this.entries.values())
      this.forget(e, "signed-out", "服务已关闭");
    this.entries.clear();
  }
}
