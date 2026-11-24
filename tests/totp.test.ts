import { test } from "node:test";
import assert from "node:assert/strict";
import { generateTotp, parseTotp, decodeBase32 } from "../src/main/totp";
import { DEFAULT_TOTP } from "../src/shared/bastion";
const base32 = (input: string) => {
  let bits = 0,
    value = 0,
    result = "";
  for (const b of Buffer.from(input)) {
    value = (value << 8) | b;
    bits += 8;
    while (bits >= 5) {
      bits -= 5;
      result += "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"[(value >>> bits) & 31];
    }
    value &= (1 << bits) - 1;
  }
  if (bits)
    result += "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"[(value << (5 - bits)) & 31];
  return result;
};
const seed = base32("12345678901234567890");
test("RFC 6238 Appendix B vectors for all three algorithms, including post-2038 time", () => {
  const times = [
    59, 1111111109, 1111111111, 1234567890, 2000000000, 20000000000,
  ];
  const vectors = {
    SHA1: [
      "94287082",
      "07081804",
      "14050471",
      "89005924",
      "69279037",
      "65353130",
    ],
    SHA256: [
      "46119246",
      "68084774",
      "67062674",
      "91819424",
      "90698825",
      "77737706",
    ],
    SHA512: [
      "90693936",
      "25091201",
      "99943326",
      "93441116",
      "38618901",
      "47863826",
    ],
  };
  const seeds = {
    SHA1: seed,
    SHA256: base32("12345678901234567890123456789012"),
    SHA512: base32(
      "1234567890123456789012345678901234567890123456789012345678901234",
    ),
  };
  for (const algorithm of ["SHA1", "SHA256", "SHA512"] as const)
    times.forEach((t, index) =>
      assert.equal(
        generateTotp(
          seeds[algorithm],
          { algorithm, digits: 8, period: 30 },
          t * 1000,
        ).code,
        vectors[algorithm][index],
      ),
    );
});
test("six-digit leading zero, Unix time and exact period boundaries", () => {
  assert.equal(
    generateTotp(seed, { ...DEFAULT_TOTP }, 1111111109000).code,
    "081804",
  );
  assert.equal(generateTotp(seed, { ...DEFAULT_TOTP }, 59999).code, "287082");
  assert.equal(generateTotp(seed, { ...DEFAULT_TOTP }, 60000).code, "359152");
  assert.equal(generateTotp(seed, { ...DEFAULT_TOTP }, 59999).expiresAt, 60000);
  assert.equal(generateTotp(seed, { ...DEFAULT_TOTP }, 60000).expiresAt, 90000);
  assert.equal(
    generateTotp(seed, { ...DEFAULT_TOTP }, Date.UTC(1970, 0, 1, 0, 0, 59))
      .code,
    "287082",
  );
  assert.throws(() => generateTotp(seed, { ...DEFAULT_TOTP }, -1), /系统时间/);
});
test("Base32 normalization and strict URI parameters without secret-bearing errors", () => {
  assert.equal(
    parseTotp(
      seed
        .toLowerCase()
        .match(/.{1,4}/g)!
        .join(" "),
    ).seed,
    seed,
  );
  assert.equal(decodeBase32("JBSWY3DPEHPK3PXP").length, 10);
  const parsed = parseTotp(
    `otpauth://totp/Example:test?secret=${seed}&algorithm=SHA256&digits=8&period=45`,
  );
  assert.deepEqual(parsed.parameters, {
    algorithm: "SHA256",
    digits: 8,
    period: 45,
  });
  for (const input of [
    "not-base32-01",
    seed + "=",
    seed.slice(0, -1),
    `otpauth://hotp/X?secret=${seed}&counter=0`,
    `otpauth://totp/X?secret=${seed}&algorithm=MD5`,
    `otpauth://totp/X?secret=${seed}&period=0`,
    `otpauth://totp/X?secret=${seed}&digits=7`,
    `otpauth://totp/X?secret=${seed}&secret=${seed}`,
    `otpauth://totp/X?secret=${seed}&unknown=x`,
  ]) {
    assert.throws(
      () => parseTotp(input),
      (e) => !String(e).includes(seed) && !String(e).includes("otpauth://"),
    );
  }
});
