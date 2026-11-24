import { z } from "zod";
import { totpParameters } from "./totp";
export const bastionMetadata = z.object({
  name: z.string().trim().min(1).max(128),
  username: z.string().trim().min(1).max(256),
  portalUrl: z
    .string()
    .trim()
    .max(2048)
    .transform((s, ctx) => {
      try {
        const u = new URL(s);
        if (
          !["https:", "http:"].includes(u.protocol) ||
          !u.hostname ||
          u.username ||
          u.password ||
          u.search ||
          u.hash
        )
          throw 0;
        return u.href;
      } catch {
        ctx.addIssue({ code: "custom", message: "入口地址无效" });
        return z.NEVER;
      }
    }),
  totp: totpParameters,
});
export const bastionInput = bastionMetadata.extend({
  id: z.string().min(1).max(128).optional(),
  passwordAction: z.enum(["keep", "replace", "clear"]),
  password: z.string().max(16384).optional(),
  seedAction: z.enum(["keep", "replace", "clear"]),
  seedInput: z.string().max(4096).optional(),
  totpEnabled: z.boolean(),
});
