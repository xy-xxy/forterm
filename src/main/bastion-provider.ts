import type { BastionAccount } from "../shared/bastion";
import type { BastionProtocol } from "../shared/bastion-access";
/** Implement only after the vendor/version contract and permitted origins are verified.
 * No renderer URL/path/header/body proxy. Opaque objects may contain secrets in main memory.
 * Each adapter must bound HTTP bodies, deny unexpected redirects/origins, verify TLS, and
 * use its own isolated identity/cookie store. Test adapters live exclusively under tests/.
 */
export interface PortalIdentity {
  handle: unknown;
  expiresAt: number;
}
export interface PortalChallenge {
  handle: unknown;
  kind: "otp" | "browser";
  expiresAt: number;
}
export type LoginResult =
  | { kind: "authenticated"; identity: PortalIdentity }
  | { kind: "challenge"; challenge: PortalChallenge };
export interface ProviderAsset {
  handle: unknown;
  name: string;
  accounts: { handle: unknown; name: string; protocols: BastionProtocol[] }[];
}
export interface PortalGrant {
  handle: unknown;
  expiresAt: number;
}
export type GrantResult =
  | { kind: "pending" | "approved"; grant: PortalGrant }
  | { kind: "denied" };
export interface TemporaryEndpoint {
  /** Deliberately excludes keyPath/jumpId, persisted-profile references and portal credentials. */
  protocol: BastionProtocol;
  host: string;
  port: number;
  username: string;
  password: string;
  expiresAt: number;
}
export class BastionProviderError extends Error {
  constructor(
    readonly reason: "expired" | "denied" | "invalid" | "unsupported",
  ) {
    super("堡垒机提供器请求未完成");
  }
}
export interface BastionProvider {
  login(
    account: BastionAccount,
    credentials: { username: string; password?: string },
    signal: AbortSignal,
  ): Promise<LoginResult>;
  respond(
    challenge: PortalChallenge,
    code: string,
    signal: AbortSignal,
  ): Promise<LoginResult>;
  assets(
    identity: PortalIdentity,
    query: { search: string; cursor?: unknown },
    signal: AbortSignal,
  ): Promise<{ items: ProviderAsset[]; nextCursor?: unknown }>;
  request(
    identity: PortalIdentity,
    selection: {
      asset: unknown;
      account: unknown;
      protocol: BastionProtocol;
      requestId: string;
    },
    signal: AbortSignal,
  ): Promise<GrantResult>;
  approval(
    identity: PortalIdentity,
    grant: PortalGrant,
    signal: AbortSignal,
  ): Promise<GrantResult>;
  exchange(
    identity: PortalIdentity,
    grant: PortalGrant,
    signal: AbortSignal,
  ): Promise<TemporaryEndpoint>;
  /** Acknowledges remote cancellation only if the actual contract guarantees it. */
  cancel?(
    identity: PortalIdentity,
    grant: PortalGrant,
    signal: AbortSignal,
  ): Promise<boolean>;
  logout(identity: PortalIdentity, signal: AbortSignal): Promise<void>;
  /** Local memory cleanup only, never a network request or automatic ticket retry. */
  dispose(handle: unknown): void;
}
