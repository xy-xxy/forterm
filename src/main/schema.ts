import { z } from "zod";
import { DEFAULT_KEEP_ALIVE } from "../shared/keep-alive";
export const keepAliveSchema = z.object({
  enabled: z.boolean().default(DEFAULT_KEEP_ALIVE.enabled),
  intervalSeconds: z
    .number()
    .int()
    .min(5)
    .max(600)
    .default(DEFAULT_KEEP_ALIVE.intervalSeconds),
  sftpProbe: z.boolean().default(DEFAULT_KEEP_ALIVE.sftpProbe),
});
export const connectionSchema = z.object({
  protocol: z.enum(["ssh", "sftp", "ftp"]),
  host: z
    .string()
    .min(1)
    .max(253)
    .regex(/^[^\s/\\\x00]+$/),
  port: z.number().int().min(1).max(65535),
  username: z.string().min(1).max(4096),
  password: z.string().max(16384).optional(),
  keyPath: z.string().max(4096).optional(),
  passphrase: z.string().max(16384).optional(),
  title: z.string().max(256).optional(),
  temporary: z.boolean().optional(),
  jumpId: z.string().max(128).optional(),
});
export const profileSchema = connectionSchema
  .omit({ temporary: true, passphrase: true })
  .extend({
    id: z.string().max(128).optional(),
    group: z.string().max(128).default("我的连接"),
  });
export const remotePath = z
  .string()
  .min(1)
  .max(8192)
  .refine((p) => !p.includes("\0"));
export const identifier = z.string().min(1).max(128);
