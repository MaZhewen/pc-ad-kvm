# SDD ledger — plan: docs/superpowers/plans/2026-09-21-phase2-walking-skeleton.md

分支：phase2-skeleton（从 master @ 894acf9 分出）
Spec：docs/superpowers/specs/2026-09-21-pc-android-kvm-design.md
阶段一实测约束：FINDINGS.md

前置检查：GameViewer.exe 未运行 ✅（但 GameViewerService 仍 Running，已提示用户改 Manual）；手机已连；r8.jar / javac / csc 均就位；工作区干净。

## 飞行前冲突扫描

### 跨任务共享（文件 / 接口）

| 任务对 | 共享什么 | 一方产出 vs 另一方消费 | 发现 |
|---|---|---|---|
| 1 ↔ 3 | `build/build-agent.ps1` | T1 创建，T3 追加复制 jar 的一行 | 干净（顺序追加） |
| 1 ↔ 4,5,6,7,8,9 | **`src/agent/Program.cs`** | T1 创建，其后 **7 个任务**都改它 | ⚠️ 聚合点，见 Ruling 3 |
| 2 ↔ 3 | `src/injector/Injector.java` | T2 写 stdin 循环，T3 换成 socket 并把 stdin 搬到 `StdinMode.java` | ⚠️ T3 的 Files 块漏列，见 Ruling 1 |
| 2 ↔ 3 | `%TEMP%\pckvm.jar` | T2 的 build-injector.ps1 产出，T3 的 build-agent.ps1 复制 | 干净（构建顺序已在 T3 Step 6 写明） |
| 3 ↔ 5 | `src/agent/Protocol.cs` | T3 定义 9 种消息，T5 加 `MsgHome` | 干净（T5 同时改 `PayloadLength`） |
| 3 ↔ 8 | `Transport.MessageReceived` 事件 | T3 挂一个处理器（记 PONG 日志），T8 再挂一个（记时间戳） | 干净（C# 事件多播合法） |
| 4 ↔ 5 ↔ 6 | `MouseMoved` 处理器的同一段代码 | T4 写入 → T5 改写 → T6 整体替换 | ⚠️ 三次改写，见 Ruling 5 |
| 5 ↔ 6 | `cursorResetPending` 机制 | T5 引入并在 MouseMoved 里消费；T6 替换整个处理器 | ⚠️ T6 后成死代码，见 Ruling 4 |
| 6 ↔ 7 | `src/agent/EdgeTracker.cs` | T6 创建，T7 追加 `AbortTakeover()` | 干净（T7 明确写了要补） |
| 6 ↔ 7 | `MessageHost` 类 | T1 定义（`SetVisibleCore` 强制不可见），T7 整体替换为可见小窗 | 干净（T7 给了完整新类定义） |
| 2 ↔ 9 | `src/injector/ScancodeMap.java`、`KeyState.java` | T3 创建骨架，T9 补全 | 干净 |

### 任务自洽性

| 任务 | 检查项 | 发现 |
|---|---|---|
| 1 | 构建脚本路径 vs 源文件位置 | 干净 |
| 1 | `RAWMOUSE` 的 `_pad` → size=24 判定 | 干净（与阶段一探针一致） |
| 1 | C# 5 合规（用局部变量 + null 判断替代 `?.`） | 干净 |
| 2 | UHID 偏移表 vs `UhidDevice` 常量 | 干净（4/132/196/260/262/264/268/272/276/280 逐项吻合） |
| 2 | 描述符声明的报告长度 vs `sendMouse`/`sendKeyboard` 写入字节数 | 干净（5 / 9） |
| 2 | d8 是否需要 `--lib` | 干净（无 lambda/默认方法/try-with-resources） |
| 2 | Step 6 的一行 `echo ... >> /dev/null` | ⚠️ 噪音行，见 Ruling 6 |
| 3 | `PayloadLength` vs 各 `Encode*` 帧长 | 干净（5/5/3/5/5/1/8/5/5） |
| 3 | PC 侧消息常量 vs 手机侧 `MSG_*` | 干净（0x01–0x09 一一对应） |
| 4 | `EmitButton` 的 down/up 位对（downBit<<1） | 干净（0x0001→0x0002 等） |
| 5 | 归零所需的 40 次 −127 是否够覆盖 3200px | 干净（需 26 次，留了余量） |
| 6 | 比例入屏 vs spec「两套比例不能混用」 | 干净（入屏比例映射、接管期 1:1） |
| 7 | **`GetWindowThreadProcessId(_prevForeground, out uint _)`** | ❌ **C# 7 内联 out 声明，csc 4.0.30319 编不过**，见 Ruling 2 |
| 7 | 三条 ClipCursor 安全不变量 | 干净（`Release()` 首行、四条恢复路径、夺取失败不锁） |
| 8 | 逃逸键在 `ProcessCmdKey` + `ProcessDialogKey` 双路径 | 干净 |
| 9 | `modifiers` 局部变量被 lambda 捕获并修改 | 干净（C# 3 闭包语义，合法） |

## 裁决

**Ruling 1 — Task 3 的 Files 块不完整，实际还要新建 3 个文件。**
Task 3 的步骤正文要求创建 `src/injector/StdinMode.java`（承接 T2 的 stdin 循环）、`KeyState.java`、`ScancodeMap.java`，但 Files 块只列了「Modify: Injector.java」。属计划笔误。
处置：随 Task 3 dispatch 下发这 4 个文件路径的完整列表。
代价：若判错，实现者少建文件导致编译失败，一轮返工。

**Ruling 2 — Task 7 的 `Suppressor.cs` 有一处 C# 7 语法，会让编译直接失败。**
`GetWindowThreadProcessId(_prevForeground, out uint _)` 是 C# 7 的**内联 out 变量声明**，而本机唯一可用的编译器 `csc.exe 4.0.30319` 自报只支持到 C# 5。C# 5 要求 out 变量**先声明后使用**。
处置：随 Task 7 dispatch 下发修正后的写法：
```csharp
uint fgPid;
uint fgThread = GetWindowThreadProcessId(_prevForeground, out fgPid);
```
代价：若判错，Task 7 编译失败返工一轮。**这是本次扫描最有价值的一条**——它本来会在 Task 7 才暴露，而 Task 7 是安全最敏感的任务，返工代价最高。

**Ruling 3 — 接受 `Program.cs` 作为组合根，但设一个体量红线。**
T1 创建它，T3/4/5/6/7/8/9 都改它。这是刻意的：它是唯一知道全部部件的装配点。但若任一任务结束时它超过 **250 行**，该任务的实现者应报告 `DONE_WITH_CONCERNS` 而不是自行拆分（拆分是设计决定，须由控制方裁决）。
代价：若判错，Program.cs 长成上帝对象，终审会指出。**上限为 250 行是防患于未然，不是已经出问题。**

**Ruling 4 — Task 6 必须顺手删掉 `cursorResetPending`。**
T5 引入它（连上后第一次鼠标移动时归零），T6 用「跨越时归零」取代了这个触发点。**T6 的位置才是对的**：归零的价值在于「进入接管前对齐」，而不是「程序刚启动时对齐」。T6 替换整个 MouseMoved 处理器时应一并移除 `cursorResetPending` 字段与其分支，不留死代码。
代价：若判错，留一个永不触发的布尔字段，无害但脏。

**Ruling 5 — T4→T5→T6 对同一段 `MouseMoved` 处理器改写了三次，其中 T5 是「单行替换」写法。**
计划里 T5 Step 4 说「把 `EncodeMove` 那行改成」，但 T6 会整体替换。为避免实现者找不到精确文本，随 T5 dispatch 说明：**按 Step 4 给出的目标状态重写整个处理器，不拘泥于"只改一行"。**
代价：若判错，T5 实现者在匹配文本上多花几分钟。

**Ruling 6 — Task 2 Step 6 里有一行 `adb shell "echo 'M 20 20 0 0' >> /dev/null"  # 占位` 是噪音。**
它是计划书写时的残留，无功能意义。随 T2 dispatch 说明：忽略该行，直接用手工输入验证。
代价：无。

## 执行记录

Task 1: dispatched implementer (haiku) at BASE 894acf9。
Task 1: implementer DONE — commit 37fac82（build-agent.ps1 + RawInput.cs + Program.cs + .gitignore 追加）。编译 0 警告，launch 成功并写出日志启动行。
Task 1: review package → review-894acf9..37fac82.diff；task reviewer (haiku) 判 **Spec compliant / Task quality Approved**；0 Critical、0 Important、5 Minor。七项点名风险全部在 diff 上核实通过（含 `_pad` 布局 24 字节、`RAWINPUTHEADER` x64 对齐、C# 5 全量扫描零命中、`.gitignore` 仅追加）。
Task 1: minor (deferred): `Program.cs` 的 `kc` 计数器递增但从未读取——死变量，继承自 brief
Task 1: minor (deferred): `Program.cs` 退出时未 `Dispose()` NotifyIcon，`SystemIcons.Application` 未释放——进程即退，无害
Task 1: minor (deferred): `RawInput.cs` 首次 `GetRawInputData` 的返回值未检查（`size==0` 守卫恰好覆盖失败情形）——继承自 brief
Task 1: minor (deferred): `RawInput.cs` 的 `catch {}` 吞掉一切异常（含 OOM），符合 brief 意图但缺少注释说明预期的异常类型
Task 1: minor (deferred): `.gitignore` 的 `*.log` 使既有的 `probes/**/*.log` 冗余——纯装饰，且追加约束下不便删
Task 1: ⚠️ 已由控制方核实并关闭：报告中的编译/启动等经验性声明 → 已查 `dist/pc-kvm.exe` 存在（9216 B）、`dist/pc-kvm.log` 含 `# 启动 2026-09-21 17:35:01`；另核实三个文件**均带 UTF-8 BOM**、`.gitignore` 确为仅追加 ✅
Task 1: **未标记 complete** — 代码审查全清，但计划 Step 5 的六项人工观察（托盘提示、非焦点采集、托盘退出）需用户实测后方能产生。
Task 1: 人工验证完成，控制方读日志判定：**全部通过**。日志 166 行：MOUSE 55 条（增量真实有变化）、KEY 108 条（scancode 真实：0x20=D/0x1E=A/0x1F=S/0x39=Space）、末尾有 `# 退出`（托盘退出路径验证，实现者未覆盖、由用户补上）。
Task 1: **「非焦点采集」由构造证明而非仅靠观察** — Task 1 的 `MessageHost` 覆写 `SetVisibleCore` 使其永不显示，不可能持有前台，故日志中每一条事件都是非焦点状态下捕获的。这等于在产品代码里复现了阶段一验证 2 的结论。
Task 1: **complete** (commits 894acf9..37fac82, review clean)
Task 2: dispatched implementer (opus) at BASE 37fac82。Ruling 6 已随 dispatch 下发。本任务的 HID 描述符比阶段一探针更进一步：**组合描述符**，两个 Report ID（1=鼠标 5 字节、2=键盘 9 字节）。
Task 2: implementer DONE — commit 7be0e02（HidDescriptor.java + UhidDevice.java + Injector.java + build-injector.ps1）。javac/d8/push 一次通过，jar 3107 字节。
Task 2: **实现者自查抓到并修复了 Global Constraint 里的坑**：Write 工具产出的是无 BOM 的 UTF-8（首三字节 `24 45 72`），实现者检测后重写为 `EF BB BF`。控制方已复核确认带 BOM ✅ —— 这条约束不是纸上的，它真的会踩。
Task 2: 机械证据（强）：`dumpsys input` 显示 `PC-KVM Virtual Input Mouse`(event15, CURSOR|EXTERNAL) + `Keyboard`(event16)，bus/vendor/product/location 全部正确回读；**motion range min=0 max=3199/2135 恰为手机屏尺寸**，证明描述符被正确解析。`getevent` 抓到 `REL_X/REL_Y=±30`、`BTN_LEFT` down/up、`REL_WHEEL=1`，与指令值逐项吻合。
Task 2: review clean — task reviewer (haiku) 判 **Spec compliant / Task Approved**；0 Critical、0 Important、4 Minor。六项点名风险全部核实（含描述符声明长度 vs 写入字节数 5/9 逐字节推导）。
Task 2: minor (deferred): `Injector.java` 的 `K` 路径未做 clamp，`K 300 ...` 会静默回绕——手工调试工具可接受，socket 化后继任者应校验
Task 2: minor (deferred): `UhidDevice` 每个事件都新分配 4376 字节数组——实时流式化后值得改为复用缓冲
Task 2: minor (deferred): `UhidDevice` 建设备后未读 `UHID_START` 即发 input2；内核在设备未 start 时会拒 `EINVAL`。本机实测未出现，但**若后续出现零星 EINVAL，这就是要补的检查**（与阶段一该发现的记录一致）
Task 2: minor (deferred): `build-injector.ps1` 硬编码 PyCharm JBR 路径，换机器即失效——骨架阶段可接受
Task 2: ⚠️ 均已由控制方核实并关闭：
  - ⚠️ 设备侧清理 → 已查：`/data/local/tmp` 无 `pckvm.jar`；无残留 app_process/Injector 进程；`dumpsys input` 无 PC-KVM 设备 ✅
  - ⚠️ `build-injector.ps1` 的 BOM → 已查首三字节 `EF BB BF` ✅
  - ⚠️ 键盘端到端路径未实测 → **接受为已知缺口**，Task 9 会正式覆盖，不单独补测
  - ⚠️ dumpsys/getevent 的经验性证据无法事后复核（设备已销毁）→ 但审查者已独立验证代码与所报数值自洽，且控制方看到 motion range 与屏尺寸吻合，判定可信
Task 2: **未标记 complete** — 代码审查全清，但人眼确认（光标是否真的出现并按指令移动）需用户实测。
Task 2: 人眼确认完成 —— **通过**。用户实测：`INJECTOR ready` 后光标**未立即出现**，发第一条 `M 30 0 0 0` 后出现，此后全部按指令正确移动。
Task 2: **Ruling 7 — "光标在第一条 M 之后才出现"判定为正确行为，非缺陷。**
  理由：Android 的指针只在收到实际指针输入时渲染；创建设备本身不触发绘制。阶段一探针之所以一跑即出光标，是因为它启动后立刻持续发 `+3/+3` 位移，掩盖了这一点。真实 USB 鼠标同理（插入后动一下才出现）。
  对后续任务的影响：**无**。进入 TAKEOVER 时 `EncodeHome()` 会先发一串位移执行推角落归零，光标自然在那一刻出现，用户不会看到"接管了但没光标"的空窗。
  代价：若判错（即 Android 本应在设备创建时即绘制光标），则接管瞬间会有一小段无光标期——但推角落归零的位移会立刻补上，实际不可感知。
Task 2: **complete** (commits 37fac82..7be0e02, review clean)
Task 3: dispatched implementer (opus) at BASE 7be0e02。Ruling 1 已随 dispatch 下发（补全 3 个被 Files 块漏列的文件）。本任务是本计划最大的一个（9 个文件），dispatch 里点名了协议帧长与跨语言常量一致性两项最易静默出错的地方。
Task 3: implementer DONE — commit 9488386（9 文件，+643/-35）。两端均构建成功。**隧道双向验证**：`dist\pc-kvm.log` 含 `# 设备已连接` 与 `# PONG seq=1`。
Task 3: review clean — task reviewer (opus) 判 **Spec compliant / Task Approved**；0 Critical、0 Important、4 Minor。八项点名风险全部核实；审查者额外做了两件超出要求的事：①逐对比对每个 `Encode*` 的 `new byte[N]` 与 `PayloadLength` 返回值 ②不只比消息类型常量，还比了**字段偏移**（Key 的 down@payload[2]、mods@payload[3]；Scroll 的 dy@payload 偏移 2）两端一致 ③**直接读字节**验证两个 .ps1 的 BOM，而非只看 diff。
Task 3: minor (deferred): `DeviceLauncher.RunAdb` 顺序 `ReadToEnd()` stdout 再 stderr，理论上在 adb 输出很大时可死锁——当前命令输出都很短，无害
Task 3: minor (deferred): `Program.cs` 的事件订阅发生在 `Prepare`+`Start` 之后，理论上存在一个窗口让上一轮残留的手机进程连上却无 Ping 处理器——重试循环给了 10 秒，实际不可达
Task 3: minor (deferred): `Injector.java` 的 `BufferedReader`/`InputStreamReader` import 在 stdin 循环搬去 `StdinMode` 后成为死引用——继承自 brief 原文，javac 不报
Task 3: minor (deferred): `Transport` 的 `_stream`/`_client` 未标 volatile（只有 `_running` 标了）——所有使用点都有 try/catch 兜底，最坏后果是被吞掉的异常
Task 3: **Ruling 8 — Task 3 的人眼验证折入 Task 4，不单独执行。**
  理由：Task 2/3 的人眼步骤高度重叠（同一套构建 + 启动流程），而 Task 3 最硬的那条证据（`# PONG seq=1`）已由机器验证。让用户为同一件事跑两遍启动是浪费。
  处置：Task 3 剩余的两个人眼项（**手机上无任何图标**=零安装验证、托盘退出写入 `# 退出`）并入 Task 4 的验收清单，显式列明，不丢项。
  代价：若判错，Task 4 的验收清单多两项，或用户需回头补跑一次——成本均为分钟级。
Task 3: **complete** (commits 7be0e02..9488386, review clean；人眼项按 Ruling 8 折入 Task 4)
Task 3: **人眼验证已由用户实际完成（早于 Ruling 8 的折入，故 Ruling 8 的折入不再需要）**。控制方读日志与设备状态核实：两次运行均出现 `# 设备已连接` + `# PONG seq=1`；末尾 `# 退出` + `# 设备已断开`（托盘退出路径与断开回调均工作）；`/data/local/tmp` 无 `pckvm.jar`；`adb reverse --list` 为空；无残留 app_process。**零安装特性经实测确认**（手机侧零残留）。用户另行确认了手机上无任何图标/权限弹窗。
Task 3: Ruling 8 的处置因此变为：Task 4 的验收清单**不再需要**补列那两项——已在本任务完成。
Task 4: dispatched implementer (haiku) at BASE 9488386。**本任务的验收清单需显式包含 Ruling 8 折入的两项**：手机上无任何图标（零安装）、托盘退出写入 `# 退出`。
Task 4: implementer DONE — commit a57081b（仅 `src/agent/Program.cs`，+29/-7）。两端构建成功。
Task 4: **实现者发现了一个飞行前扫描漏掉的计划缺陷**：brief 要求把 `transport.Send(...)` 加进 `MouseMoved` 处理器，但 `transport` 的声明在 `Main` 中该处理器**之后**——C# 局部变量不支持前向引用。实现者把 transport 装配块整体上移（代码一字未改，仅位移，并加注释说明）。**扫描教训：我查了共享文件、接口、常量一致性，但没查声明的先后顺序**。此类问题只有真去编译才暴露。
Task 4: review clean — task reviewer (haiku) 判 **Spec compliant / Task Approved**；0 Critical、0 Important、3 Minor。六项点名风险全部核实，含 Ruling 8 无关的「声明位移在语义上等价」一项。
Task 4: minor (deferred): `Transport` 的 `Send` 在断线时的行为不在本 diff 内可见——已由控制方核实（见下），实际安全
Task 4: minor (deferred): 高精度/平滑滚轮的非 120 倍数增量会被 `/120` 整除静默丢弃——**对本用户不成立**（阶段一日志显示其鼠标滚轮严格 ±120 倍数、0 条例外），留给未来的平滑滚轮设备
Task 4: minor (deferred): X 键（`0x0040+`）与 down+up 合并标志位未处理——超出 brief 范围
Task 4: ⚠️ 三项均已由控制方读源码核实并关闭：
  - (a) 断线时 `Send` 是否抛异常 → `Transport.cs:108-113`：先 `if (s == null) return;`，`Write` 外有 try/catch。**不会抛**，审查者担心的「异常逃出 Raw Input 处理器导致 agent 不稳定」不成立 ✅
  - (b) ±127 拆分循环是否真存在 → `Injector.java:79-83` 确有此 `while` 循环 ✅
  - (c) `WheelDelta` 取值 → `RawInput.cs:139` 从 `usButtonData` 取且仅在 WHEEL 标志置位时 ✅
Task 4: **未标记 complete** — 代码审查全清，但里程碑验收（光标是否真的跟随、快甩是否丢位移）需用户实测。
Task 4: 里程碑验收完成 —— **通过**。用户实测：光标同步移动、**快速甩动不丢位移**（int8 拆分逻辑实证有效）、滚轮生效、左键生效、右键生效、PC 自身照常不受影响。
Task 4: **Ruling 9 — 「中键无可见表现」判定为正确行为，非缺陷。**
  理由：中键（HID button 3）在 Android 映射为 `MotionEvent.BUTTON_TERTIARY`，系统正确接收但绝大多数安卓应用不处理它（桌面 Chrome 的"中键关标签页"是桌面约定，安卓无此惯例）。且中键与左右键走**完全同一条代码路径**（同一 `EmitButton` + 同一 `downBit << 1` 位映射），左右键已验证，接线正确性随之确认。
  代价：若判错（即中键接线实际有误），影响面为零——没有任何下游任务依赖中键。
Task 4: **complete** (commits 9488386..a57081b, review clean)
Task 5: dispatched implementer (opus) at BASE a57081b。Ruling 5 已随 dispatch 下发（MouseMoved 处理器按目标状态重写，而非字面单行替换）。协议新增第 10 种消息 `MsgHome`(0x0A)，两侧 `PayloadLength` 必须同步。
Task 5: implementer DONE — commit 37e4542（4 文件，+96/-2：CursorModel.cs 新建、Protocol.cs 加 MsgHome、Injector.java 加 HOME 处理、Program.cs 接线）。两端构建零警告。
Task 5: 实现者自报一处**行为变化**（已点名交审查者判）：`cursor == null` 时不再发送 move 包（Task 4 时是无条件发）。属合理守卫，但需确认不存在"设备已连、模型未建"的窗口会静默丢输入。
Task 5: review package → review-a57081b..37e4542.diff；已派 task reviewer (haiku)。
Task 5: review clean — task reviewer (haiku) 判 **Spec compliant / Task Approved**；0 Critical、0 Important、2 Minor。六项点名风险全部核实（含"两侧 payloadLength 均加 MsgHome→0x0A 且一致"、"NextDx/NextDy 返回并应用钳制后实际值"、"40×127=5080 > 3200 覆盖充足"、"处理器重写保留了节流日志/滚轮/按键三处旧行为"）。
Task 5: minor (deferred): **`cursor` / `cursorResetPending` 跨线程发布未同步** —— `Program.cs`：`Connected` 处理器（transport 线程）写，RawInput 窗口线程读。引用与 bool 写是原子的，x86-64 CLR 实际不会撕裂，但严格内存模型下允许读方看到 `cursor != null` 而 `cursorResetPending` 仍为 false，**跳过那一次 HOME**。后果恰是本任务要防的静默漂移。修法：`cursorResetPending` 加 `volatile`（或改为先置 flag 后赋 cursor）。**留终审分级**——理论性竞态，一行可修。
Task 5: minor (deferred): `new CursorModel(2136, 3200)` 硬编码 —— brief 明确许可（后续从 CONFIG 协商）；同时设备侧 `MsgConfig` 亦未接线，与骨架范围一致
Task 5: ⚠️ 两项均已由控制方读源码核实并关闭：
  - (a) 重连时 `Transport.Connected` 是否再次触发 → **是**。`Transport.cs:46-61` 的 `AcceptLoop` 是 `while (_running)` 循环，每接受一个新连接即重走一遍并触发 `Connected`。故"重连后重新归零"路径成立 ✅
  - (b) 断线时 `Send` 是否为安全 no-op → 复认 `Transport.cs:108-113`：先 `if (s == null) return;`，`Write` 外有 try/catch ✅（与 Task 4 的核实一致）
Task 5: **未标记 complete** — 代码审查全清，但归零行为需用户实测（反复重启看是否漂移）。
Task 5: **环境故障（第二次 adb 掉线）**：用户执行 `build-injector.ps1` 时在 `adb push` 步失败（`no devices/emulators found`），javac 与 d8 均已通过。控制方执行 `adb kill-server` + `start-server` 后恢复，设备重新可见。根因确认为**本机 3 个 adb server 争抢同一 USB 设备**（InputShare 自带一个 + platform-tools 两个）。已建议用户结束 InputShare 的 adb 进程以根治。
Task 5: minor (deferred) — **Ruling 10：不在计划中途为 adb 掉线加防御性检查。**
  现象：`build-injector.ps1` 在浪费了 javac+d8（约 30 秒）之后才因 `adb push` 失败退出。
  可做的改进：脚本开头加一次 `adb devices` 预检，早失败、错误信息更明确。
  裁决：**不做**。理由：①错误信息本身已明确（`no devices/emulators found`）②节省的是 30 秒，而改动会引入一个未在计划中评审过的脚本分支 ③根治手段在环境侧（结束多余 adb server），加脚本防御只是掩盖。交由终审分级，若终审认为值得则一并处理。
  代价：若判错，每次掉线多浪费约 30 秒构建时间。
Task 2: **环境隐患记录（影响后续所有任务）**：
  - **本机同时运行 3 个 adb server**（两个 `D:\software\platform-tools\adb.exe` + 一个 `D:\software\InputShare\_internal\adb-bin\adb.exe`）。会话中曾出现"Windows 侧可见 Android ADB Interface 但 `adb devices` 为空"的掉线，`adb kill-server` + `start-server` 恢复。**多 adb server 抢同一 USB 设备是这种症状的常见成因**，后续再掉线优先用此法恢复。
  - 手机上有 **`com.inputleaf.android:input_injector` 进程在跑**（用户此前装过 input-leaf）。与 GameViewer 同类隐患：第三方注入器会污染输入相关测试。Task 4/6 若结果反常，首查此项。

## 2026-09-21 晚 · Task 5 人眼验证后的缺陷排查（第二会话）

**会话前提**：手机电量耗尽可能离线，用户离开充电。**本次只做可离线完成的工作；一切需要真机的验证挂账到手机回线后补。**

### Task 5 验收结果（用户实测）

- ✅ **主判据通过**：用户原话「上一步验证的最后，每次都会从左上角开始」——归零生效、反复重启不累积漂移。
- ❌ **新缺陷**：用户原话「有时候鼠标移动的时候，向左平移的时候，移动到某个位置就不再移动了。向上的时候也是，像是有虚拟的边界，但是这个边界还并不固定」。

### Ruling 12 — 我的 bash 探针被 MSYS 路径转换篡改，产品 `DeviceLauncher` 无辜。

排查过程中我用 Git Bash 跑 `adb shell "CLASSPATH=... app_process / Injector"` 一律 `Aborted`，一度以为 `DeviceLauncher` 的启动方式在设备上失效。**实测反证**：`adb shell echo "-Djava.class.path=/x / Injector"` 回显为 `-Djava.class.path=X:/ C:/Users/.../Git/ Injector` —— **MSYS 在改写 `/` 开头的参数**（§六 已记录过同类坑：`csc /nologo` 被吃成 `C:/.../Git/nologo`）。而产品走 .NET `ProcessStartInfo`，**不经过 MSYS**。直接跑 `dist\pc-kvm.exe` 验证：日志出现 `# 设备已连接` + `# PONG seq=1` ✅。
**处置：不修改 `DeviceLauncher` 的启动形式。** 代价：若判错（真有设备侧行为漂移），会在手机回线后的复验中再次暴露。
**教训（写给未来的自己）：本项目在 Git Bash 下跑 adb 时，含 `/路径` 的参数一律不可信；要么用 `MSYS_NO_PATHCONV=1`，要么别从 bash 发这类命令。**

### Ruling 13 — 「虚拟边界」的根因是**显示几何硬编码**，不是指针加速。

证据链（全部来自 `dumpsys input` 实机 dump，设备活着时抓取）：

| 观测 | 值 |
|---|---|
| 我们设备在 InputReader 里的登记名 | `Device 49: PC-KVM Virtual Input Keyboard`，`EventHub Devices: [76 75]`，`Sources: KEYBOARD \| MOUSE`（复合设备被并成一个 InputDevice） |
| **Motion Ranges（实测）** | `X: min=0 max=3199` / `Y: min=0 max=2135` → **逻辑尺寸 3200×2136（横屏）** |
| `Cursor Input Mapper` 增益 | `XScale: 1.000` / `YScale: 1.000` |
| 指针加速参数 | **不存在** `PointerVelocityControlParameters`；而同段里 `WheelYVelocityControlParameters: scale=1.000, ..., acceleration=4.000` **打印了**——"有就一定会打印"，故本设备**无指针加速**，增量与像素 1:1 |
| 对照 `Device 11: Xiaomi Mouse`（真鼠标） | 同样 `XScale/YScale=1.0`、同样无指针加速参数 |
| 对照 `Device 12: Xiaomi Touch`（触控板） | 才有 `Pointer Acceleration (boolean): [true]` / `Pointer Sensitivity (integer): [3]` |
| 物理 vs 逻辑 | `wm size` = `Physical size: 2136x3200`；`mRotation=ROTATION_90` → 逻辑 3200×2136 |

失效机制：`CursorModel` 硬编码 `(2136, 3200)`（竖屏）。于是 **模型 X 上限 2135 < 真实 3199**，向右推时模型先撞上限 → `NextDx` 返回 0 → **停止发包 → 真光标就地冻结在屏幕 2/3 处的"虚拟边界"**；**模型 Y 上限 3199 > 真实 2135**，向下推时真光标顶住下边缘而模型继续空走 → 记账错位，之后左/上的停点随错位量漂移 → 「边界不固定」。
**代价：若判错（真有加速），Task 5B 修完症状会残留，需要在真机上做一次定量位移测量。**

### Ruling 14 — `screencap` 抓不到 UHID 光标，像素法不能用于验证光标位置。

三张阶梯截图（每次发 +400 纵向）实测：**连续两两之间的变化像素为 0**；`dumpsys SurfaceFlinger --list | grep -i cursor` 返回空 → 光标是**硬件光标 plane**，不进合成结果。视觉子代理独立看同一批图也报「没有看到鼠标光标」。
**处置：今后一切"光标位置"的验证靠日志 + 用户肉眼，不要再用截图做像素分析。** 代价：产品级自动化验证做不到，验收仍需人眼（与本计划一贯做法一致）。

### Ruling 15 — 撤销计划里「手机分辨率硬编码」这条刻意 YAGNI（**用户裁决**）。

原计划 §自审记录把「手机分辨率硬编码 2136/3200」记为刻意 YAGNI，交接 §四 还专门写了「不要以为是遗漏」。实测证明几何量**不是常量**（随旋转变），Ruling 13 的失效机制直接由它产生。已就此向用户提问并获裁决：**几何改为运行时读取 + 轮询检测中途旋转**。代价：引入一个计划外的新机制（每 2 秒一次 adb 调用），需评审；轮询频率/开销已记为 deferred minor。

### Ruling 16 — 插入 Task 5B 于 Task 5 与 Task 6 之间；`cursorResetPending` 提前删除。

- 新增 **Task 5B：手机显示几何 —— 运行时读取 + 轮询检测旋转**（计划文件已写入完整代码）。
- `cursorResetPending` 原本按 Ruling 4 应在 Task 6 删除；Task 5B 用 `volatile bool _geometryChanged` 完全覆盖了它的语义（"连接后首次移动时归零对齐"），且顺带修掉 Task 5 审查挂着的 **跨线程未同步 bool** minor。故**提前到 Task 5B 删除**，Ruling 4 的时机条款由本裁决取代。
- 代价：若判错，Task 6 的处理器里少一个早已冗余的分支，无害。

### Ruling 17 — 飞行前扫描查到 Task 6 的 3 处跨任务冲突，已在计划文件里就地修正。

| # | 冲突 | 处置 |
|---|---|---|
| a | Task 6 的 `EdgeTracker` 同样硬编码 `phoneW: 2136, phoneH: 3200` | 加 `SetPhoneSize(int,int)`，构造值标注为占位初值，真值由 Task 5B 的几何变化消费点灌入 |
| b | Task 6 的 `MouseMoved` 替换文本**不含** Task 5B 新加的几何变化消费 → 会把旋转恢复**静默删掉** | 计划里直接给出整合后的完整处理器文本，要求照抄 |
| c | Task 6 的处理器**去掉了** `cursor != null` 守卫 → 手机未连接时推到边缘会触发 `EnterTakeover` → `cursor.Reset()` 空引用；异常被 `RawInput` 的 `catch{}` 吞掉，表现为**状态机卡死在 TAKEOVER 且零报错** | 计划里给出带守卫的完整文本，并写明这条失效症状 |

**c 是本次扫描最有价值的一条**：它是实现者极难自查出来的静默失效（无异常、无日志），且 Task 7 会让它后果升级（卡在 TAKEOVER + 抑制已夺取 → 用户鼠标被锁）。代价：若判错，多一层无害的 null 判断。

### 污染源处置（用户裁决）

- **手机侧**：已 `am force-stop com.inputleaf.android` + `pkill -f input_injector`，实测 `ps -A | grep inputleaf` 计数归 0 ✅。
- **PC 侧**：**用户决定保留 `GameViewer.exe`**（不杀）。故 §Global Constraints 里「开工前必查 GameViewer 未运行」对后续输入类任务的验证**不再成立**，Task 9（键盘）验收时若出现可疑的 `KEY` 日志需记得排除它（它注入的是 UsagePage 0x0C 音量键，普通键盘探针看不到，风险有限）。

### 挂账：等手机回线后必须补的真机验证

1. **Task 5B 端到端**：`# 屏幕几何 3200x2136`（横屏）、旋转时 `# 几何已应用` 触发；以及 Task 5 的复验（推到四边到底，确认不再有虚拟边界）。
2. **Task 5 的原始验收补证**：四边推到端点是否都在物理边缘。
3. Task 6–9 各自的验收（见计划各任务）。

### 执行记录 · Task 5B

Task 5B: 计划已修订并提交（`a7b6d69`）。修订同时扫掉 Task 6 的 3 处跨任务冲突（Ruling 17）。
Task 5B: dispatched implementer (sonnet) at BASE **a7b6d69**。brief 已生成：`.superpowers/sdd/2026-09-21-phase2-walking-skeleton/task-5B-brief.md`（268 行，抽取边界已核：起于 Task 5B 标题、不含 Task 6 内容）。dispatch 里已下发：手机离线（adb 无设备）→ 只做「编译 + 纯函数解析测试」两项离线验证，设备相关步骤由控制方挂账；并澄清了 brief 里唯一一处歧义（保留 T5 的 `if (cursor != null)` 外壳，只替换内层 reset 分支）。

### Ruling 18 — 第二轮跨任务扫描（Task 5B 引入的连接守卫改变了 Task 7/8 的行为边界）查出 2 处，均为**安全相关**，已在计划里就地修正。

| # | 冲突 | 失效路径 | 处置 |
|---|---|---|---|
| a | Task 7 的 `EnterTakeover` 缺连接检查 | 手机掉线后 `tracker` 已回 IDLE；用户**再**把鼠标推到边缘 → 重新触发 `EnterTakeover` → `Engage()` 夺取前台 + `ClipCursor` 把光标钳在一个像素上。而 Task 8 的心跳安全网开头是 `if (!transport.IsConnected) return;`，**掉线时整体短路**，救不了这个状态 → 用户只剩 `Ctrl+Alt+Esc` | 计划里新增 **Task 7 安全不变量 4「未连接时绝不夺取前台」**，并在 `EnterTakeover` 处理器开头给出连接检查代码 |
| b | Task 8 心跳安全网被 `if (!transport.IsConnected) return;` 短路 | 同 a 的后果：这条早返回本意是"没连接就没必要心跳"，但它同时把"没连接时的**解除**职责"也一并短路了 | 计划里把安全网提到早返回**之前**：处于 TAKEOVER 时，「没连接」与「PONG 超时 >2s」都立刻 `AbortTakeover + Release`。这同时覆盖了 `TcpClient.Connected` 滞后（拔线后它会保持 true 直到下一次 I/O 失败）的情形 |

**为什么这两条值得动用"计划修订"而不是留给实现者**：它们都属于**无异常、无日志的静默陷阱**，且后果是"用户鼠标被锁死"——本计划明写 Task 7 是唯一有系统级副作用的任务。代价：若判错，只是多两次无害的连接判断。

## 2026-09-21 收尾状态（手机离线期间）

- Task 5B 实现者运行中；其后接 task review → 修轮 → 完成。
- **Task 5B 完成后若要继续 Task 6–9**：可在手机离线期间实现 + 编译 + 代码评审，但**真机验收会全部积压**。当前挂账清单见上文「等手机回线后必须补的真机验证」。

### 执行记录 · Task 5B（续）

Task 5B: implementer DONE — commit **0193f06**（3 文件，+137/−7：`DeviceLauncher.cs` 加 `RunAdbCapture`/`QueryDisplay`/`ParseDisplaySize`/`ParseWxH`/`ParseRotation` 且 **`RunAdb` 未被动**、`CursorModel.cs` 去 `readonly` 加 `SetBounds`、`Program.cs` 加 3 个静态 volatile 字段 + 2 秒后台轮询线程 + 消费 `_geometryChanged` 并删除 `cursorResetPending`）。`git show --stat` 已核实**只含允许的 3 个 src 文件**。
Task 5B: 机械证据（离线）：`build/build-agent.ps1` 编译成功、**零警告**、exit 0；`%TEMP%\geotest\` 纯解析测试 **10/10 通过**（含实测抓到的横屏向量 → `true 3200x2136`）。`Program.cs` **186/250 行**，未越 Ruling 3 红线。
Task 5B: 实现者观察记录：它发现 `docs/superpowers/plans/...md` 在工作区里被"外部进程"改过（初始 `git status` 是干净的）。**那是控制方自己在做的事**（Ruling 16/17/18 的计划修订），已由控制方另行提交，非实现者改动 ✅。
Task 5B: review package → `review-a7b6d69..0193f06.diff`（1 commit, 11808 B）；已派 task reviewer (sonnet)。派发时点名了两处最易静默出错的点：①`Override size` 与 `Physical size` **顺序颠倒时仍要以 Override 优先**，以及四种必须返回 `false` 的情形；②`MouseMoved` 在 `cursor == null` 时**仍须发滚轮与按键**、且不得夹带 brief 未要求的行为。另点名两个本设计下的承重点：2 秒轮询线程与 UI 线程共享状态的竞态、以及是否有任何东西会阻塞 UI 线程（Raw Input 消息泵）或泄漏 adb 进程。
Task 5B: review clean — task reviewer (sonnet) 判 **Spec compliant / Task quality Approved**；**0 Critical、0 Important、4 Minor**。裁判要点全部核实（`RunAdb` 的 hunk 头 `@@ -58,12 +58,98 @@` 证明该文件零删除、Override 优先靠"循环后统一取优先级"实现因而天然与行序无关、四种 false 向量、滚轮/按键路径在 `if (cursor != null)` 之外未变、C# 5 全量扫描零命中、Program.cs 实测 186 行）。审查者另做了 3 项点名风险的定点核查：`Connected` 由 `Transport._acceptThread` 触发而非 UI 线程（故新增的 `QueryDisplay` 不阻塞消息泵）、轮询线程读 `transport.IsConnected` 的陈旧读最坏只是浪费一次 adb 调用、按钮/滚轮路径完整。
Task 5B: ⚠️ **控制方已独立解决**那条 "Cannot verify from diff"（编译与 10 条向量）：①自己跑 `build/build-agent.ps1` → `编译成功`、6 个源文件、exit 0、零警告、exe 18944 B；②**从已提交源码重新编译** harness（`Main.cs` + `src/agent/DeviceLauncher.cs` → geotest2.exe）并运行 → **10/10 全过、exit 0**。不是复读实现者的报告，是独立复算 ✅
Task 5B: minor (deferred): `RunAdbCapture` 沿用 `RunAdb` 的"先 ReadToEnd stdout 再 ReadToEnd stderr"顺序——经典管道死锁形态，且 `ReadToEnd` 无超时；本命令输出仅几字节故只是理论问题。**属 brief 逐字规定的代码**（plan-mandated）
Task 5B: minor (deferred): `RunAdbCapture` 的 `Process` 从不 `Dispose`，`WaitForExit(5000)` 超时时也不 `Kill` → 遗弃的进程与句柄靠终结器回收。每 2 秒一次轮询下是持续但受 GC 约束的churn。**与既有 `RunAdb` 同构**，而 brief 明确禁止改动 `RunAdb`
Task 5B: minor (deferred): `Program.cs:77-78` 有一处**窄撕裂窗口**——UI 线程先清 `_geometryChanged` 标志、再读 `_phoneW/_phoneH`；若轮询线程恰在此间隙写入新几何，本次鼠标移动会应用到一对"新旧混合"的尺寸。自愈（轮询会把标志重新置起，下一次移动即应用正确值），代价是多一次 HOME+Reset（光标跳到角落，与真实几何变化的既有行为一致）。终审可判是否需要加小锁
Task 5B: minor (deferred): `QueryDisplay` 放在 `Connected` 里 → 在 `_acceptThread` 上最多阻塞一次 adb 时长，从而**推迟 `ReadLoop` 启动**（即设备→PC 的 Pong 通道）。本阶段该通道只有 Pong，无后果；Task 6+ 若要依赖 Pong 时延（Task 8 心跳）值得回头处理
  - ⚠️ **给终审的提醒**：这一条与 Ruling 18(b) 相关——Task 8 的心跳安全网判据是"PONG 超时 >2s"，而连接建立后的一小段窗口内可能还没有任何 PONG。Task 8 实现者需确认：进入 TAKEOVER 的前提是已连接，而连接后 `Connected` 处理器立刻发了一次 PING，故进入接管时 `lastPongTicks` 已至少被更新过一次。
Task 5B: **complete** (commits a7b6d69..0193f06, review clean, ⚠️ 由控制方独立复算关闭)

### Ruling 19 — 第三轮跨任务扫描（Task 7/8/9）查出 3 处，全部就地修正。

扫描方法（两把不同的筛子，缺一不可）：
- **筛子一：C# 7 语法静态扫描**（正则找内联 out 声明、`?.`、`$""`、`nameof`、`using static`）。命中 1 处真问题。
- **筛子二：逐个 handler 手工追声明顺序**——把 Task 6/7/8/9 里每个 `+=` 处理器引用的**每一个标识符**列出来，逐个回查它在 `Main` 里的声明位置是否更早。命中 2 处。**筛子一完全扫不到筛子二的问题**，反之亦然。

| # | 问题 | 后果 | 处置 |
|---|---|---|---|
| a | Task 7 的 `Suppressor.Engage()` 里 `GetWindowThreadProcessId(_prevForeground, out uint _)` —— **C# 7 内联 out 声明** | 本机 csc 4.0.30319 直接编不过，Task 7 整整返工一轮；而 Task 7 是安全最敏感的任务 | Ruling 2 原本的处置是"随 Task 7 dispatch 下发修正写法"，但那意味着**简报里带着一段已知编译不过的代码**。改为**直接在计划正文里改对**（`uint fgPid; uint fgThread = GetWindowThreadProcessId(_prevForeground, out fgPid);` 并加注释）。Ruling 2 的"靠 dispatch 兜"条款由本裁决取代 |
| b | Task 8 在 `transport.Disconnected` 处理器里调 `supp.Release()`，而 Task 7 把 `Suppressor supp` 的声明放在"transport 装配之后" | **前向引用 → 编译不过**（Task 4 正是同一类坑） | 计划里加"声明位置"说明：`supp` 必须声明在 `log` 创建之后、**transport 装配块之前**；它只依赖 `hwnd` 与 `log`，无阻碍 |
| c | Task 9 的 `byte modifiers = 0;` 未指明声明位置，而 `ri.KeyChanged` 处理器会捕获并修改它 | 若被放到订阅之后 → 前向引用 → 编译不过 | 计划里指明放在 `int mc = 0, kc = 0;` 旁边 |

另：Task 6 的 `Interfaces` 清单是**过期**的（列了不存在的 `OnMouseMoved` 与 2 参 `OnIdleMove`，与 Step 1 类代码的 4 参版本不符）。已在 Task 6 的 dispatch 里点名要求"以代码为准"。**未改计划正文**——因为 5 个代码块与 2 个调用点彼此自洽，只有那一段散文清单是旧的，改它反而有引入新不一致的风险。代价：若判错，Task 6 实现者按清单实现会编译不过（但 dispatch 已点名，且编译会立刻暴露）。

**代价（若本裁决判错）**：a/b/c 三条都只是"多/少一次编译期报错"，无运行时风险。

### Ruling 20 — `Program.cs` 体量红线由 250 上调至 320；`MessageHost` 抽成独立文件（控制方裁决，不留给实现者）。

**触发**：Task 6 结束时 `Program.cs` 实测 **230 行**，而 Task 7/8/9 还需往里加约 65 行纯接线（Task 7 约 +20、Task 8 约 +36、Task 9 约 +30）→ 终值约 301 行，**红线必破**。Ruling 3 明写"超线时报 DONE_WITH_CONCERNS，不要自行拆分（拆分是设计决定，须由控制方裁决）"，故这是控制方现在就得决的事，不能等它炸在 Task 9。

**实算的取舍**：要把终值压回 250 以内，需要抽 4 个微文件（`MessageHost`、几何轮询线程、心跳/守卫定时器、`ModifierBit`），PC 侧会从 7 个文件涨到 12 个。而其中三个抽取只是**为了数字好看的文件系统体操**——它们抽出去之后仍然是同一批接线，只是多了一层跨文件跳转。

**裁决**：
1. **抽 `MessageHost` → 新文件 `src/agent/MessageHost.cs`，由 Task 7 做，且分两个 commit：先纯搬迁（一个字不改），再在新区位里改写。** 理由不是行数，是它本来就**不属于**装配根——一个 `Form` 子类（含 Task 8 要加的逃逸键 `ProcessCmdKey`/`ProcessDialogKey` 覆写）是 UI 行为，与"接线"是两回事。这与设计文档"按风险域分文件"的初衷一致。分两个 commit 是为了让审查者能干净地确认"纯移动、零行为变化"。
2. **红线 250 → 320**。理由：这条红线的目的是防 Program.cs 长成上帝对象；而它现在要装配 9 个子系统 + 1 个轮询线程 + 3 个定时器，**纯接线**本身就值 300 行左右。把 320 行接线判成"上帝对象"，是把"文件行数"误当成"耦合度"。
3. 若 Task 9 结束时仍超 320，下一步抽的是**三个 watcher（几何轮询 + 心跳 + 前台守卫）→ `Watchers.cs`**——那是唯一还有独立理由的抽取（它隔离"后台存活性检查 + adb 进程 churn"，正是审查者点名的风险区）。

**代价（若判错）**：Program.cs 若仍长到难以维护，终审会指出，补救（抽 watchers）成本约半小时；反之若判错的是"该抽而没抽"，则 Task 7/8/9 每个实现者都要在 300 行的文件里定位自己的接线点，返工风险更高。

### 执行记录 · Task 6

Task 6: dispatched implementer (sonnet) at BASE **86f3da9**；brief 预生成 260 行。dispatch 里点名：brief 的 `Interfaces` 清单是**过期**的（列了不存在的 `OnMouseMoved` 与 2 参 `OnIdleMove`），以 Step 1 类代码的 4 参版本为准；tracker 的**声明**必须早于 `ri.MouseMoved +=`；构造处的 `phoneW/phoneH` 是占位值。另**额外要求**写 `EdgeTracker` 离线状态机测试（T1–T8）——因为计划里 Task 6 的验收全是真机步骤而手机离线，而边缘几何/冷却逻辑恰是纯函数可测的。
Task 6: implementer DONE — commit **5a06c23**（`EdgeTracker.cs` 新建 112 行 + `Program.cs` +54/−5）。`git show --stat` 已核实只含这 2 个文件。编译零警告；离线状态机 harness **8/8 PASS**；`Program.cs` **230 行**（触发 Ruling 20）。
Task 6: review package → `review-102b8c2..5a06c23.diff`（BASE 取 102b8c2 而非记录的 86f3da9 —— 因为控制方在两者之间插了一个纯文档 commit，取 86f3da9 会把计划 diff 混进审查包。这是簿记修正，非规格变更）。已派 task reviewer (sonnet)。
Task 6: review **Needs fixes** — **0 Critical、2 Important、3 Minor**。两条 Important 都是"brief 逐字强制的代码本身有缺陷"（plan-mandated）：①TAKEOVER 的**入口条件在真机上不可达** ②**回程判定用了钳制后的增量**，导致"跨进去再推回"永远出不来。审查者还自己在本机做了模拟与实测（把 `CursorModel`+`EdgeTracker` 编在一起跑接线仿真、在本机虚拟桌面调 `SetCursorPos`）。

### Ruling 21 — 两条 Important 均**独立复现属实**，改计划正文后派修轮。

**控制方独立测量**（未采信审查者的转述，自己跑了一遍）：

```
虚拟桌面: x=[0,3839] y=[0,1079]   (SM_CXVIRTUALSCREEN=3840)
SetCursorPos(3839,540) -> 3839,540
SetCursorPos(3840,540) -> 3839,540     ← 被钳回
SetCursorPos(3841,540) -> 3839,540
```
（首次调用新定义类型时曾出现一次 1919 的瞬态，随后三次测量与审查者一致，判为瞬态。）
**结论：`cursorX >= 3840` 永不成立 → TAKEOVER 永远进不去。** 更值得记的是**离线 harness 为何没抓到**：T1 喂了 `cursorX=3840`——一个真实光标**物理上达不到**的坐标；T4 反而把 3839（真实可达的最大值）编码成了"不在边缘"。**这是"用实现者的心智模型当测试输入"的典型失效**，比缺陷本身更值得进教训清单。

**裁决与计划修正**：
1. `_edgeX` 的约定改为**"触发侧最外侧有效像素列的 x"**（右侧=3839，左侧=0），构造处传 `edgeX: 3839`。这样左右两侧的 `atEdge` 与安全带判定**写法对称**，不必各自 ±1。_没采用审查者给的另一个选项（保留 3840 并写 `>= _edgeX - 1`）——那会让左边缘（传 0）与右边缘（传 3840）使用两套不同的坐标约定，是给下一个人埋坑。_
2. `OnTakeoverMove(actualDx, actualDy, ...)` 改名为 **`OnTakeoverMove(rawDx, rawDy, vx, vy)`**，方向判定改用**原始增量**；调用点相应改为 `tracker.OnTakeoverMove(e.Dx, e.Dy, cursor.X, cursor.Y)`。语义依据已写进计划注释：**原始增量表达用户意图，钳制后增量表达实际位移，回程判定要的是前者。**
3. 保留 4 参形态（未借机瘦身）：`rawDy/vy` 在当前实现中未使用，但原计划的 `actualDy/vy` 同样未使用，故不是本修轮引入的新坏味；且设计明写"只做左右边缘"，将来补上下边缘时签名不用再改。

**代价（若判错）**：①若 `_edgeX` 约定判错（例如真实桌面几何在别的机器上不同），入口会变成"贴到最右列才触发"，最多是手感偏严；②若"原始增量"判错，用户跨进去后**立即**推回会被立刻弹回 PC——但这正是期望手势。两条都不引入新的系统级风险。

### Ruling 22 — Task 6 审查的第 3 条 Important（掉线时 tracker 卡在 TAKEOVER）**裁决为在 Task 8 关闭**，不并入本次修轮。

理由：Task 8 的计划正文**已经**在 `transport.Disconnected` 处理器里写了 `tracker.AbortTakeover(); supp.Release();`，而 `AbortTakeover()` 正好把 `Current` 与 `Armed` 一起复位。这属于计划早已覆盖、只是排在后面的任务里，不是遗漏。已把该结论**明文写进 Task 8 的计划正文**（"这一段不是可选的…Task 8 实现者不得省略"），防止它被当成可选项删掉。
风险窗口的缓解：Task 7 尚未落地（无 `ClipCursor`），且 Ruling 21 的修正让"推左即回程"，用户在任何状态下都能靠推左逃出接管态。
**代价（若判错）**：Task 7 落地后到 Task 8 落地前，若恰在接管态掉线，用户需推左一次才能解除抑制（而非自动解除）。

Task 6: minor (deferred): **Program.cs 体量 230 行**——审查者独立复算了 186−5+49=230 并预测"Task 7 就会破线，不是更晚"。**与 Ruling 20 的算术一致**（我已提前裁决）。
Task 6: minor (deferred): 实现者报告里有两处数字不准（称 `EdgeTracker.cs` 为 106 行，实为 112 行；harness 用横屏 3200×2136 而 `Program.cs` 用竖屏占位 2136×3200）——对状态机数学无影响，但**映射比例从未用生产宽高比验证过**。
Task 6: minor (deferred) — **真机待决的开放问题**：旋转发生在接管期间时，回程边界恒为「逻辑 x=0」，因为 `_phoneRight` 在构造时固定、`SetPhoneSize` 只改范围。若旋转后"物理上邻接 PC 的那条边"映射到另一条逻辑边，回程几何就错了。**这是几何无关的代码里解不了的问题，必须在真机验证时确认**：横屏与竖屏下分别测「哪条逻辑边对着 PC」。若确实翻转，`SetPhoneSize` 需同时接收一个"邻接边"参数。
Task 6: fix round 1/5 dispatched（resume 原实现者 a24fc2e，Ruling 21 的两条修改 + 新增 T9/T10 回归用例；明确告知第 3 条 Important 归 Task 8、本轮不做）。
Task 6: fix round 1/5 实现完成 — commit **c2e0dbf**（仅 `EdgeTracker.cs` +`Program.cs`，+20/−9；`git show --stat` 已核实）。编译零警告；harness 更新为 **10/10 PASS**（T1 改用可达的 3839、T4 用 3838、新增 T9「入口处推左必须触发 LeaveTakeover」、T10「rawDx=0 不得触发回程」）。
Task 6: 复审包 → `review-4ea5d21..c2e0dbf.diff`（FIX_BASE 取 4ea5d21 而非 5a06c23，同样因为中间夹了一个纯文档 commit）。已派 scoped re-review (sonnet)，并点名两处最可能被修错的地方：①左挂配置（`phoneRight == false`，`_edgeX = 0`）在新"最外侧像素列"约定下语义是否仍自洽、安全带判定有没有引入左右不对称；②钳制后的 `sdx/sdy` 是否**仍然**是发给协议的值（修轮只该改"喂给方向判定的值"，不该动上线字节）。

### 关于 Task 7 的**预判**（等复审结果出来后处理）

Task 7 是唯一有系统级副作用的任务，而手机离线会带来一个必须正视的后果：**安全不变量 4（未连接绝不夺取前台）意味着 Task 7 的 `Engage()` 在手机回线前根本无法被触发**，因此 `ClipCursor` 那条路径在 Task 7 落地的当刻会是**零运行时验证**的。处置预案：
- **不为此写自动化 harness。** 一个会真去调 `ClipCursor` 的测试，若中途卡住/崩掉，用户回来会面对"鼠标被锁死且找不到原因"——为验证安全性反而制造不安全，得不偿失。
- Task 7 的验收 = 编译零警告 + 审查者对三条（Ruling 18 后为四条）不变量的逐条代码审计 + 计划里那套**必须在真机+用户在场时按序执行**的手工步骤（`taskkill` 解锁演练那条尤其不能省）。
- 这一点要在 ledger 与最终交接里标成"最高风险未验证项"。

Task 6: fix round 1/5 (2 addressed, 0 open — TAKEOVER 入口改用可达末列 3839、回程方向判定改用原始增量；commits 4ea5d21..c2e0dbf)
Task 6: 复审 clean — scoped re-review (sonnet) 判 **All findings addressed, no new Critical/Important breakage**。三项均逐条给出行号证据，并额外确认了两件我点名的事：①左挂配置（`phoneRight == false`、`_edgeX = 0`）在新约定下**没有引入左右不对称**——入口区 `≤0` 与安全带 `0..12` 的关系、`phoneX` 取 `_phoneW-1`、`backAtEdge`/`pushingBack` 三者都与右侧镜像；②**钳制后的 `sdx/sdy` 仍然照发**（`Program.cs:127-130` 那个 hunk 除加注释外未变），上线字节与修前一致。另确认第 3 条 Important **确实没有被实现**（diff stat 只有那两个文件）。
Task 6: **complete** (commits 102b8c2..c2e0dbf, review clean after 1 fix round)

### 实际交出的 Task 6（供最终交接对账）

`EdgeTracker.cs`（新，约 120 行）+ `Program.cs`（234 行，红线 320 内）。核心结论：
- **离线状态机 harness 10/10**，含两条针对本次修轮新增的回归用例。
- **Ruling 21 是本项目至今最有价值的一次审查收获**：两条缺陷都是"计划正文自己写错"，且**离线 harness 因为用它自己的错误假设当输入而无法发现**。教训已入 ledger：离线用例的输入必须来自外部约束（API 物理边界），不能来自实现者的自述。

### 执行记录 · Task 7（唯一有系统级副作用的任务）

Task 7: brief 预生成 286 行；三处计划修正已确认带进简报（`MessageHost.cs` 抽取、`out uint _` 已改为 `uint fgPid` 先声明、`supp` 声明位置、安全不变量 4）。
Task 7: dispatched implementer (**opus** —— 本任务出错形式是"用户鼠标被锁死"，是全项目最高风险，故实现者与审查者都用最强模型) at BASE **c2e0dbf**。dispatch 里下发：
  - **Ruling 20 的两个 commit**：commit 1 = `MessageHost` 纯搬迁到新文件（零改动，且新文件需自带 using）；commit 2 = 其余全部。目的：让审查者能干净确认"无行为变化"。
  - **Ruling 20 的红线 320**；`Program.cs` 当前 234 行。
  - **安全不变量 4 是承重的**，并说明了它的失效链：掉线后 tracker 回 IDLE → 用户再推边缘 → 夺取前台 + `ClipCursor`，而 Task 8 的心跳安全网在 `!IsConnected` 时短路，无人能救。
  - **`supp` 声明位置**必须早于 transport 装配块（Task 8 会在 Disconnected 里用它）。
  - **明确禁止**：实现 Task 8 的掉线处理；加任何 `SetWindowsHookEx`；动其它 5 个文件。
  - **明确禁止运行** `dist\pc-kvm.exe`（手机离线 → `Prepare` 失败 → 模态 MessageBox 永久阻塞；且落地后会在桌面左上留一条可见置顶条）。
  - **明确禁止写任何会调 `ClipCursor` 的自动化 harness**，并写明了理由（测试卡住 → 用户回来面对锁死的鼠标且找不到原因 → 为验证安全性反而制造不安全）。这是我作为控制方的刻意决定，已在 dispatch 里说明"这不是遗漏"。
  - 验收方式改为：编译零警告 + 全文搜索确认无 `SetWindowsHookEx` + **对四条不变量逐条做文件行号自审**（并说明哪几条恢复路径此刻已存在、哪几条 Task 8 才到）+ 报告行数。

## 2026-09-23 · 会话恢复（手机回线，Task 7 首次尝试作废）

### Task 7 第一次尝试失败并被回退 —— 记全过程，防止重复踩

**现象**：Task 7 的 opus 实现者起了 **1 天**未报告、未写报告文件、未做第 2 个 commit。停掉它时，它最后一句话是"Both tools now agree (8828 bytes) — but that's NOT what I measured after my surgery (7572), with the mtime frozen at 19:46. Something replaced the file with preserved mtime." —— **它陷在"有人用保留 mtime 的方式替换了文件"的偏执循环里**，反复纠缠文件字节数。

**真相（控制方查明）**：它提到的"someone replaced the file"是**它自己编辑到一半的产物**。它提交的 commit `5570a4e`（"MessageHost 纯搬迁至独立文件（零行为变化）"）**根本编译不过**：

```
src\agent\Program.cs(10,11): error CS0101: 命名空间"PcKvm"已经包含了"MessageHost"的定义
src\agent\Program.cs(22,27): error CS1519: 类、结构或接口成员声明中的标记"="无效
...
```
—— 它把 `MessageHost` 的**类声明剪掉了、却把类体留在了 Program.cs 里**。工作区里那份未提交的半成品同样是编辑到一半（`MessageHost.cs` 只有 598 字节，明显被截断）。

**处置**：
1. `TaskStop` 掉该 agent。
2. 半成品**双保险留存**：`%TEMP%\task7-wip\`（4 个文件的原样拷贝）+ `git stash`（`stash@{0}: task7-wip-von-stuck-agent`，含未跟踪的 `Suppressor.cs`）。
3. 确认**无远程**（`git remote -v` 为空）→ 回退本地未推提交安全。
4. `git reset --hard c2e0dbf`（最后一个已审查 + 可编译的状态）。回退后重编译：**编译成功，7 个源文件，exit 0** ✅

**教训（写给未来）**：**"先纯搬迁、再改写"的两段式要求，对弱上下文/长任务是一个陷阱**——中间那个 commit 天然处于"编译不过"的状态，实现者若在此时被打断或开始自我怀疑，就会卡在那里。本轮我要的是"便于审查确认无行为变化"，代价是一个必然坏掉的中间态。**下次若还要搬迁，正确做法是：搬迁 commit 必须当场验证编译通过**。已按此改掉 Task 7 的要求（见 Ruling 23）。

### Ruling 23 — Task 7 重做：取消"纯搬迁 commit"，改单 commit；并给实现者加反卡死护栏。

理由：见上。两段式的中间态是实现者卡死的直接原因，而它换来的审查便利（"确认纯移动"）可以用别的方式拿到（审查者对比 Program.cs 被删块与 MessageHost.cs 新增块即可）。
新增护栏（写进 dispatch）：**若对同一件事重复验证超过 2 次仍未收敛，立刻停下来报告 DONE_WITH_CONCERNS / BLOCKED，不要继续。** 这次的失败模式就是"对同一个字节数反复测量、怀疑外部进程篡改"。
**代价（若判错）**：审查者需要多花一点力气确认搬迁与改写的关系，但少了整整一类卡死风险。

### ✅ 真机验证：Task 5B 的几何修复**已确认生效**

在干净状态 `c2e0dbf` 上重编译并运行，日志实测：

```
# 启动 2026-09-23 09:57:27
# 设备已连接
# 屏幕几何 3200x2136      ← 从手机读到的真实逻辑尺寸（横屏）
# PONG seq=1
```
对照修复前的硬编码 `CursorModel(2136, 3200)`：现在读到的 `3200x2136` 与 `dumpsys input` 实测的 mapper 范围（X 0–3199 / Y 0–2135）**完全吻合** → 模型边界与真实可动范围终于一致，Ruling 13 的失效机制（模型先撞上限即停发包）在算术上已消除。
**仍待用户肉眼确认的**：四边推到底是否都能到物理边缘（尤其原症状方向：向左、向上）。

### ✅✅ 真机验收通过：用户报告的「虚拟边界」缺陷已修复（2026-09-23）

在干净状态 `c2e0dbf` 上做完整真机验收，**用户确认「到了」**（手机光标能走到屏幕物理右边缘，不再卡在 2/3 处）。

日志侧的机械证据（`dist\pc-kvm.log` 第 1040 行起本次会话）：

| 观测 | 值 |
|---|---|
| `# 屏幕几何 3200x2136` | 从手机读到的真实逻辑尺寸，与 `dumpsys input` 实测 mapper 范围（X 0–3199 / Y 0–2135）**完全吻合** |
| `# 几何已应用 3200x2136` | 几何变化消费点生效（模型与 tracker 都被更新） |
| **ENTER / LEAVE 各 22 次** | 状态机在真机上被反复走通 |
| 进入点 y 值序列 | 124 / 164 / 288 / 506 / 506 / 650 / 700 / 1271 / 1493 / 1532 / 1714 / 1807 … | 
| （映射正确性）| `phoneY = cursorY × 2136 / 1080`，y 随用户在 PC 边缘的高度单调变化、全部落在 `[0,2135]` 内 ✅ 比例入屏成立 |
| `# LEAVE takeover` 紧跟 `ENTER` 且其中有约 550 个鼠标事件 | 「跨进去 → 在手机屏上移动 → 推左退出」全链路成立；**且"跨进去后立刻推左就能退出"这条 Ruling 21 修好的路径实测可用**（修复前永远触发不了） |

**结论**：Ruling 13 的机制推断（模型边界与真实可动范围不一致 → 模型先撞上限停发包 → 光标冻在中途）**在真机上被证实**，Task 5B 的修复**在真机上被证实有效**。这是本会话最重要的一个闭环。

**过程中纠正的一个我自己的误报**：我曾用 `ps -A | grep -i injector` 判定"手机侧注入器已死"，那是**错的**——`ps -A` 显示的是进程名（`app_process`），不含命令行的 `Injector`。正确判据是 `dumpsys input | grep -i PC-KVM`（能看到 `Device 17: PC-KVM Virtual Input Mouse`）。**教训：判定设备侧进程存活，要用设备注册表而非进程名匹配。**

**顺带发现的真实缺陷（非新 bug，是 Task 7 的职责）**：当前构建里按键转发是**无条件**的——用户在 PC 上的每次点击都会被同时转发到手机光标所在位置。Task 7 的抑制功能正是为此。

**验收后清场**：app 已停；手机侧 `pkill -f Injector`、`/sdcard` 临时截图已删、`adb reverse` 已拆；`dist\` 只剩三个构建产物。

### Task 7 重做（Ruling 23 之后）

Task 7: REDO dispatched，**fresh implementer (opus)**，BASE `c2e0dbf`。dispatch 里**明文写入了上次失败的全过程**并给出四条护栏：
  1. **禁止用文件字节数/mtime/行数作为任何判断依据** —— 上次就是从这里开始跑偏的（"something replaced the file with preserved mtime"）。文件可疑就去读代码。
  2. **本任务只有 1 个 commit** —— 取消"先纯搬迁、再改写"的两段式，因为那个中间态天然编译不过，正是上次卡死的现场。搬迁与改写合并，保证每个 commit 都能编译。
  3. **同一处修复尝试两次仍不成就停下报 BLOCKED**，不许第三次，也不许开始怀疑"有外部东西在干扰"——没有。
  4. 上次的遗留物（stash `task7-wip-von-stuck-agent` 与 `%TEMP%\task7-wip`）**明说不要去找、已作废**，避免它拿半成品当起点。
另：本次仍**禁止运行 `dist\pc-kvm.exe`** —— 与上次不同，手机现在在线，`Engage()` **真的可能被触发**（用户正在用这台机器，万一推到边缘就会锁光标），故运行时验证留给审查通过后由用户在场监督进行。验收仍为「编译零警告 + 无 `SetWindowsHookEx` + 四条安全不变量逐条 file:line 自审 + 行数」。

Task 7: REDO implementer DONE — commit **734af75**（单 commit，4 文件 +200/−17：`Suppressor.cs` 新建 122 行、`MessageHost.cs` 新建 38 行、`EdgeTracker.cs` +7 即 `AbortTakeover`、`Program.cs` +50/−17）。实现者自报四条不变量全部按构造满足（`ClipCursor(Zero)` 为 `Release()` 首句见 `Suppressor.cs:91`；失败路径不钳光标 `Suppressor.cs:74-81`；未连接不夺取见 `Program.cs:77` 首句；detach 在 `finally` 见 `Suppressor.cs:67`），`Release()` 可达于 LeaveTakeover / FormClosing / ApplicationExit / guard 定时器四路。
Task 7: **控制方独立核实**（未采信自述）：`git log c2e0dbf..HEAD` 计数 = **1** ✅（Ruling 23 的单 commit 要求达成，上次那个坏中间态没有重演）；`git show --stat` = 恰好那 4 个文件 ✅；**自己跑 `build-agent.ps1` → `编译成功`、9 个源文件、exit 0、零警告** ✅；`Program.cs` 实测 **225 行**（红线 320）；`src/agent/` 全文搜索 `SetWindowsHookEx|WH_KEYBOARD_LL` **无命中** ✅。
Task 7: review package → `review-c2e0dbf..734af75.diff`（1 commit, 15121 B）；已派 task reviewer (**opus** —— 全项目最高风险 diff)。dispatch 里除四条不变量原文外，另点名了六个"清单式审查会漏掉"的失效面：①`ClipCursor` 成功之后到 `Engage()` 返回 true 之间是否有任何可能抛出/提前返回、导致"光标被钳住但调用方以为失败"；②是否可能存在"已抑制但 `Release()` 不可达"的状态（含重复 `Engage()`、与 `AbortTakeover()` 的交互、定时器 tick 中途进程拆除）；③`_prevForeground` 保存/恢复在"期间前台合法变更"时会不会把焦点从当前程序硬拽走；④新 `MessageHost` 是可见置顶无边框小窗，能否被点击/聚焦而干扰接管外的正常使用、是否需要 `WS_EX_NOACTIVATE`（Task 8 要往它上面挂 `Ctrl+Alt+Esc`）；⑤`Engage/Release` 与 `guard` 定时器的线程关系；⑥250ms 定时器会不会重入或累积。并写明"任何可能导致光标被钳住或前台被窃取而不释放的，都按 Critical 定级"。
Task 7: review clean — task reviewer (opus) 判 **Spec compliant / Task quality Approved**；**0 Critical、0 Important、4 Minor**。四条安全不变量逐条给出**行号证据**且与 diff hunk 算术复现一致（`Suppressor.cs:88` 首句、失败返回 `:68-72` 在唯一的 `ClipCursor(ref r)` `:78` 之前、未连接守卫 `Program.cs:77` 在唯一的 `Engage()` `:83` 之前、detach 在 `finally` `:58-65`）。审查者另独立追了三条路径并给出结论：**"不存在已抑制而 `Release()` 不可达的状态"**（tracker 只能经 LeaveTakeover 或 AbortTakeover 回 IDLE，每个 AbortTakeover 调用点要么在 `CheckForeground()` 里 `Release()` 之后、要么在从未 Engage 的路径上）；`Release()`/`Engage()` 幂等故 `Application.Exit()` 触发的 FormClosing+ApplicationExit 双重释放安全；`guard` 是 `System.Windows.Forms.Timer` 故在 UI 线程 tick 且 WM_TIMER 会合并，无重入无累积；`_prevForeground` 恢复在"前台已被抢走"时因后台进程 `SetForegroundWindow` 失败而无害、在正常 LeaveTakeover 时正是期望行为。
Task 7: minor (deferred): `Suppressor.cs:79-81` —— `_log(...)` 夹在 `ClipCursor(ref r)` 成功与 `IsEngaged = true` 之间。**日志写入若抛异常 → 光标已钳住但 `IsEngaged == false` → 守卫定时器（`CheckForeground` 在 `!IsEngaged` 时提前返回）被废掉。**退路仍在（推左 / Alt+Tab / 进程退出时系统释放 clip），且是 brief 逐字规定的代码（plan-mandated）。终审可判是否值得一行修改（把 `IsEngaged = true` 提到 `_log` 之前）。
Task 7: minor (deferred): `Suppressor.cs:98` 的 `CheckForeground()` 文档注释写"由心跳线程定时调用"，实际接在 UI 线程的 `guard` 定时器上。**这是给 Task 8 的明确约束**：Task 8 的心跳若改用真正的后台线程去调 `CheckForeground()`，其 `ForegroundLost` 处理器会碰 `host.SetStatus()` → 跨线程异常。**故 Task 8 的心跳必须继续用 `System.Windows.Forms.Timer`（UI 线程）。**
Task 7: minor (deferred): `MessageHost.cs:62-86` 的置顶 220×28 条**可被点击激活**（无 `WS_EX_NOACTIVATE`）→ 接管外误点它会聚焦到一个吞按键的窗口。审查者判为可接受（接管的前台夺取正依赖它可被激活，且 Task 8 要往它上面挂 `Ctrl+Alt+Esc`），建议只在类注释里写明这一取舍。
Task 7: minor (deferred): `Suppressor.cs:44` —— 任何第三方进程都能调 `ClipCursor` 清掉钳制，而 `Engage()` 的幂等早返回不会重新施加钳制、守卫也只盯前台不盯钳制。brief 原设计，后续硬化时可考虑在 `CheckForeground` 里重新施加。
Task 7: **complete** (commits c2e0dbf..734af75, review clean)

### Ruling 24 — Task 7 的运行时验收**推迟到 Task 8 之后**，与 Task 8 合并为一次在场监督的测试。

理由：Task 8 恰恰是给抑制子系统补安全网的任务（`Ctrl+Alt+Esc` 逃生键、心跳超时解除、断线自动解锁）。**现在就让用户测，等于让他在缺少逃生键的状态下冒险**；合并后一次测完 Task 7+8，既更安全又省用户一轮时间。计划里 Task 7 Step 3 的原意是"落地即测"，此处按"安全优先"调整。
**代价（若判错）**：若 Task 7 存在运行时缺陷，它会先与 Task 8 的改动叠加，定位时要多分辨一层。缓解：两者改动的文件基本不重叠（Task 8 动 `Program.cs` + `MessageHost.cs`，且都是叠加式接线），且审查已把 Task 7 的静态面查到很细。

### Ruling 25 — 两处放弃路径补发 `MSG_LEAVE`（防手机侧按键永久残留）。

自查发现：接管期间若按着鼠标键/修饰键，然后走**放弃**路径（`Ctrl+Alt+Esc` 逃逸、或心跳超时自动解除），`AbortTakeover()` **不发送 LEAVE**，而设备侧只有 `MSG_LEAVE` 分支才会 `buttonsDown = 0` 且 `KeyState.releaseAll` → **手机侧会永久残留一个"按住的键"**。只有正常 `LeaveTakeover` 路径才发 LEAVE。
处置：计划正文两处 abort 前各加 `transport.Send(Protocol.EncodeLeave());`（连接已断时 `Send` 本身是安全 no-op，故心跳路径补发无副作用）。已随 Task 8 的 brief 下发。
**代价（若判错）**：多一次无害的发包。

### Task 8 派发

Task 8: brief 重新生成（147 行，`EncodeLeave` ×2 与"这一段不是可选的"均已确认在内）；dispatched implementer (sonnet) at BASE **6ce342c**。dispatch 里下发四条强制约束：
  1. **心跳必须是 `System.Windows.Forms.Timer`（UI 线程）**，绝不能用后台线程 —— 依据正是 Task 7 审查的 Minor 2：`CheckForeground()` 的 `ForegroundLost` 处理器会碰 `host.SetStatus()`，真从别的线程调会抛跨线程异常。
  2. **心跳安全网必须放在 `if (!transport.IsConnected) return;` 早返回之前**（Ruling 18b）。
  3. **`Disconnected` 扩展不是可选的**（Task 6 审查的 Important 3 裁决在此关闭），三条 `AbortTakeover` / `supp.Release()` / `host.SetStatus` 一个都不能少。
  4. **两条放弃路径都要先发 `MSG_LEAVE`**（Ruling 25）。
  另：明确要求"扩展既有的 `Disconnected`/`MessageReceived` 处理器，不要新增订阅造成重复日志"；`MessageHost` 现在在**独立文件**里，逃逸键的 `Escape` 事件与两个覆写要写在那；`supp` 已提前声明故在 `Disconnected` 里可用（不要移动它）。仍**禁止运行 exe**（手机在线 + 用户在用机）。验收 = 编译零警告 + **七条恢复路径逐条审计**（LeaveTakeover / FormClosing / ApplicationExit / guard→ForegroundLost / 心跳超时 / 逃逸键 / Disconnected）+ 行数。

### 预告：Task 7+8 合并的真机实测方案（等 Task 8 审查通过后交给用户）

准备：控制方启动 app 并确认日志 `# 设备已连接` + `# 屏幕几何`；用户打开记事本点进去确认能打字；四条退路就位（推左 / `Alt+Tab` / `Ctrl+Shift+Esc` / `taskkill`）。
步骤（按风险从低到高）：
1. 启动健全性：托盘在、状态条显示 `IDLE`、日志无异常
2. 推右进接管 → 状态条变 `TAKEOVER → 手机`
3. **键盘抑制**：不点记事本，直接敲字 → **不应出现**（核心判定）
4. **鼠标抑制**：移动鼠标 → PC 光标应被钳住不动
5. 推手机光标到左边缘继续推 → 回 `IDLE`、PC 光标恢复
6. 焦点恢复：随便点一个窗口应正常
7. **逃逸键**（Task 8）：再进接管，按 `Ctrl+Alt+Esc` → 立即回 `IDLE`
8. **断线解锁**（Task 8）：进接管后拔线（或由控制方杀注入器）→ 2 秒内回 `IDLE`、鼠标解锁
9. **锁死恢复演练**（最关键）：接管态下用任务管理器 `taskkill /F /IM pc-kvm.exe` → 鼠标应**立刻**解锁
10. **UAC 抢占**（Task 8）：接管态下触发 UAC 弹窗 → 250ms 内检测到前台丢失并解除
任何一步异常 → 用第 5 步或第 9 步的方式脱身；控制方全程读日志核对。

### Task 8 实现完成 + Ruling 26

Task 8: implementer DONE_WITH_CONCERNS — commit **03e24d2**（2 文件：`Program.cs` +67/−1、`MessageHost.cs` +25）。三条 concern 我已逐条评估（详见下）。**控制方独立复核**：`git show --stat` = 恰好那 2 个文件 ✅；**自己跑编译 → `编译成功`、9 个源文件、exit 0、零警告** ✅；`MessageHost.cs` 内确有 `public event Action Escape` + `ProcessCmdKey`/`ProcessDialogKey` 两个覆写 ✅；心跳是裸 `Timer`（WinForms）且全文无 `System.Threading.Timer` ✅（实现者还在该处写了注释说明"换 System.Threading.Timer 会引入跨线程调用"）。

**三条 concern 的控制方初评**（终评交审查者）：
1. **设备侧 `MSG_LEAVE` 分支尚不存在** —— ✅ 属实且重要。`Injector.java` 的 `handle()` 结尾仍是"由后续任务接管"，所以（a）Ruling 25 补发的 `EncodeLeave` 目前是空操作；（b）**更严重的是手机侧 `buttonsDown` 从来没有被清零过**（用户按着鼠标键时无论怎么退出都会在手机上留下"按住的键"）。已把这层写进 Task 9 Step 3 的必做说明。
2. **`Disconnected` 里的 `host.SetStatus` 跑在 Transport 的 accept 线程上**（跨线程碰 `Label.Text`）—— ⚠️ 这正是我禁止心跳用后台线程的同一条理由，却出现在计划自己规定的代码里。**交审查者判定真实严重性**（WinForms 的 `CheckForIllegalCrossThreadCalls` 默认仅在附加调试器时为 true，故实际多半不抛，属潜在缺陷）。
3. 运行时验证未做 —— ✅ 按指令，留给用户在场监督。

### Ruling 26 — 行数预算按实测重算（320→360）；`ModifierKey` 移入 `KeyMap.cs`；并承认一次测量错误。

**发现**：Task 8 实现者报告"基线其实是 249 不是 225"。控制方复核：**是我错了**。`Program.cs` 是 **CRLF + BOM**，我用 PowerShell `Measure-Object -Line` 得到 225，而权威口径 `wc -l` 实测 Task 7 末为 **250**。真实序列：Task 6 末 234 → Task 7 末 **250** → Task 8 末 **315**（+65，远超我估的 +36）。Task 9 后将达约 345 → **320 红线必破**。
**处置**：①红线 320 → 360，并**明文记为"修正一个基于错误测量的预算"，不是因不便而放宽**；②Task 9 的 `ModifierBit` 改放新建 `src/agent/KeyMap.cs`（纯静态映射，与 `Protocol.cs` 的纯函数惯例一致，可离线测试）；③承诺：**此后若再超 360 必须抽取，不得再上调**；下一步唯一有独立理由的抽取是三个 watcher（几何轮询线程 + 心跳 + 前台守卫）→ `Watchers.cs`。
**教训**：**对 CRLF+BOM 文件，`Measure-Object -Line` 不可作准，一律用 `wc -l`。** 这条已让两个实现者（Task 7 报 225、Task 8 报 249）和我先后踩到——Task 8 的实现者主动发现并报告偏差，值得记一笔。
**代价（若判错）**：若 360 仍不够，Task 9 之后要补一次 watcher 抽取（约半小时）。

### Ruling 27 — 查出一个**既有缺陷**：滚轮与鼠标键的转发没有状态门控（IDLE 态照发）。

**发现**：控制方复查 `MouseMoved` 处理器全文时发现，Task 4 写的滚轮与三个鼠标键转发**放在状态判定之外**，Task 6 引入状态机时也没有把它们收进去：

```csharp
                // 这两段在 IDLE 态同样执行
                short wheel = (short)(e.WheelDelta / 120);
                if (wheel != 0) transport.Send(Protocol.EncodeScroll((short)0, wheel));
                if (e.ButtonFlags != 0) { EmitButton(...) x3 }
```

**后果**：IDLE 态（= 用户正常使用 PC 的全部时间）里，**每一次点击、每一格滚轮都被转发到手机**，作用在手机光标停留处 → 可能误开手机 App、误触按钮，且用户**无法"正常用 PC 而不影响手机"**。

**自我纠正**：我在真机验收后曾对用户说"这属于 Task 7 的职责"——**那是错的**。Task 7 的抑制解决的是**反方向**（接管时 PC 不该响应自己的输入），而"转发要不要门控"这件事**没有任何任务覆盖**，属计划遗漏。

**处置（Ruling 27）**：在 Task 9 里新增 **Step 2b**，把这两段整体包进 `if (cursor != null && tracker.Current == KvmState.Takeover)`，并在计划里写明"改完后 `MouseMoved` 里不应有任何在 IDLE 态发包的路径，请在报告里逐条确认"。放进 Task 9 而非单独成任务的理由：Task 9 本就在给键盘补同一条门控（`if (tracker.Current != KvmState.Takeover) return;`），鼠标与键盘用同一条不变量、一次审完更清楚；且 `Program.cs` 已在 Task 9 的文件清单里。
**代价（若判错）**：若门控过严（例如某些场景本应在 IDLE 转发），用户会发现"接管外手机收不到输入"——但这正是设计意图（IDLE 态鼠标归 PC、手机不该有任何反应）。

### Task 8 审查 + Ruling 28

Task 8: review **Approved** — task reviewer (opus) 判 Spec compliant / Task quality Approved；**0 Critical、1 Important、4 Minor**。四条强制约束逐条按语义核实通过（含"TAKEOVER 守卫块在 `if (!transport.IsConnected) return;` 之前"这一条的位置正确性）。审查者还确认了两件我没点名的事：①`supp.Release()` 幂等且与线程无关（首句无条件 `ClipCursor(IntPtr.Zero)`、两个 Win32 调用都允许跨线程）→ accept 线程调用与任意双重 Release 组合都安全；②`guard` 与 `heartbeat` **都是 WinForms 定时器**，tick 在消息循环上串行化，**不存在交错风险**；③逃逸键可达性在静态上成立（`Engage` 要求自有窗口为前台，Label 子控件不可聚焦故窗体保持焦点，`Ctrl+Alt+Esc` 非系统保留键，且 250ms 守卫定时器是独立兜底）。**七条释放路径确认无组合能钳住光标。**

### Ruling 28 — 修那条 Important（跨线程 `host.SetStatus`）：与 C1 是同一条约束的两面，不得双标。

审查者判定：`transport.Disconnected` 在 accept 线程触发（`Transport.cs:59-60`），计划原文在那里直接改 `host` 的 `Label.Text` → **非法跨线程 WinForms 访问**。不挂调试器时靠 `SendMessage` 侥幸不抛；**挂调试器即 `InvalidOperationException`**；UI 线程若阻塞还会连带卡住 accept 线程、拖慢重连。不会钳光标（`AbortTakeover`/`Release` 均在其前同步执行，异常被 `AcceptLoop` 的 catch 吞掉并走 finally 清理）。
**裁决：修。** 决定性理由不是严重性而是**一致性**——我给 Task 8 的约束 C1 就是"心跳必须用 UI 线程 Timer，理由是那条路径会碰控件、不许跨线程"。同一条约束必须同样适用于 Disconnected 里的控件访问，否则我就是双标。
**改法**（已就地改计划正文，随修轮下发）：`AbortTakeover`/`Release` **保持同步、保持在 marshal 之前**（释放抑制绝不能延后进一个被 marshal 的委托），只把最后那句 `host.SetStatus("IDLE")` 包进 `host.BeginInvoke((MethodInvoker)delegate { ... });`。
**代价（若判错）**：`BeginInvoke` 在窗体已销毁的极端时序下自身可能抛（会落进 `AcceptLoop` 的 catch，无害）。

Task 8: minor (deferred): `lastPongTicks` 重连后可能瞬时陈旧 → 理论上 `Connected` 与首个 PONG 之间的窗口里若恰有一次心跳 tick 可能误判超时（毫秒级、方向安全）。加固法：`Connected` 处理器开头重置 `lastPongTicks`。
Task 8: minor (deferred) → **归入 Task 9**：`ForegroundLost` 那条 abort 路径没发 `EncodeLeave`，与 C4 同一条"防手机侧按键残留"的理由。裁决归 Task 9 —— 那才是让设备侧 LEAVE 真正生效的任务，届时一并补，避免现在做一次"发了也没人听"的改动。
Task 8: minor (deferred): 逃逸键在 IDLE 态也会触发（无害：多发一次 LEAVE + 解除武装）。与 brief 原文一致。
Task 8: fix round 1/5 dispatched（resume 原实现者 a022cd3，只修那一句 marshal；四条 Minor 明确告知不要动）。

Task 8: fix round 1/5 完成 — commit **a3e0205**（`Program.cs` +7/−1，只那一句进 `BeginInvoke`，`AbortTakeover`/`Release` 仍在 marshal 之前同步执行）。控制方独立核实：`git show --stat` = 1 文件 ✅；`wc -l` = **321 行**（红线 360）；**自己跑编译 → 零警告 exit 0** ✅。
Task 8: 复审 clean — scoped re-review (sonnet) 判 **All findings addressed, no new Critical/Important breakage**。审查者另做了两项我点名的定点核查：①确认 `AbortTakeover`/`Release` **没有**被移进 lambda（`Program.cs:197-200` 同步块在前）；②确认 `BeginInvoke` 在窗体销毁时序下抛出的异常会落进 `Transport.AcceptLoop` 的 catch（`Transport.cs:62`）且 `finally` 仍清理流与客户端，**故即使 marshal 失败也不可能把抑制挂在身上**——因为释放已经同步做完了。
Task 8: **complete** (commits 6ce342c..a3e0205, review clean after 1 fix round)

### ✅ Task 7 + Task 8 真机验收通过（2026-09-23，用户在场监督）

按 Ruling 24 合并一次测试。**用户确认「验证都正确」**，日志侧的机械证据：

| 观测 | 值 | 结论 |
|---|---|---|
| `# 抑制已生效` | 本次会话 7 次、上一会话 15 次 | 夺取前台 + `ClipCursor` 核心机制反复成功 ✅ |
| `# ENTER takeover` / `# LEAVE takeover` | 7 / 5（差额由 abort 路径补齐，abort 不打 LEAVE 日志） | 状态机与抑制状态一致（日志实测 `# 抑制已生效` 在 `# ENTER takeover` **之前**，与代码顺序吻合）✅ |
| **键盘抑制** | 用户复测：记事本先打 `AAA`，接管期打 `BBB`，退出后**只有 `AAA`** | ✅ **抑制方案存在的理由被验证** |
| **断线解锁** | 日志显示 `# ENTER takeover at phone(0,464)` 后**无 LEAVE**、直接 `# 设备已断开` → 用户报鼠标 2 秒内解锁 | ✅ Task 8 的断线处理器实测有效 |
| **不变量 4 兜底** | 断开后用户连推 4 次，日志 4 条 `# 未连接设备，放弃这次跨越` | ✅ **Ruling 17c 的守卫救了一次真实险情**：若无此守卫，用户会处于"前台被夺 + 光标被钳 + 手机不在" |
| **`ForegroundLost` 自动解锁** | `# 前台被抢走，强制解除抑制` 累计 3 次 | ✅ 实测有效（这也是下面 Ruling 29 的成因） |
| `Ctrl+Alt+Esc` | **`# 逃逸键触发` = 0** | ⚠️ 见 Ruling 29 |

### Ruling 29 — `Ctrl+Alt+Esc` 在本机是**死代码**（按键组合被系统层吃掉），但用户可见的"能退出来"由既有安全网兜住，故判 Minor 不单开修复轮。

**证据（原始日志，铁证级）**：
```
KEY scancode=0x1D DOWN   ← Ctrl
KEY scancode=0x38 DOWN   ← Alt
KEY scancode=0x01 DOWN   ← Esc
KEY scancode=0x01 UP
# 前台被抢走，强制解除抑制   ← 守卫在这里解除
```
按键**确实按下了**（Raw Input 三个键全收到），但 `# 逃逸键触发` 从未出现 → 组合键在到达我们窗口的 `ProcessCmdKey` 之前就被系统/第三方工具截了，并且**把前台抢走了** → 于是 250ms 守卫走 `ForegroundLost` 路径解除抑制。
**为何判 Minor**：用户实际可用的退路一条没少，且都是独立验证过的（推左 / 断线 / 前台易主）；被"吃掉"的那条仍然触发了**另一条设计内的安全网**，不是靠运气。
**候选改法**（留给终审 triage，不单独开轮）：①把组合改为带 Shift 的（如 `Ctrl+Alt+Shift+Esc`）；②让处理器同时接受两个组合并在日志里打出命中的是哪一个——这样一次实机测试就能判定"是按键被截"还是"处理器机制本身不工作"。
**本机风险背景**：这台机器装了大量会绑热键的东西（青骓、InputShare、UU远程、GameViewer 等），任何热键方案都必须实测而非假定。

### 环境记录（影响所有后续真机验证）

**adb/USB 链路今晚掉了 2 次**，第二次的恢复过程修正了 ledger 原有的手册：**`adb kill-server` 单独不够**——当时有**第二个 adb 进程**（pid 20992）仍占着设备，必须**强杀所有 `adb` 进程**再 `adb start-server` 才恢复。
**连带的产品缺口（记为待办，终审 triage）**：链路一断，**注入器会随 `adb shell` 会话一起死、`adb reverse` 隧道也一起消失**，而 app **不会自动重建**（`DeviceLauncher` 只在启动时跑一次；`Transport` 只会等重连，但隧道没了就永远等不到）。现实后果是"线一抖就得重启 app"。属计划"先用 USB 跑通、网络问题留到最后"之外的新信息。

### Task 9（最后一个任务）派发

Task 9: brief 262 行（Ruling 26/27 的 `KeyMap.cs`、Step 2b 门控、设备侧 LEAVE 缺口说明均已确认在内）；dispatched implementer (sonnet) at BASE **a3e0205**。
dispatch 里把**必须做的四件事**写死：①新建 `KeyMap.cs` 放 `ModifierBit`（Ruling 26）；②重写 `KeyChanged` 并门控在 TAKEOVER；③**Step 2b 的既有缺陷修复**——滚轮与鼠标键转发目前完全没门控，IDLE 态也照发（Ruling 27，这是真机测试时用户"没法正常用 PC 而不影响手机"的直接原因）；④**补上设备侧缺失的 `MSG_LEAVE` 分支**（Task 8 实现者发现的缺口：它一落地，Task 8 补发的 `EncodeLeave` 才真正生效，且手机侧 `buttonsDown` 才第一次有了清零路径）；另加 Task 8 审查的 Minor 2（`ForegroundLost` 那条 abort 路径也补发 LEAVE）。
**验收方式（离线可做的都要求做了）**：编译零警告 + 设备侧 javac/d8/push 通过 + **两个纯映射各写一个离线 harness**（`KeyMap.ModifierBit` 用 csc 编、`ScancodeMap.toHidUsage` 用 javac 编，各给出具体断言向量）+ **IDLE 门控审计表**（逐条列出所有发包语句的 file:line 与门控条件，证明 IDLE 态零发包）。
另在 dispatch 里写入了今晚新学到的 adb 恢复法（`kill-server` 不够，必须强杀所有 adb 进程）。
**控制方补救**：派发后才想起 app 仍在运行会锁住 `dist\pc-kvm.exe` 导致实现者编译失败，已立刻停掉 app 并清理设备侧 ✅。
**Task 9 的真机验收**（键盘字符出现在手机上、以及"按住的键不再残留"）仍需手机 + 用户在场，安排在审查通过之后。

Task 9: implementer DONE — commit **6bd4e76**（5 文件 +120/−10：`KeyMap.cs` 新建 30 行、`Program.cs` +44/−8、`Injector.java`、`KeyState.java`、`ScancodeMap.java`）。实现者自报：`Program.cs` 349/360 行；`KeyMap` harness **12/12**、`ScancodeMap` harness **26/26** 全过；IDLE 门控审计确认 Idle 态无用户输入发包路径（Ruling 27 的修复在 `Program.cs:157`）；`ForegroundLost` 那条 abort 也已补发 `EncodeLeave`（`:110`）；设备侧 `MSG_LEAVE` 分支 + `KeyState.releaseAll` 已加。
Task 9: **控制方独立核实**（未采信自述）：`git log a3e0205..HEAD` 计数 = **1** ✅；`git show --stat` = 恰好那 5 个文件 ✅；`wc -l` = **349 行** ✅；**自己跑两端编译** —— PC 侧 `编译成功`、10 个源文件、exit 0 零警告 ✅；设备侧 `javac --release 8` + `d8` + `adb push` 全程通过、jar 5647 字节已推到 `/data/local/tmp/pckvm.jar` ✅。
Task 9: review package → `review-a3e0205..6bd4e76.diff`（1 commit, 15038 B）；已派 task reviewer (**opus** —— 本任务跨 C#/Java 两种语言共用一个线上协议，最易出**静默**错误：位序或偏移错一点就是"手机收到的键不对，但哪儿都不报错"）。除四条交付物外，另点名了五处重点：①**修饰位契约端到端**（`KeyMap.ModifierBit` → `Protocol.EncodeKey` 的 mods 字节 → 设备 `sendKeyboard` 的修饰字节 → HID 描述符的 8 位语义顺序，错位会表现为"能打字但修饰键不对"）；②**穷举每个修饰键**（左右 Shift/Ctrl/Alt/Win）在真机产出的 scancode 形态（E0 或非 E0）是否被 `ModifierBit` 覆盖、以及是否**同时**被 `ScancodeMap` 映射进按键槽（若两者都命中就是"同一个按键被双报"）；③`KeyChanged` 门控与修饰态是否会跨接管残留；④Ruling 27 门控是否**每一条**发送都覆盖，并裁定实现者的 concern 2（旋转重对齐的 `EncodeHome` 算不算用户输入转发）；⑤设备侧 LEAVE 分支是否真的清干净（`buttonsDown`、槽位、修饰键）+ Java 侧是否越界（不得出现 `android.*`、lambda、default method、try-with-resources，因为构建刻意不带脱糖）。
Task 9: review **Approved** — task reviewer (opus) 判 Spec compliant / Task quality Approved；**0 Critical、2 Important、1 Minor**。**跨语言契约核查全部通过**：修饰位顺序（`KeyMap` 产出的 8 位 ↔ `HidDescriptor` 的 Usage Min 0xE0→Max 0xE7）逐位吻合；KEY 线上格式（`Protocol.EncodeKey` 的 u16 scancode LE + down + mods ↔ 设备 `u16(p,0)/p[2]/p[3]` 与 `payloadLength=4`）偏移宽度一致；E0 约定（`RawInput` 置 `MakeCode|0xE000` ↔ `ScancodeMap` 测 `& 0xE000`）端到端一致；`MSG_LEAVE` 清理完整（鼠标键清零**并真发报告**、修饰键与 6 槽清零**并真发报告**）；三条 PC 侧 abort 路径现在都发 LEAVE。审查者还独立复核了实现者报告的 13 处发包点自审表，**逐行准确**。

### Ruling 30 — Task 9 审查的 2 Important + 1 Minor 同源，合并为一次修轮；三处计划正文就地改对。

| # | 缺陷 | 证据 | 修法 |
|---|---|---|---|
| 1 | **左 Win 在手机上完全无效**（plan-mandated） | 真机左 Win = **E0 0x5B**；`KeyMap` 只写了非 E0 的 0x5B（死条目）、E0 分支漏 0x5B → LGUI 位永不置位。而实现者以为能兜住的另一条路（E0 0x5B → 槽位 0xE3）**也是死的**：0xE3 超过 `HidDescriptor.java:70` 的 Usage Maximum 0x65，解析器丢弃 | `ModifierBit` 的 E0 分支补 `if (mk == 0x5B) return 0x08;` |
| 2 | **纯修饰键不发 HID 报告**（pre-existing/plan-level） | `KeyState.java:9-11`：`if (usage < 0) return;` 落在 `sendKeyboard` 之前 → 单按修饰键时手机端修饰态不更新。组合键能work只因下一个键捎带了 mods；**但鼠标报文不带 mods 字节 → 手机上"按住 Ctrl 再点击"永远看不到修饰键**。且这违背计划自己在 PC 侧写的意图 | `apply` 里修饰态变化时即使 `usage < 0` 也发报告；抽出 `slotsToBytes()` 复用 |
| 3 | **E0 修饰键白占按键槽**（Minor，与 1/2 同源） | 右 Ctrl/右 Alt/右 Win 映射成 0xE4/0xE6/0xE7（超 Usage Max 被丢弃）却仍占 `KeyState` 槽位 → 按住右 Ctrl+右 Alt 再按 5 键会静默丢第 6 键 | 四个 E0 修饰键一律 `return -1`（与计划自己的规则一致，也顺手消除"同一按键既进修饰字节又进槽位"的双报隐患） |

**为何三条一起修**：修 3 会让 E0 修饰键走进"`usage < 0`"这条路，与修 2 是同一条代码路径；分开修会互相引入回归。**代价（若判错）**：若判定"纯修饰键该发报告"有误，只是多几条无害的键盘报告。
Task 9: 另注 —— **计划自身有内部矛盾**（审查者指出）：Step 1 的说明文字写"修饰键应返回 -1"，但紧随其后的 E0 示例代码却把四个修饰键映射成槽位 usage。实现者按**代码**实现并如实上报，判定为正确处置；矛盾已由本次计划修订消除。
Task 9: minor (deferred): 旋转重对齐的 `EncodeHome`（Task 5B 引入）是 `MouseMoved` 里唯一未按状态门控的发包。审查者裁定**不违反 Ruling 27**（它是几何重对齐、不是用户输入转发），且最多每次旋转触发一次、与 ENTER 时的 HOME 冗余。保留原样，仅记录。
Task 9: fix round 1/5 dispatched（resume 原实现者 a1a7c7e，三处修改 + 两个 harness 断言更新；明确告知 `KeyState.apply` 无法离线单测、改用逐行审查确认）。
Task 9: fix round 1/5 完成 — commit **cd86784**（仅 `KeyMap.cs` +6/−1、`KeyState.java`、`ScancodeMap.java`；`Program.cs` 与 `Injector.java` 未动）。自报 `KeyMap` harness 13/13、`ScancodeMap` 29/29 全过；两端构建通过；设备侧 push 一次成功（5683 字节）。
Task 9: **控制方独立核实**：`git show --stat` = 恰好那 3 个文件 ✅；`wc -l` = 349 ✅；**自己跑 PC 侧编译 → exit 0** ✅；三处修改逐行核对落地 ✅（`KeyMap` 非 E0 与 E0 两个分支都有 `0x5B→0x08`；`ScancodeMap` 的 E0 `0x1D/0x38/0x5B/0x5C` 全为 `-1` 而 `0x5D→0x65` 保留；`KeyState` 有 `modsChanged` 早发与 `slotsToBytes`）。
Task 9: 复审包 → `review-3a750f1..cd86784.diff`（1 commit, 7295 B）；已派 scoped re-review (sonnet)，并点名三处该修法自己可能修错的地方：①E0 修饰键现在走 `usage < 0` 路径，**按下与抬起都要能发出报告**（只发变化还不够，得让去断言后的字节也到手机）；②`slotsToBytes()` 抽出的两处调用载荷语义是否与原来一致、早发那次会不会带上不该有的陈旧槽位；③`modsChanged` 的比较顺序——`modifiers` 是 `static int`，若在赋值之后才比较，早发就会"永不触发"或"永远触发"（静默错）。

Task 9: 复审 clean — scoped re-review (sonnet) 判 **All findings addressed, no new Critical/Important breakage**。三条逐条给出 file:line 证据，并且把我点名的三处"该修法自己可能修错的地方"逐项查了：①**E0 修饰键的按下与抬起都能发出报告**（按下时 `newMods != modifiers` → 带断言字节的报告；抬起时 → 带去断言字节的报告）✅；②`slotsToBytes()` 抽出后两处调用载荷语义与原来完全一致，且早发那次打包的是**当前真实按住的键**（槽位在每个按键事件都更新），不会带陈旧内容 ✅；③`modsChanged` 的比较顺序正确——`KeyState.java:10` 拿 `newMods` 与**赋值之前**的 `modifiers` 比较，没有"赋值后才比较"那种静默错 ✅。审查者还确认 `0x5D`（Menu）保持 `0x65`（恰在 Usage Maximum 上，合法），以及两条回归核查：E1 前缀 Pause 现返回 -1（原本也是死值，净行为不变）、所有可入槽的 usage 均 ≤ 0x65 故 `(byte)` 转型永不截断。
Task 9: **complete** (commits a3e0205..cd86784, review clean after 1 fix round)

### 全计划状态：Task 1–9 + 5B 全部完成并通过审查

| Task | 状态 | 真机验证 |
|---|---|---|
| 1 PC 骨架 / 2 注入器 / 3 协议传输 / 4 最小闭环 / 5 推角落归零 | ✅ | ✅（用户实测） |
| **5B 显示几何运行时读取**（计划外插入） | ✅ | ✅ **用户确认原「虚拟边界」缺陷已修** |
| 6 边缘状态机与坐标映射 | ✅ | ✅ 22 次 ENTER/LEAVE |
| 7 抑制器（`ClipCursor`） | ✅ | ✅ 抑制生效 22 次、键盘抑制验证 |
| 8 边界情况（心跳/断线/逃逸键） | ✅ | ✅ 断线解锁实测；⚠️ `Ctrl+Alt+Esc` 被系统层吞掉（Ruling 29） |
| 9 键盘支持 | ✅ | ✅ 接管期按键已转发（`Ctrl+Z`×2、小键盘 `Enter`×2，日志实证）；**手机屏幕是否显示字符仅用户目视确认**（见 2026-09-23 第二会话） |

**下一步**：①Task 9 真机验收（需用户在场）；②整分支终审（merge-base `894acf9`，23 个提交、src 净增 1914 行）；③`finishing-a-development-branch` 合并到 master（需用户点头）。

---

## 终审输入：deferred minor 汇总与我的分级建议
截至 Task 9 修轮，ledger 共记录 **34 条 deferred minor**。下面按"我建议怎么处理"分组，供终审 triage。

### A. 建议合并前修（原 3 条；**A1 已修**）

| # | 条目 | 理由 |
|---|---|---|
| ~~A1~~ | ✅ **已修** —— 2026-09-23 第二会话（「接管秒退」那一轮）已把 `IsEngaged = true` 提到 `_log` 之前，并注明「Task 7 审查 Minor A1，合并前必修」 | 已消，无需终审再判 |
| A2 | **链路断开后 app 不会自愈**（今晚真机测试发现，非计划内）：注入器随 `adb shell` 会话一起死、`adb reverse` 隧道消失，而 `DeviceLauncher` 只在启动时跑一次 → "线一抖就得重启 app" | 今晚实测掉了 2 次，**这是用户每天都会遇到的形态**。要么补一个重连后重建隧道/注入器的路径，要么至少写进文档并让错误可见 |
| A3 | Task 6 的**真机开放问题**：旋转发生在接管期间时，`_phoneRight` 在构造时固定，若旋转后"物理邻接 PC 的那条边"映射到另一条逻辑边，回程几何就错 | 代码层面解不了，**必须在真机验证**（横屏/竖屏各测一次哪条逻辑边对着 PC）。若确实翻转，`SetPhoneSize` 要同时接收"邻接边"参数 |

### B. 已被后续任务自然解决（1 条，仅需在终审确认）

| # | 条目 | 状态 |
|---|---|---|
| B1 | Task 5 的 `cursorResetPending` 跨线程未同步 | **已由 Task 5B 解决**：该字段被删除，语义由 `static volatile bool _geometryChanged` 取代 ✅。终审只需确认仓库里已无该字段 |

### C. 建议保留原样（其余 30 条）

按"为何可以不动"归类：

- **纯装饰/死代码**：Task 1 的 `kc` 死变量、未 `Dispose` 的 `NotifyIcon`、`.gitignore` 冗余；Task 3 的 `Injector.java` 死 import。（均继承自 brief，且不影响行为）
- **理论性、且已由现有兜底覆盖**：Task 3 的 `Transport` `_stream`/`_client` 未标 `volatile`；Task 5B 的 `RunAdbCapture` 管道顺序与 `Process` 未 `Dispose`；Task 5B 的几何撕裂窗口（自愈）；Task 5B 的 `QueryDisplay` 延迟 `ReadLoop` 启动；Task 8 的 `lastPongTicks` 重连瞬时陈旧。
- **本用户场景不成立**：Task 4 的平滑滚轮非 ±120 倍数增量（阶段一日志证明其滚轮严格 ±120）；Task 4 的 X 键与 down+up 合并标志（超出范围）。
- **明确的设计取舍**：Task 7 的 `MessageHost` 可点击（取舍已记录，且接管前台夺取依赖它可激活）；Task 7 的第三方可清 `ClipCursor`（brief 原设计，硬化留待后续）；Task 8 的逃逸键在 IDLE 也触发（无害）；Task 9 的旋转重对齐 `EncodeHome` 未门控（审查者裁定不违反 Ruling 27）。
- **需观察而非修改**：Task 2 的 `UHID_START` 未读 → 若后续出现零星 `EINVAL`，那就是要补的检查（本机至今未出现）。
- **文档/注释级**：Task 7 的 `CheckForeground` 注释写"心跳线程"实为 UI 线程 `guard` 定时器；Task 9 的 `KeyMap` 非 E0 `0x5B` 成为死条目（保留无害，且 harness 有断言）。






Task 8: brief 预生成者请注意 —— Task 9 的 brief 已按 Ruling 26/27 重新生成（`KeyMap.cs`、Step 2b 门控、设备侧 LEAVE 缺口说明）。

---

## 2026-09-23 · 第二会话 —— 接管秒退根因修复 + 键盘补全（真机验收通过）

**入口**：交接 `2026-09-23-pc-kvm-接管秒退与键盘修复已验收.md`。
**用户指令**：「其余都正常，TASK 9 手机无响应」→「PC 上的鼠标在状态条内部移动，这是预期内的吗」→「能隐藏掉吗？」→「验收完成，新窗口中我有新需求」。

### 缺陷与根因（两条都是真机实测出来的）

用户报：**进接管 0–6 秒就被踢回 PC**，且接管期内手机毫无反应。

**根因 A —— 钳制区选错了像素。** `Suppressor.Engage()` 把光标钳在"进接管前光标所在的那个像素"，即屏幕最右列 `(3839, y)`。而鼠标点击会送给**光标下**的窗口并激活它 → 接管期每一次点击都激活了那个像素下的**别人的窗口** → 我们丢前台 → 250ms 守卫立刻解除抑制。
**证据**：失效会话 20 次 `ENTER takeover`，其中 **8 次「前台被抢走」+ 12 次 `LEAVE` = 20**（无第三种结束方式），且**接管窗口内 0 条按键记录**——用户每次都是被踢出后才开始打字。最快一次 `ENTER` 的**下一行**就是 `LEAVE`。
**修法**：钳进**我们自己的置顶状态条**；再收成**本窗口中心的那一个像素**（整个窗口矩形会让光标在状态条里滑动——用户实测到并提问）；`Release()` 里用 `SetCursorPos` 把光标搬回原位（否则每次退出都留在左上角）。

**根因 B —— 回程判定看单次增量符号。** 入屏瞬间虚拟光标恰在 `x=0`（= 与 PC 相邻的那条边），旧实现 `rawDx < 0` 即回程 → **任何一次 `-1` 抖动都立刻回程**，接管寿命 0–6 秒。
**修法**：在边界上**累积外推**，越过 `BackPushThreshold = 40` mickeys 才回程；新增 `_lastVx` 区分「从屏内滑到边界」（= 走到边界，不计）与「在边界上继续外推」（= 越过边界，计入）；`outward == 0` 的纯纵向事件**既不累积也不清零**（日志里 `dx=0 dy=N` 大量存在，若拿它清零，斜推时累积量被反复打散，表现为"回不去"——比"退得太容易"更糟）。

### 其余改动

- **接管期隐藏 PC 光标**（用户要求）：`MessageHost.SetCursorHidden` 用 `CreateCursor` 造 32×32 全透明光标挂在本窗口与 Label 上，并显式 `SetCursor` 施加一次。**刻意不用全局 `ShowCursor(false)`**——它是桌面级显示计数器，app 被硬杀时不自动恢复，而 `taskkill /F /IM pc-kvm.exe` 正是本项目写明的应急退路，不能跟它打架。
- **小键盘 15 条 HID 映射**（Task 9 那条"手机无响应"的真因）：`ScancodeMap` 非 E0 表整段缺小键盘 → 返回 `-1` → 注入器静默丢弃。现场：接管期内用户按的 6 个键全是这一段（`0x52 0x52 0x47 0x52 0x47 0x52` = 小键盘 `0 0 7 0 7 0`），而早先会话实测 `0x50 0x52 0x50 0x4D` = `"2026"` —— **证明用户平时就用小键盘**。
- `SetLastError = true` 补到 `ClipCursor`/`GetWindowRect`（否则 `Marshal.GetLastWin32Error()` 读到的是上一次别的调用留下的值，`err=` 日志全是垃圾——本项目已多次栽在"诊断仪器说谎"上）。
- **A1 顺手修掉**（终审输入 A1 已消）：`IsEngaged = true` 提到 `_log` 之前。
- `CheckForeground` 顺序改为 `Release()` → `ForegroundLost` → **最后**才取证写日志（`_log` 是 AutoFlush 的 `StreamWriter`，`TitleOf` 是同步跨进程 `SendMessage`，排在前面一旦出事就把光标留在钳住状态）；日志加"抢走者"标题。
- 接管期**按钮事件不抽样记录**（MOUSE 行是 2% 采样，一次点击只有 2 个事件、几乎必然被漏掉——本项目正因此无法证伪"点击导致前台被抢"这个假设）。

### ✅ 真机验收（用户报「验收完成」）

日志最后一次会话（行 22565 起，`# 启动 2026-09-23 14:42:45` = 新构建）：

| 观测 | 值 | 结论 |
|---|---|---|
| `ENTER`/`LEAVE` | **7/7**（完全配平） | 无异常终止 |
| **`前台被抢走`** | **0**（修复前 8） | **根因 A 在实测中消失** |
| 接管期按钮事件 | **28 次** | 28 次点击全被转发、零被踢回 |
| 回程累积量 | 40/41/41/42/…/47（14 次全 ≥ 40） | **根因 B 的阈值语义成立** |
| 接管期按键 | `Ctrl+Z`×2、小键盘 `Enter`×2 | 接管期按键真实转发 |
| 心跳失联 / 逃逸键 / 断开 | 0 / 0 / 0 | 无异常路径 |

### ✅ 两项无日志可证的验收 —— 用户已明确确认

1. **接管期手机是否显示字符**（日志只能证"已发出"）
2. **PC 光标是否真的被隐藏**（`SetCursorHidden` 没有日志埋点）

**用户于 2026-09-23 本次会话中明确表示「两项目已经验收过」**，故这两项计入已验收。
**但如实记录**：它们**只有目视证据、没有机械证据**。后续若手机侧行为反常、或"PC 光标仍可见"，这两处是首选怀疑对象——尤其是光标隐藏，`Program.cs` 只剩 1 行余量（Ruling 26），当初就是为了不占预算才没给它加日志埋点。

### 控制方独立复算（本次新做，未采信任何自述）

- **二进制对应真相源**（复查 §六 那个坑）：`dist\pc-kvm.exe` `14:42:21` **晚于全部源文件**（最新 `Suppressor.cs` `14:16:16`），`CreateCursor`/`SetCursorHidden` 各命中 1；`pckvm.jar` MD5 `0bce282a641ac111d79192b8822c1c4e` 与设备侧一致。**用户验收的那一份就是当前源码编出来的。**
- **从仓库当前源码重新编译两个 harness 并实跑**（不复用 `%TEMP%` 里的旧 exe）：`EdgeTracker` **15/15 PASS**、`ScancodeMap` **45/45 PASS**，均 exit 0。

### Ruling 31 — 两个 offline harness 入仓 `tests/`，各带一个从源码现编的 runner。

**理由**：`%TEMP%` 里的会丢，而其中 **T11/T12/T14 是"接管秒退"这个真缺陷的直接回归**——丢了同样的缺陷下次会静默复发，而它的真机形态（0–6 秒被踢出）极难定位（第一次排查靠翻 570 KB 日志）。
**处置**：`tests/edge-tracker/{Main.cs,run.ps1}`、`tests/scancode-map/{TestMain.java,run.ps1}`、`tests/README.md`。两个 runner **每次从零编译仓库当前源码**，绝不复用预编译产物（本项目栽过一次"验的不是用户实际会跑的那一份"）；两个 harness 自身在 `fail > 0` 时返回非零（`Environment.Exit(1)` / `System.exit(fails)`），故 runner 能判失败。harness 按字节原样入仓（已核 md5 一致），文件名沿用 `%TEMP%` 原件。
**未做**：`Suppressor`（`ClipCursor`）的 harness——刻意的，理由与 Task 7 dispatch 相同（测试若卡住 → 用户面对"鼠标被锁死且找不到原因"，为验证安全性反而制造不安全）。
**代价（若判错）**：多两个脚本要维护，且 JDK 路径硬编码（与 `build-injector.ps1` 同一套，换机器要改两处）。

### 本会话结束时的状态

- 5 文件改动（+211/−18）已提交（`cd86784` 之后）；`tests/` 随另一个提交入仓。
- **`Program.cs` 实测 359/360 行**（`wc -l` 口径）——**只剩 1 行**。Ruling 26 的承诺仍然生效：**下次再往 `Program.cs` 里加任何东西，必须先抽 `Watchers.cs`**（几何轮询线程 + 心跳 + 前台守卫），不得再上调红线。
- 挂账未做（见终审输入 A2/A3）：**A2 链路断开后 app 不自愈**（注入器随 `adb shell` 死、`adb reverse` 消失，`DeviceLauncher` 只在启动跑一次 → "线一抖就得重启 app"）；**A3 旋转发生在接管期间时"邻接边"是否翻转**（代码层面解不了，需真机横竖屏各测一次）。

### Ruling 32 — ledger 与 SDD 工作产物入仓；查明"未被跟踪"其实是**工具刻意 ignore**，不是疏漏。

**发现（纠正一条我此前的误述）**：ledger 一直未被 git 跟踪，我一度记录为"既成事实"。
实查 `git check-ignore -v` → **`.superpowers/sdd/.gitignore` 的内容只有一行 `*`**，即 superpowers 的 SDD 工具**刻意**把它自己的工作目录排除在版本控制之外。这是**规则**，不是没人管。
另外 `docs/handoffs/` **并未被 ignore**（根 `.gitignore` 不涉及它），它只是从来没被 `git add` 过——两者成因不同，不要混为一谈。

**用户裁决**：入仓。
**处置**：改 `.superpowers/sdd/.gitignore`，在 `*` 后补一行 `!2026-09-21-phase2-walking-skeleton/`，**显式放行**该目录（而不是用 `git add -f` 绕过——显式规则能让下一个人看懂"为什么这个文件被跟踪"）。根因是 git 无法重新包含"父目录已被排除"的文件，故必须放行目录本身。
入仓内容：`progress.md`（本 ledger）+ 9 份 `task-*-brief.md` / `task-*-report.md` + 14 份 `review-*.diff`。

**为何值得入仓**：ledger 含 Ruling 1–32 与 34 条 deferred minor，是**不可再生**的恢复地图
（终审的「deferred minor 汇总」一节直接靠它）；brief/report 记录了每个任务的实现边界与实现者当时的判断。
`review-*.diff` 名义上可由 git 重现（它们是两次已提交之间的 diff），一并入仓只是让该目录自洽。

**保留意见**：这是**过程文档**而非代码。若日后仓库要对外，这批（约 500 KB 纯文本）应是最先被考虑移出的东西。
**代价（若判错）**：仓库历史里多出一批过程产物；`indent-check.txt`、`t7-program-indentmap.txt` 两个临时产物也一并进了仓（未单独剔除，免得留下不完整的目录）。





















## 2026-09-23 · 阶段三 Task 8（#4 断联自愈）落地 —— 按任务要求补进这份恢复地图

本节由阶段三（`.superpowers/sdd/2026-09-23-phase3-config-and-resilience/`）Task 8 的实现者按任务指令**追加**。它落在阶段二的 ledger 里，因为它关闭的正是下面「终审输入」挂账的 **A2**（「链路断开后 app 不会自愈」：注入器随 `adb shell` 会话一起死、`adb reverse` 隧道一并消失，而 `Transport` 只会等重连——隧道没了就永远等不到，于是"线一抖就得重启 app"），而它的**覆盖边界**直接关系本 ledger 的 adb 故障恢复手册（§Task 5 环境隐患记录、§2026-09-23 环境记录两处）。

**落地了什么**（commit 见阶段三 progress）：

- `Watchers` 新增后台重连监督线程（线程纪律：绝不直接写日志/碰控件，一律经 `Report()`/`LogFromWorker()` 的 `_host.BeginInvoke` 回 UI 线程——R9）。每 `ReconnectSeconds`（默认 5，可在设置里调 2–60）检查一次，判据统一为"该周期结束时 `transport.IsConnected` 仍为 false"。分级升级：**①** 重建 `adb reverse` 隧道 + 重启注入器（每次未连接都做；重启前先 Kill 旧的，幂等，否则设备侧堆多个 `app_process`）；**②** 连续 3 个周期仍不通 → `adb kill-server`+`start-server`（温和，每档只做一次）；**③** 再 3 个周期仍不通 且 `Config.AllowKillAdb=true` → `taskkill /F /IM adb.exe`+`start-server`（激进，默认关，因本机同时跑着 3 个 adb，强杀会打断其它正在用 adb 的工具）。
- `DeviceLauncher.Prepare` 拆成 `EnsureTunnel`/`PushJar`（重连只建隧道，不重推 jar——设备侧那份通常还在）；设备侧进程所有权从 `Program` 移到 `Watchers`（重连会换进程），`TrayUi` 退出清理改用 `Watchers.DeviceProcess`。
- 断开期间状态条显示"等待设备…（已重试 n 次，…）"，日志**只在状态变化时**打一行（不刷屏）。adb 可见性判据拆成纯函数 `DeviceLauncher.ParseDeviceVisible`（必须跳过 "List of devices attached" 表头——naive `Contains("device")` 会把空列表误判成"有设备"），由离线用例 D1–D5 钉住（agent-logic 24→29）。

**⚠️ 覆盖边界（实测形态，写给下一个会话）**：2026-09-23 现场实测到一次**设备级**掉线，其形态是——`adb devices` 全空；Windows 侧 `Android ADB Interface`（`VID_18D1&PID_4E11\04053891899C1540`）状态显示 OK，adb 却打不开它；`adb kill-server`+`start-server` **已实测无效**；且当时机器上**只有 1 个 adb 进程**（故**不是**本 ledger §Task 5 记的"3 个 adb server 抢同一 USB 设备"，也不是 §2026-09-23 晚间记的"第二个 adb 进程仍占着设备"那条恢复路径）。

**明确结论：本自愈级联不覆盖该形态。** 它覆盖的是"隧道断 / 注入器死"（A2 的日常形态）；对"adb 整个看不见设备"，①②③ 全走完也无济于事（②③ 动的是 adb server 层，而那时 server 是好的、坏的是设备在 Windows 侧的呈现）。这种掉线仍需人看一眼手机（重插线/解锁/确认 USB 调试），**不要以为 #4 已经修好一切**。

**真机三档演练**（杀注入器 / `adb kill-server` / 拔线，任务 8 Step 6 的 8 项清单）按 R11 推迟：编写本代码时手机离线，无法当日实跑，待用户批量的硬件会话一并执行。
