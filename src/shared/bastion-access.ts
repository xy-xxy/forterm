/** Public projections only. Vendor IDs, identity handles and credentials stay in main. */
export type BastionProtocol = "ssh" | "sftp";
export interface BastionAccessState {
  accountId: string;
  available: boolean;
  status:
    | "unavailable"
    | "signed-out"
    | "authenticating"
    | "mfa"
    | "authenticated"
    | "expired"
    | "unknown"
    | "error";
  message: string;
  expiresAt?: number;
  challenge?: { ref: string; kind: "otp" | "browser"; expiresAt: number };
}
export interface BastionAsset {
  ref: string;
  name: string;
  accounts: { ref: string; name: string; protocols: BastionProtocol[] }[];
}
export interface BastionAssetPage {
  items: BastionAsset[];
  nextCursor?: string;
}
export interface BastionOpenOperation {
  id: string;
  accountId: string;
  assetRef: string;
  accountRef: string;
  protocol: BastionProtocol;
  status:
    | "requesting"
    | "pending-approval"
    | "exchanging"
    | "connecting"
    | "ready"
    | "denied"
    | "cancelled"
    | "expired"
    | "unknown"
    | "error";
  message: string;
  sessionId?: string;
}
export interface BastionAccessSnapshot {
  accounts: BastionAccessState[];
  operations: BastionOpenOperation[];
}
