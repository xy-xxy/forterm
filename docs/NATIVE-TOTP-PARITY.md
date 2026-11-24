# 原生TOTP严格兼容回归

2026-10-27。经理用公开合成seed的独立核对指出：早期Go实现缩窄了旧版周期，并放宽了URI和Base32输入。该实现尚未接入最终账户UI或发布；已以真实0.1.7 `src/main/totp.ts` 为基线修复。

- Normalize/Generate统一5–300秒；6/8位，SHA1/SHA256/SHA512。
- URI是独立配置，缺参数一律SHA1/6/30，不继承先前界面参数。
- URI拒绝重复/未知参数、userinfo/port/fragment、空/过长/控制字符label；输入4096、Base32输入1024（UTF16长度）边界。
- Base32检查合法字符、规范填充、合法余数及未用尾位为零，保留10–128字节限制。
- 新定向测试包括RFC6238三算法、前导零、5/300边界、URI独立默认、上述非法项；仅公开合成数据，不触及钥匙串或真实key。证据 `native/evidence/totp-parity-race.log`。
