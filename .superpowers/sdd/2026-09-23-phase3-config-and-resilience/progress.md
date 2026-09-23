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

