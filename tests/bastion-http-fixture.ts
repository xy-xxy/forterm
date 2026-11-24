/** Test-only contract, not a claimed vendor API. Never imported by src/. */
import { createServer } from "node:http";
import type { Socket } from "node:net";
import type {
  BastionProvider,
  PortalIdentity,
  PortalChallenge,
  LoginResult,
  ProviderAsset,
  GrantResult,
  PortalGrant,
  TemporaryEndpoint,
} from "../src/main/bastion-provider";
import { BastionProviderError } from "../src/main/bastion-provider";
import type { BastionAccount } from "../src/shared/bastion";
import type { BastionProtocol } from "../src/shared/bastion-access";
export const portalPassword = "PORTAL_LONG_PASSWORD_SYNTHETIC",
  portalToken = "PORTAL_TOKEN_SYNTHETIC",
  challengeSecret = "CHALLENGE_SECRET_SYNTHETIC",
  sshPassword = "one-time@password";
export async function bastionHttpFixture(sshPort: number) {
  const peers = new Set<Socket>(),
    counts: Record<string, number> = {},
    used = new Set<string>();
  const behavior = {
    challenge: true,
    grant: "approved" as "pending" | "approved" | "denied",
    approval: "approved" as "pending" | "approved" | "denied",
    dropAt: "",
    delayAt: "",
    delayMs: 0,
    expireAt: "",
    identityTtl: 60000,
    grantTtl: 60000,
    endpointProtocol: "" as "" | BastionProtocol,
  };
  const seen: { path: string; body: any }[] = [];
  let serial = 0;
  const server = createServer(async (req, res) => {
    const name = req.url!.slice(1);
    counts[name] = (counts[name] ?? 0) + 1;
    const chunks: Buffer[] = [];
    for await (const c of req) chunks.push(Buffer.from(c));
    const body = JSON.parse(Buffer.concat(chunks).toString() || "{}");
    seen.push({ path: name, body });
    const send = (status: number, value: unknown) => {
      if (!res.destroyed) {
        res.writeHead(status, { "Content-Type": "application/json" });
        res.end(JSON.stringify(value));
      }
    };
    if (behavior.expireAt === name) {
      send(401, { message: portalToken });
      return;
    }
    if (behavior.dropAt === name) {
      req.socket.destroy();
      return;
    }
    let result: any;
    if (name === "login") {
      if (body.username !== "portal-user" || body.password !== portalPassword) {
        send(403, { message: portalPassword });
        return;
      }
      result = behavior.challenge
        ? {
            kind: "challenge",
            challenge: {
              handle: { value: challengeSecret },
              kind: "otp",
              expiresAt: Date.now() + 60000,
            },
            extraSecret: portalToken,
          }
        : {
            kind: "authenticated",
            identity: {
              handle: { value: portalToken },
              expiresAt: Date.now() + behavior.identityTtl,
            },
            extraSecret: portalPassword,
          };
    } else if (name === "respond") {
      if (body.challenge !== challengeSecret || !/^\d{6}$/.test(body.code)) {
        send(403, { message: portalToken });
        return;
      }
      result = {
        kind: "authenticated",
        identity: {
          handle: { value: portalToken },
          expiresAt: Date.now() + behavior.identityTtl,
        },
        extraSecret: portalPassword,
      };
    } else {
      if (body.identity !== portalToken) {
        send(401, { message: portalToken });
        return;
      }
      if (name === "assets")
        result = {
          items: [
            {
              handle: body.cursor ? "ASSET_B" : "ASSET_A",
              name: body.cursor ? "测试资产 B" : "测试资产 A",
              accounts: [
                {
                  handle: body.cursor ? "ACCOUNT_B" : "ACCOUNT_A",
                  name: "授权账号",
                  protocols: ["ssh", "sftp"],
                },
              ],
            },
          ],
          nextCursor: body.cursor ? undefined : "CURSOR_SECRET",
          extraSecret: portalToken,
        };
      else if (name === "request")
        result =
          behavior.grant === "denied"
            ? { kind: "denied" }
            : {
                kind: behavior.grant,
                grant: {
                  handle: {
                    value: `GRANT_SECRET_${++serial}`,
                    protocol: body.protocol,
                  },
                  expiresAt: Date.now() + behavior.grantTtl,
                },
                extraSecret: portalToken,
              };
      else if (name === "approval")
        result =
          behavior.approval === "denied"
            ? { kind: "denied" }
            : { kind: behavior.approval, grant: body.grant };
      else if (name === "exchange") {
        const ticket = body.grant.handle.value;
        if (used.has(ticket)) {
          send(409, { message: ticket });
          return;
        }
        used.add(ticket);
        result = {
          protocol: behavior.endpointProtocol || body.grant.handle.protocol,
          host: "127.0.0.1",
          port: sshPort,
          username: "ticket",
          password: sshPassword,
          expiresAt: Date.now() + 60000,
        };
      } else if (name === "cancel") result = { ack: true };
      else if (name === "logout") result = { ok: true };
      else {
        send(404, { message: portalPassword });
        return;
      }
    }
    if (behavior.delayAt === name)
      await new Promise((r) => setTimeout(r, behavior.delayMs));
    send(200, result);
  });
  server.on("connection", (socket) => {
    peers.add(socket);
    socket.on("close", () => peers.delete(socket));
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  const url = `http://127.0.0.1:${(server.address() as any).port}/`;
  return {
    url,
    counts,
    seen,
    used,
    behavior,
    async close() {
      for (const peer of peers) peer.destroy();
      await new Promise<void>((resolve) => server.close(() => resolve()));
    },
  };
}
export class LoopbackBastionProvider implements BastionProvider {
  disposed: unknown[] = [];
  constructor(private base: string) {
    const u = new URL(base);
    if (
      u.protocol !== "http:" ||
      u.hostname !== "127.0.0.1" ||
      u.pathname !== "/" ||
      u.search ||
      u.hash ||
      u.username ||
      u.password
    )
      throw new Error("test loopback only");
  }
  private async post(name: string, body: unknown, signal: AbortSignal) {
    const res = await fetch(new URL(name, this.base), {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
      signal,
      redirect: "error",
    });
    if (!res.ok)
      throw new BastionProviderError(
        res.status === 401
          ? "expired"
          : res.status === 403
            ? "denied"
            : "invalid",
      );
    const text = await res.text();
    if (text.length > 1024 * 1024) throw new BastionProviderError("invalid");
    return JSON.parse(text);
  }
  private identity(i: PortalIdentity) {
    return (i.handle as any).value;
  }
  login(
    account: BastionAccount,
    credentials: { username: string; password?: string },
    signal: AbortSignal,
  ): Promise<LoginResult> {
    return this.post("login", credentials, signal);
  }
  respond(
    challenge: PortalChallenge,
    code: string,
    signal: AbortSignal,
  ): Promise<LoginResult> {
    return this.post(
      "respond",
      { challenge: (challenge.handle as any).value, code },
      signal,
    );
  }
  assets(
    i: PortalIdentity,
    q: { search: string; cursor?: unknown },
    s: AbortSignal,
  ): Promise<{ items: ProviderAsset[]; nextCursor?: unknown }> {
    return this.post("assets", { identity: this.identity(i), ...q }, s);
  }
  request(
    i: PortalIdentity,
    selection: {
      asset: unknown;
      account: unknown;
      protocol: BastionProtocol;
      requestId: string;
    },
    s: AbortSignal,
  ): Promise<GrantResult> {
    return this.post(
      "request",
      { identity: this.identity(i), ...selection },
      s,
    );
  }
  approval(
    i: PortalIdentity,
    grant: PortalGrant,
    s: AbortSignal,
  ): Promise<GrantResult> {
    return this.post("approval", { identity: this.identity(i), grant }, s);
  }
  exchange(
    i: PortalIdentity,
    grant: PortalGrant,
    s: AbortSignal,
  ): Promise<TemporaryEndpoint> {
    return this.post("exchange", { identity: this.identity(i), grant }, s);
  }
  async cancel(i: PortalIdentity, grant: PortalGrant, s: AbortSignal) {
    return !!(
      await this.post("cancel", { identity: this.identity(i), grant }, s)
    ).ack;
  }
  async logout(i: PortalIdentity, s: AbortSignal) {
    await this.post("logout", { identity: this.identity(i) }, s);
  }
  dispose(handle: unknown) {
    this.disposed.push(handle);
    if (handle && typeof handle === "object" && "value" in handle)
      (handle as any).value = "";
  }
}
