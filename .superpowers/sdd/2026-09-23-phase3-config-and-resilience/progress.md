# SDD ledger — plan: docs/superpowers/plans/2026-09-23-phase3-config-and-resilience.md

分支：**phase2-skeleton**（本计划从 `095b8eb` 起）。不建 worktree，见 R5。
Spec：`docs/superpowers/specs/2026-09-23-phase3-config-and-resilience-design.md`（binding authority）
上一阶段 ledger（**只追加、不改写**，见 R4）：
`.superpowers/sdd/2026-09-21-phase2-walking-skeleton/progress.md`
上一阶段计划：`docs/superpowers/plans/2026-09-21-phase2-walking-skeleton.md`

> 工作区说明：本目录是 git-ignored 的 scratch（`.superpowers/sdd/.gitignore` 里是一行 `*`，
> 那是 superpowers 工具的默认行为）。上一阶段的 `progress.md` 因为用户明确要求而**已被跟踪**
> （提交 `b8d167b`）——**已跟踪的文件不受 .gitignore 影响**，所以它仍然安全。

## 飞行前冲突扫描（2026-09-23）

### 跨任务共享（文件 / 接口）

| 任务对 | 共享什么 | 一方产出 vs 另一方消费 | 发现 |
|---|---|---|---|
| 1 ↔ 2,3,4,5,6,7,8 | **`Program.cs`** | T1 大幅减行；其余每个任务都改它 | ⚠️ **行数预算会破线 → R1** |
| 1 ↔ 8 | `Watchers.cs` | T1 建（含 `_stop`/`Stop()`），T8 加重连线程与 `_devProc`/`DeviceProcess` | 干净（T8 是叠加式） |
| 1 ↔ 3 | `TrayUi.cs` | T1 建（只有「退出」），T3 加「设置…」+ `SettingsApplied` 事件 + 多吃一个 `Config` 参数 | ⚠️ 构造签名在 T3 变更 → R2 |
| 1 ↔ 8 | `TrayUi` 构造函数 | T1 传 `Process devProc`；T8 去掉该参数、改用 `watchers.DeviceProcess` | 干净（T8 明写要同时改构造与调用点） |
| 2 ↔ 3,4,5,8 | `Config` 类型 | T2 定义字段/`Load`/`Save`；T3/4/5/8 消费 | 干净（字段名与默认值在 spec §5 已统一钉死） |
| 2 ↔ 4,7 | `tests/agent-logic/Main.cs` | T2 建骨架，T4/T7 追加用例 | 干净（追加式；runner 带"文件不存在则跳过"的过滤，故 T2 阶段可只编 Config） |
| 3 ↔ 4,5 | `trayUi.SettingsApplied` 处理器 | T3 建立（只打日志）；T4 加 `SetSensitivity`；T5 加 `SetEdge` | ⚠️ 三段接力改写同一块 → R3 |
| 4 ↔ 5,7 | `Program.cs` 的 `MouseMoved` / `EnterTakeover` / `KeyChanged` | T4 改前两者；T5 改构造与 `SettingsApplied`；T7 只改 `KeyChanged` 尾部 | 干净（三段互不重叠） |
| 5 ↔ 7 | `Program.cs` 的 DllImport 区 | T5 加 `GetSystemMetrics`；T7 加 `GetKeyState` | 干净（同区不同行，追加式） |
| 6 ↔ 1,3 | `TrayUi.cs` 的 `Tray.Icon` 那一行 | T1 建 `= SystemIcons.Application`；T6 替换该行 | 干净（T6 明写"替换那一行"） |
| 8 ↔ 1,3,5 | `ApplicationExit` 清理链 | T1 建（`watchers.Stop()` 打头）；T8 把 `Cleanup` 的实参换成 `watchers.DeviceProcess` | 干净 |

### 任务自洽性

| 任务 | 检查项 | 发现 |
|---|---|---|
| 1 | 搬迁块行数 vs 目标行数 | ❌ 初稿估"约 306 行"，且只搬 62 行 → 计划末尾必然破线 → **R1** |
| 2 | 用例条数 vs 实现 | 干净（C1–C8 = 8 条，runner 期望 `pass=8`） |
| 3 | 对话框代码 vs C# 5 硬约束 | 干净（无内联 `out`、无 `?.`、无插值串；`using(...)` 是 C# 3） |
| 3 | `TrayUi` 拿不到 `Program` 局部量 | ❌ 初稿把"应用设置"写在托盘回调里，会导致改速度/换边无从下手 → 已改为事件回传 → R3 |
| 4 | 用例条数 vs 实现 | 干净（S1–S8；`pass=16` 与 C8 相加一致） |
| 4 | 小数余量语义 vs 用例期望 | 干净（逐条复算：S2 得 0/1/0、S3 得 0/-1、S8 二百次共 100） |
| 5 | 左挂用例 L4/L5 vs 设计 | ❌ 初稿 L4 是恒真断言（等于抄 L1）；L5 方向反了——入屏瞬间虚拟光标就在邻接边上，连推 +127 会**立刻**回程 → 两处已在计划内修正 |
| 5 | 左挂用例条数 | 干净（T1–T15 + L1..L6b = 24 条） |
| 6 | 图标与构建脚本 | 干净（无外部素材依赖；`.ico` 先建再改 `.ps1`，同任务内） |
| 7 | 翻译的返回值语义 | ❌ 初稿没写清"返回 scancode 还是 usage" → 已在计划内加 N4 钉住 |
| 7 | scancode-map 新用例的 RED 判断 | ❌ 初稿断言会 FAIL，实际设备侧 E0 表本来就有那些映射（`0x4C` 未定义→`-1` 恰与预期一致）→ 已修正为"应当场全过；不过则 BLOCKED，不许改用例迁就" |
| 8 | 分级触发的"失败"判据 | ❌ 初稿"连续失败 3 次"未定义 → 已定为"该周期结束时 `IsConnected` 仍为 false"，并给出时间估计 |
| 8 | 重连线程 vs 日志线程安全 | 干净（全部经 `Report()` 的 `BeginInvoke`；已在自检表里立了"`ReconnectLoop` 里不得直接 `_host.SetStatus`"的检查项） |

## 裁决

**R1 = Ruling 33 —— Task 1 的抽取扩到第二块（`TrayUi.cs`），否则本计划必然踩破 360 红线。**
证据（实测，非估算）：`Program.cs` = 359 行；Task 1 初稿只搬三个 watcher
（几何轮询 17 + 前台守卫 5 + 心跳 40 = **62 行**）；本计划后续 6 个任务要往里加约 60 行
→ `359−62+14+60 ≈ 371`，**超 360 红线 11 行**，而任务 5/6/7/8 的验收里都写着 "≤360"。
那条自检会在任务 5 就开始踩空，而 Ruling 26 明文禁止再上调红线。
处置：Task 1 同时把托盘（7）+ 逃逸键（11）+ 生命周期（10）搬进新文件 `src/agent/TrayUi.cs`。
共搬 90 行、补回约 17 行接线 → 286；加完功能约 346，**余量 14**。
**代价（若判错）**：搬进 TrayUi 的托盘/退出路径也让 Task 1 的接触面变大。两者都是低风险 UI 代码，
但确实增加了 Task 1 的回归面。换来的是后面 7 个任务不再有破线风险。
若 14 行余量仍不够，下一步抽 `InputRouter.cs`（`MouseMoved` + `KeyChanged` 共 81 行）——
**不得再上调红线**（这是 Ruling 26 的原话）。

**R2 —— `TrayUi` 的构造签名在 T3 与 T8 各变一次**（T3 加 `Config`、T8 去 `Process devProc`）。
这不是缺陷，是"每个任务只知道自己那一步"的正常演化；两处变更都在计划正文里明写了
"任务 N 的实现者负责同时改构造函数与调用点"。
**代价（若判错）**：实现者只改一处的话编译立刻报错——可见、低成本，不会静默。

**R3 —— `trayUi.SettingsApplied` 处理器由 T3/T4/T5 三段接力改写，且行为必须留在 `Program.cs`。**
起因：托盘菜单在 R1 之后搬进了 `TrayUi.cs`，而"应用设置"要用到的
`scaler`/`tracker`/`supp`/`transport`/`screenW`/`screenH` 全是 `Program.cs` 的局部量，
`TrayUi` 看不到。故 `TrayUi` 只弹对话框 + 写盘 + 抛事件，应用行为由 `Program.cs` 订阅。
T4/T5 的正文给的是**整个处理器的完整文本**（而非"在某行后加一行"），照抄即可、不会互相覆盖。
**代价（若判错）**：若实现者改成局部插入，可能丢掉前一轮加的行；故三处正文都要求"整体替换为下面这段"。
另一层代价：`Program.cs` 因此多约 16 行（已计入 R1 的 60 行估算）。

**R4 —— 本工作区 ledger 与上一阶段 ledger 的关系。**
skill 规定每个 plan 只读写自己的工作区；但本计划 Task 8 Step 7 要求把 #4 的覆盖边界
写进上一阶段的 `progress.md`，因为那是**项目级**恢复地图（含 Ruling 1–32 与 34 条 deferred minor）。
处置：本工作区承担全部 SDD 记账；Task 8 落地时**额外**向上一阶段 ledger 追加一节，
**只追加、不改写它已有内容**。
**代价（若判错）**：上一阶段 ledger 会多出一节本可只留在本工作区的文字——无害。

**R5 —— 不建 worktree，留在 `G:\pc-kvm` 的 `phase2-skeleton` 分支上做。**
理由：①本项目从阶段一起就在单工作树上做（阶段二全部 25 个提交如此）；
②`dist\pc-kvm.exe`（用户实际运行的产物）、`pc-kvm.ini`、`dist\pc-kvm.log` 都在 `G:\pc-kvm\dist`，
换 worktree 会打断用户既有的"退出 exe → 控制方构建 → 用户运行"流程，而本计划每个任务都要这个流程；
③分支不是 master，skill 的硬要求（不得在 master/main 上实施）不适用。
**代价（若判错）**：隔离性弱一些。缓解：本计划每个任务都独立提交、且每个任务都有独立审查关，
回退粒度足够细。

## 执行记录

（下面按任务追加。每个任务：dispatch → 报告 → 审查包 → 审查 → 修轮 → 完成。）

Task 1: dispatched implementer (**sonnet** —— 4 文件搬迁 + 接口适配，属"多文件集成"档) at BASE **ae78fdc**。
  派发时随附简报之外的承重信息：Global Constraints 全量（C# 5 禁用清单、wc -l 口径、
  Git Bash 路径篡改、单文件 exe、零全局钩子）、`watchers` 声明位置为什么拆成两处（承重）、
  两处有意行为新增（`# 检测到几何变化` 与退出路径的 `watchers.Stop()`）不要删。
  另下发了三条歧义处置：①用户已退出 pc-kvm.exe → dist\ 未锁定，构建应正常；若被占用则
  **不许杀进程**、报 BLOCKED；②同一问题两次不成即停报 BLOCKED，**不许开始怀疑有外部进程改文件**
  （阶段二 Task 7 的 opus 实现者就是在那个偏执循环里烧掉一天，真因是它自己编辑到一半）；
  ③`build-agent.ps1` 末尾要复制 `%TEMP%\pckvm.jar`，缺失时报出来、不许凭空造 jar。
  开工前置已满足：用户已退出 pc-kvm.exe，`dist\` 解锁。
Task 1: implementer 报 **NEEDS_CONTEXT** —— 它拦下了**计划里的真缺陷**，未提交、未擅自修好。
  缺陷：Step 1b 的文字说搬"逃逸键"（`:325-335`），但给的 `TrayUi.cs` 代码块里**没有**这个处理器，
  且 `TrayUi` 的构造器收不到 `EdgeTracker`。照抄的后果是 **Ctrl+Alt+Esc 整条逃生通道失效**
  （实现者用 `rg` 核实 `host.Escape` 零订阅者）。它按简报的"任何一项不符就报 BLOCKED，
  不要自行修好"停下——**纪律正确，这正是我要的行为**。
Task 1: **R6 —— 逃逸键处理器放进 `Watchers.cs` 的构造函数。**
  三选项取舍：A 留在 `Program.cs`（实现者测出会是 295 行，计划末尾约 355/360，只剩 5 行余量
  ——正是 Ruling 33 要防的）→ 否决；B 进 `TrayUi` 并加 `tracker` 参数（同时违背它声明的
  Produces 签名与我自己写的"不参与状态机逻辑"类注释）→ 否决；C 进 `Watchers` → 采纳。
  采纳理由是净收益而非折中：它做的四件事（`Send(EncodeLeave)` → `AbortTakeover` → `Release`
  → `SetStatus("IDLE")`）与 `Watchers.HeartbeatTick` 里那条安全网**逐字相同**，
  放隔壁等于让"放弃序列"在这个项目里只有一处需要维护。
  **代价（若判错）**：`Watchers` 的职责从"后台守护"扩到"后台守护 + UI 触发的放弃路径"，
  类名的语义与内容略有偏差。若日后认为该另置，搬走约 12 行，成本极低且可见。
Task 1: **R7 —— 实现者自行解决的两处，均确认。**
  ①两个新类由 `public class` 降为 `internal class`：`MessageHost` 是 `class MessageHost : Form`
  （internal），public 类的 public 构造器不能收 internal 参数类型 = CS0051；而"不得修改既有文件"
  排除了改 `MessageHost` 的路，故降级是唯一解，单程序集内零运行时差异。已核实修饰符清单
  （`Transport`/`Suppressor`/`RawInput`/`Protocol`/`KeyMap`/`EdgeTracker`/`DeviceLauncher`/
  `CursorModel` 都是 public；`MessageHost`/`Program` 是 internal）。
  后续任务不受影响：`Config`/`MouseScaler`/`SettingsForm` 只吃 public 类型。
  ②托盘文本 `"PC-KVM（阶段二骨架）"` → `"PC-KVM"`：保持新值（旧标签已过时），
  作为**第三处有意改动**补进了计划的等价表——初稿漏列，由实现者发现。
Task 1: 计划已就地修正（Step 1 的 Watchers 构造器补上逃逸键订阅、Step 1b 去掉"逃逸键"并写明
  为什么不在 TrayUi、Step 2 表格与 Step 6 等价表同步、Step 6 有意改动由两处改为三处、
  新增两个新类必须 internal 的说明）。
Task 1: implementer **DONE** — commit **ccf32a7**（3 文件 +225/−95；commits 列表核对只有这 1 个）。
  自报：`Program.cs` 359→**283**、`Watchers.cs` 140、`TrayUi.cs` 66；R6 落地（逃逸键在
  `Watchers.cs:60`，`rg` 恰 1 处、Program.cs 零命中）；R7 两处确认；Step 6 等价表全 PASS。
Task 1: **控制方独立复算（未采信自述）**：
  - 自己跑 `build-agent.ps1` → `编译成功`、12 个源文件、exit 0、30.0 KB
  - 自己跑两套 harness → `pass=15 fail=0`（exit 0）、`45/45 passed, 0 failed`（exit 0）
  - `wc -l`：`Program.cs` **283** / `Watchers.cs` 140 / `TrayUi.cs` 66 —— 与原 359 相比腾出 76 行，
    Ruling 33 的余量目标达成（计划末尾估约 346，距 360 有 14 行）
  - **产物真的换了吗**（§六 的教训）：exe 里 `Watchers`/`TrayUi`/`GeometryQueried` 各命中 1
  - 零全局钩子：`SetWindowsHookEx|WH_KEYBOARD_LL` 零命中 ✅
Task 1: **教训 —— 我自己的 C# 5 扫描正则说谎了，查出来 6 处"违规"全是假阳性。**
  首次用 `out (int|uint|string|bool|byte|short) ` 扫，命中 6 处：其中 4 处是**参数声明**
  （`out uint pid`、`out string stdout`、`out int w, out int h` —— C# 1 就合法，不是内联声明），
  2 处是**注释文字**（`Suppressor.cs:67` 正写着"本机 csc 不支持内联 out"、`RawInput.cs:141`
  写着"C# 5 没有 ?."）。收紧为"只匹配**调用点**的内联 out"（`\.[A-Za-z_]\w*\(\s*out\s+<type>\s+<name>`）
  与"要求 `?.` 前是标识符"（避开注释里的空格+`?.`）后重扫：**四类全 0 命中**。
  **这正是本项目反复栽的"诊断仪器说谎"**——若照首次结果报出去，就是一次彻头彻尾的假警报。
  已写进下面的复核清单：**C# 5 扫描必须区分"参数声明"与"调用点内联声明"，且必须能排除注释。**
Task 1: review package 有两个版本，取后者：
  `review-ae78fdc..ccf32a7.diff`（2 commits —— 混进了我在派发后插的纯文档提交 339115f）
  → 按 ledger 既有做法取 `review-339115f..ccf32a7.diff`（**1 commit**，18732 B）。
  这是簿记修正、非规格变更（阶段二 Task 6 也做过同样的事）。
Task 1: dispatched **task reviewer（sonnet）** —— 逐字搬迁属中等风险，但它是后 7 个任务的地基。
  除 Global Constraints 原文外，点名了"纯搬迁这一类形状"的结构性风险面（订阅被丢或重复、
  定时器间隔/条件在转录中改变、退出路径的顺序变化、新增的 `Stop()` 在定时器仍可能 tick 时
  从 `ApplicationExit` 被调用、以及任何"原来读捕获的局部量、现在读字段"的地方）。
  **刻意没有**告诉它任何"这不算缺陷"——避免预判（skill 明令）。
Task 1: **review 回来 —— Spec ✅ 合规 / Task quality Approved**，0 Critical、**1 Important**、4 Minor。
  审查者逐行核了等价表 12 行（全 PASS）、五项点名风险（订阅无丢无重、常量逐字、退出顺序无变、
  字段与原捕获局部量同对象、`_lastPongTicks` 无脑裂）、以及 De Morgan 等价性。
  它还独立确认了承重声明位置（`watchers` 在 `TrayUi.Install()` 之前、`Start()` 在 `Application.Run` 之前）。
Task 1: **Important 1（plan-mandated）= 打的是我自己加的那行日志。** 原话要点：
  新增的 `# 检测到几何变化` 跑在**几何轮询线程**上直接 `log.WriteLine`，恰好破坏 `Stop()` 想防的风险，
  且违背了这次改动自己写下的线程纪律（`Watchers` 类头承诺几何轮询"绝不直接碰控件或写日志"）。
  具体后果：退出时 `Stop()` 只置 `_stop`、不 Join，于是一次已越过 `if (_stop) return;` 且正卡在
  `QueryDisplay`（adb 调用，最长 5s）里的轮询迭代，可以在 `log.Close()` **之后**抛事件 →
  `log.WriteLine` 抛 `ObjectDisposedException` → **后台线程未捕获异常终结进程**，而不是干净退出。
  改动前那个线程只更新字段，所以这个"写已关闭 writer"的窗口是**本次新增的**。
  **R8 —— 裁决：删掉那行日志，而不是改成 marshal。** 理由：它是我（控制方）自己加的、不是任何
  用户需求；消费者侧本来就会打 `# 几何已应用 WxH`，诊断价值边际很小；删掉才让 Task 1 真正回到
  "零行为变化"，并且让那句类头承诺变成真的。**代价（若判错）**：少一个"几何变化被检测到但用户
  一直没动鼠标所以没应用"的诊断信号——真需要时在**消费点**（`MouseMoved`，UI 线程）补更合适。
Task 1: **R9 —— 把同一条约束前移到 Task 8（那边必然出现第二个后台写者）。**
  Task 8 的 `ReconnectLoop`/`TryRebuildLink` 原本也直接 `_log(...)`，形状与 Important 1 完全相同。
  已改计划：新增 `LogFromWorker()`（后台线程写日志的**唯一通道**，内部 `BeginInvoke` + try/catch），
  `Report()` 改成把去重比较与两处 UI 触碰**整块**放进 `BeginInvoke`（于是 `_lastReport` 只被 UI 线程
  读写），并把静态核对表的两行改成"后台线程有没有直接 `_log(`/`_host.SetStatus` → 一处都不许有"。
  **代价（若判错）**：日志行比直接写晚一拍出现（异步投递），退出瞬间的最后一两条状态可能不落盘
  ——相比"进程崩溃"是可接受的取舍。
  另说明为什么 BeginInvoke 在这里是安全的：消息循环结束后它要么抛（被吞）、要么投递后再也不会被
  泵出，两种情况都不会真写到已关闭的 writer。
Task 1: minor (deferred): `Watchers` 类头提到"重连监督"与 `Report()`，但本次代码里都不存在
  （是 Task 8 的前向引用）——类头当下**高估**了该类的职责。已折进修轮 1（只动注释）。
Task 1: minor (deferred): 计划 Interfaces 里写的是 `NotifyIcon TrayUi.NotifyIcon { get; }`，
  而实际属性名是 `Tray`（与计划自己的代码块一致，故实现是对的）。**已改计划正文**，
  免得 Task 3 按错误的名字编码。
Task 1: minor (deferred): PONG 订阅注册顺序对调了（原来先注册只打日志的那个，现在 Watchers 的时间戳
  订阅在前）——两个处理器彼此独立，无功能影响。
Task 1: minor (deferred): `Program.cs` 里 `using System.Drawing;` 在 `NotifyIcon`/`SystemIcons`
  搬走之后可能已无用（csc 不报未用 using，故"零警告"一致）。留待后续任务碰到该区域时顺手清。
Task 1: fix round 1/5 dispatched（resume 原实现者，**只**带 Important 1 + 折进去的 Minor 1）；
  已同时改计划正文（删掉那行日志、删掉对应的"有意改动"条目、修 `NotifyIcon` 笔误、
  把 R9 落进 Task 8 的 `Report`/`LogFromWorker` 与静态核对表）。
Task 1: fix round 1/5 implementer **DONE** — commit **64c350b**（2 文件 +2/−4，追加提交未 amend）。
  Important 1 按 R8 删除：`GeometryQueried` 处理器现为三行纯字段赋值（比较 → 赋 `_phoneW/_phoneH`
  → 置 `_geometryChanged`），**无任何 I/O**。Minor 1 折进：类头"重连监督/Report()"各补"（任务 8 落地）"。
  自报：`Program.cs` **281**（283−2）；build 零警告 exit 0；两套 harness 15/15 与 45/45，均 exit 0。
Task 1: **控制方独立核实（未采信自述）**：`rg 'log\.WriteLine|_log\(' src/agent/{Watchers,TrayUi}.cs`
  只有 3 处，**全部在 UI 线程上**（`TrayUi.cs:58` 退出路径 / `Watchers.cs:62` 逃逸键（MessageHost 热键）/
  `Watchers.cs:125` 心跳（WinForms Timer tick））；几何轮询后台线程**零写日志点**；
  `GeometryQueried` 处理器实测 3 行无 I/O；`wc -l Program.cs` = 281 ✅。
Task 1: scoped re-review package：`review-ccf74b8..64c350b.diff`（1 commit, 3836 B，同样做了 BASE 簿记修正
  —— 我在 ccf32a7 之后插了文档提交 ccf74b8）。dispatched **scoped re-reviewer（sonnet）**，
  除两条 finding 外点名了一个聚焦检查："删掉那一行是否**完整**堵住竞态，还是仍有别的路径从非 UI 线程写 log"。
Task 1: fix round 1/5 复审 **clean** —— scoped re-reviewer（sonnet）判
  **All findings addressed, no new Critical/Important breakage**。
  两条逐条给出行号证据：①`Program.cs:82-87` 处理器现为纯字段赋值、无 I/O，与改动前行为一致；
  ②`Watchers.cs:15-16` 两处前向引用已补"（任务 8 落地）"，且 diff 显示 Watchers.cs **零代码改动**。
  它另做了一项我点名的聚焦检查：报告里那份 `rg` 的 3 个日志点**确系全在 UI 线程**
  （`TrayUi.cs:58` ApplicationExit / `Watchers.cs:62` 逃逸键走 MessageHost 的 ProcessCmdKey /
  `Watchers.cs:125` WinForms Timer.Tick），故该竞态由本次修复**完整堵住**。
Task 1: minor (deferred) —— **复审者发现的既有隐患（先于本次改动）**：`Program.cs` 仍在 Transport 的
  **后台 accept 线程**上写日志（`Program.cs:262` 的 `# PONG seq=`、`:224/:245` 的 Connected/Disconnected）。
  理论上退出时存在同形状的"写已关闭 writer"窗口，但目前靠退出顺序缓解
  （`TrayUi.cs:53` 的 `_transport.Stop()` 早于 `:63` 的 `_log.Close()`）。
  **本次不修**：它是 Task 1 之前就有的形状，且 Task 1 的章程是零行为变化。
  交终审 triage：要么把这三处也 marshal，要么把退出顺序硬化（`Transport.Stop()` 之后 Join accept 线程）。
  **代价（若不修）**：一条理论性的退出竞态残留；实际触发需要 accept 线程恰在 `log.Close()` 后收到一个
  消息，概率极低，但后果与 R8 那一处相同（进程被终结而非干净退出）。
  **已预判给 Task 8**：`Transport.Stop()` 若在那轮被改动，顺手评估是否加 Join。
Task 1: **complete**（代码提交 `ccf32a7` + 修轮 `64c350b`；派发前 BASE = `ae78fdc`，
  中间夹两个控制方文档提交 `339115f`/`ccf74b8`，故审查包做了两次 BASE 簿记修正）
  —— review clean after 1 fix round。

## Task 2 准备

- 简报已预生成：`task-2-brief.md`（362 行）。
- 注意：Task 1 的教训要随派发下发 —— ①Git Bash 会改写 `/路径` 参数，跑 csc/adb 一律用 PowerShell；
  ②`wc -l` 口径；③新建 `.ps1` 必须带 UTF-8 BOM（Write 工具产出无 BOM，须查前三字节并补）；
  ④**只有 `Watchers`/`TrayUi` 因为吃 `MessageHost` 而必须 internal**，`Config`/`MouseScaler`/
  `SettingsForm` 只吃 public 类型，可以（也应该）是 `public`——别把 internal 规则过度套用。

## 执行记录 · Task 2

Task 2: dispatched implementer (**sonnet** —— 3 新文件 + 新 runner 基建，属"多文件集成"档) at BASE **0cfa1fc**。
  派发时把 Task 1 换来的教训一并下发：①**别过度套用 internal 规则**（只有 `Watchers`/`TrayUi`
  因吃 internal 的 `MessageHost` 才必须 internal；`Config` 只吃 public 类型，该保持 `public`）；
  ②新建 `.ps1` 必须查前三字节并补 BOM；③Git Bash 会改写 `/路径` 参数，csc/pwsh 一律走 PowerShell。
  另下发歧义处置：RED 必须是 `CS0246 找不到 Config` 这一种编译失败；同一问题两次不成即停报 BLOCKED；
  **报告实际用例数**，不要为了凑 8 去改任何东西。
Task 2: implementer **DONE** — commit **014d55a**（4 文件 +274）。
  TDD 证据齐全：RED = `error CS0246: 未能找到类型或命名空间名称"Config"`（Config.cs 尚不存在）；
  GREEN = 同一命令 → C1–C8 全 PASS、`TOTAL: pass=8 fail=0`、exit 0。
  **BOM 陷阱复现并被正确处理**：`run.ps1` 首写出为 `24 45 72`（无 BOM），用
  `[IO.File]::WriteAllText(..., UTF8Encoding($true))` 重写后核实 `ef bb bf`。
  自审对比：`Main.cs`/`Config.cs` 与简报代码块逐字节一致，`run.ps1` 仅差所需 BOM。
Task 2: **控制方独立核实（未采信自述）**：三套 harness 全部复跑 —— `agent-logic` exit 0
  `TOTAL: pass=8 fail=0`、`edge-tracker` exit 0 `pass=15 fail=0`、`scancode-map` exit 0 `45/45`；
  `run.ps1` 前三字节实测 `efbbbf` ✅；**`git diff 0cfa1fc..014d55a -- src/agent/Program.cs` 为空**
  —— 本任务确实没碰 `Program.cs`（仍 281）✅；`wc -l`：`Config.cs` 163 / `Main.cs` 78 / `run.ps1` 31。
Task 2: review package → `review-0cfa1fc..014d55a.diff`（1 commit, 15459 B，**无需 BASE 簿记修正**
  —— 本轮前后没有控制方文档提交）。dispatched **task reviewer（sonnet）**，除 Global Constraints 原文外
  点名了四个聚焦风险面：①"绝不因配置坏掉而启动失败"的契约有没有漏路径（会不会返回半应用的 Config 或抛异常）；
  ②InvariantCulture 的写读往返是否自洽；③新 runner 的"源文件不存在则跳过"过滤是不是简报的本意
  （后续任务才加 `MouseScaler.cs`）还是有静默弱化构建；④`tests/README.md` 加入第三套 harness 后是否自相矛盾。
  **未预判任何结论**。
Task 2: minor (deferred) —— **我自己又踩了 Ruling 12**：独立核实那一步我用 Git Bash 跑
  `pwsh -File G:\...\run.ps1`，拿到的是乱码报错。改用 PowerShell 工具后三套全绿。
  这条已在本项目 ledger 里记过一次（阶段二 Ruling 12），**我这次仍然犯了**——说明"写在文档里"
  不足以阻止复发。对策：把"跑 pwsh/csc/adb 一律用 PowerShell 工具"写进本工作区的派发模板级提醒。
Task 2: **review 回来 —— Spec ✅ 合规 / Task quality Approved**；**0 Critical、0 Important、5 Minor**。
  审查者逐条核了硬约束（C# 5 的 out 全是先声明后使用、phase-2 等值默认、无模态框/无异常逃逸的
  启动保证、首次运行不建文件、Save 带 BOM、InvariantCulture 写读往返），并对四个聚焦风险面各跑了一次检查。
  关键结论：`Parse` 无任何抛出路径（`eq <= 0` 同时挡住 -1 与空键；`Substring(eq+1)` 不会越界）；
  某个键解析失败**只影响该键**、已解析的键原样保留——这正是 C4「不牵连」契约要求的形状，不是半应用损坏。
Task 2: **按流程 Minor 不进修轮** —— Task 2 直接 **complete**（提交 `014d55a`，review clean，无需修轮）。
Task 2: minor (deferred): `tests/README.md` 的"两个脚本都会…"在加入第三套后仍有"两个"字样
  （跑法区块已列了三条命令）。一个词的事。
Task 2: minor (deferred) → **转交 Task 4**：`tests/agent-logic/Main.cs:52-53` 的 **C5 实际上没有钉住
  InvariantCulture**。本机 locale 是 zh-CN、小数分隔符就是句点，所以一个依赖 locale 的 `double.TryParse`
  同样会通过 C5；将来有人删掉 `CultureInfo.InvariantCulture` 参数，本机套件仍然全绿（而换成逗号小数
  地区的用户会静默回退默认值）。**这是计划里我自己写的用例**，非实现者之过。
  处置：Task 4 本来就要往同一个 `Main.cs` 追加 S1–S8，**顺手把 C5 改成有判别力的形式**
  （解析前把 `Thread.CurrentThread.CurrentCulture` 设成逗号小数地区，如 `de-DE`；C# 5 合法）。
  **代价（若不改）**：一条回归保不住——症状是"某些地区设置下配置静默失效"，很难查。
Task 2: minor (deferred): `Save` 把灵敏度静默四舍五入到两位小数（`Parse` 接受 `1.234`，`Save` 写成 `1.23`）。
  一分钱的漂移、实际不可观测，但往返不自洽。属计划写死的代码。
Task 2: minor (deferred): `tests/agent-logic/Main.cs:3` 的 `using System.IO;` 未使用（简报逐字如此，无害）。
Task 2: minor (deferred): `run.ps1` 的"文件不存在则跳过"过滤是计划授予的自由度；长期看它**静默容忍
  多余文件的缺失**（`KeyMap.cs` 目前被编进来但零测试引用它）。计划本意，仅记录。
Task 2: **complete**（提交 `014d55a`，review clean，无需修轮）。BASE 前后无控制方文档提交，审查包无簿记修正。

## 执行记录 · Task 3

Task 3: dispatched implementer (**sonnet**) at BASE **29ce7e1**。派发随附 Task 1/2 的接口事实
  （`TrayUi` 是 `internal class`、构造签名、属性名是 **`Tray`** 不是 `NotifyIcon`、`Install()` 现状；
  `SettingsForm` 应为 public；`Config.Load` 必须放在 `log` 创建之后、`supp` 之前——因为 T4 的
  `scaler`、T5 的 `edgeX`、`TrayUi` 构造都要用它，而 C# 局部量不能前向引用）。
Task 3: **R10 —— 计划里的 Step 5「启动 exe 手工看托盘/对话框」作废，改为机器可核的手段。**
  三条理由：①**子代理看不见也点不了 GUI**；②手机不在线时 `DeviceLauncher.Prepare` 失败会弹
  **模态 MessageBox 永久阻塞**（本项目有记录的同类事故，交接 §六 也提过）；③跑起来的 app 会锁住
  `dist\pc-kvm.exe`，**后面 4 个任务全都编不了**——这是最硬的一条。
  处置：GUI 那几项**并入 Task 4/5 的真机验收**（那时用户本来就在场，而设置对话框正是给速度与
  手机侧用的），实现者不启动 exe。
Task 3: **R10 顺带补上一个 spec 缺口** —— spec §11 明写 `Config` 要覆盖「**写回往返**」，
  而计划里的 C1–C8 **完全没碰 `Save()`**（只有 `Parse` 被测）。故本任务追加 C9（Save→Load 四项往返，
  自包含在 `%TEMP%\pckvm-tests\agent-logic\`，测完自删）与 C10（Save 写出的文件前三字节是
  `EF BB BF`、且文本含 `MouseSensitivity=1.25` 与 `PhoneSide=Left`）。runner 计数 8→10。
  **代价（若判错）**：两条用例把 `Save()` 的实现细节（F2 格式、键名拼写）钉住了，
  将来改 ini 格式要同时改用例——这正是回归测试该有的样子，可接受。
Task 3: minor (deferred) → **转交 Task 4**（那轮同样编辑 `tests/agent-logic/Main.cs`）：
  ①Task 2 审查指出的 **C5 没钉住 InvariantCulture**（本机 zh-CN 小数分隔符就是句点，
  删掉 `InvariantCulture` 参数套件仍全绿）；
  ②`tests/README.md` 的跑法区块仍有"两个脚本"字样，而现在是三套——一个词的事。
  两条都在 Task 4 触碰的同一个子系统内，一并处理比单开修轮划算。
