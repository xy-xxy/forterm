import { readFileSync, mkdirSync, writeFileSync, renameSync } from "node:fs";
import path from "node:path";
import { randomUUID } from "node:crypto";
import type { Profile, Connection, KeepAliveSettings } from "../shared/types";
import { profileSchema, keepAliveSchema } from "./schema";
import type {
  BastionAccount,
  BastionAccountInput,
  TotpReading,
} from "../shared/bastion";
import { bastionInput, bastionMetadata } from "./bastion-schema";
import { parseTotp, generateTotp } from "./totp";
import { DEFAULT_KEEP_ALIVE } from "../shared/keep-alive";
type StoredProfile = Profile & { encryptedPassword?: string };
type StoredBastionAccount = Omit<
  BastionAccount,
  "hasPassword" | "hasTotpSecret"
> & { encryptedPassword?: string; encryptedSeed?: string };
type Vault = {
  bastionAccounts?: StoredBastionAccount[];
  profiles: StoredProfile[];
  hosts: Record<string, string>;
  keepAlive?: KeepAliveSettings;
};
export interface Cipher {
  encrypt(s: string): Promise<string>;
  decrypt(s: string): Promise<string>;
}
export class Store {
  private accountQueue: Promise<unknown> = Promise.resolve();
  private data: Vault = { profiles: [], hosts: {} };
  constructor(
    private directory: string,
    private cipher: Cipher,
  ) {
    mkdirSync(directory, { recursive: true });
    try {
      const raw = JSON.parse(
        readFileSync(path.join(directory, "vault.json"), "utf8"),
      );
      if (
        Array.isArray(raw.profiles) &&
        raw.hosts &&
        typeof raw.hosts === "object"
      )
        this.data = raw;
    } catch (e) {
      if ((e as NodeJS.ErrnoException).code !== "ENOENT")
        throw new Error("本地配置无法读取，请备份 vault.json 后修复");
    }
  }
  private flush() {
    const dest = path.join(this.directory, "vault.json");
    writeFileSync(dest + ".tmp", JSON.stringify(this.data, null, 2), {
      mode: 0o600,
    });
    renameSync(dest + ".tmp", dest);
  }
  profiles(): Profile[] {
    return this.data.profiles.map(({ encryptedPassword, ...p }) => ({
      ...p,
      hasPassword: !!encryptedPassword,
    }));
  }
  keepAlive(): KeepAliveSettings {
    const value = keepAliveSchema.safeParse(this.data.keepAlive ?? {});
    return value.success ? value.data : { ...DEFAULT_KEEP_ALIVE };
  }
  setKeepAlive(input: unknown): KeepAliveSettings {
    const settings = keepAliveSchema.parse(input);
    const previous = this.data.keepAlive;
    this.data.keepAlive = settings;
    try {
      this.flush();
    } catch (e) {
      this.data.keepAlive = previous;
      throw e;
    }
    return { ...settings };
  }
  async save(input: unknown) {
    const p = profileSchema.parse(input);
    const previous = this.data.profiles.find((x) => x.id === p.id);
    const { password, ...fields } = p;
    const encryptedPassword =
      password === undefined
        ? previous?.encryptedPassword
        : password
          ? await this.cipher.encrypt(password)
          : undefined;
    const item: StoredProfile = {
      ...fields,
      id: p.id ?? randomUUID(),
      encryptedPassword,
    };
    if (item.jumpId === item.id) throw new Error("不能将自身设为跳板机");
    const next = this.data.profiles.filter((x) => x.id !== item.id);
    next.push(item);
    let cursor: StoredProfile | undefined = item;
    const seen = new Set<string>();
    while (cursor) {
      if (seen.has(cursor.id) || seen.size >= 8)
        throw new Error("跳板链存在循环或超过 8 层");
      seen.add(cursor.id);
      cursor = next.find((x) => x.id === cursor?.jumpId);
    }
    this.data.profiles = next;
    this.flush();
    return this.profiles();
  }
  delete(id: string) {
    if (this.data.profiles.some((p) => p.jumpId === id))
      throw new Error("此连接仍被用作跳板机");
    this.data.profiles = this.data.profiles.filter((p) => p.id !== id);
    this.flush();
    return this.profiles();
  }
  async resolve(id: string): Promise<Connection> {
    const p = this.data.profiles.find((p) => p.id === id);
    if (!p) throw new Error("找不到连接");
    const { encryptedPassword, hasPassword, ...fields } = p;
    return {
      ...fields,
      password: encryptedPassword
        ? await this.cipher.decrypt(encryptedPassword)
        : undefined,
      temporary: false,
    };
  }
  bastionAccounts(): BastionAccount[] {
    return (this.data.bastionAccounts ?? []).map((p) => ({
      id: p.id,
      revision: p.revision ?? p.id,
      name: p.name,
      portalUrl: p.portalUrl,
      username: p.username,
      totpEnabled: p.totpEnabled,
      totp: { ...p.totp },
      hasPassword: !!p.encryptedPassword,
      hasTotpSecret: !!p.encryptedSeed,
    }));
  }
  private accountMutation<T>(fn: () => Promise<T> | T): Promise<T> {
    const result = this.accountQueue.then(fn);
    this.accountQueue = result.catch(() => {});
    return result;
  }
  saveBastionAccount(input: unknown): Promise<BastionAccount[]> {
    return this.accountMutation(async () => {
      const parsed = bastionInput.safeParse(input);
      if (!parsed.success)
        throw new Error("堡垒机账户配置格式无效，入口地址不能含凭据或查询参数");
      const p: BastionAccountInput = parsed.data;
      const previous = (this.data.bastionAccounts ?? []).find(
        (a) => a.id === p.id,
      );
      if (p.id && !previous) throw new Error("账户配置已不存在，请重新打开");
      let encryptedPassword = previous?.encryptedPassword,
        encryptedSeed = previous?.encryptedSeed,
        parameters = p.totp;
      if (p.passwordAction === "clear") encryptedPassword = undefined;
      if (p.seedAction === "clear") encryptedSeed = undefined;
      const totp =
        p.seedAction === "replace"
          ? parseTotp(p.seedInput ?? "", p.totp)
          : undefined;
      if (totp) parameters = totp.parameters;
      if (p.passwordAction === "replace" && !p.password)
        throw new Error("请输入要保存的堡垒机登录密码");
      try {
        if (p.passwordAction === "replace")
          encryptedPassword = await this.cipher.encrypt(p.password!);
        if (totp) encryptedSeed = await this.cipher.encrypt(totp.seed);
      } catch {
        throw new Error("系统加密不可用，未保存凭据；不会回退明文");
      }
      if (p.totpEnabled && p.seedAction !== "clear" && !encryptedSeed)
        throw new Error("启用验证器前请先填写 key");
      const item: StoredBastionAccount = {
        id: previous?.id ?? randomUUID(),
        revision: randomUUID(),
        name: p.name,
        portalUrl: p.portalUrl,
        username: p.username,
        totp: parameters,
        totpEnabled: p.seedAction === "clear" ? false : p.totpEnabled,
        encryptedPassword,
        encryptedSeed,
      };
      const old = this.data.bastionAccounts;
      this.data.bastionAccounts = [
        ...(old ?? []).filter((a) => a.id !== item.id),
        item,
      ];
      try {
        this.flush();
      } catch {
        this.data.bastionAccounts = old;
        throw new Error("账户配置写入失败，原配置保持不变");
      }
      return this.bastionAccounts();
    });
  }
  deleteBastionAccount(id: string): Promise<BastionAccount[]> {
    return this.accountMutation(() => {
      const old = this.data.bastionAccounts;
      this.data.bastionAccounts = (old ?? []).filter((a) => a.id !== id);
      try {
        this.flush();
      } catch {
        this.data.bastionAccounts = old;
        throw new Error("账户配置删除失败");
      }
      return this.bastionAccounts();
    });
  }
  // Main-process capability only; no IPC returns a decrypted portal password.
  async withBastionPassword<T>(
    id: string,
    use: (password?: string) => Promise<T>,
  ): Promise<T> {
    const item = (this.data.bastionAccounts ?? []).find((a) => a.id === id);
    if (!item) throw new Error("堡垒机账户已不存在");
    let password: string | undefined;
    try {
      if (item.encryptedPassword)
        password = await this.cipher.decrypt(item.encryptedPassword);
    } catch {
      throw new Error("堡垒机密码无法解密，请在本机重新配置");
    }
    try {
      if ((this.data.bastionAccounts ?? []).find((a) => a.id === id) !== item)
        throw new Error("账户配置已改变，请重新操作");
      return await use(password);
    } finally {
      password = undefined;
    }
  }
  async bastionCode(id: string, now?: number): Promise<TotpReading> {
    const item = (this.data.bastionAccounts ?? []).find((a) => a.id === id);
    if (!item?.totpEnabled || !item.encryptedSeed)
      throw new Error("验证器未启用或未配置 key，可手动输入验证码");
    let seed: string;
    try {
      seed = await this.cipher.decrypt(item.encryptedSeed);
    } catch {
      throw new Error("验证器 key 无法解密，请在本机重新配置");
    }
    try {
      const current = (this.data.bastionAccounts ?? []).find(
        (a) => a.id === id,
      );
      if (current !== item) throw new Error("验证器配置已改变，请重新查看");
      return generateTotp(seed, item.totp, now ?? Date.now());
    } finally {
      seed = "";
    }
  }
  host(host: string, port: number) {
    return this.data.hosts[JSON.stringify([host, port])];
  }
  trust(host: string, port: number, fingerprint: string) {
    this.data.hosts[JSON.stringify([host, port])] = fingerprint;
    this.flush();
  }
  export() {
    return {
      version: 1,
      bastionAccounts: this.bastionAccounts().map(
        ({ hasPassword, hasTotpSecret, totpEnabled, revision, ...metadata }) =>
          metadata,
      ),
      profiles: this.profiles().map(({ hasPassword, ...p }) => p),
    };
  }
  async import(raw: unknown) {
    const doc = raw as {
      version?: unknown;
      profiles?: unknown;
      bastionAccounts?: unknown;
    };
    if (
      doc?.version !== 1 ||
      !Array.isArray(doc.profiles) ||
      doc.profiles.length > 1000
    )
      throw new Error("需要 Forterm v1 JSON 文件，最多 1000 个连接");
    const parsed = doc.profiles.map((p) => profileSchema.parse(p));
    const ids = new Map(
      parsed.filter((p) => p.id).map((p) => [p.id!, randomUUID()]),
    );
    if (ids.size !== parsed.filter((p) => p.id).length)
      throw new Error("导入文件包含重复连接标识");
    if (parsed.some((p) => p.jumpId && !ids.has(p.jumpId)))
      throw new Error("导入文件缺少跳板连接，请导出完整配置");
    const items = parsed.map(({ password, ...p }) => ({
      ...p,
      id: ids.get(p.id ?? "") ?? randomUUID(),
      jumpId: p.jumpId ? ids.get(p.jumpId) : undefined,
    }));
    for (const item of items) {
      let c: typeof item | undefined = item;
      const seen = new Set<string>();
      while (c) {
        if (seen.has(c.id) || seen.size >= 8)
          throw new Error("导入文件包含循环跳板链");
        seen.add(c.id);
        c = items.find((x) => x.id === c?.jumpId);
      }
    }
    let accounts: StoredBastionAccount[] = [];
    if (doc.bastionAccounts !== undefined) {
      if (
        !Array.isArray(doc.bastionAccounts) ||
        doc.bastionAccounts.length > 1000
      )
        throw new Error("堡垒机账户导入格式无效");
      accounts = doc.bastionAccounts.map((value) => {
        const parsed = bastionMetadata.safeParse(value);
        if (!parsed.success) throw new Error("堡垒机账户导入格式无效");
        return {
          ...parsed.data,
          id: randomUUID(),
          revision: randomUUID(),
          totpEnabled: false,
        };
      });
    }
    const oldProfiles = this.data.profiles,
      oldAccounts = this.data.bastionAccounts;
    this.data.profiles = [...oldProfiles, ...items];
    this.data.bastionAccounts = [...(oldAccounts ?? []), ...accounts];
    try {
      this.flush();
    } catch (e) {
      this.data.profiles = oldProfiles;
      this.data.bastionAccounts = oldAccounts;
      throw e;
    }
    return this.profiles();
  }
}
