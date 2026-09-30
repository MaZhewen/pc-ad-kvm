# PC-AD-KVM 无线连接实现方案

日期：2026-09-29

代码基线：`3a17326`

状态：方案草案；本次只编写设计，不代表功能已经实现或真机验证通过。

## 1. 目标与范围

让 Windows PC 无需持续连接 USB 数据线，即可通过局域网控制 Android 的鼠标和键盘，继续使用现有免安装 jar 注入器、绝对坐标指针、双次贴边和全局快捷键。

本稿默认首期支持 **Android 11+ 的无线调试配对模式**；用户尚未确认是否需要同时实现旧式 USB 转网络 ADB，因此将其列为兼容扩展，不作为首期交付条件。首期只管理一个活动设备，允许切换 USB / 无线，不做自动跨链路切换、多设备同时接管、公网中继、蓝牙、Wi-Fi Direct、投屏或 APK。

项目历史环境记录为 Windows 有线联网、Android 16 小米平板。PC 不必新增无线网卡，但这依赖有线网与设备所在 Wi-Fi 之间允许 TCP 通信；历史记录不能证明当前网络互通。办公网的客户端隔离、VLAN 或防火墙可能阻断链路。配对成功、ADB 在线和 KVM 可用是三个不同阶段，必须分别验证。

验收目标：首次在手机确认无线调试并完成配对后，PC 可以保存目标设备并重新连接；网络中断时恢复 PC 输入，网络恢复后重新初始化 KVM，但不自动恢复接管。

## 2. 技术路线比较

| 路线 | 实现方式 | 优点 | 限制与结论 |
|---|---|---|---|
| A：无线调试 + ADB reverse | `adb pair/connect` 建立连接，沿用 reverse 和 app_process | 保持零 APK；复用输入协议和部署路径；无线调试提供 TLS | Android 11+；需处理发现和端口变化。**首期推荐** |
| B：USB 开启网络 ADB | USB 下执行 `adb -s <serial> tcpip 5555`，然后连接设备 IP | 可兼容较旧 Android | 初次需要 USB；传统 TCP ADB 不提供该无线调试 TLS 通道。作为用户主动启用的兼容扩展 |
| C：自建 TCP/UDP 数据通道 | ADB 只负责启动，注入器直接连接 PC 网络端口 | 可独立控制传输策略 | 另需认证、加密、网络监听与防火墙适配；仍须解决 shell 权限和注入器启动。当前没有足够收益 |

Android 官方支持通过配对码建立无线调试连接；AOSP 区分配对服务与连接服务，并说明无线调试连接端口动态分配。[Android ADB 文档](https://developer.android.com/tools/adb)、[ADB Wi-Fi 架构](https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/docs/dev/adb_wifi.md)。

## 3. 现有代码与必须补齐的能力

| 位置 | 当前行为 | 本次设计的改动 |
|---|---|---|
| `src/agent/DeviceLauncher.cs` | 所有设备命令不带目标；运行结果丢弃 stderr；普通命令统一 5 秒超时 | 明确设备选择；结构化结果；配对输入、取消和分类超时 |
| `src/agent/Program.cs` | 启动时同步 Prepare；失败弹出 USB 提示并返回 | 先启动托盘和消息循环；后台连接；无设备时仍能打开连接窗口 |
| `src/agent/Watchers.cs` | 按 socket 状态重连；连续失败后重启全局 ADB | 连接管理交给协调器；无线失败采用目标级重试；心跳失联使会话失效 |
| `src/agent/Transport.cs` | 监听 loopback；接受单连接；没有单独断开当前会话的入口 | 增加会话校验与关闭当前连接；保留监听能力；明确回调代次 |
| `src/agent/Config.cs` | 无连接配置 | 保存模式、设备身份和最近成功端点；兼容旧 ini |
| `src/agent/TrayUi.cs` | 设置和退出入口；直接调用全局 Cleanup | 增加连接管理入口；清理准确归属到活动会话 |
| `src/injector/Injector.java` | 连接 Android loopback；阻塞读取，无接收超时 | 会话握手、帧读取期限、心跳看门狗和可靠退出 |

两个与无线可靠性直接相关的现有缺口：

1. PC 心跳超时只解除输入抑制，若 TCP 仍显示 Connected，重连线程会一直跳过恢复；Android 阻塞读取也可能无法退出。
2. `adb shell` 客户端进程退出不能作为远端注入器、UHID 一定释放的保证。无线黑洞场景需要 Android 自己判断租约到期并退出。

保留现有输入算法和队列策略，只改连接生命周期；不顺带重构快捷键、触边或设置布局。连接管理采用独立窗口，避免与已有设置布局方案耦合。

## 4. 数据路径与组件边界

```text
PC 键鼠 → 现有编码 / FrameQueue / FrameWriter
                    ↕
           PC 127.0.0.1:<localPort>
                    ↕
             adb reverse 隧道
                    ↕
       ADB server ←局域网 TLS→ Android adbd
                    ↕
           Android 127.0.0.1:27183
                    ↕
            Injector → /dev/uhid
```

图中 Android 注入器主动连接设备 loopback，通过 reverse 到达 PC listener；键鼠与确认消息在同一条双向连接传输。PC 的 KVM listener 继续只绑定 `IPAddress.Loopback`，不开放 `0.0.0.0:27183`，也不自动修改防火墙规则。

新增或拆分以下小组件，兼容当前 C# 5 / .NET Framework 构建链，不引入新运行时：

| 文件 | 职责与接口约定 |
|---|---|
| `AdbClient.cs` | 唯一进程执行入口；输入命令、参数列表、可选目标、超时与取消；返回退出码、stdout、stderr、耗时、超时/取消标志 |
| `AdbDevice.cs` | 解析 `devices -l`；保存 serial、state、transport_id、USB 标记、model；不启动进程 |
| `WirelessDiscovery.cs` | 解析 mDNS 连接服务快照，输出服务名和 IPv4 端点；只提供候选，不直接决定控制目标 |
| `ConnectionProfile.cs` | 持久化配置与活动会话快照的类型；快照不可变 |
| `ConnectionCoordinator.cs` | 单个后台执行者负责配对后连接、部署、重试、切换和清理；向 UI 发布带 generation 的状态 |
| `ConnectionForm.cs` | USB/无线选择、设备列表、配对和连接操作、错误提示；通过协调器操作，不自行执行 adb |

`DeviceLauncher` 保留 reverse、push、启动、屏幕查询的职责，但每个方法必须接收显式目标/会话，通过 `AdbClient` 执行。`Watchers` 保留前台守卫、UI 心跳与几何轮询，将重连循环和注入器进程所有权移交协调器。任何改造步骤均不能同时启用两套重连线程。

## 5. 设备识别与命令约束

### 5.1 明确区分三个标识

- **设备身份**：成功连接后读取的非空 `ro.serialno`，辅以无线 GUID（若设备允许读取）和显示名。用于防止端点变化后误选另一台设备，不视作额外密码学认证。
- **ADB 目标**：当前 `adb devices -l` 返回的完整 serial，例如 USB 序列号、`IP:port` 或 mDNS 服务形式。不能假设无线 serial 一定包含冒号。
- **连接端点**：当前 IPv4 与端口，只是可失效的连接线索，不能代替设备身份。

持久化身份缺失或不唯一时，仍可在本次会话中使用用户明确选择的目标；重启或地址变化后要求重新选择，不以型号相同、列表第一项或相同 IP 自动认定为原设备。

### 5.2 目标选择规则

1. 老配置缺少新字段时使用 USB 模式；无历史绑定且只有一个可用 USB 设备时可自动选取。
2. 多个 USB 设备时显示选择列表；无线模式首次总要用户选择在线目标或输入端点。
3. USB 与无线同时出现时按当前模式选择，不因 USB 插拔静默改变链路。
4. 网络重连后先校验设备身份，再执行 push、reverse、shell 注入等操作；不匹配进入“需要选择设备”。
5. 每轮生成不可变目标快照。设备命令优先使用本次枚举得到的 `-t <transport_id>`；兼容使用 `-s <完整 serial>`。transport_id 只限本轮使用，ADB 重启后重新解析。
6. `pair`、`connect`、`devices -l`、`mdns services` 属于主机命令，不机械添加设备选择器。`push`、`reverse`、屏幕查询、启动、清理全部指定目标。

参数只通过 `ProcessStartInfo` 启动 adb，不借助 `cmd /c`。由于当前框架没有现代 `ArgumentList`，在 `AdbClient` 中集中实现 Windows 参数转义并测试。用户端点首期只接受标准 IPv4 和 1–65535 的十进制端口，拒绝额外参数、换行和控制字符；显示名不拼接到远端 shell 命令。

## 6. 用户流程

### 6.1 首次配对

1. 托盘新增“连接设备…”，进入独立连接窗口，选择“无线调试”。手机开启无线调试并打开“使用配对码配对设备”。
2. 输入手机配对弹窗的 **配对地址、配对端口、六位配对码**。配对码以字符串处理，允许前导零。
3. 后台执行 `adb pair <pair-ip>:<pair-port>`，从重定向 stdin 写入配对码及换行，随即关闭 stdin。UI 显示进度并允许取消。
4. 先检查 `devices -l` 中是否已经出现在线无线设备；否则读取 `mdns services` 中的 `_adb-tls-connect._tcp` 服务候选。
5. 自动发现不能唯一绑定目标时，请用户选择候选，或从手机无线调试主页面输入 **连接地址和连接端口**。配对端口不能直接当作连接端口使用。
6. 执行 `adb connect <connect-ip>:<connect-port>` 后，必须再次枚举并验证目标 state 为 `device`。不能仅根据进程退出码或包含“connected”的文字宣告成功。
7. 校验身份、部署注入器、验证会话和指针 READY，显示“无线已就绪”。

配对码不写配置、日志或命令行；流程结束清空 UI 和持有引用，不承诺托管字符串可被安全擦除。ADB 自己管理授权密钥，程序不复制或解析用户的 ADB 私钥。取消或网络失败不循环重试配对码；用户重新打开手机配对弹窗后再尝试。

### 6.2 再次连接和手动操作

- 有已保存无线配置时：先查在线设备，再发现连接服务，最后尝试最近成功端点；每次最终都校验身份。
- mDNS 无结果但 TCP 可达时允许手动连接；显示“未发现设备”，不能仅凭空列表断言防火墙或 Wi-Fi 故障。
- 用户点击“断开”后停止自动重连，关闭 KVM 会话并释放输入；不执行无参数的全局 `adb disconnect`。
- 关闭连接窗口不等于断开；正在运行的配对操作则由“取消”显式终止。
- 修改连接模式或目标后点击“连接”才应用；校验失败保留原活动会话。校验通过准备切换时先释放接管，再停止旧会话。
- “忘记本程序的设备”仅清除配置；撤销系统 ADB 配对需到手机执行“忘记此计算机”，界面说明这一区别。

## 7. 建链、会话与重连

### 7.1 建链顺序

```text
Idle → Resolving → Connecting → Preparing → Handshaking → Ready
                          ↘ Waiting / NeedsPairing / NeedsSelection
Ready → 链路失败 → Reconnecting → Resolving
任意状态 → 用户断开/退出 → Stopping → Idle
```

配对是独立的短操作 `Pairing`，成功后进入 Resolving；不会把 Pairing 当作正常重连状态。

每次连接严格按以下顺序：

1. 选择并验证目标，分配递增 generation 和随机 session token。
2. PC loopback listener 准备就绪，获取实际 localPort。
3. 对目标执行 `reverse tcp:27183 tcp:<localPort>`，push 本次构建的 jar。
4. 启动目标上的 `app_process / Injector --session <token>`；保存本次客户端进程句柄。
5. Android 先完成下面的会话握手，再创建 UHID；握手失败不得进入输入循环。
6. 从相同目标查询屏幕几何，配置 PointerSession，等待当前代次 READY 与首个有效 PONG。
7. 进入 Ready。几何查询失败继续等待或超时重试，不使用固定的 2136×3200 回退宣告无线就绪。

`Handshaking` 阶段总体期限初始设为 20 秒，包含输入设备注册；过期关闭会话再按重试策略处理。这是设计参数，需真机测量后调整。

### 7.2 会话隔离与协议增量

现有键鼠、GEOMETRY、READY、ACK、PING/PONG 消息格式保持不变。新增连接前导握手，避免旧注入器因 reverse 重建误接到新会话：

- PC 每轮用 `RandomNumberGenerator` 生成 32 字节 token，命令行用 64 个十六进制字符传递，不记日志、不落 ini。它只用于本机/本设备会话隔离，不能抵御具有相同本机权限的进程读取。
- Android 建连后发送固定 37 字节前导：ASCII `PKVM`（4 字节）、版本 `1`（1 字节）、token（32 字节）。
- PC 在 2 秒总期限内读取全部前导，魔数、版本或 token 不匹配则关闭此候选 socket，继续接受其他连接；校验成功发送单字节 `0x01`，Android 收到后才能创建输入设备。
- `Connected` 事件只能在验证成功后发布；旧 jar 无握手会超时，显示版本不匹配并重新部署，不降级到无校验通道。
- 接受、消息、断开与几何查询回调都携带 generation；旧回调不得重置新 PointerSession、更新新状态或拆除新隧道。
- `Transport` 增加关闭当前连接的入口；只关闭该代的 socket 和 writer、清空队列，不停止 listener。每代断开通知恰好一次，异常路径也须覆盖。

Android 同一时间只允许一个正式 Injector 持有 UHID：使用独立锁文件的进程文件锁，持锁到退出；新实例在有界期限内等待旧实例释放，不使用全局 `pkill -f app_process`。会话握手在持锁并创建 UHID 之前完成；等待锁期间仍要维持已认证会话的活性检查。`--stdin` 探针入口保持独立行为，不加入生产握手。

### 7.3 心跳与输入释放

- PC 每 1 秒发 PING，以单调时钟记录当前代次、已发序号和有效 PONG；无关序号、重复 PONG 和旧代 PONG 不刷新活性。
- Ready 状态无论是否正在接管，最后有效 PONG 超过 2 秒即使会话失效；按现有 1 秒 UI tick 检查，正常调度下在最后 PONG 后约 2–3 秒处理。UI 线程不能执行阻塞 ADB 命令。
- 失效处理先阻止新的接管并释放 PC 输入、清按键/修饰键/触边等待状态，再关闭旧 socket、清发送队列、重置 PointerSession，最后启动重连。不能等 ADB 重试结束才还鼠标。
- Android 使用 `System.nanoTime()` 计时，正式输入循环中 3 秒未收到有效 PING 即终止会话；流读取设短轮询超时，并为整帧设置总期限，防止读到半包永久挂起。
- Android 初始化、等待锁和 UHID 注册期间也运行会话看门狗；该阶段允许最长 20 秒初始化期限，不能被正式输入循环的 3 秒规则误杀。进入正式循环后以当前时间开始首个 3 秒租约。
- Socket 始终只有一个读取者，初始化期间先读取并应答 PING，将 GEOMETRY 保留为最新一份，待 UHID 初始化后交给输入处理；正式循环同样经该读取者分发。输出通过一个串行写入口发送。看门狗只标记失效、关闭 socket 和执行退出兜底，不并发发送 HID 报告或读取同一条流。
- 异常、EOF、租约到期和主动退出统一走 `finally`：尽力发送零按键/零按钮报告并关闭 pointer、键盘 UHID 和 socket，各资源独立清理，前一步异常不跳过后续步骤。
- UHID 写入或关闭历史上可能阻塞，因此设置进程级退出兜底：终止状态开始后 1 秒仍未完成，由独立守护线程执行进程 halt，让内核回收 FD。需通过真机故障演练确认，无验证不宣称“必定清干净”。
- 重连后只恢复 Ready，不复用按键队列或恢复 Takeover；用户重新贴边或按快捷键接管。

### 7.4 重试与退出

无线恢复顺序：重新枚举已在线目标 → mDNS 连接服务候选 → 最近成功端点 → 提示手动更新地址/配对。mDNS 名称和 GUID 只提供发现线索；最终身份验证失败必须停在 NeedsSelection。

采用完成一轮后再等待的串行退避：`b = ReconnectSeconds`，等待 `min(b × 2^n, 60)` 秒（n 从 0 开始），加 0–20% 抖动并封顶 60 秒；成功归零。手动“重试”可提前唤醒，断开和退出可以取消命令及等待。

普通枚举/查询默认 5 秒，connect 10 秒，pair 30 秒，push 15 秒；都具有进程退出和 stdout/stderr 排空的共同总期限。启动注入器是长驻命令，由会话所有权管理，不套用普通命令超时。

无线模式不沿用每三次失败全局重启 ADB 的策略，`AllowKillAdb` 在无线模式不生效；连接窗口明确说明此项只控制 USB 恢复。网络断开、端口变化和授权问题不应触发全局 kill-server / taskkill。USB 保留原策略，但设备命令必须同样完成目标化。

退出顺序：禁止新操作 → 取消后台任务并使 generation 失效 → 立即释放 PC → 尽力发送 LEAVE 并关闭传输 → 终止自己持有的 adb shell 客户端 → 有界清理目标 reverse 和本程序 jar → 关闭日志。离线时不等待远端清理成功，依靠 Android 租约回收；旧任务不得在退出后重新 push 或启动进程。

固定设备端口 27183 仍意味着一个设备只支持一个 PC-KVM 会话；发现该端口已有不同归属映射时停止并提示，不自动覆盖其他会话。清理仅删除本代创建的资源，不能使用 `reverse --remove-all`。

## 8. 配置、状态与诊断

建议扩展 `pc-kvm.ini`：

```ini
ConnectionMode=Usb
UsbSerial=
WirelessDeviceSerial=
WirelessDeviceGuid=
WirelessServiceName=
WirelessLastEndpoint=
```

`ConnectionMode` 只接受 `Usb` / `WirelessTls`，默认 Usb。Wireless 字段只在已验证成功的连接后保存；GUID、服务名允许缺失，LastEndpoint 是缓存。字段缺失兼容旧配置；无效连接字段显示配置错误并等待用户选择，不静默换到另一台设备。原鼠标速度、快捷键等字段保持原有兼容规则。

状态分别展示“连接状态”和“输入状态”：例如“无线：已就绪 / 当前控制 PC”；Ready 不等于 Takeover。错误按原因提供动作：

| 原因 | 用户可执行动作 |
|---|---|
| 找不到 adb 或缺少 pair 能力 | 选择可用的 platform-tools；启动时固定本次使用的 adb 路径并显示版本 |
| 配对码失效/被拒绝 | 在手机重新打开配对码窗口后重试 |
| 已配对但没有连接服务 | 检查无线调试是否开启；输入主页面连接端口 |
| TCP 不可达 | 核对地址、网络隔离和路由；可改回 USB |
| offline / unauthorized | 提示检查设备端授权或重新配对，不视作在线 |
| 目标身份变化 | 停止部署，要求重新选择设备 |
| reverse / push / UHID / READY 失败 | 显示具体阶段和经过裁剪的错误输出，保留重试入口 |

日志记录时间、模式、generation、目标标识、连接阶段、命令类别、耗时和错误类型；不记录配对码、token、键盘输入内容或完整带敏感参数的命令。日志按状态变化去重，原 PONG 逐条日志改为可选诊断，常规模式只记录健康摘要。stderr/stdout 缓存各自设 64 KiB 上限，继续排空管道但丢弃超出内容。

## 9. 分阶段实施与验收

下列为可执行的工作拆分；每阶段先建立列出的失败用例，再实现并运行回归。阶段 0 的网络验证是后续真机验收的前置条件；本次编写文档没有执行配对或改变设备设置。

### 阶段 0：验证目标网络和 ADB 能力

- 使用当前实际 adb 路径执行 `version`、`help`、`devices -l` 和 `mdns services`，记录能力。不要仅根据项目旧记录假定版本。
- 在手机显示配对码后执行 `adb pair <配对地址>:<配对端口>`，交互输入；再 `adb connect <连接地址>:<连接端口>`。
- 指定无线目标验证 `get-state`、`shell id`、`reverse --list`；验证 reverse 到独立 loopback 探针的往返，避免占用正在运行的 KVM 端口。
- 实测有线 PC 与 Wi-Fi 平板互通。失败时区分发现失败和 TCP 不通；不要用关闭整个防火墙作为验证方法。
- 产出：平台版本、网络结果、无线目标格式、reverse 成功证据和延迟基线。失败原因如网络隔离必须如实保留，不能通过增加重试次数宣称解决。

### 阶段 1：统一 ADB 执行与设备选择

新增 `AdbClient.cs`、`AdbDevice.cs`、`ConnectionProfile.cs`，修改 `DeviceLauncher.cs`、`Config.cs`。扩展 `tests/adb-timeout`，新建 `tests/connection/Main.cs` 与 `run.ps1`。

验收用例：设备列表表头/空行、带 metadata 的 device、offline/unauthorized、多 USB、USB+无线并存、mDNS 形式 serial；所有设备命令都有准确选择器；带空格的本地路径；前导零配对码；stdout/stderr 洪泛；超时与取消；旧 ini 默认 USB、非法目标不自动替换。假 adb 必须记录参数和 stdin，断言实际调用而不只测试字符串拼接。

### 阶段 2：双端会话安全与半断线恢复

修改 `Transport.cs`、`Watchers.cs`、`Program.cs`、`Injector.java`，按需新增 `SessionHandshake.cs`、`SessionLease.java`；扩展 `tests/transport` 和 Java 离线测试。

验收用例：正确/错误 token、旧版本、前导分片/超时；断开通知一次；旧 generation 回调被忽略；TCP 无 FIN 且无 PONG 时 PC 退出 Ready 并触发重连；Android 半帧不阻塞租约；READY 超时；旧队列不重放；进程锁冲突；清理一步失败不跳过后续。时间逻辑使用可注入单调时钟，避免把长时间 sleep 当作主要测试手段。

### 阶段 3：发现、重连和连接 UI

新增 `WirelessDiscovery.cs`、`ConnectionCoordinator.cs`、`ConnectionForm.cs`；修改 `Program.cs`、`Watchers.cs`、`TrayUi.cs` 和配置读写。将首次启动和所有重连迁入同一个后台协调器。

验收用例：配对服务不当成连接服务；mDNS 为空仍可手动连接；动态端口变化；同 IP 换设备；配对成功但未在线；慢配对可取消且 UI 响应；连接窗口关闭不误断开；手动断开不自动重连；模式切换无旧回调污染；退出后没有新 adb 子进程；无线失败不调用全局 ADB 重启。

### 阶段 4：整体回归与真机验收

先执行仓库现有离线测试和新增连接测试：

```powershell
./tests/agent-logic/run.ps1
./tests/adb-timeout/run.ps1
./tests/transport/run.ps1
./tests/absolute-pointer/run.ps1
./tests/edge-tracker/run.ps1
./tests/message-host/run.ps1
./tests/scancode-map/run.ps1
./tests/connection/run.ps1
```

新增脚本由相应阶段提供；当前仓库还没有 `tests/connection/run.ps1`。随后按现有顺序构建 injector 和 agent，注意 `build/build-agent.ps1` 会结束正在运行的 PC-KVM，真机验收前先正常退出旧程序。

| 场景 | 通过标准 |
|---|---|
| 首次无线配对、后续启动 | 配对后可用；授权仍有效且网络可达时后续不要求重复配对 |
| PC 有线 / 设备 Wi-Fi | 明确记录实际拓扑；完成 reverse 和 KVM 往返 |
| USB 与无线同时在线、多设备 | 只向选定目标部署与发送输入，无歧义错误和误控 |
| 按住 Ctrl 或鼠标键时断 Wi-Fi | PC 在最后有效 PONG 后约 2–3 秒恢复；Android 按租约退出并回收虚拟设备 |
| 空闲时断 Wi-Fi、无 FIN 黑洞 | 同样使会话失效并进入恢复，不能只覆盖 Takeover |
| 端口变化、切换 Wi-Fi、设备重启 | 能重新发现则自动连接；不能发现则提示更新端点，不控其他设备 |
| 取消授权、错误配对码 | 明确要求重新配对，无无限配对循环 |
| 横竖屏切换、睡眠唤醒 | 沿用现有几何与 READY 安全规则；未就绪不接管 |
| 强制结束 PC 进程、注入器异常 | Android 自行释放；重启后无两个持有 UHID 的生产实例 |
| 退出与重连同时发生 | 输入恢复，后台无新启动，清理不影响其他 ADB 工具 |
| USB 回归 | 原双次贴边、快捷键、鼠标速度、旋转、断连恢复仍通过 |

性能指标作为验收目标而非已测承诺：受控局域网下采集至少 300 个 PING RTT 样本，记录 p50/p95/max，初始目标 p95 ≤ 30 ms；连续 30 分钟键鼠操作无非预期断线。PING RTT 不等于画面反馈延迟，指针体感和实际输入至显示延迟另行实测。未达到目标先分析网络和队列，不直接放宽输入释放超时。

## 10. 兼容扩展与交付清单

若需要 USB 引导模式，再新增 `WirelessLegacy` 配置值与独立引导：明确选择 USB 设备 → 读取身份 → 用户主动启用 `tcpip 5555` → 连接已确认的设备 IP → 校验身份 → 建立 KVM 会话。设备重启后可能需要重新引导，不自动执行全局 usb/tcpip 切换。界面说明该模式缺少现代无线调试的 TLS 保护，仅面向可信网络；它不作为无线配对失败时的静默降级。

首期交付包括：连接管理窗口、无线配对及手动连接、目标身份绑定、动态发现与可取消重连、双端会话失效和清理、自动化回归，以及 README 的无线使用/排障说明。发布时 exe 与 jar 必须一起更新，不能只替换 exe。

尚需实际验证的环境条件：当前 PC 与平板网络是否互通、目标 ROM 的无线 reverse 与 UHID 行为、mDNS 在当前网络是否可用、租约退出能否在该设备释放 UHID。它们有上述明确的验证步骤，不作为已经成立的设计前提。

## 11. 参考依据

- 项目现状：本基线的 `DeviceLauncher.cs`、`Transport.cs`、`Watchers.cs`、`Program.cs`、`Config.cs`、`TrayUi.cs`、`Injector.java`、`AbsoluteSession.java`；环境来源为已有 PC-Android KVM 总体设计记录。
- [Android Developers：ADB 无线调试与 USB 引导无线连接](https://developer.android.com/tools/adb)。用于核对配对流程与版本边界；首期不依赖 Android 17 的 Wi-Fi 2.0 能力。
- [AOSP：Architecture of ADB Wifi](https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/docs/dev/adb_wifi.md)。用于核对 TLS、动态端口、配对/连接服务区分和 mDNS 行为；ROM 与工具版本差异仍按能力探测及真机结果处理。
