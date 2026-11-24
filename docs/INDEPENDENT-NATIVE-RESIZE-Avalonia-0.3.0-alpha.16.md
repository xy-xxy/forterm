# alpha.16 独立实际Mac启动与缩放有限复核（2026-10-31）

经理获用户本次明确许可，用冻结alpha.16独立实例、自有ZDOTDIR、启动前预写的实际多行彩色中文长左提示与右提示做真实Mac UI复验。PID49932，identity中的apphost SHA `27eaa7b55753c9f91c3fae052f612cc811dde582af9d6b95277ee8a8056dccb1` 与冻结IDENTITY一致。

本聊天只归档原图、identity、manager-result与launch.log，未操作窗口、运行测试、改应用源码或重打包。目录 `avalonia/evidence/independent-native-resize-alpha16-20261031`，PROVENANCE含来源绝对路径、逐项SHA、PNG尺寸；全部按原字节复制，9张原图。经理操作事实依据其报告和manager-result；本聊天实际查看restored原图与身份/结果文件。没有把软件UI图替换成原生图或修改冻结清单。

经理报告的有限结果：

- 正常“本地终端”按钮启动，无空引用错误。独立实际窗口与包内DLL软件复验是两条分别留存的证据。
- 边角拖拽未改变尺寸，不作为缩放成功。双击标题实际1150×788→2560×1326→1150×788→再次2560×1326，当前多行左右提示未新增重复/交错。
- 文件分隔线把终端收窄到约284px，再双击重置宽度，当前多行提示正常。历史右提示保留旧列符合已说明的不reflow策略，不冒称历史重新排版。
- 粘贴 `echo mgr16ok` 加Return精确执行，有独立输出，输入位置通过。CUA typeText大写样本被工具改变，不归因为产品输入缺陷，也不当成成功样本。
- 正常UI关闭自建终端并CmdQ，经理ps确认PID49932退出；用户旧会话未动。无真实连接/vault/用户.zshrc访问。

本轮没有协议尺寸抓取，stty/WindowChange的先前软件与真实PTY证据仍独立；此轮不增加此项声明。仅合成提示实际Mac启动/缩放/收窄和一次粘贴有限通过，不代表用户原shell主题、Windows/ConPTY/IME、picker、系统vault或生产连接/上传通过。三包及软件138图冻结记录、校验值和旧版本保持不变，不重跑无变化全量。
