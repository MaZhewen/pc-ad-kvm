# 独立网络键鼠共享实施计划

> 2026-11-11 归档说明：下文保留编制时的授权边界与执行计划；独立 Wi-Fi 实现现已提交于 `4a9f0dc`（`v1.1.0-rc.1`）。计划复选框不是最新验收结论，当前证据与剩余范围见[验证记录](../../validation/2026-11-direct-network.md)。

> 给执行者：用户当前仅授权文档。后续获得实施指令后，使用 executing-plans 技能逐项执行；本计划不自动授权提交、发布或设备操作。步骤以复选框追踪。

**目标：** 让 ADB 仅负责启动，Android 在无线调试关闭后仍通过独立加密局域网连接共享键鼠与剪贴板。

**架构：** Android 主动连接 PC 单接口 TLS 网关；输入与可选剪贴板分通道，以 instanceId、token、sessionId 和本地 generation 隔离实例及重连。外层 daemon 管理脱离 ADB 的进程与重连，现有输入处理保持复用。

**技术栈：** C# 5、.NET Framework 4.8、Java 8、Android app_process、UHID、TLS 1.2、PowerShell。正式约束见 [设计文档](../specs/2026-11-04-direct-network-design.md)。

日期：2026-11-04。状态：供审阅，未开始正式实施。2026-11-03 草稿不作为实施依据。

## 执行边界及文件分工

当前工作分支为 codex/direct-network。执行前重新检查工作区与文件，记录用户既有 src/agent/Program.cs 按键修复差异，禁止覆盖或回滚。已有探索文件不是可直接交付的成品；按本计划修正后才记为完成。不为文档任务继续写源码。

| 文件 | 变更责任 |
| --- | --- |
| src/agent/DirectBootstrap.cs | 路由、唯一 ADB target、私有部署、串行启动、取消与实例清理 |
| src/agent/DirectNetworkGateway.cs | v2 握手、sessionId、可选剪贴板、截止时间、证书清理 |
| src/injector/PinnedTls.java | v2 双向握手与 pin 校验；向调用者返回 sessionId |
| src/injector/DirectSessionConfig.java、DirectOwner.java（新增） | 配置解析、私有路径、Java owner 锁 helper 与每实例 stop |
| src/injector/DirectDaemon.java | 脱离 ADB 生命周期、就绪期限、退避与停止 |
| src/injector/Injector.java、SessionLink.java、DeviceGeometry.java | 会话释放、心跳、设备几何与 STOP |
| src/agent/Protocol.cs、PointerSession.cs、Watchers.cs | 0x10/0x11/0x12、generation/epoch、运行期无 ADB 依赖 |
| src/agent/ConnectionCoordinator.cs、ConnectionSnapshot（原文件内） | 生命周期状态、取消、显式重新启动、bootstrap 身份 |
| src/agent/ClipboardService.cs、src/injector/ClipboardSession.java | 可选通道、sessionId 归属与失败隔离 |
| src/agent/ConnectionForm.cs、TrayUi.cs、ConnectionProfile.cs、Config.cs、Program.cs | 连接入口、状态文案、旧配置兼容及必要接线；不重写既有按键修复 |
| build/build-agent.ps1、build/build-injector.ps1、README.md、tests/README.md | 配套构建、运行要求、启动与故障说明 |
| tests/direct-network、direct-daemon、direct-bootstrap、coordinator、connection、connection-form、clipboard | 新协议、竞态、集成与回归测试 |
| docs/validation/2026-11-direct-network.md（实施期创建） | 真机步骤、环境、结果与失败证据 |

新的接口以职责契约为准，不把当前探索 API 当作冻结接口：Gateway 启动结果提供地址、端口、证书摘要；输入认证结果提供 sessionId 和本地 generation；Bootstrap 接受不可变 target 和启动配置，返回 instanceId 与分阶段结果；Daemon 的“已就绪”回调必须来自完成几何及心跳准备的会话，不可由 TLS 成功替代。Clipboard 接受当前会话身份，可独立禁用或重连。

## 阶段 0：最小真机可行性门槛（任务 D01–D04）

### D01 基线、约束与环境

- [ ] 读取规则、项目相关记忆；检查 git diff 与已有探索测试，保存用户按键修复的差异基线。
- [ ] 核查目标序列号、系统版本、现时 WLAN 地址、无线 ADB 可用性及 .NET 4.8；不复用历史 IP 作为事实。
- [ ] 检查 GameViewer.exe 是否运行；若存在，先记录其对输入测试的干扰，不把污染结果归为 KVM 故障。
- [ ] 运行既有 transport、session、coordinator、connection、wifi-latency 回归；记录环境拒绝 socket 与断言失败的区别。

通过条件：基线结果与现有修改归属清楚，无需修改用户代码来获得基线。

### D02 脱离 ADB 进程和私有部署探针

文件：DirectBootstrap.cs、DirectSessionConfig.java、DirectDaemon.java、tests/direct-bootstrap/Main.cs、tests/direct-daemon/TestMain.java。

- [ ] 先新增取消、独立目录、owner 锁互斥、旧实例清理不删新 owner、持锁进程退出释放锁的测试；确认测试会捕获当前无锁删除实现。
- [ ] 仅实现探针所需的私有部署与 detached 启动，完整关闭或重定向标准流；配置不进入命令行。owner 操作由上传 jar 中新增的 DirectOwner.java helper 执行：ADB 调用 app_process 的 publish/stop/cleanup 子命令，helper 与 daemon 都对稳定锁文件使用 Java FileChannel.lock；不要求 shell 实现文件锁协议。第一次启动先上传 jar 再调用 helper，publish 成功后才 detached 启动 daemon。
- [ ] 无线 ADB 启动探针，确认独立 TCP 心跳后关闭无线调试并结束启动 shell；持续观察 5 分钟，再通过重新打开 ADB 核验进程及日志。
- [ ] USB ADB 启动，拔线后同样观察；分别记录两种路径，不能相互替代。
- [ ] 检查配置和目录权限、日志无 token；验证 owner 替换及失败清理。

通过条件：两种启动均能脱离 adbd 存活。失败时停在本阶段，调查具体生命周期限制并报告，不用 UI 集成掩盖基础不可行。

### D03 目标设备 TLS 和基础网络

文件：DirectNetworkGateway.cs、PinnedTls.java、tests/direct-network/Main.cs、TestMain.java。

- [ ] 在目标 Android 的 app_process 上完成 TLS 1.2 双向数据交换；验证正确 pin/token 成功，错 pin/token 拒绝。
- [ ] 检查 PC 证书私钥生命周期、PFX 导入、构造失败和退出清理；确认 .NET 4.8 前置条件。
- [ ] 用实际 PC 有线网络与平板 Wi-Fi 路径测试；PC 网卡按路由选择，不要求两端都连接 Wi-Fi。
- [ ] 核查防火墙具体程序、接口与网络配置文件；若企业策略阻止入站，记录阻断，不自动放宽策略。

通过条件：目标设备能连接指定 PC 接口，TLS 资源可控；本机 Java 测试不能替代此项。

### D04 无 ADB 条件下恢复

- [ ] 关闭无线调试，令 WLAN 短暂断开再恢复，确认 daemon 重连同一 PC 地址。
- [ ] 记录 daemon 是否存活、恢复耗时和实际 socket 来源，不使用 ADB reverse 辅助成功。
- [ ] 输出最小可行性结果；若无法满足零 APK 的存活要求，暂停后续阶段并呈报边界。

通过条件：无 ADB 时独立数据交换恢复；此时尚不宣称真实 AP 漫游或完整键鼠验收通过。

## 阶段 1：协议与生命周期定稿（D05–D09）

### D05 DKVM v2 网关与客户端

- [ ] 冻结 SESSION_READY（0x12、4 字节 geometry epoch）接口及完整就绪回调语义；D07 用可控 READY 信号测试生命周期，D08 再接入真实消息，避免任务循环依赖。
- [ ] 在 tests/direct-network 中先写相同 70 字节请求与 17 字节响应的 C#/Java 对照向量；覆盖分段读取、截断、版本、非法通道、错误 token/pin、字节序。
- [ ] 服务端先校验 6 字节公共头再读取剩余字段；测试 v1 客户端不发送 v2 尾部时仍能立即收到版本错误，分段读取不能重新计算握手截止时间。
- [ ] 实现输入成功后随机 sessionId、剪贴板绑定当前 sessionId、重复通道拒绝和未认证连接上限 4。
- [ ] 测试旧输入断开后旧剪贴板迟到、重复输入不踢健康会话、5 秒总握手超时、Dispose 与 accept 竞争。
- [ ] 将 clipboardLocalPort 改为可缺省能力；禁用时输入正常、channel 2 返回状态 5。

通过条件：真实跨语言 TLS 测试覆盖隔离，错误客户端从未接入回环业务端口。

### D06 bootstrap 事务与设备身份

- [ ] tests/direct-bootstrap 增加上传任一步失败、发布 owner 前后失败、取消后晚到结果、清理与新启动交错用例。
- [ ] target 固定为用户选择或已绑定的设备；USB 引导独立网络时必须匹配该身份，不取设备列表首项。
- [ ] 实现目录权限、完整性校验、锁内原子 owner 发布及每实例 stop；失败清理只操作本实例。
- [ ] PC 临时配置与日志执行秘密脱敏；配置凭据仅本次进程持有，不写长期 ini。

通过条件：所有失败点有确定终态；任何旧清理不能撤销新实例；无线和 USB bootstrap 都可用。

### D07 daemon 重连与退出

- [ ] tests/direct-daemon 用可控时钟和伪 session 测试 60 秒首次期限、30 分钟连续离线、1/2/4/5 秒退避、取消、owner 替换与 STOP。
- [ ] “成功”改为完整已就绪；TLS 成功但几何失败不得刷新离线期限。
- [ ] 采用包含休眠的单调时钟，网络建立与握手总截止 5 秒；所有循环和资源关闭可取消。
- [ ] 每次会话全新 KeyState、队列、UHID 和 ClipboardSession；离线立即释放输入，退出只清理自己目录。

通过条件：不存在无限半就绪重试延寿、跨会话按键或线程/socket 泄漏。

### D08 几何、心跳与安全接管

- [ ] tests/direct-daemon、tests/session、tests/transport 增加 0x10/0x11/0x12 的方向、长度、非法值、未知帧与旧 generation 用例。
- [ ] 设备首次及变化上报几何，500 毫秒检查；PC 用现有 epoch/PointerReady 流程准备指针，心跳有效后发送匹配 epoch 的 SESSION_READY；Android 收到此确认才更新 daemon 的成功状态。旋转期间释放旧输入并暂停发送，同一健康会话准备完成后恢复原接管状态；网络断线后仍需用户重新接管。
- [ ] SESSION_READY 的“写出成功”取当前 generation 的实际写入完成通知，不以进入发送队列为依据；补充排队后断线及旧完成回调晚到的测试，均不得将新会话提前标为已就绪。
- [ ] 两端以 500 毫秒心跳、2 秒失效释放；模拟静默丢包，验证无需 TCP EOF 也能释放。
- [ ] 运行既有 scancode、pointer、double-click 与按键回归，确认推角落归零和用户修复保持。

通过条件：认证、几何和心跳未全部完成时不能接管；断线与旋转没有旧输入重放。

### D09 剪贴板独立通道

- [ ] tests/clipboard 增加输入正常但剪贴板禁用/初始化失败、输入断开时传输未完成、旧 sessionId 迟到与新会话回调交错用例。
- [ ] 复用原有剪贴板内容协议和大小限制；仅增加 TLS 会话身份与生命周期，不扩大内容权限。
- [ ] 剪贴板失败只重试当前 sessionId 的该通道；输入失效则取消全部旧剪贴板工作。

通过条件：剪贴板状态不影响输入就绪，旧会话不能向新会话发布内容。

## 阶段 2：协调器、配置与界面（D10–D12）

### D10 协调器和运行监测

- [ ] tests/coordinator 构建 ADB 在线启动后立即离线、连接重建、停止/重试交错及已取消回调晚到的状态用例。
- [ ] 分开未连接、准备启动、等待认证、等待设备就绪、已就绪、等待重连与需重新启动；运行期直连健康仅由网络会话决定。
- [ ] Watchers 对直连不再调用 ADB 几何查询或 ADB 在线检测；USB 分支保持既有逻辑。
- [ ] 输入断开原子撤销 sessionId 和 generation，先回到本机再等待重连；显式 Retry 才执行 ADB bootstrap。

通过条件：在 ADB 不可用的测试替身中，运行期连接和重连无 ADB 调用；停止与重试不产生双 owner；停止先拒绝新准入，当前认证通道最多保留 1 秒用于发送 STOP，再撤销 sessionId。

### D11 配置和界面

- [ ] tests/connection、tests/connection-form 覆盖旧 WirelessTls 保存值、绑定身份、无线 bootstrap 与 USB bootstrap 的入口。
- [ ] 保持读取旧 WirelessTls 值；将其用户可见行为迁移为独立 Wi-Fi 启动流程，不静默切为 USB，也不继续恢复旧 reverse 数据通道。
- [ ] UI 展示分阶段进度、剪贴板独立状态及“重新启动连接”；说明启动支持无线 ADB，正常使用不依赖无线调试。
- [ ] 网络策略阻断、PC 地址变化、daemon 退出、运行环境不满足分别给出可执行提示。

通过条件：旧配置可用，不选错设备，不在 TLS 成功时提前宣布可控制。

### D12 配套构建与使用说明

- [ ] 调整必要构建输入和 .NET 4.8 检查，打包匹配的 exe/jar；协议版本不符给出更新提示。
- [ ] 先执行 build/build-injector.ps1，再执行 build/build-agent.ps1（后者有 jar 新鲜度守卫），记录真实输出路径；不手改生成产物。
- [ ] README 写明首次无线/USB启动、独立 Wi-Fi 和 USB 传输差异、防火墙边界、30 分钟退出及重启恢复方式。
- [ ] tests/README 登记新测试与所需 socket 权限；PowerShell 构建脚本沿用 UTF-8 BOM，其他文件保持既有编码和换行。

通过条件：在当前支持环境可构建，说明不把启动成功等同功能完成。

## 阶段 3：综合验证与交付（D13–D15）

### D13 自动化回归

在仓库根目录逐项运行，不用测试数量代替场景说明。现有命令均可直接执行；新增用例纳入对应入口。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/direct-network/run.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/direct-daemon/run.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/direct-bootstrap/run.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/transport/run.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/session/run.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/coordinator/run.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/connection/run.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/connection-form/run.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/agent-logic/run.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/wifi-latency/run.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/clipboard/run-integration.ps1
```

- [ ] 预期全部退出码 0 且无失败断言；socket 权限不足记为环境阻塞，取得合法运行条件后复测，不修改断言绕过。
- [ ] 对 D08 的指针及双击、D09 的剪贴板分项运行对应 tests 目录既有脚本，并在验证记录列出实际命令和结果。
- [ ] 检查源文件差异、编码、资源释放和秘密日志；核对用户 Program.cs 修复仍保留。

### D14 真机验收矩阵

- [ ] 两种 ADB 启动各测：认证→几何→就绪→接管→停止；无线关闭调试、USB 拔线后继续使用。
- [ ] 真实 BSSID 漫游且无线调试关闭：至少 3 次记录旧/新 BSSID、ADB 状态、断线时刻及就绪时刻；不能现场复现时标记未验收。
- [ ] 亮屏连续使用至少 60 分钟，覆盖按住修饰键时断网、鼠标拖动、滚轮、中文文本及图片剪贴板、旋转；无粘键和旧输入重放。
- [ ] 注入短断网、超过 30 分钟离线、PC 睡眠唤醒、PC 地址变化、PC/平板重启、辅助进程退出和并发重试。
- [ ] 测量网络恢复至就绪≤10秒、失效释放≤2.5秒的目标；超出记录原因并修正，不删减场景。
- [ ] 回归 USB 模式与关闭剪贴板模式。

### D15 发布前审阅与交付

- [ ] 将所有结果写入 docs/validation/2026-11-direct-network.md，明确已测设备、未测范围与失败重现。
- [ ] 逐项对照设计第 1–9 节及任务清单，不把探索报告升级为当前版本证据。
- [ ] 输出代码差异、匹配构建产物和使用说明供审阅；本计划不要求自动提交或发布。

通过条件：必测门槛全部通过，未通过项明确阻止相应交付声明。实际企业网络或系统进程策略不可控时，结论限定到已测环境。
