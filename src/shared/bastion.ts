export interface TotpParameters {
  algorithm: "SHA1" | "SHA256" | "SHA512";
  digits: 6 | 8;
  period: number;
}
export const DEFAULT_TOTP: Readonly<TotpParameters> = {
  algorithm: "SHA1",
  digits: 6,
  period: 30,
};
export interface BastionAccount {
  id: string;
  revision: string;
  name: string;
  portalUrl: string;
  username: string;
  hasPassword: boolean;
  hasTotpSecret: boolean;
  totpEnabled: boolean;
  totp: TotpParameters;
}
export interface BastionAccountInput {
  id?: string;
  name: string;
  portalUrl: string;
  username: string;
  passwordAction: "keep" | "replace" | "clear";
  password?: string;
  seedAction: "keep" | "replace" | "clear";
  seedInput?: string;
  totpEnabled: boolean;
  totp: TotpParameters;
}
export interface TotpReading {
  code: string;
  generatedAt: number;
  expiresAt: number;
  period: number;
}
