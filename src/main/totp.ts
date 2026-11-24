import { createHmac } from "node:crypto";
import { z } from "zod";
import {
  DEFAULT_TOTP,
  type TotpParameters,
  type TotpReading,
} from "../shared/bastion";
export const totpParameters = z.object({
  algorithm: z.enum(["SHA1", "SHA256", "SHA512"]),
  digits: z.union([z.literal(6), z.literal(8)]),
  period: z.number().int().min(5).max(300),
});
export function decodeBase32(input: string): Buffer {
  if (typeof input !== "string" || input.length > 1024)
    throw new Error("验证器 key 格式无效");
  const clean = input.replace(/[ \t\r\n-]/g, "").toUpperCase();
  if (!/^[A-Z2-7]+={0,6}$/.test(clean))
    throw new Error("验证器 key 必须是 Base32 字符");
  const raw = clean.replace(/=+$/g, "");
  const remainder = raw.length % 8;
  const padding: Record<number, number> = { 0: 0, 2: 6, 4: 4, 5: 3, 7: 1 };
  if (
    !(remainder in padding) ||
    (clean.includes("=") && clean.length - raw.length !== padding[remainder])
  )
    throw new Error("验证器 key 长度或填充无效");
  let bits = 0,
    value = 0;
  const bytes: number[] = [];
  for (const c of raw) {
    value = (value << 5) | "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".indexOf(c);
    bits += 5;
    if (bits >= 8) {
      bits -= 8;
      bytes.push((value >>> bits) & 255);
      value &= (1 << bits) - 1;
    }
  }
  if (value !== 0 || bytes.length < 10 || bytes.length > 128)
    throw new Error("验证器 key 必须为有效的 10–128 字节 Base32 密钥");
  return Buffer.from(bytes);
}
export function parseTotp(
  input: string,
  parameters: TotpParameters = { ...DEFAULT_TOTP },
): { seed: string; parameters: TotpParameters } {
  if (typeof input !== "string" || input.length > 4096)
    throw new Error("验证器配置格式无效");
  let secret = input.trim(),
    settings = parameters;
  if (/^otpauth:/i.test(secret)) {
    let uri: URL;
    try {
      uri = new URL(secret);
    } catch {
      throw new Error("验证器 URI 格式无效");
    }
    if (
      uri.protocol !== "otpauth:" ||
      uri.hostname.toLowerCase() !== "totp" ||
      uri.username ||
      uri.password ||
      uri.port ||
      uri.hash
    )
      throw new Error("仅支持 otpauth TOTP，不支持 HOTP 或其他类型");
    try {
      const label = decodeURIComponent(uri.pathname.slice(1));
      if (!label || label.length > 256 || /[\x00-\x1f]/.test(label)) throw 0;
    } catch {
      throw new Error("验证器 URI 标签无效");
    }
    const allowed = new Set([
      "secret",
      "issuer",
      "algorithm",
      "digits",
      "period",
    ]);
    for (const key of uri.searchParams.keys())
      if (!allowed.has(key) || uri.searchParams.getAll(key).length !== 1)
        throw new Error("验证器 URI 含不支持或重复的参数");
    secret = uri.searchParams.get("secret") || "";
    const algorithm = uri.searchParams.get("algorithm") || "SHA1",
      digits = uri.searchParams.get("digits") || "6",
      period = uri.searchParams.get("period") || "30";
    if (
      !["SHA1", "SHA256", "SHA512"].includes(algorithm) ||
      !["6", "8"].includes(digits) ||
      !/^\d{1,3}$/.test(period)
    )
      throw new Error(
        "验证器参数不支持：算法须为 SHA1/SHA256/SHA512，位数为 6 或 8",
      );
    settings = {
      algorithm: algorithm as TotpParameters["algorithm"],
      digits: Number(digits) as 6 | 8,
      period: Number(period),
    };
  }
  const parsed = totpParameters.safeParse(settings);
  if (!parsed.success)
    throw new Error("验证器周期须为 5–300 秒，位数为 6 或 8");
  const bytes = decodeBase32(secret);
  bytes.fill(0);
  return {
    seed: secret.replace(/[ \t\r\n=-]/g, "").toUpperCase(),
    parameters: parsed.data,
  };
}
export function generateTotp(
  seed: string,
  parameters: TotpParameters,
  now = Date.now(),
): TotpReading {
  if (!Number.isSafeInteger(Math.floor(now)) || now < 0)
    throw new Error("系统时间无效，请检查时钟");
  const checked = totpParameters.safeParse(parameters);
  if (!checked.success) throw new Error("验证器参数无效");
  const key = decodeBase32(seed),
    counter = Buffer.alloc(8);
  const step = Math.floor(now / 1000 / parameters.period);
  counter.writeBigUInt64BE(BigInt(step));
  let digest: Buffer | undefined;
  try {
    digest = createHmac(parameters.algorithm.toLowerCase(), key)
      .update(counter)
      .digest();
    const offset = digest[digest.length - 1] & 15;
    const binary = digest.readUInt32BE(offset) & 0x7fffffff;
    return {
      code: String(binary % 10 ** parameters.digits).padStart(
        parameters.digits,
        "0",
      ),
      generatedAt: now,
      expiresAt: (step + 1) * parameters.period * 1000,
      period: parameters.period,
    };
  } finally {
    key.fill(0);
    counter.fill(0);
    digest?.fill(0);
  }
}
