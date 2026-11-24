> 本文保留dev.2原审查索引。最新计时修订dev.3见PROGRESS首节与 `checkpoints/native-0.2.2-dev.3-review-20261028/SOURCE-DELTA.diff`；dev.2原稿未改，运行审查尚未通过。

# 0.2.2-dev.2 独立审查入口

本轮目标仍是 Windows 实际按钮响应、上传及原生界面改善。审查候选不等于最终发行，不以短暂空配置诊断代替实际交互验收。

## 候选身份

- `release/Forterm-Native-0.2.2-dev.2-review-windows-x64.zip`
- ZIP SHA256：`258d887c098f2aba3530252551e91b151b90ba3e641186219b03ae813eddfb02`
- 主PE SHA256：`62c41b96a3f329da477e4666487d2325f2ae28e8400ad6d5e6bfd0c880eaa148`
- `checkpoints/native-0.2.2-dev.2-review-20261028/native-source.tar.gz`
- 包内 `BUILD-ID.json`、`SOURCE-HASHES.json`，外部 `SHA256SUMS-Native-0.2.2-dev.2-review.txt`。

四入口AMD64、ZIP CRC和逐入口摘要已核对。源码清单字段是实际文件字节SHA。不要覆盖旧0.2.1或改冻结diag1/diag2。

## 优先代码检查

1. `internal/settings/store.go`：值拷贝替代JSON深拷贝；`ViewSnapshot`仅显示最后已提交的不可变metadata，不等待凭据/磁盘事务，不能作为认证数据读取入口。
2. `internal/settings/profile_login.go`：登录链在原事务门内取realm和凭据，后台调用；敏感临时字节清除/禁止JSON序列化；显式空密码保留。TOTP账户也取一致metadata/secret。对应 `view_concurrency_test.go`、原有配置与凭据补偿回归。
3. `internal/desktop/profile_tree.go`、`library_row.go`：树行不反复取整份配置；库列表只创建可见行；搜索不写坏用户分组折叠偏好，行回收不串ID。偏好/导入/导出IO后台完成。
4. `internal/desktop/file_directory.go`、`file_operations.go`、`transfers.go`、`internal/localfile/uri.go`：REALPATH错误步骤明确、绝对目录恢复须明确确认、来源/目录快照、Windows驱动器/中文/空格/字面百分号路径、失败计数及队列可见。不猜相对目录，不自动重登/重传或重放票据。
5. `internal/vtwidget` 和本地VT fork：精确字体advance、同样式ASCII run、宽grapheme独立格、有界输出/预算、历史循环缓冲与分配复用。旧版控制输入/ANSI/alternate screen/历史/resize功能保留。
6. `internal/uiresponse`、`cmd/forterm`、`cmd/entry`：只有显式单flag进入离线手动模式；controller参数不被吞掉。临时合成0/1000配置、无OS凭据/网络/本地终端。采样只含固定动作和单调时钟数字；不以Raster时间当最终呈现。

## 关键证据

证据根为 `native/evidence/performance-0.2.2/`。

| 范围 | 记录 |
|---|---|
| 真实Windows短诊断 | `windows-field-report/` 三份原始JSON与assessment；系统返回10.0.19045，保留与先前Win11描述的差异；Forterm按钮0样本，不能称旧版已修 |
| 同负载/实际格数性能 | `comparison.json`、`NATIVE-PERFORMANCE-0.2.2.md`；Mac数据，分清排队/实际回调/绘制/Swap与最终呈现 |
| 长负载限制 | `history-long`最大758.56ms仍有，未隐藏；后续scratch/run分配优化只有定向证明，没有新GUI时延结论 |
| 配置及按钮CPU | `config-snapshot-before.log`、`config-snapshot-after.log`、`button-callback-comparison.json`；Mac纯软件，不是Win总点击延迟 |
| 非阻塞视图与认证一致性 | `view-vault-coherence-race.log`、`committed-view-popup-race-fixed.log` |
| 原生picker正向闭合 | `picker-upload/run-45327/ready.json`、`upload-committed.json`、`result.json`、`cua-identity.json`；PID一致，取消0队列、字节/目标正确、认证1 |
| 早期矛盾保留 | `picker-upload/cua-record-discrepancy.json`和旧result，不改写成通过 |
| 连接库/小窗实操 | `virtual-library-native/library-last.jpg`已逐图核对至0999；`callbacks.json`为Mac两个实际回调约20/22ms。`preferences-small.jpg`复核发现是预览到期后的欢迎页，不能作小窗设置截图证据；见capture-assessment.json，正确存图仍待补齐 |
| 组件/全量检查 | `latest-config-ui-full-race.log`、`latest-config-ui-vet.log`、`dev2-entry-ui-tests.log`；Go缓存复用如实保留，不重复无变化全量 |

## 尚未完成与运行协调

- 新构建的实际Windows按钮正样本、UI反馈及最新高DPI四区组合仍待。离线手动模式为 `forterm-diagnose.exe --ui-check`，须审查协调后再运行，程序员不同时控制Windows App。
- 普通主程序启动会读取原生配置/偏好并进入正常实例目录，不能与隔离模式混同。既有0.2.1运行文件不替换；不生产上传、终端命令、重登或重放票据。
- 最终版本号、安装器/正式完整ZIP/便携三包、SHA、更新说明与最终独立复核尚未交付。不得把本审查ZIP当成最终完成。
