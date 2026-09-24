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
Task 3: implementer **DONE** — commit **b29ad71**（4 文件 +213/−4）。`Program.cs` **281 → 294**。
  agent-logic `pass=10 fail=0`（C9/C10 落地）、edge-tracker `pass=15 fail=0`、scancode-map `45/45`；
  build 零警告 exit 0。
Task 3: **控制方独立核实（未采信自述）**：自己重编 → `编译成功`、**14 个源文件**、exit 0、37.0 KB；
  三套 harness 复跑全绿；`rg 'MenuItem' src/agent/TrayUi.cs` → **恰两个**（`:54` 设置…、`:69` 退出）；
  `rg 'Config.Load|SettingsApplied'` → `Program.cs:47` 一处 Load（位置正确：在 `log` 创建之后、
  `supp` 之前，后续 `scaler`/`edgeX`/`TrayUi` 才用得上）、事件声明一处（`TrayUi.cs:32`）、
  订阅一处（`Program.cs:275`）；`wc -l`：`Program.cs` 294 / `SettingsForm.cs` 139 / `TrayUi.cs` 87 /
  `Main.cs` 114。
Task 3: **行数预算跟踪（R1/Ruling 33 的预测兑现情况）**：Task 3 用掉 +13；剩余 T4/T5/T6/T7/T8
  估约 +43 → 计划末尾约 **337/360**，余量 23。目前预测成立，无需再抽。
Task 3: implementer 自报三处刻意偏离，逐条初评（终评交审查者）：
  ①**CS0108**：简报逐字的 `void Capture()` 与 WinForms 的 `Control.Capture` 撞名，与简报自己的
    "零警告"要求冲突；实现者按编译器建议加 `new`（`new void Capture()`），语义不变。
    ⚠️ **我的初判：`new` 能消警告，但留下一个遮蔽 `Control.Capture` 的成员**——对 `Form` 子类来说
    这仍是个陷阱（将来有人写 `this.Capture` 会撞上意外的绑定）。更干净的是**改名**（如 `ReadControls`）。
    属计划笔误（名字是我定的）。交审查者判定，若它也认为该改名则进一次修轮。
  ②**简报的 `git add` 只列了 2 个文件**，而 Step 2 还要改 `TrayUi.cs`、R10 又加了测试用例——
    只提交 2 个会让工作树坏掉。实现者提交了全部 4 个改动文件，**判断正确**。计划笔误，我改。
  ③测试 ini 的清理放在 C10 末尾（C10 消费 C9 写的文件）。能跑通，但把两条用例耦合了；
    若 C9 中途失败则产物留存。轻微，交审查者看。
Task 3: review package → `review-59d12e4..b29ad71.diff`（1 commit, 15598 B，做过 BASE 簿记修正）。
  dispatched **task reviewer（sonnet）**，点名四个风险面（`new Capture` 的处置是否恰当、
  `cfg` 的声明位置、C9/C10 的断言与清理、以及 **R10 之下对话框的交互行为根本没有被本任务验证**
  ——要它把这条作为 ⚠️ 明说，而不是拿机器检查当覆盖）。**未预判结论**。
Task 3: **review 回来 —— Spec ✅ 合规 / Task quality Approved**；**0 Critical、0 Important、2 Minor**。
  审查者逐条核了：`SettingsForm` 是 public、`TrayUi` 严格限于"对话框+持久化"（运行时应用在
  `Program.cs` 的订阅者里）、`cfg` 的声明位置对**所有**后续消费者都安全、C# 5 合规、
  两个既有 harness 未被触碰、C9 确实断言了四项往返、C10 断言了 BOM 与键值。
  它还额外查了一件我没点名的事：`run.ps1` 每次运行前会 `Remove-Item -Recurse -Force` 输出目录，
  所以即使测试进程在 C9 与 C10 之间死掉，重跑也是干净的——C9/C10 的清理耦合因此不构成风险。
Task 3: ⚠️ 审查者如实标出两项**无法从 diff 验证**（我已确认，均非缺口）：
  ①对话框的**交互行为**（R10 决定移交用户）——机器检查不覆盖它，用户在有设备时应走一遍那份清单；
  ②零警告构建是报告里的声明，它没有重跑 csc（我已独立重编核实过：14 文件、exit 0、零警告）。
Task 3: **按流程 Minor 不进修轮 → Task 3 complete**（提交 `b29ad71`，review clean，无需修轮）。
Task 3: minor (deferred) → **parked 交终审（我与审查者有分歧，两侧都记下）**：
  `SettingsForm.cs:214` 的 `new void Capture()` 永久遮蔽了 `Control.Capture`（WinForms 的鼠标捕获属性）；
  类内裸写 `Capture`（不带括号）仍指向那个属性——将来若需要在本窗体上做鼠标捕获就是陷阱。
  **审查者判**：`new` 是编译器自己的建议、偏差只有一关键字 + 两行注释、调用点无歧义（bool 属性不可调用），
  「改名更干净但偏离逐字代码更多」，故 Minor。
  **我的不同意见**：那份"逐字代码"是**我写的**（名字是我定的），所以"偏离"不是有效理由；改名（如 `ReadBack`）
  能彻底消除遮蔽，而 `new` 只是消掉警告、把陷阱留在原地。
  **处置**：它是 Minor、且审查已 Approved，故不为其单开修轮；**parked 交终审**，由终审决定改名还是保留。
  **代价（若保留）**：一个低概率陷阱（只有将来在这个设置对话框上需要鼠标捕获时才会咬人）。
Task 3: minor (deferred): `tests/agent-logic/Main.cs` 的 C10 用 `catch (Exception) { }` 丢掉了失败原因，
  失败时只看到 `bom=False keys=False`、看不出是文件缺失还是被占用。非阻塞（C9 同进程写它）。
Task 3: 计划笔误已修：Task 3 Step 6 的 `git add` 初稿只列两个文件（会提交出编译不过的树）——
  实现者拦下并改用全部四个；计划正文已改为四个并注明原因。

## 执行记录 · Task 4

Task 4: dispatched implementer (**sonnet**) at BASE **69d12fe**。随附 Task 1–3 的接口事实
  （`cfg` 已在 `Program.cs:47` 声明且不许挪；`SettingsApplied` 在 `Program.cs:275` 已有**唯一**订阅，
  要**扩展**它而不是再加一个；`Main.cs` 现有 10 条用例、新用例要插在 `TOTAL:` 行之前；
  `MouseScaler` 只吃 public 类型故应为 public）。
Task 4: **R11 —— 不许启动 `dist\pc-kvm.exe`，真机的手感验收移交用户。**
  三条理由：①跑起来会锁 `dist\pc-kvm.exe`，**Task 5–8 全都构建不了**；②手机目前**离线**，
  启动会弹模态框永久阻塞；③子代理感受不到鼠标手感。
  处置：实现者的验收 = build + 三套 harness + 行数 + 三条静态核对；**运行时验收并入一次汇总的真机
  会话**（连同 R10 移交的 Task 3 对话框清单）。
Task 4: 派发里点名一处**承重的语义**要它自查：虚拟光标模型与发给手机的增量**必须同时按缩放后的值**
  前进（`cursor.NextDx(scaler.ApplyX(e.Dx))`），而**回程判定必须继续用原始增量**
  （`tracker.OnTakeoverMove(e.Dx, e.Dy, ...)`）——两者一旦混用，模型就会与手机真实光标漂移。
Task 4: 两条前序 deferred 转交本任务（都在它要编辑的文件里）：
  ①**C5 改成有判别力**（解析前把 `Thread.CurrentThread.CurrentCulture` 设成逗号小数地区如 `de-DE`，
  再恢复）——否则删掉 `InvariantCulture` 参数本机套件仍全绿；
  ②`tests/README.md` 跑法区块的"两个脚本"改成三套。
  这在流程上对应 skill 的"If an earlier task parked a finding in the area this task touches,
  carry a pointer to that ledger entry in the dispatch"。
Task 4: implementer **DONE** — 两个提交：**d7099a0**（feat #1）+ **c0adf03**（docs tests 计数 两个→三个）。
  agent-logic `pass=18 fail=0`（**总数精确命中预期**）、edge-tracker `pass=15 fail=0`、
  scancode-map `45/45`；build 零警告；`Program.cs` **294 → 298**。
Task 4: **C5 的修复带了负对照（值得记一笔）**：实现者把 harness 复制到 `%TEMP%`、**删掉
  `Config.Parse` 的 `InvariantCulture` 参数**，观察到 `FAIL C5 sens=0.5` —— 这就**证明了这条用例
  现在真有判别力**，而不只是"改了看着对"。这正是本项目"控制方独立复算/不采信自述"的精神，
  被实现者主动用上了。计划里那条 Minor 至此真正关闭。
Task 4: **控制方独立核实（未采信自述）**：自己重编 → `编译成功`、**15 个源文件**、exit 0、37.5 KB；
  三套 harness 复跑 `18/15/45` 全绿；`rg 'sensitivity' src/agent/` → **`Program.cs` 里那处裸局部量已消失**，
  剩下的全在 `MouseScaler.cs` 内部（字段/构造参数/方法参数/注释）；
  `scaler.` 的用法实测为 `:116 Reset()`（在 EnterTakeover 内）、`:168-169 ApplyX/ApplyY`（在 MouseMoved 内）、
  `:280 SetSensitivity`（在 SettingsApplied 处理器内）—— **位置全部正确**；
  `SettingsApplied += ` 计数 = **1**（未新增第二个订阅）；
  `wc -l`：`Program.cs` 298 / `MouseScaler.cs` 56。
Task 4: **行数预算**：已用 298；剩 T5/T6/T7/T8 估约 +30 → 末尾约 328/360，余量 32。
Task 4: implementer 自报一条 minor（终评交审查者）：**几何变化路径（接管中旋转）没有 `scaler.Reset()`**，
  只有 `EnterTakeover` 有 —— 属简报设计（Step 5 只接了 EnterTakeover）。残余 <1px，靠累积自闭合。
Task 4: review package → `review-69d12fe..c0adf03.diff`（**2 commits**，15835 B —— 两个都是实现者的提交，
  故**无需** BASE 簿记修正）。dispatched **task reviewer（sonnet）**，点名五个风险面：
  ①小数余量的符号对称/截断方向/200 次不漂移；②**缩放值与原始值的那条分界线**（模型与上行用缩放、
  回程判定用原始）要核两个调用点；③几何变化路径不清余量是否会真造成可见漂移；④中途改系数是否有状态不一致；
  ⑤C5 的 culture 切换是否真能判别、且**每条路径都恢复了原 culture**。另要求把"真机手感验收已按 R11 移交用户"
  作为 ⚠️ 明说。**未预判结论**。
Task 4: **review 回来 —— Spec ✅ 合规 / Task quality Approved**；**0 Critical、0 Important、4 Minor**。
  审查者做了五项定点核查：**手算**了余量算术（`Truncate` 对负方向对称、200×dx=1@0.5 恰好 100 像素
  无漂移、余量恒落在 (−1,1) 故不可能溢出）；确认**缩放值与原始值的分界线在两个调用点都正确**
  （`:143-144` 缩放上行、`:149` 原始判回程）；读了 diff 之外的 `Program.cs:149-157` 确认几何变化路径
  确实没调 `scaler.Reset()` 并判断"真·亚像素且自闭合，保留余量其实更正确（用户的手确实动了那些 mickey）"；
  确认 `SetSensitivity` 中途改值不会造成状态不一致；**逐条论证了 C5 的 culture 切换为什么真的能判别**
  （de-DE 会把 "0.35" 读成 35 → 越界回退 0.50，与它报告的 `FAIL C5 sens=0.5` 一致）。
  它还核了 S1–S8 是否"构造即真"：S2 在旧实现下会是 0/0/0、S3 在 `Math.Floor` 下会是 −1/−1、
  S6 在 Reset 不清残差时必红、S8 钉住闭合 —— 结论是这套用例**有判别力**。
Task 4: **按流程 Minor 不进修轮 → Task 4 complete**（提交 `d7099a0` + `c0adf03`，review clean）。
Task 4: minor (deferred) → **parked 交终审（附我的倾向）**：`MouseScaler.cs:52` 的 `Reset()` 文档注释写
  "进入接管/**几何变化**时清零残差"，但只有 `EnterTakeover` 真的调它，几何变化那条路径没有——
  **注释夸大了接线**。文本是简报逐字的（我写的），但未来读者看到的是代码。
  **我的倾向**：修注释（或顺手在几何路径也接上，虽非必需）。**理由不是洁癖**：本项目已经因为
  一句**写错的注释**（`ScancodeMap` 那句"NumLock 关时发 E0 前缀"）让一整个任务建立在错误假设上。
  **代价（若保留）**：未来读者可能以为几何变化会清余量，据此做判断。
Task 4: minor (deferred): C5 的 culture 恢复没放在 `try/finally` 里（`Config.Parse` 从不抛，故无害，
  但 `finally` 能让不变量对未来改动更显式）。
Task 4: minor (deferred) → **Task 5 会自然消掉**：`Program.cs:276` 的注释"速度在 Task 4 接（MouseScaler）"
  现在读起来像在做完的事上留 TODO —— Task 5 本来就要整体重写那个 `SettingsApplied` 处理器。
Task 4: minor (deferred): 对二进制不可精确表示的因素（0.2、0.7），边界那一像素可能早一拍或晚一拍
  （double 舍入），且没有用例钉住非二进制可表示的因素。余量从不丢弃故误差有界在 1 像素内并自闭合，
  纯装饰性观察。
Task 4: **complete**（提交 `d7099a0` + `c0adf03`，review clean）。

## 执行记录 · Task 5

Task 5: dispatched implementer (**sonnet**) at BASE **1cf954f**。除 Task 1–4 的接口事实外，派发里
  特意**把计划文字换成了我亲自核过的源码语义**（因为这一步的边界算术错一位就是"入口永远不可达"
  或"回程方向反了"这类静默失效）：
  - `OnIdleMove` 的 `cursorY >= _edgeBottom` 判定 ⇒ **`edgeBottom` 是开区间**，传 `SM_CYVIRTUALSCREEN` 正确；
  - 右侧入口是 `cursorX >= _edgeX` 且桌面 3840 时光标最远到 3839 ⇒ **`edgeX = screenW - 1`**；
  - 左侧入口是 `cursorX <= _edgeX` ⇒ **`edgeX = 0`**；安全带左右对称（左用 `cursorX > _edgeX + 12`）。
  另明确：`EdgeTracker` 里 `_phoneW/_phoneH` **本来就是非 readonly**（`SetPhoneSize` 会改它们），
  **只有四个** edge 字段需要去掉 `readonly` —— 免得实现者按"四个字段"的字面去找错对象。
Task 5: 派发里点名那条**为什么不许重建 tracker**：`EnterTakeover`/`LeaveTakeover` 的订阅挂在对象上，
  重建会**静默丢订阅**。要求实现者不得"简化"成重建。
Task 5: 要求实现者**用读代码而不是假设**的方式自查左右镜像：`SetEdge` 之后，左侧的入口点、
  安全带、比例映射（`phoneX = _phoneW - 1`）与回程累积是否都是右侧的严格镜像；
  若哪条 L 用例只是"碰巧通过"，要说出来。
Task 5: R11 继续生效（不许启动 exe）；**A3（接管中旋转时邻接边是否翻转）的真机横竖屏判据
  随本任务的 Step 8 一并移交用户**，并入那一次汇总的真机会话。

Task 5: implementer **DONE_WITH_CONCERNS** — commit **821fa8f**（3 文件 +154/−14）。
  edge-tracker `TOTAL: pass=24 fail=0`（15 + 9 条 L）、scancode-map `45/45`、agent-logic `pass=18`；
  build 零警告；`Program.cs` **298 → 324**（≤360）；`EdgeTracker.cs` 175。
Task 5: **concern 1 又是计划里的真 bug（值得记）**：我给 L4/L5 写的 harness 代码**漏了
  `c.SetPosition(3199, 1068)`** —— 入屏后 `CursorModel` 仍停在初值 (0,0)，而左挂入屏点是 x=3199，
  于是 4 条用例失败（`pass=20 fail=4`）。实现者补上这句后转绿。
  **它另指出一件更值得记的事**：右侧 T 用例之所以不需要这句，**只是碰巧**——入屏点 x=0 恰好等于
  `CursorModel` 的初值；而 `Program.cs` 在 `EnterTakeover` 里是**真的**做了 `Reset()` + `SetPosition(px,py)`。
  计划正文已就地修正（L4/L5 各补一句 `c.SetPosition(3199, 1068)` 并注明原因）。
  **教训**：harness 必须复刻接线顺序，不能依赖"初值刚好对"——这与阶段二 T1 用 `cursorX=3840`
  那个不可达坐标是同一类错（"用实现者的心智模型当测试输入"）。
Task 5: minor (deferred): 既有右侧 T 用例同样依赖"初值刚好等于入屏点"这一巧合。
  它们目前是正确的，但脆弱：若哪天 `CursorModel` 的初值变了（或入屏点变了），T 用例会静默变成
  在测别的东西。可考虑给 T 用例也补显式 `SetPosition`。交终审 triage。
Task 5: concern 2（`SetEdge` 无边界守卫）：调用方都传推导/守卫过的值，`OnIdleMove` 对垃圾边界
  也能安全退化（不会崩、只会判不出边缘）。实现者未采取行动，交审查者判定。
Task 5: concern 3（agent-logic 是 18 不是简报写的 16）：属 Task 3 加 C9/C10 后的账目漂移，与本事无关。
Task 5: review package → `review-1cf954f..821fa8f.diff`（1 commit —— 实现者只有一个提交，无簿记修正需求）。
Task 5: **review 回来 —— Spec ✅ 合规 / Task quality Approved**；0 Critical、**1 Important**、4 Minor。
  审查者做的事：walks 了 `EdgeTracker` 全部**六个** `_phoneRight` 分支（它为了这个把整个文件读了，
  因为 diff 的上下文不含那些行）并逐条判定左侧是右侧的**严格符号/关系镜像**；把九个 L 用例逐条对上分支，
  结论"无一条靠构造通过"（并用中间那次 `pass=20 fail=4` 作为判别力证据）；
  确认 `SetEdge` **不重建**、订阅存活；确认 `screenW/screenH` 在**两个**消费者（构造与处理器闭包）
  之前声明、无前向引用；确认 `SetEdge` 的解除武装既不"吸人"（L6b 钉住）也不"困人"
  （`OnIdleMove` 在光标移出 ±12 安全带后会重新武装）；确认 `_lastVx` 在换边后残留无害
  （只在 Takeover 里读，且下次入屏会重新播种，何况处理器已先强制退出接管）。
  它**独立核实**了 L4/L5 那个偏差的事实基础（`CursorModel.cs:20-21` 停在 (0,0)、
  `Program.cs:130-131` 真的 `Reset()`+`SetPosition`），判为**正确且是改进**。
Task 5: **Important 1（plan-mandated）= 又是我写的设计缺口**：入口边推导**只用尺寸、没用原点**。
  原码 `edgeX = cfg.PhoneOnLeft ? 0 : screenW - 1` 只读 `SM_CX/CYVIRTUALSCREEN`，把原点默认成 (0,0)。
  **任何把显示器挂在主屏左侧或上方的布局**都会让 `SM_X/YVIRTUALSCREEN` 非零 —— 此时左边缘应是
  `SM_X` 而非 0、右边缘应是 `SM_X + W - 1` 而非 `W - 1`，且 `cursorX >= W - 1` **可能永远不可达**。
  **讽刺之处**：这正是我亲手写在那一行旁边注释里的失效（"写死 3839 时，换一套显示器布局会让入口
  条件永远不可达"）—— 我只堵了**尺寸**变化、没堵**原点**变化。**设计层面，非实现者之过。**
Task 5: **R12 —— 裁决：现在就修，不 park。** 理由：①它是 **Important** 不是 Minor，按规则默认进循环；
  ②修复只约 6 行，且直接实现代码注释与 spec 动机已写明的原则；③留成潜在静默 bug 等于"为布局健壮性
  做的功能在一种常见布局下仍然失效"；④代价是一次小修轮。
  处置：加 `SM_XVIRTUALSCREEN`(76)/`SM_YVIRTUALSCREEN`(77) 两个常量，改
  `edgeX: 手机在左 ? screenX : screenX + screenW - 1`、`edgeTop: screenY`、
  `edgeBottom: screenY + screenH`（**原点非零时原来那个 `0`/`screenH` 同样错**），
  `SetEdge` 同步；日志补打 `x= y=` 以便真机问题一眼可诊断。
  **一处判据也要点明**：尺寸是判成败的判据（必须为正），而**原点允许为负**（左侧挂屏就是负的），
  所以原点**不能**拿 `<= 0` 当失败——只有尺寸坏了才整体回退，且回退时原点必须归零。
  **代价（若判错）**：多读两个 system metric、多约 8 行；`Program.cs` 预算仍充裕。
Task 5: minor (deferred): 实时改显示器布局时 `screenW/screenH` 是启动时快照，`SettingsApplied` 闭包沿用旧值；
  在处理器内部重读 `GetSystemMetrics` 就能让换边自纠正。非阻塞（要重启才生效，与现状一致）。
Task 5: minor (deferred): **左侧的重新武装路径没有被钉住** —— L6b 只验了"光标停在新边缘时进不去"，
  没有覆盖"向右移出安全带 → `Armed=true` → 再向左推能进"。补一条用例即可闭合。
Task 5: minor (deferred): `SetEdge` 没有边界守卫，而 `SetPhoneSize` 有。同意实现者的不动作：
  两个调用点都传推导/守卫过的值，`OnIdleMove` 对垃圾边界也只是"永不触发"而不崩。仅当出现第三个
  调用点时再考虑加守卫。
Task 5: minor (deferred): 右侧 T 用例仍依赖"`CursorModel` 初值 (0,0) 恰等于入屏点"这一巧合
  （虽在 L4 的注释里写明了，但 T 用例本身没改）。它们目前正确但脆弱；后续清理时给 T 用例也补显式
  `SetPosition(0, y)` 即可消除。
Task 5: fix round 1/5 dispatched（resume 原实现者，只带 Important 1）。
  派发里额外要求它**自查一处连带语义**：`edgeTop` 从 `0` 变成 `screenY` 之后，入屏的比例映射
  （`span = _edgeBottom - _edgeTop`、`phoneY = (cursorY - _edgeTop) * _phoneH / span`）
  在原点非零时是否仍正确 —— 这是我改动的连带面，不让它默认没事。
  计划正文已就地修正（三个常量 + 两处推导 + 那段"为什么原点也要读"的注释）。

Task 5: **控制方自己的失误（记一笔，防复发）**：提交 `5802635` 的信息末尾被塞进了两行 shell 噪音
  （一个多余的 `"` 与一句 `echo ...; git log ...`）——我在同一条命令里用了**两个 heredoc**
  （先给 ledger 追写、再给 commit 传信息），嵌套把第二个 heredoc 的终止符吃错了。
  **决定不改写**：`--amend` 虽然技术上安全（未推送、纯信息变更），但**Task 5 的实现者此刻正在同一
  仓库里改 `Program.cs`**，重写 HEAD 等于与活跃写入者抢索引——正是本项目一贯要避免的协调风险，
  两行噪音不值得冒。**对策**：提交信息改用单条 `-m "多行字符串"`，不再用嵌套 heredoc。
Task 5: fix round 1/5 implementer **DONE** — commit **47cbadc**（+20/−10，追加提交未 amend）。
  四常量齐全（新增 `SM_X=76` / `SM_Y=77`）；几何块同时读原点；**尺寸为正才判活、原点允许为负、
  尺寸坏才整体回退且回退时原点归零**；构造（`:102-103`）与 `SetEdge`（`:314-315`）两处都改为原点感知；
  `edgeTop = screenY`、`edgeBottom = screenY + screenH`（保持开区间）。
  **它还多做了一件我没要求的**：日志里那个 `edgeX=` 也改成原点感知值（`:317`），免得日志与实际边界不一致
  ——这正是本项目"日志要能定位问题"的纪律。
  语义自查（我要求的第 5 项）：比例映射仍正确 —— `span = screenY+screenH-screenY = screenH` 不变，
  纵向频带闸已保证 `cursorY - edgeTop ∈ [0, screenH-1]`，与原点为零时是同一个"桌顶相对偏移"。
Task 5: **控制方独立核实（未采信自述）**：重编 → 15 文件、exit 0、38.5 KB；三套 harness `18/24/45` 全绿；
  `rg` 实测原点四常量在 `:21-22`、读取 `:87-88`、回退归零 `:95`、日志 `:97`、构造 `:102-103`、
  `SetEdge` `:314-315`、日志 `:317` **七处齐全**；`edgeTop|edgeBottom` 无残留的裸 `0`/`screenH`
  （唯一命中是 `:85` 那句解释开区间的注释）；`Program.cs` **334**。
Task 5: scoped re-review package → `review-b5235ca..47cbadc.diff`（1 commit, 6369 B；BASE 取了
  `b5235ca` 而非 `821fa8f` —— 中间夹了我的三个文档提交，又一一次 BASE 簿记修正）。
  dispatched **scoped re-reviewer（sonnet）**，除 finding 本身外点名两个聚焦检查：
  ①`edgeTop` 不再是 0 之后比例映射是否仍成立（要它对着代码验证实现者的论证，而不是接受）；
  ②原点是否**真的允许为负**（有没有哪个 `<= 0` 判断或 int 假设会把负原点静默钳成 0）。
Task 5: fix round 1/5 复审 **clean** —— scoped re-reviewer（sonnet）判
  **All findings addressed, no new Critical/Important breakage**。
  它逐处给了行号（四常量 `:21-22`、读取 `:87-88`、回退 `:93/:95`、构造 `:102-103`、
  `SetEdge` `:314-315`、日志 `:97-99`），并确认**判据"只测尺寸"被遵守**（`screenX/screenY` 从不被 `<= 0` 测，
  `GetSystemMetrics` 返回有符号 `int` 故负原点原样流过）。
  两项我点名的聚焦检查它都做了、且是**对着代码做的**：
  ①比例映射的原点无关性——它自己追了 `EdgeTracker.cs:107` 的频带闸与 `:124-125` 的算式，
    确认 `span` 恒为 `screenH`、分子恒落在 `[0, screenH-1]`，故**构造上就与原点无关**，
    既有夹具（原点 0）仍是同一映射的有效测试；
  ②确认无 `<= 0` 判断、无无符号转换、无钳制触及 `screenX/screenY`。
  **按流程 → Task 5 complete**（代码提交 `821fa8f` + 修轮 `47cbadc`，review clean after 1 fix round）。
Task 5: minor (deferred，复审者提出，**与本轮无关**): `Program.cs:87-90` 的虚拟桌面几何是**启动时快照**，
  `SettingsApplied`（`:314`）沿用那批捕获值 —— 运行中改显示器布局会让 `SetEdge` 用旧边界。
  这是**原实现就有的形态**（构造处也是一次性读取），本轮原点修复没有改变它。
  一个观察：**本轮没有端到端覆盖非零原点**（夹具全是原点 0）。可接受：`EdgeTracker` 设计上就与原点无关，
  而原点逻辑住在**不可离线测的 `Program.cs`** 里（P/Invoke）。记录在案、无需动作。

## 执行记录 · Task 6

Task 6: dispatched implementer (**sonnet**) at BASE **30358ad**（`Program.cs` 334）。派发要点：
  - **纠正一处会走错门的定位**：要替换的托盘那行 `Tray.Icon = SystemIcons.Application;` 在
    **`TrayUi.cs` 的 `Install()` 里**（Task 1 搬过去的），**不在 `Program.cs`**——免得实现者在错文件里找。
  - **BOM 陷阱前置**：`build-agent.ps1` **本来带 BOM**，编辑它有丢掉 BOM 的风险；要求**改完必查前三字节**、
    必要时用 `[IO.File]::WriteAllText(..., UTF8Encoding($true))` 补回。本项目已被此坑咬过两次，故写成本轮
    的**必做步骤**而非"注意事项"。
  - **零视觉的应对（本轮的设计点）**：实现者没有视觉、代码审查者也没有，而用户要的是"**美化一点的**"
    —— 纯结构检查（ICO 头、图像数、能被 `ExtractAssociatedIcon` 取出）**证明不了好不好看**。
    故要求它额外产出两张**预览 PNG** 并入库：`assets/pc-kvm-preview-256.png`（原始 256）
    与 `assets/pc-kvm-preview-16.png`（16×16 **用最近邻放大到 256**，因为 16×16 原尺寸没法判读）。
    用途：**控制方随后可以派有视觉能力的代理去看这两张图**，把"好不好看"变成一次可执行的检查，
    而不是只能交给用户。最后一关仍是用户的肉眼（并入汇总真机会话）。
  - 四项机器验证写死：①ICO 头 `00 00 01 00` + 图像数（应 ≥6）；②`ExtractAssociatedIcon(dist\pc-kvm.exe)`
    的尺寸非零（防"其实还是 csc 默认图标"）；③`build-agent.ps1` 前三字节仍是 `ef bb bf`；④两张预览 PNG 存在且尺寸对。
  - 明确要求：生成器脚本**不要留在仓库里**（除非它真是构建的一部分），删掉并说明。
  - R11 继续生效（不许启动 exe；托盘/资源管理器里的观感并入用户的汇总真机会话）。
Task 6: implementer **DONE** — commit **c92859c**（5 文件：`assets/pc-kvm.ico` 106250 B / 7 尺寸 /
  256 用 PNG 压缩；两张预览 PNG；`build/build-agent.ps1` 加 `/win32icon`；`TrayUi.cs` 托盘换嵌入图标）。
  自报：逐**目录项**核了 6 个 BMP 头 + 256 的 PNG 签名；`ExtractAssociatedIcon` 32×32 且**逐像素比对
  确认是我们的设计而非通用图标**；生成器是一次性脚本放在 `%TEMP%` 用完已删、仓库无残留。
Task 6: **控制方独立核实（未采信自述）**：重编 → 15 文件、exit 0、**142.5 KB**
  （比之前的 38.5 KB 多出的约 104 KB 正是嵌入的图标——这是"图标真进去了"的旁证）；
  三套 harness `18/24/45` 全绿；**`build-agent.ps1` 前三字节实测 `efbbbf`（BOM 保住了）**；
  ICO 头 `00000100` + 图像数 **7**；`ExtractAssociatedIcon(dist\pc-kvm.exe)` = **32×32** 非零；
  `TrayUi.cs` 97 / `Program.cs` 334（未被本任务触碰）。
Task 6: 计划笔误已修（第 4 个被实现者拦下的）：Task 6 Step 7 的 `git add` 初稿写的是
  `src/agent/Program.cs`，而托盘那行在 `TrayUi.cs`（Task 1 搬走的）—— 实现者按任务上下文判断并
  提交了 `TrayUi.cs`，**判断正确**。计划正文已改为 `TrayUi.cs` 并补上预览 PNG，注明原因。
Task 6: **零视觉的应对已执行**：派了有视觉能力的代理去看 `assets/pc-kvm-preview-256.png` 与
  `pc-kvm-preview-16.png`（16px 最近邻放大），要求它给出**可据以返工的具体判断**
  （实际看到什么、16px 是否还认得出、深/浅背景各处会不会消失、有无明显缺陷、一句话结论），
  并明确"读不出来就直说、不要按文件名猜"。这一路与代码审查**并行**、互不依赖。
Task 6: review package → `review-4def56f..c92859c.diff`（1 commit, 3818 B，做过 BASE 簿记修正）。
  dispatched **task reviewer（sonnet）**，并明确把"图标**长什么样**"划出它的范围（文本 diff 里
  二进制只显示 "Binary files differ"），它审的是**代码与构建接线**：BOM 是否真的保住（它是整文件
  重写还是逐行改动）、托盘回退在 `ExtractAssociatedIcon` **返回 null 而不抛**时是否正确、
  `Tray.Icon` 会不会留 null、`/win32icon:` 的路径/引号/缺失时是否 fail-fast、
  以及是否引入了新的运行时文件依赖（单文件 exe 硬约束）。
Task 6: **代码 review 回来 —— Spec ✅ 合规 / Task quality Approved**；0 Critical、0 Important、2 Minor。
  审查者的关键核实：①**BOM 是靠逐行插入保住的**（hunk 起于旧文件第 3 行、承载 BOM 的第 1 行未被触碰，
  无整文件重写）；②托盘回退**同时覆盖"抛异常"与"返回 null"两条路径**（`if (Tray.Icon == null)` 那句），
  且图标赋值在 `Tray.Visible = true` **之前**——托盘不会先闪一下通用图标；
  ③`/win32icon:` 用绝对路径 + `Test-Path` fail-fast + 正确引号；④**单文件 exe 性质保持**
  （托盘用的是 `Icon.ExtractAssociatedIcon(Application.ExecutablePath)`，即 exe 自带的嵌入资源，
  不是去读 `assets\pc-kvm.ico`）；⑤它**独立读了 ICO 字节**核对头与 7 个目录项；
  ⑥一条漂亮的旁证：`assets/pc-kvm-preview-256.png` **恰 4100 字节**，与 ICO 目录里 256 那条 PNG 项
  大小完全一致 ⇒ 预览图是图标里的**真实载荷**，不是另渲染的一份。
  它另判 GDI+ 那条怪癖（`new Icon(path,256,256)` 落到 128）"plausible and immaterial"：
  本任务唯一依赖的提取路径是 `ExtractAssociatedIcon`（返回 32×32 BMP 项），256 那条 PNG 是给
  资源管理器大缩略图用的、shell 自己能处理。
Task 6: minor (deferred): `Tray.Icon = null` 在 catch 里是冗余的（抛之前赋值根本没发生，新建的
  `NotifyIcon` 的 Icon 本来就是 null）。无害，且是简报逐字代码（我写的），纯装饰。
Task 6: minor (deferred): `ExtractAssociatedIcon` 来的 `Icon` 在托盘销毁时没被 Dispose。
  单实例托盘程序可忽略（进程退出即回收），修它要引入超出简报范围的释放管道。
Task 6: **代码部分 complete**（待视觉判读确认"好不好看"这一条，那才是用户需求"美化一点的"的判据）。
  **重要**：spec 里"16px 下必须仍能辨认"与"深浅色背景下都能看清"两句是**可验证的要求**，
  故视觉判读若在这两条上给出问题，就是**对着 spec 的真 Important finding**，该进修轮——
  这正是我设计这一步的原因（否则"美观"只能整条推给用户）。

## 执行记录 · Task 7

Task 7: dispatched implementer (**sonnet**) at BASE **85f763c**（`Program.cs` 334；Task 6 未动它）。
  派发要点：
  - 把**今晚实测的根因**原样交给它（`0x47` vs `0xE047` 那两行日志），而不是让它自己推断——
    这是本任务唯一"事实"来源，也是那句错误注释产生错误整个任务假设的反面教材。
  - **明确画了两条不同的预期**（这是本任务最容易搞错的地方）：`agent-logic` 的红是**编译失败**
    （`CS0117` 找不到 `NumpadNavE0Scancode`），绿是 `pass=24`；而 `scancode-map` 新增的 13 条是
    **跨语言契约钉子、不是新行为**（设备侧逻辑一行不改），**应当场 58/58 全过** ——
    并写死"**若有一条不过，不许改用例去迁就**，那是设备侧 E0 表与 spec 期望不符，报 BLOCKED 并给出失败对"。
  - 写死"**返回的是 scancode 不是 usage**"（线上协议送 scancode，设备侧做 scancode→usage），
    免得实现者按 N4 的字面把它"修"成返回 usage。
  - **禁止跑 `build-injector.ps1`**（它会推设备，而手机离线）——本任务 Java 侧只改注释、
    设备侧验证走本地编译的 `scancode-map` harness，不需要真机。
  - 点名**承重的自查**：不属数字键盘的普通码是否全部原样透传、修饰键那段提前 return 的路径是否
    完全未受影响、以及**日志是否仍打原始 scancode 而上行送翻译后的值**（日志记观测、不记翻译）。
  - 行数硬线：334 + 约 15 应在 360 内；**若超线则报 BLOCKED，不许从别处省**。
Task 7: implementer **DONE** — commit **b018951**（5 文件 +128/−3）。
  agent-logic 红（`CS0117`）→ 绿 `pass=24`；**scancode-map 的 13 条跨语言契约钉子当场全过**
  （`58/58`）—— 说明设备侧 E0 表与 spec 期望一致，那条"若有一条不过就报 BLOCKED、不许改用例迁就"
  的纪律**没被触发**（这是本轮最想知道的事：设备侧是否真如推断）。edge-tracker `pass=24`。
  `Program.cs` **334 → 353**，`KeyMap.cs` 72。
Task 7: **控制方独立核实（未采信自述）**：重编 → 15 文件、exit 0、143.0 KB；三套 `24/24/58` 全绿；
  `GetKeyState`/`VK_NUMLOCK` 在 `:29/:32`，调用在 `:254`（**在状态门控之后**，符合要求），
  翻译在 `:256`；`KeyMap.ModifierBit` 仍在 `:10` 未动、新函数在 `:50`；
  **`git diff` 实测 `ScancodeMap.java` 增删两边全是 `//` 行 —— 零代码行改动** ✅。
Task 7: implementer 指出计划一处**陈旧计数**（Task 7 Step 6 写 `pass=22`，实际 24，源于 Task 3
  加 C9/C10 之前）。它按 24 报、并主动说明差异，**判断正确**；计划正文已就地标注更正。
Task 7: **R13 —— 行数预算比预测紧，预先裁定超线时的动作。**
  R1/Ruling 33 当时的推演是"末尾约 337、余量 23"，**实测已到 353**（Task 3 的 +13、Task 5 的 +36、
  Task 7 的 +19 都比估的高）。距 360 **只剩 7 行**给 Task 8。
  分析：Task 8 的 `Program.cs` 增量其实很小（把 `Process devProc = DeviceLauncher.Start();` 换成
  `watchers.AttachConfig(cfg)` + `StartDevice()`、并把 `TrayUi` 构造里的 `devProc` 去掉，约 ±3 行），
  它的**主体在 `Watchers.cs`**（重连线程、`Report`/`LogFromWorker`、`TryRebuildLink`）。
  故预期落在约 356，仍在线内。
  **预先裁定（若判错要付什么代价）**：**若 Task 8 发现 `Program.cs` 需要超过那 7 行，它必须
  抽 `InputRouter.cs`（`MouseMoved` + `KeyChanged` 两个处理器，实测约 81 行）来腾地方，
  绝不允许上调 360 红线**——这是 Ruling 26 的原话，也是 R1 当时就写好的下一步。
  代价：Task 8 会多一个前置搬迁步骤。
Task 6: **外观判读回来 —— 判它需要返工。**
  **重要前提（影响这些证据的效力）**：我派的视觉代理**没能真正"看图"** —— 代理网关把图片从视觉
  输入里剥掉了。它诚实说明了这一点，改用 **PIL 逐像素分析**（ASCII 结构图、精确边界框、
  16×16 逐像素 alpha 表、深浅背景合成对比度模拟）。所以证据是**数值**而非主观观感——
  客观性反而更强，但**至今没有任何"眼睛"真正看过这个图标**。
  判读确认 256px 的构成**正是设计意图**（左显示器 `x31-175/y49-160`、右手机 `x191-241/y23-233`、
  橙色 `#e8963a` 箭头从显示器穿过右边框指向手机），且**配色思路正确**（无纯黑纯白、双底友好）。
  它撞上 spec 里两条**可验证的**要求：
  - "16px 下必须仍能辨认「两块屏幕 + 一支箭头」，小尺寸下**简化**"：**16px 是 256 直接降采样出来的，
    不是专门绘制的像素版** —— 显示器边框 alpha=128、角 alpha=64、底边 alpha 在 64→192 渐变、
    底座整行 alpha=128；**手机只有 3px 宽，而箭头头部占 5 行×4 列、把手机左边框与屏幕整列覆盖**，
    并产生橙×浅蓝的抗锯齿污泥（`#b29b86`/`#a58d79`）。读出的是"细棍中间插着橙色一坨"。
  - "深浅背景都要看得清"：白底上手机端盖 alpha=64 → 合成亮度 216/255 → **对比 1.35:1，直接消失**。
  另有三条 256px 客观几何缺陷：底座偏心 10-13px（显示器轴心 x=103 / 底座中心 x=90）；
  箭头头部**捅进手机 35px**（占手机全宽 69%）且与灰色焊死无留白；整体构图右偏（左留白 31 / 右 14）。
Task 6: **R14 —— 裁决：进修轮（不"看着还行"就放行）。** 理由：上面两条是 spec 写明的**可验证判据**
  而非主观口味，三条几何缺陷是**客观测量**；且用户的原话就是"美化一点的"。
  修轮内容（按重要性）：①**16px 必须专门绘制像素版**——实色 1px 边框、无任何半透明像素、
  底座在该尺寸直接不要、箭头收短让手机保住完整 3px 边框；②256px 箭头头部**最多到 x≈180**
  （手机左边框前留 8-10px 呼吸空隙）；③256px 底座居到 x=103、整幅内容居中；
  另要求 24/32 也检查同样伪影。**不建议改配色。**
  并要求**重新生成两张预览 PNG**（我会拿同一套 PIL 判读**再跑一遍**核实返工是否到位），
  以及报告里给**实测数字而不是形容词**（新的头部最大 x、新底座中心 x、16px 手机边框剩几列、
  16px 是否还有 alpha 非 0/255 的像素）。
  **代价（若判错）**：多一轮重画；换来的是 spec 那两条要求真的成立。
  **保留的不确定**：判读代理没有眼睛，其"读不出第二块屏幕"是**推断**；但支撑它的三个数字
  （alpha=64 端盖、3px 手机宽、头部 4 列覆盖）是客观的，足以支持"16px 不合格"的结论。
Task 7: **review 回来 —— Spec ✅ 合规 / Task quality Approved**；0 Critical、0 Important、3 Minor。
  它把五个点名风险**逐个对着代码核**：①**跨语言契约**——直接读 `ScancodeMap.java:21-30` 的真实 E0 表，
  确认函数可能发出的**全部十个**低字节逐一映到 spec 期望的导航 usage（0x47→0x4A Home … 0x53→0x4C Delete），
  且 `0x4C` 不在表中故走 `default: return -1`（与"丢弃"哨兵一致）；②**位置**——插入块严格位于
  `if (tracker.Current != KvmState.Takeover) return;` 之后，修饰键分支提前 return 且其 `Send` 仍用原始码；
  ③**观测 vs 上行**——`Program.cs:233` 在任何翻译之前打 `e.Scancode`（原始），上行送 `sendSc`，
  "下一次真机排查不会被日志骗"；④**透传完整性**——`sendSc` 只在 `!e.IsE0 && NumLockOff && nav > 0` 时被改写，
  主键盘 `/`（非 E0 `0x35`）走 `default → -1` 原样透传，小键盘 `/` 以 E0 0x35 到来被 `!e.IsE0` 排除；
  ⑤`ScancodeMap.java` **零代码行改动**（它从 diff 独立确认，未采信报告）。
  **按流程 → Task 7 complete**（提交 `b018951`，review clean，无需修轮）。
Task 7: minor (deferred): **按住小键盘键的途中切换 NumLock** —— 开状态下按下小键盘 7 发的是普通 `0x47`（KP7 down），
  中途关掉 NumLock 后**抬起**会被翻成 `0xE047` UP，即设备收到一个它从没收到过 down 的 up。
  HID 注入器会把报告位清掉，几乎必然无害；属**本次之前就存在的状态失同步类别**，超出本任务范围。
Task 7: minor (deferred): `N4` 的断言被 `N1` 的精确相等断言包含（简报逐字如此，保留不算错，仅记录可合并）。
Task 7: minor (deferred): `tests/scancode-map` 控制台对中文用例名显示乱码（代码页问题）。
  这是**既有**的显示行为（原有 45 条也用中文名），非本次引入或加重。

## Task 8 的派发时机（控制方决定，非滞后）

Task 8 **暂不派发**，理由具体而不只是"守规矩"：
- 正在跑的 **Task 6 返工轮**与本任务**都要动 `src/agent/TrayUi.cs`**（本任务要把 `Process devProc`
  参数从 `TrayUi` 构造里去掉、改用 `watchers.DeviceProcess`）。
- skill 明令"Never dispatch multiple implementation subagents in parallel (conflicts)"。
- 在这里违反它有**具体的可见代价**：两路同时提交会让**审查包的 commit 区间被对方的提交污染**，
  本会话已经因此做过**三次** BASE 簿记修正（Task 1 的 `339115f`、Task 5 的 `b5235ca`、Task 6 的 `4def56f`）。
- 故等 Task 6 返工落地后再派 Task 8。

Task 8: 已把 **R13 的行数预案写进计划正文**（`Program.cs` 实测 353、距 360 仅 7 行；
若本任务需要超过那 7 行，**必须先抽 `InputRouter.cs`**，不得上调红线）。
Task 8: 派发时要随附的承重信息已梳理：
  ①Task 1 在 `Watchers.cs` 里已建好的东西（`_stop`/`Stop()`、`GeometryQueried`、PONG 订阅、逃逸键处理器）
    ——本任务是往同一个类里叠加，不是新建；
  ②**R9**：后台线程**绝不直接写日志**（`LogFromWorker`/`Report` 内部 `BeginInvoke`），
    因为 `log` 会在退出时被 `TrayUi` 关掉，而 `Stop()` 只置标志不 Join——写已关闭的 writer
    就是后台线程未捕获异常 = 进程被终结（Task 1 审查 Important 1 与 R8 就是这个形状）；
  ③**R13** 的行数预案；
  ④设备侧进程**所有权从 `Program.cs` 移到 `Watchers`**（重连会换进程）；
  ⑤幂等：重启注入器前先 Kill 旧的，否则设备侧堆多个 `app_process`；
  ⑥jar 只推一次（`Prepare` 拆成 `EnsureTunnel` + `PushJar`）；
  ⑦Step 7 要往**上一阶段 ledger** 追加"#4 覆盖边界"一节（按 R4：**只追加、不改写**）；
  ⑧R11：不许启动 exe，三档演练（杀注入器 / `adb kill-server` / 拔线）并入用户的汇总真机会话
    ——而且**手机此刻离线，这一条现在也做不了**，如实挂账。

Task 6: fix round 1/5 implementer **DONE** — commit **e75563d**（**只动 3 个二进制资源**：
`assets/pc-kvm.ico` 106250→106393、`pc-kvm-preview-256.png` 4100→4243、`pc-kvm-preview-16.png` 5196→4093；
**0 insertions / 0 deletions —— 零代码改动**）。它报告的返工实测值（逐条对上 R14 的要求）：
  - 16px：**alpha 非 0/255 = 0 个**（原边框 128 / 四角 64）、**底座已删**、
    橙最大 x=10 vs 手机左边框 x=12（**整列空隙**）、**手机边框被橙覆盖 0 像素**
  - 24px：同标准像素版（alpha 二值），橙最大 x=13 vs 手机 x=18（留 4px）
  - 32px：矢量原生非降采样（橙最大 x=22 < 手机边框 23）
  - 256px：含描边橙最大 x=173、手机左边框 x=182 ⇒ **呼吸隙 9px**（原捅进 35px、占手机全宽 69%）；
    支架中心 x=95 / 底板中心 x=95（原 93.5/90）；内容 bbox x22-234（留白 22/21，原 31/14）；圆角统一 13
  - 它**主动披露自己一个校验脚本有 bug**（ASCII 打印行序错、数据没错），改用 .NET 解码 + 定点字节探针双重核实
Task 6: **控制方独立核实（未采信自述）**：重编 → 15 文件、exit 0、**143.5 KB**（原 143.0 KB
  —— **exe 确实换了**，这是"产物真的变了"的证据，§六 那条教训）；三套 `24/24/58` 全绿；
  `build-agent.ps1` 前三字节仍 `efbbbf`；ICO 头 `00000100`、**图像数仍 7**、106393 字节；
  `ExtractAssociatedIcon(dist\pc-kvm.exe)` = 32×32 非零。
Task 6: **流程判断（有意的偏离，记录理由）**：skill 要求"每轮修轮后做 scoped 复审"，但**本轮的 diff
  只有 3 个二进制文件**，文本 diff 里它们只显示 "Binary files differ"（我实测 `review-package`
  在该区间还混进了 9 个其它提交）—— 派文本审查者等于让它审三行"二进制不同"。
  故本轮的独立验证由**更强的两件事**承担：①上面这一组构建/BOM/ICO 结构/产物变更核实（控制方）；
  ②**逐像素独立复测**（把同一套 PIL 判读代理唤醒，用与首次完全相同的方法重跑，
  并要求它给出**自己的读数**并逐项声明"一致/不一致"，且明确"读不出来就说做不到，不许拿实现者的数字填空"）。
  这条偏离是**用更强的检查替换形式上的检查**，不是省略检查。
Task 6: **独立逐像素复测回来 —— 十项全部【一致】，且复测者用**自己的读数**撤回了上次对 16px 的"不合格"判定。**
  它的实测（三路交叉：16px 从预览文件与 ICO 内嵌两路、24/32 从 ICO 提取、256 用新预览）：
  - 16px alpha 直方图**仅 {0:127, 255:129}** —— 零中间值，上次的 128/64 边框与 64→192 渐变底边全消失
  - 16px 手机 cols 12-14（3px 宽）、`col 11` 全列透明 ⇒ 与橙色**净空 1px、重叠 0 像素**
  - **双底最差对比度从 1.35:1 提升到 2.37:1**（白底橙色箭头）；深底最弱 2.97:1、浅色填充 6.2-6.9:1
  - 24px 二值 alpha（576/576 全为 0/255）、净空 4px、零粘连；32px 矢量原生（149 个非二值像素全在 1px 边缘，
    属正常矢量 AA，无多像素宽泥色带）
  - 256px 呼吸隙实测 **9px**（描边 bbox 右界 x=173、手机左边框 x=183 —— 与实现者报的 182 差 1px，
    属阈值差异，结论不变）；底座三者（主体轴心/颈部/底板）对齐在 **0.5px 内**；留白 22/21/22/21 均衡
  - 结论原话："**16px 现在能辨认出「两块屏幕 + 一支箭头」——合格，撤回上次的「不合格」判定**"。
    唯一残留弱点：16px 下箭头退化成 1px 宽 × 6px 高的竖条（三角在 16px 无法保有水平收窄，靠方向语境读出），
    它判为 16px 下的合理取舍、不构成返工理由。
Task 6: minor (deferred，**复测者发现、实现者未报**): **24px 的内容四边全出血**（bbox x0-23/y0-23，零边距；
  对比 16px 有 1px、256px 有 22px）。在 **150% DPI 的任务栏**里会与相邻图标贴边。
  判为小瑕疵、不挡收尾。**用户的屏今天是 100%**（3840x1080 双 1920x1080），碰不到；记录在案，
  若将来换高分屏可顺手加 1px 边距。
Task 6: **complete**（初版 `c92859c` + 修轮 `e75563d`，代码审查 Approved + 外观经独立逐像素复测确认）。
  真机观感（资源管理器/托盘/深浅主题）仍按 R11 并入用户的汇总真机会话。

## 执行记录 · Task 8（最后一块）

Task 8: dispatched implementer (**sonnet**) at BASE **c2a912f**（`Program.cs` 353）。随附的承重信息：
  ①**`Watchers.cs` 已存在**（Task 1 建的），本任务是**往里叠加**而不是新建——明确列出它现在已有什么
  （几何轮询线程 / 前台守卫 Timer / 心跳 Timer / `_stop` / `Stop()` / `GeometryQueried` / PONG 订阅 /
  逃逸键处理器），并要求先读类头注释里的线程纪律；
  ②**`TrayUi` 现在收 `Process devProc`**，本任务要把设备侧进程所有权移到 `Watchers`（故要删那个参数、
  改用 `_watchers.DeviceProcess`），并提醒 **Task 6 刚改过那个文件的 `Tray.Icon`，别碰**；
  ③**R9**（后台线程绝不直接写日志）连同"为什么"一起下达——写已关闭的 writer 就是后台线程未捕获异常
  = 进程被终结，这个形状在 Task 1 已经被抓到过一次；
  ④**R13 的行数预案**（只剩 7 行；超了就先抽 `InputRouter.cs`，不许上调红线）；
  ⑤**禁止跑任何 adb 变更命令**（手机离线；`adb kill-server`/`taskkill adb.exe` 会打断本机其它工具）——
  本任务是写代码而不是执行它；
  ⑥Step 7 要往**上一阶段 ledger** 追加一节（按 R4：**只追加、不改写**），内容必须含 2026-09-23 那次
  设备级掉线的实测形态与"**本自愈不覆盖该形态**"的结论；
  ⑦静态核对里要求它**把每一处 `_log(`/`_host.SetStatus` 按线程分类**（UI 线程经 BeginInvoke vs 后台线程），
  并明说"后台线程上直接写 `_log` 就是 Critical"。
Task 8: **控制方加了一项计划外的要求（有明确理由）**：Task 8 原本**没有任何可离线测的逻辑**——
  它的验证只剩"能编译" + 一场今天做不了的真机演练，对一个最大且最不可验证的任务来说太薄。
  而本项目有现成先例（`DeviceLauncher.ParseDisplaySize`/`ParseWxH`/`ParseRotation` 都是**纯解析函数 + 用例**）。
  故要求把 `DeviceVisible()` 拆成 `public static bool ParseDeviceVisible(string adbOutput)` + 一个薄 adb 调用，
  并补 5 条用例（`agent-logic` 24 → **29**）：
  **D1 是最关键的一条**——只给 `"List of devices attached\n\n"` 表头时必须返回 **false**，
  因为表头含有 "devices" 字样，**任何 `Contains("device")` 式的粗判都会把空列表误判成"有设备"**，
  而本任务整条分级升级都建立在这个判据上（误判 ⇒ 永远不会升级 ⇒ 自愈形同虚设）。
  D3/D4 钉住 `unauthorized`/`offline` **必须算不可用**（它们含 "device" 的子串风险低但仍需钉），
  D5 钉 null/空串不崩。
  **代价（若判错）**：多约 10 行重构 + 5 条用例；换来的是这个最大任务至少有一处真正的机器验证。
Task 8: implementer **DONE_WITH_CONCERNS** — commit **1886118**（7 文件 +300/−15，含上一阶段 ledger 的追加）。
  agent-logic **29/29**（D1–D5 落地）、edge-tracker 24/24、scancode-map 58/58；编译零警告 exit 0；
  `Program.cs` **357/360** ⇒ **R13 的 `InputRouter.cs` 抽取未触发**（余量 3 行，全程未上调红线）。
  四条 concern：
  ①**与 brief 的两处受控偏差**：(a) 日志按**绑定的 R9** 改经 `LogFromWorker()` 的 BeginInvoke——
    **brief 原文正是"后台线程直写 `_log`"那个 Critical 形态**，故它**选择服从 R9 而不是 brief 字面**，
    这是对的（R9 就是我为这个形状下的绑定裁决，brief 那几行是旧的）；(b) `TryRebuildLink` 与②级
    升级前加了 4 个 `_stop` 守卫，防"掉线时退出"竞态弄脏退出清理。
  ②静态检查 2 的 1 个命中是**注释**（Task 1 写的禁令文档本身），代码零命中。
  ③**真机三档演练按 R11 推迟，且手机今日离线无法实跑 ⇒ 运行时验证为零**（如实申报）。
  ④工作区在开工前已有 `M .superpowers/sdd/.gitignore`（非它所改、未提交、未动）。
Task 8: **控制方独立核实（未采信自述）**：
  - **上一阶段 ledger 是纯追加**：`git diff --numstat` = **`16 0`**（零删除），761 → 777 行；
    追加内容以 Task 8 小节开头、以 R11 推迟说明结尾 ✅（这正是 R4 要求的"只追加、不改写"）
  - 重编 → 15 文件、exit 0、146.0 KB；三套 `29/24/58` 全绿
  - `System.Threading.Timer` **唯一命中是注释**（零代码命中）；`SetWindowsHookEx|WH_KEYBOARD_LL` **零命中**
  - `Watchers.cs` 的日志点分类：`_log(` 在 `:70`（逃逸键，UI 线程走 MessageHost 热键）与
    `:156`（心跳，UI 线程 WinForms Timer）；重连线程的写入在 `:267`（`LogFromWorker`）与
    `:281`（`Report`），而 `LogFromWorker` 自身在 `:298` 走 `_host.BeginInvoke(... _log(s) ...)`
    ⇒ **重连线程零直接写日志** ✅
  - 进程所有权：`devProc` 已从 `TrayUi` 消失，`TrayUi.cs:90` 改用 `_watchers.DeviceProcess` ✅
  - 行数：`Program.cs` 357 / `Watchers.cs` 303 / `DeviceLauncher.cs` 230 / `TrayUi.cs` 95 / `Main.cs` 246
Task 8: review package → `review-b74ef93..1886118.diff`（1 commit, 34610 B，做过 BASE 簿记修正）。
  dispatched **task reviewer —— 用 opus**（本计划风险最高的 diff：并发 + 线程 + 生命周期 + 外部进程；
  按 skill 的 Model Selection"subtle concurrency change 用最强模型"）。派发里点名六个风险面：
  ①升级状态机（`_failStreak`/`_level` 会不会跳级/成功后重升级/循环/因瞬时抖动升级）；
  ②`Report()` 的状态比较是否已收敛到单一线程；③退出顺序（`Stop()` vs 重连线程 vs `log.Close()`）
  ——那 4 个 `_stop` 守卫是"真的关上了窗口"还是"只是收窄"；④进程生命周期（谁会 Kill、会不会泄漏或双杀）；
  ⑤初始化顺序（`AttachConfig`/`StartDevice` 与 `Start()` 的先后）；⑥D1–D5 是否真钉住了表头误判陷阱。
  另要求它**明确区分"已验证的代码"与"未验证的行为"**（本任务运行时验证为零），让用户知道自己在信什么。
  并告知：上一阶段 ledger 的纯追加、`System.Threading.Timer` 仅注释命中、构建与三套 harness 的结果
  **均已由控制方核实，无需重算**。**未预判任何结论。**
Task 8: **review 回来 —— 0 Critical、1 Important、4 Minor；判 Needs fixes。**
  审查者（opus）对**并发设计**这一最高风险部分的结论是**扎实的**，逐项给了证据：
  升级状态机（`_failStreak` 逐轮 +1、②级门控 `>=3 && _level<1`、③级 `>=6 && _level<2`、
  **`_level` 在动作前赋值故每级只触发一次**、成功路径把两者都归零 ⇒ 既不跳级也不会在健康时重升级、
  瞬时抖动被吸收）；`Report` 的 `_lastReport` **单写者**（只在重连线程）；退出顺序 Stop-before-Close 正确
  （并论证：`ApplicationExit` 在消息循环结束**之后**触发，故已排队但未派发的 `BeginInvoke` 永远不会执行，
  那个残留窗口实际是关上的）；进程生命周期三处 kill 都有 `HasExited` + try/catch，**双杀无害**；
  初始化顺序（它去 diff 之外 grep 了 `Program.cs`：`AttachConfig`(273)→`StartDevice`(274)→
  `Install()`→`Start()`(345)→`Application.Run`(346)）；**D1–D5 确实有判别力**（逐条说明朴素实现过不了 D1/D3/D4）。
Task 8: **Important 1 = 又是我的计划缺陷，而且是同一模式的第二次。**
  `ReconnectLoop` 末尾 `Report("等待设备…（已重试 " + _failStreak + " 次，" + why + "）")`
  ——**把每轮递增的 `_failStreak` 拼进了拿去去重的字符串**，于是 `if (s == _lastReport) return;`
  **永远不相等** ⇒ **每轮都写一行日志**（默认 5s 一轮，掉线一小时约 **720 行**）。
  **它直接违反 brief 自己的 Step 6 验收项 6**（"日志不刷屏——每个状态只出现一次，不是每轮一次"），
  也违反 commit message 自己的声明；而 `Report` 的文档注释引用的正是本项目"刷日志淹没有效信息"的历史。
  **换句话说：我的判据是对的，我的代码违反了我自己的判据** —— 本会话第二次（第一次是 L4/L5 的
  `SetPosition`：判据"harness 必须复刻接线"对，代码却依赖了"初值刚好对"）。
  **通用教训（值得进交接）：计划里凡是"去重/幂等/缓存"的键，都不能包含会随时间变化的计数或时间戳。**
Task 8: **R15 —— 裁决：修**（Important 默认进循环，且它是我自己的计划缺陷）。
  修法：拆开"状态条每轮更新"与"日志按状态种类去重"——加 `_lastLoggedState` 作共用去重键，
  新增 `ReportWaiting(why, attempts)`（状态条含次数、每轮更新；日志只在 `why` 变化时写一行），
  `Report(string)` 在写日志时同步更新 `_lastLoggedState` 以免两者互相打架。
  并把 **Minor 3 折进本轮**（`_devProc` 加 `volatile`，一个关键字 + 一句注释，零风险）。
  Minor 2/4/5 记入 ledger 交整分支终审 triage（重建期退出的微秒级窗口、`Process` 未 Dispose 的句柄慢泄漏、
  `tests/agent-logic/run.ps1` 里一句过时注释）。
  计划正文已就地修正（`_lastLoggedState` 字段 + `ReportWaiting` 方法 + 循环末尾改调用 +
  一段"为什么去重键里不能有计数"的注释）。
Task 8: fix round 1/5 implementer **DONE** — commit **3e6c274**（**仅 `Watchers.cs` +44/−5**，
  `Program.cs` 未被碰、仍 **357**）。agent-logic 29/29、edge-tracker 24/24、scancode-map 58/58；
  build 零警告 exit 0。
  它给的证据正是我要的形式：**推理**——连续 N 轮同样 `why` 只写 **1 行**（首轮记键，其后仅状态条更新次数）；
  **可核事实**——`rg -n '_lastLoggedState|_lastReport'` 的全部读写点 + 逐点声明线程归属：
  `_lastReport`（L43/287/288）只在重连后台线程（单线程）；`_lastLoggedState`（L47/294/317/319）
  **只在 UI 线程**（全在 `BeginInvoke` 委托体内）。它另复跑了 11 处 `_log(`/`SetStatus`，
  确认全在 UI 线程或 BeginInvoke 内、**重连线程零直写**。
  Minor 3 折入（`_devProc` 加 `volatile` + 注释）；Minor 2/4/5 未动、留终审。
Task 8: **控制方独立核实（未采信自述）**：实测 `_lastLoggedState` 的读写点确在 `BeginInvoke` 委托体内
  （`Report` 的 L291-294、`ReportWaiting` 的 L313-321）；`_devProc` 已是 `volatile`（L39）且带注释
  （L37-38 说明"重连线程写 / UI 线程读（DeviceProcess、TrayUi 清理、Stop 的 Kill）"）。
  **同时发现它做了一处我计划里没有的设计改动**：它把 `_lastReport` 的**去重比较移出了 `BeginInvoke`**
  （放到重连线程上），只把实际 I/O 留在委托体内 —— 与计划代码不同。已在 scoped 复审里**专门点名要它判**
  （不是预判，我确实不知道哪种更好）：两种安排各自会不会造成**漏打**或**重打**状态变化日志、
  以及把去重移出 UI 线程是否引入新风险。
Task 8: scoped re-review package → `review-44e50c4..3e6c274.diff`（1 commit, 7209 B，BASE 簿记修正）。
  dispatched **scoped re-reviewer（sonnet）**。

## R16 —— 裁决 `.superpowers/sdd/.gitignore` 的长期脏状态

背景：该文件被 superpowers 工具改回一行 `*`（本工作区会话中一直显示为 `M`），
两个实现者都如实报来"未提交、未动、待控制方裁决"。
裁决：**提交它**。理由：①它让工作树永久脏，而我在一个用 commit 区间算审查包的流程里工作，
脏树是个隐患；②**已跟踪的文件不受 `.gitignore` 影响** —— 两份 ledger（阶段二 `b8d167b`、
阶段三）都已被跟踪，故用户此前"ledger 入仓"的决定**继续成立**；
③唯一后果是未来的 SDD 产物（brief/report/review 包）默认不入 git —— 那恰好符合 skill 对工作区的定位
（"git-ignored scratch"），也比我先前 `-f` 强推更干净。
**代价（若判错）**：将来若想跟踪某个新的 SDD 文件，需要显式 `git add -f`。
Task 8: **scoped 复审 —— 两条 finding 都已 ADDRESSED，但判 Fix round: Findings remain open**
  （修复**引入了一处新的 Important**）。它同时回答了我专门点名的那个设计问题：
  **把 `_lastReport` 的去重移出 `BeginInvoke` 本身是成立的** —— 它读完整文件确认 `_lastReport`
  只被重连线程读写（`Report` 只从 `ReconnectLoop` 调）、`_lastLoggedState` 只被 UI 线程读写，
  且同一调用线程上的 `BeginInvoke` 是 FIFO 入队，故"两把键由不同线程选"这个担心不成立。**该处不改。**
Task 8: **新 Important（修轮 1 引入）** —— `Report` 的早返回吞掉共用键的重置。
  `Watchers.cs:287` 的 `if (s == _lastReport) return;` **发生在入队之前**，于是被门掉的那次调用
  **既不打日志、也不设状态条、也不重置共用键**。触发链：一次恢复后 `_lastReport = "已连接"`
  → **一次 1–2 轮的短掉线**（还没到 `_failStreak>=3` 的②级升级去改写 `_lastReport`）→ 恢复时
  `Report("已连接")` 仍等于 `_lastReport` ⇒ **整条被门掉**：
  （a）**`SetStatus("已连接")` 从未执行**，而连接时**没有别处设状态**（`Connected` 处理器不调
      `SetStatus`、`Disconnected` 只在 TAKEOVER 时置 IDLE）⇒ 状态条**一直显示"等待设备…"而实际已连上**；
  （b）`_lastLoggedState` 停在旧 `why` ⇒ **下一个相同 `why` 的掉线期等待日志被去重吃掉**。
  **触发条件不是边角情形**：注入器单轮即被重建成功（正是 ledger A2 记的形态）就是 **1 轮事件**，
  故从**第二次**这种死亡起就会命中。
Task 8: **根因在我的计划设计**：初稿有**两把去重键**（`_lastReport` 管"是否变了"、`_lastLoggedState` 管日志），
  而两者的域不同 ⇒ **一把的早返回会吞掉另一把的重置**。（初稿那版把两者都放在委托体内，同样会吞——
  所以这不是实现者引入的，是实现者的改动**暴露**了我设计里的这个耦合。）
Task 8: **R17 —— 裁决：去掉 `_lastReport`，只留一把键，并把"状态条"与"日志去重"彻底分开。**
  新增 `ReportState(string status, string key)` 作唯一入口：**状态条每次都设、绝不去重**
  （它是当前真值，被去重跳过就会出现"已连上却显示等待中"），**日志才按 key（状态种类）去重**；
  `Report(s)` = `ReportState(s, s)`、`ReportWaiting(why, n)` = `ReportState("等待设备…（已重试 n 次，why）", why)`。
  **`_lastReport` 字段必须一并删除**：①已无读取者；②**csc 会对"只赋值从不读取"的私有字段报 CS0414**，
  留着会直接破坏本项目的零警告要求。
  **代价（若判错）**：`Report` 由"整体去重"变成"状态条每次都设、日志去重"——重复调用同一个
  `Report("已连接")` 现在会重复设一次状态条（幂等、无副作用），换来的是状态条**永不撒谎**。
  计划正文已就地修正（字段块 + 两个方法 + 一段讲清"为什么只留一把键"的注释）。
Task 8: fix round 2/5 implementer **DONE** — commit **5a6e36b**（**仅 `Watchers.cs` +31/−37**，净减 6 行；
  未 amend `1886118`/`3e6c274`）。`_lastReport` 字段与全部读写**已删**；新增
  `ReportState(status, key)`（**状态条在去重分支之前、每次都设**；日志按 key 去重），
  `Report(s)`=ReportState(s,s)、`ReportWaiting(why,n)`=状态带次数/键只用 why。
  build 零警告（**无 CS0414**）exit 0；三套 `29/24/58` 全绿；`Program.cs` 仍 **357**（未碰）。
  它给了我要的两个推演：**短掉线（第二次）→ 2 行日志、状态条回到"已连接"**；
  **10 轮含②级 → 5 行日志、状态条每轮更新次数、最终"已连接"**。
  另一处如实申报：我在派发给的注释文本里有两处提到 `_lastReport`，删字段后那两句会自相矛盾，
  它**最小改写为"旧键"**并声明语义未丢——**判断正确**。
Task 8: **控制方独立核实（未采信自述）**：`rg '_lastReport' src/agent/Watchers.cs` → **零命中** ✅；
  `SetStatus` 三处命中（L82 逃逸键、L167 心跳、**L297 `ReportState` 在去重判断 L298 之前**）
  ⇒ **三处都在去重分支之外**，状态条必被设置 ✅。
Task 8: scoped re-review package → `review-abf9a5a..5a6e36b.diff`（1 commit, 7883 B；FIX_BASE 取 `abf9a5a`，
  跳过我的两个文档提交）。dispatched **scoped re-reviewer（sonnet）**，除 finding 外点名四个检查：
  ①`Watchers.cs` 里**每一处** `SetStatus` 是否都在去重分支之外；
  ②**合键的逆向风险** —— `_lastReport` 去掉后，有没有哪个状态变成会**重复打日志**？
  要求它自己走一遍监督循环并给出**四种情形**（稳态连接 / 1 轮掉线 / 10 轮含②级 / 到达③级）
  各自的日志行数，并与实现者的数字对照；
  ③R9 是否仍不受影响（重连线程零 `_log`/`SetStatus`）；④被删的键是否真的彻底消失（留半截会 CS0414）。
Task 8: fix round 2/5 复审 **clean** —— 判 **All findings addressed, no new Critical/Important breakage** ✅。
  它逐点给了行号（`ReportState` 在 `:291-306`、`SetStatus` 在 `:297` 位于 `if (key != _lastLoggedState)` 之前
  故不可能被跳过、两个转发器 `:310-313`/`:316-319` 与 R17 裁决完全一致），并确认触发场景已修
  （1–2 轮掉线后 `Report("已连接")` 一定执行 `SetStatus`，状态条不再卡在"等待设备…"；
  `_lastLoggedState` 在 UI 线程重置，下一个同 why 的等待阶段能正常记日志）。
  **它做了我要的逆向检查并给出自己的四种情形行数**：稳态连接 **0** 行；1 轮掉线 **2** 行；
  10 轮含②级（默认 AllowKillAdb=false）**4** 行；到达③级 **6** 行 —— 并论证**没有新增重复打日志**：
  ②/③级报告由 `_level` 保证每级只触发一次，`已连接` 报告之间总隔着会移动键的等待报告。
  它另**抓出实现者报告里一处算术失误**（实现者写"共 5 行"，其自身算式 1+1+1+1=4）——判为报告笔误、
  非代码缺陷。**这类"报告数字不准"它没有放过，记一笔。**
  `_lastReport` 零命中（`rg` exit 1）、`_lastLoggedState` 在 `:298` 既赋值又被读 ⇒ 无 CS0414 路径、
  与"零警告"声明自洽；`Program.cs` 本轮未碰（diff 只有 `Watchers.cs`）。
Task 8: **按流程 → Task 8 complete**（提交 `1886118` + `3e6c274` + `5a6e36b`，review clean after 2 fix rounds）。
Task 8: minor (deferred，复审者提出、**先于本轮修复**): `AllowKillAdb=false` 时、`_failStreak >= 6` 的那些轮，
  ③级分支仍 `continue` 而不上报 ⇒ **那一轮既不打日志也不刷新状态条**（重试次数停一拍）。
  属 brief 原本的 Step 2 形状、先于本修轮；一次一拍、无害。
Task 8: minor (deferred): 实现者走查 ③ 的行数写 5、实际 4（报告笔误，非代码缺陷）。

## 全计划状态：Task 1–8 全部完成并通过审查

| Task | 提交 | 审查结果 |
|---|---|---|
| 1 P0 抽 `Watchers`/`TrayUi` | `ccf32a7` + `64c350b` | clean after 1 fix round（我加的轮询线程日志行） |
| 2 `Config` 配置层 | `014d55a` | clean（0 Important） |
| 3 设置对话框 | `b29ad71` | clean（0 Important） |
| 4 #1 速度 + 小数余量 | `d7099a0` + `c0adf03` | clean（0 Important） |
| 5 #2 手机在左/右 + 左挂回归 | `821fa8f` + `47cbadc` | clean after 1 fix round（虚拟桌面**原点**） |
| 6 #3 图标 | `c92859c` + `e75563d` | 代码 Approved + 外观经独立逐像素复测确认 |
| 7 #5 NumLock | `b018951` | clean（0 Important） |
| 8 #4 断联自愈 | `1886118` + `3e6c274` + `5a6e36b` | clean after 2 fix rounds（去重键，两次都是我设计的缺陷） |

离线用例终值：`edge-tracker` 24、`scancode-map` 58、`agent-logic` 29（原 15 / 45 / 0 → 共 **111 条**）。
`Program.cs` **357/360** —— 全程**未上调红线**，R13 的 `InputRouter.cs` 抽取未触发。
**整阶段运行时验证为零**（手机离线）：所有真机验收待用户批量的硬件会话。

## 整分支终审（合并前最后一道闸门）

dispatched **final whole-branch reviewer —— opus**。范围 `cd86784..e47804d`（阶段三全部代码），
但**刻意把包限定到代码路径**（`src build assets tests`）：全量包 955 KB / 50 提交，其中约 17 个是
控制方的文档/ledger 提交，而终审要看的是代码。代码专用包 **148 KB / 23 文件 / +2126−128**，
落在 `.superpowers/sdd/2026-09-23-phase3-config-and-resilience/final-review-cd86784..HEAD-codeonly.diff`。
派发里三处特别交代：
①**per-task 正确性已经逐任务把关过**，故要它把价值放在**跨任务一致性**（那是逐任务审查结构上看不到的）、
  以及**合并裁决**上；
②**本阶段运行时验证为零**（手机离线 + 严禁启动 exe），故要求它**明确区分**"静态推理得出"与
  "可执行检查得出"的结论——用户第一次跑的时候需要知道自己在信什么；
③两份 ledger 的 deferred minor 都要它 **triage**（哪些必须合并前修、哪些可以留），
  并明说"A roll-up nobody reads is a silent discard"。
另交代：`.ico`/预览 PNG 在文本 diff 里只是二进制差异，**图标外观另有独立逐像素验证**，让它不要审。

## 本会话我代用户做的全部裁决（R1–R17，按序）

（每条都记了"若判错要付什么代价"；这是它们唯一能到达用户的地方。）

| # | 裁决 | 若判错的代价 |
|---|---|---|
| R1 (=Ruling 33) | Task 1 的抽取扩到第二块（`TrayUi.cs`）——只搬三个 watcher 会让计划末尾 `Program.cs` 到约 371、**踩破 360 红线 11 行** | Task 1 的接触面变大（多碰托盘/退出路径，均低风险 UI 代码） |
| R2 | `TrayUi` 构造签名在 T3（+`Config`）与 T8（−`Process`）各变一次，属正常演化 | 实现者只改一处则编译报错——可见、非静默 |
| R3 | `SettingsApplied` 处理器三段接力，**应用行为必须留在 `Program.cs`**（`TrayUi` 看不到那些局部量） | 实现者改成局部插入会丢前一轮的行；`Program.cs` 多约 16 行 |
| R4 | 本工作区 ledger 承担 SDD 记账；Task 8 另向**上一阶段** ledger **只追加** | 上一阶段 ledger 多一节本可只留本工作区的文字——无害 |
| R5 | 不建 worktree，留在 `G:\pc-kvm` 的 `phase2-skeleton` | 隔离性弱一些（缓解：每任务独立提交 + 独立审查） |
| R6 | 逃逸键处理器放进 `Watchers` 构造（简报文字说要搬、代码块里却没有它） | `Watchers` 职责从"后台守护"扩到"+UI 触发的放弃路径"；另置成本约 12 行 |
| R7 | 两个新类必须 `internal`（`MessageHost` 是 internal ⇒ 否则 CS0051）；托盘文本改为 `"PC-KVM"` | 无（已由修饰符清单与"旧标签已过时"支撑） |
| R8 | **删掉**轮询线程上的那行日志（而非加 marshal） | 少一个"几何变化被检测到但从未应用"的诊断信号——真需要时在消费点补更合适 |
| R9 | 同一条约束前移到 Task 8：`LogFromWorker` 作后台线程写日志的**唯一通道** | 日志行异步晚一拍；退出瞬间最后一两条状态可能不落盘（相比"进程崩溃"可接受） |
| R10 | Task 3 的 GUI 清单作废（子代理看不见 GUI；启动会锁 `dist\pc-kvm.exe`）；改机器检查 **并补 C9/C10**（补上 spec 要求的"写回往返"缺口） | 两条用例把 `Save()` 的实现细节钉住，改 ini 格式要同步改用例——正是回归测试该有的样子 |
| R11 | 不许启动 exe；**全部真机验收合并成一次用户会话** | 运行时验证长期为零（事实已在全阶段挂账） |
| R12 | **现在就修**虚拟桌面**原点**（不是 park） | 多读两个 system metric、多约 8 行 |
| R13 | 行数预案：Task 8 若需超过 7 行，**必须先抽 `InputRouter.cs`**，不得上调红线 | Task 8 多一个前置纯搬迁步骤（**实际未触发**，收尾 357/360） |
| R14 | Task 6 图标**判返工**（16px 是降采样非像素版、箭头焊进手机、底座偏心） | 多一轮重画（换来 spec 那两条可验证要求真的成立） |
| R15 | 修等待状态的**日志去重键**（计数器拼进去重串 ⇒ 每轮一行，约 720 行/小时） | 无（修的是我自己的缺陷） |
| R16 | **提交**被工具改回的 `.superpowers/sdd/.gitignore` | 未来新 SDD 产物默认不入 git，需显式 `git add -f` |
| R17 | 两把去重键**合成一把** + `ReportState(status, key)`：状态条每次都设、日志才去重 | `Report` 重复调用会重复设一次状态条（幂等无副作用），换来状态条**永不撒谎** |

**两条通用教训（值得进交接，都是本会话自己踩出来的）**：
1. **去重 / 幂等 / 缓存的键里，不能包含会随时间变化的计数或时间戳**（R15）。
2. **别用两把键管同一件事**——一把的早返回会吞掉另一把的重置（R17）。
   两者指向同一件事：**去重逻辑要有一个单一、显式的域。**

## 终审结果（opus）：0 Critical、2 Important、11 Minor —— **Ready to merge: With fixes**

它**独立重跑了可执行检查**（fresh 全量编译 15 个 `.cs` + `/win32icon` → exit 0 零警告；
三套 harness 从仓库源码重编重跑 → **24/29/58**；`Program.cs` 357/360；零 `SetWindowsHookEx`），
其余结论明确标注为静态推理。它还额外读了 `Program.cs`/`Transport.cs`/`DeviceLauncher.cs`
的当前全文与 spec §5/§9。

**它给的 Strengths 里有几条我很看重**（都是我担心的跨任务面）：
跨任务一致性良好（`Watchers`/`TrayUi`/`Program` 三权分立无重复所有权；设备侧进程所有权
从 `Program` 到 `Watchers` 的迁移在 `StartDevice`/`DeviceProcess`/`Stop()`/`TrayUi` 退出四处一致）；
**线程纪律经静态审计成立**（重连循环在入口、`EnsureTunnel` 后、`PushJar` 后三处重查 `_stop`，
故退出路径不会留下刚建好的隧道/注入器；`_lastLoggedState` 只在 UI 线程）；
**四份"放弃序列"都正确**（心跳安全网/逃逸键/ForegroundLost/SettingsApplied 四处都是
Leave → AbortTakeover → Release → status）；且它确认了 `Suppressor.Release()` 把 `ClipCursor(0)`
放首位、UX 放末尾，`Engage()` 的 `IsEngaged` 在日志调用之前。

**Important #1 —— `Program.cs:279/300/317` 仍在本项目"最坏失效类别"里，这是最后一处。**
`Connected`/`Disconnected`/PONG 处理器在 Transport 的 accept/read 线程上直接 `log.WriteLine`；
退出时 `_transport.Stop()` 虽在 `_log.Close()` 之前，但 **`Stop()` 只置标志与关 socket、不 Join**，
故在飞的 `Disconnected` 写入仍可能落在 `Close()` 之后 = **后台线程未捕获异常 = 进程被杀**。
今天只靠顺序运气缓解（`DeviceLauncher.Cleanup` 的 adb 调用买了约 100 ms）。
**这正是我在 Task 1 复审后如实挂进 ledger 等终审的那条**（ledger 第 211 行附近），
现被终审提升为 Important 并判**合并前修**。修法约 3 行：在 `Transport.Stop()` 里 Join accept 线程。

**Important #2 —— 又是我的假话，而且正是本项目栽过的那一类。**
`Config.cs` 类头与 spec §5 都写"默认值即当前行为…与阶段二完全一致"，但
**`MouseSensitivity` 阶段二是硬编码 `1.0`、新默认是 `0.50`** ⇒ 新机器上会跑出**一半**速度，
"拷贝即等价"的承诺不成立。终审说"这条必须**有意识地**决定，因为它是一个贴着
'无行为变化'标签的静默行为变化"。（对照：`ScancodeMap` 那句错注释曾让整个任务建立在错误假设上。）
**R18 —— 我的裁决：保 `0.50`、改那两句假话。** 依据：用户的原话就是"鼠标移动速度过快"，
默认减半正是他要的方向；而"可调"由滑块提供（实时生效）。
**代价（若判错）**：把 exe 单独拷到新机器上手感与阶段二不同——但那正是用户想要的方向；
且改回 1.00 只需一行。**这条我已向用户明示，他一句话可改。**

**终审对 Minor #3 的裁决与我一致**：`new void Capture()` 该**改名**而不是加 `new` ——
它明确说"逐字简报"这条理由没有分量，因为简报那个名字本身就是错误。
（这正是我当时与 task reviewer 的分歧点；两个独立审查者先后认同改名。）

**它另发现 3 条我没记录过的 Minor**：`Config.Save()` 写进 ini 的头注释称
"改完保存即可、下次按键事件即生效"是**假的**（运行时没有任何东西重读 ini，只有设置对话框
会改内存里的对象）；`tests/README.md` 的用例数陈旧（写 15/45，实测 24/58）且漏了 L1–L6b；
`DeviceLauncher.Start()` 的 `Process.Start` 未加 try/catch（重连线程上若 adb 启动失败
= 后台线程未捕获异常 = 进程被杀，与 #1 同一类别）。

**它的 deferred-minor triage**：合并前修 2 条（#1 + #2）；同轮顺手修 4 条零边际成本
（`Capture` 改名、`MouseScaler.Reset()` 注释、ini 头注释、README 计数）；
其余（阶段二 UHID_START 未读、滚轮 /120 截断、硬编码 JBR 路径、Task 5/6/7/8 的各项、
以及**阶段二那个被明确留给终审的发布竞态**）**可以留**——它对阶段二那条的论证很实：
x64 TSO 下 `_geometryChanged = true`（volatile，写在 `cursor = …` 之后）不会被重排到写之前，
且即使理论性错过也有界（标志留置，下一次鼠标事件会应用 HOME），最坏一个事件的漂移。

**它给的首要建议**：合并后**立刻**排那次延期的真机会话——本分支对
"0.50 默认手感 / 设置对话框 / 左侧跨越 / NumLock 真机翻译 / 三档重连"
**零运行时证据**；首次实跑应刻意走：两侧边缘进出、接管中 `adb kill-server`（心跳强制解除路径）、
以及连接状态下退出（先于/后于 #1 的修复各一次，正好验那条竞态）。

**R19 —— 裁决：按终审建议派一次修复波**（skill：findings 一次派完，不逐条派），
含 #1、#2 与 4 条零边际成本 Minor，并**加进它点出的 `Process.Start` 未守卫那条**
（与 #1 同属"后台线程未捕获异常 = 进程被杀"这一本项目最坏类别，且只是一层 try/catch）。
`Capture` 改名一并做（两个独立审查者都判改名）。
**#8（设置对话框开着时推到边缘会进接管）判为"记录而非加守卫"**：加守卫需让 `EnterTakeover`
知道对话框存在 = 引入耦合，而恢复路径本就存在（逃逸键 / 原路推回 40 mickey）。
**#10（③级在开关关闭时也置 `_level=2`）与 spec §9 的措辞**留待合并后随文档一并改。
**代价（若判错）**：这一轮里改动最多的其实是注释与文档（真代码只有 #1 的 Join、
#7 的 try/catch、#3 的改名），风险低；但"一次修 8 项"本身有引入新瑕疵的可能，故仍要一次 scoped 复审。

## 终审修复波（R19 落地）

fix wave implementer **DONE_WITH_CONCERNS** — commit **c3303a9**（8 文件 +42/−15；
`Program.cs` **未被碰、仍 357/360**）。build 零警告 exit 0；三套 `29/24/58` 全绿；
`rg` 零全局钩子；`Capture` 彻底消失。
**它的 concern 抓出我给的一条指令本身编译不过**：第 8 项我写"加一个关键字 `volatile`"，
但 **`volatile long` 在 C# 里非法（CS0677）**。它改用 `Interlocked.Read`（心跳读）
/ `Interlocked.Exchange`（transport 线程写），语义**等价或更强**（有栅栏的读；32 位进程下写也无撕裂），
并把"为什么不能 volatile"写进字段注释。**它没有闷头照抄我的错误指令，这值得记一笔。**
第 1 项它选了我给的**首选修法**（有界 Join）。第 2 项只改措辞、初值保持 0.50。
第 3 项 `Capture`→`ReadBack`（两个独立审查者都判改名）。报告落在
`final-fixwave-report.md`（该目录被 ignore，按仓库约定未入 git；只有 `progress.md` 是跟踪的）。
Task: **控制方独立核实（未采信自述）**：
- 重编 → 15 文件、exit 0、146.5 KB；三套 `29/24/58` 全绿
- **`Transport.Stop()` 实测已 Join**（`:127-128` 的 `Join(2000)`，**放在监听器/客户端关闭之后**，
  注释写明：不会死锁因为阻塞中的 Accept/Read 立刻抛出；不会自 Join 因为只从 UI 线程（TrayUi）调用）
- `_lastPongTicks` 实测 `Interlocked.Exchange`（`:70` 写）/ `Interlocked.Read`（`:163` 读），
  字段上方注释记着"`volatile long` 非法（CS0677）"
- `Capture` 零命中，只剩 `ReadBack`（`:103` 调用、`:130` 声明）
- spec §5 那两句假话已改成准确表述（"除 `MouseSensitivity` 外与阶段二一致；它默认是阶段二
  `1.0` 的一半，刻意如此"）
Task: **遗留一处我决定晚一步处理**：终审 Minor #11 指出 spec §9 与 `Watchers.TryRebuildLink` 的
注释都称"jar 只推一次"，而代码**每轮都重推**（终审判"行为其实比 spec 更好"——能自愈被清空的
`/data/local/tmp`，故**改措辞不改代码**）。这一处是纯注释/文档，但它属于本项目栽过的
"错误注释让下一个会话建立错误假设"那一类（`ScancodeMap` 那句就是先例），故**不打算不管**：
等本次 scoped 复审回来后作为一条独立的小改交给实现者，并在 ledger 记明"该条未经复审"及理由
（注释与 spec 措辞、零代码语义，再开一轮复审的边际收益低于成本）。**这条要如实告知用户。**
Task: 终审修复波的 scoped 复审 **clean** —— 判 **All findings addressed, no new Critical/Important breakage** ✅。
  8 项逐条给了行号证据（Join 在 `Transport.cs:117-129`、`Config.cs:17-18` 假话已纠且初值未动、
  `ReadBack` 替换彻底、`MouseScaler` 注释改为"几何变化路径**刻意不调它**"且确认未加调用、
  ini 头注释改为实情、README 24/58 且补了 L1–L6b、`Process.Start` 已 try/catch 且两个调用方本就有 null 检查、
  `Interlocked` 读写）。
  它对 **item-8 的偏离**给了明确判断：**不是妥协，而是该语义在 C# 里唯一合法的写法，且严格更强**
  （`Interlocked.Read` 是原子 + 获取栅栏的 64 位读，`Interlocked.Exchange` 是全栅栏写，
  32 位进程下也无撕裂）。
Task: **它做了一个关键澄清，纠正了终审的严重性判断（值得单独记）。** 关于 Important #1 的
  "进程被杀"：accept 线程上的 `log.WriteLine` 抛出的 `ObjectDisposedException` **会被 `AcceptLoop`
  既有的 `catch (Exception)` 兜住**（`Transport.cs:62`），`finally` 执行、`_running` 为 false 故循环结束，
  且线程是 `IsBackground`（`:41`）**不会吊住进程** ⇒ **最坏结果是"日志尾巴被截断"，不是进程被杀**。
  它明确注明：**这个兜底来自既有的 catch、不是新代码**；Join 的价值是让**正常路径可证地写完再关**，
  而有界超时是刻意用"可证性"换"adb 在飞时也能保证退出"。
Task: **技术洞见（值得进交接）**：**"后台线程写已关闭的日志"只在【线程体没有外层 catch】时才致命。**
  `Transport` 有外层 catch ⇒ 只是丢日志尾巴；而 `Watchers` **原计划**里的重连线程没有 ⇒ 那才是真的会杀进程。
  **同一形状、两种后果**——这正是 R8/R9 那一类为什么必须按"线程体有没有兜底"分别判断，
  不能凭形状一刀切。两个独立审查者（opus 终审 vs sonnet 复审）对同一段代码给出了不同的后果判断，
  后者更具体（引了行号），我采纳后者，并把两边都记下来。
Task: 另记一条 out-of-scope：`Config.cs:17-18` 的"除 MouseSensitivity 外与阶段二一致"这句里
  把 `ReconnectSeconds=5` 也算了进去，但阶段二**根本没有重连监督** ⇒ 严格说重连行为也不同于阶段二。
  spec §5 的措辞避开了这个问题（只列 `Right` + `AllowKillAdb=false`）。纯装饰，与 R18 针对的那条无关。

## 全计划收尾

Task: Minor #11 文档纠偏落地 —— commit **21d681e**（2 文件 +3/−3，**全部是注释与 spec 措辞**）。
  `Watchers.TryRebuildLink` 那 1 行注释改为"每轮都重推（刻意：/data/local/tmp 被清或设备重启后能自愈）"，
  spec §9 两行改为"每轮都重建隧道并重推 jar（比'只推一次'更强）"。
Task: **控制方独立核实**：`git diff --unified=0` 逐行看过——`Watchers.cs` 的改动确为 1 行注释换 1 行注释、
  spec 改动仅限 §9 段落；重编 15 文件 exit 0、三套 `29/24/58` 全绿、`Program.cs` 仍 357、工作树干净。
Task: **流程偏离（如实记录）**：这最后一处 6 行文档改动**没有派独立复审**。
  理由：零可执行语义（编译与三套 harness 是它唯一能被影响的检查，都已通过），
  而再开一轮复审的边际收益低于成本。**我用自己的核实替代了独立复审，并在此明示**——
  用户可据此判断是否接受。**代价（若判错）**：注释/文档里若残留一处措辞错误，需下一次有人读到才发现。

Task: **工作区不删除（有意的偏离，记录理由）**：skill 的收尾要求是"终审干净且修复已合并后
  `rm -rf <workspace>`"，但①**合并尚未发生**（等用户点头）；②用户此前明确要求 **ledger 入仓**，
  而 skill 原文假设工作区是"git-ignored scratch"。故**保留本工作区**，ledger 继续跟踪。
  **代价（若判错）**：仓库里留着一个约 500 KB 的过程目录（briefs/reports/review 包）。

### 遗留（合并后另行处理，非阻塞）
1. `Config.cs:17-18` 的"除 MouseSensitivity 外与阶段二一致"把 `ReconnectSeconds=5` 也算了进去，
   而阶段二根本没有重连监督 ⇒ 措辞仍略不严谨（复审的 out-of-scope 观察）。
2. 终审 Minor #8：设置对话框开着时推到边缘仍会进接管（判为"记录而非加守卫"，恢复路径本就存在）。
3. 终审 Minor #10：③级在 `AllowKillAdb=false` 时也置 `_level=2`，故掉线途中打开开关不会当轮触发③。
4. Task 8 的两条 minor（③级被门掉那一轮不刷新状态条、实现者走查行数写 5 实为 4）。
5. 阶段二与阶段三的 deferred minor 全表在**两份 ledger** 里，终审已逐条 triage（"可留"居多）。
