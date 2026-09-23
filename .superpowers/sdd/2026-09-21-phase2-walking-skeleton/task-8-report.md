# Task 8 报告 —— 边界情况：心跳、断线解锁、逃逸键

**状态：DONE_WITH_CONCERNS**（实现与构建全过；两条留给后续任务/审查员的关切，见"关切"节）

- 分支：`phase2-skeleton`，基线 `6ce342c`
- 提交：`03e24d2` `feat: 边界情况 —— 心跳超时、断线解锁、紧急逃逸键、前台丢失保护`
- 变更：`src/agent/Program.cs`（+67/−1）、`src/agent/MessageHost.cs`（+25/−0），仅这两个文件（按控制方约束，不含 brief 提到的 Injector.java——见 Step 3 审计，无需改动）

---

## 实现内容

### 1. 心跳（Program.cs:243-279）

- `uint pingSeq` / `long lastPongTicks` 声明在捕获它们的 `transport.MessageReceived` 订阅与 `heartbeat.Start()` 之前（243-244 → 245-249 → 279）。
- PONG 记录采用**新增第二个订阅**（245-249，只更新 `lastPongTicks`，不打日志）；既有的 PONG 日志订阅（Program.cs:202-206）原样保留 → **PONG 日志仍只打一次**。
- `Timer heartbeat = new Timer()` 解析到 `System.Windows.Forms.Timer`（Program.cs 有 `using System.Windows.Forms;`、无 `using System.Threading;`），Tick 跑在 UI 线程 —— 满足强制约束 1。代码上方加了注释说明为什么绝不能换成线程定时器（Task 7 审查的跨线程隐患点）。
- **安全网在 `if (!transport.IsConnected) return;` 之前**（259-273 的 TAKEOVER 检查块在前，275 的早退在后）—— 满足强制约束 2，采用 brief 修正版及其解释注释，一字未改。
- 心跳放弃路径先 `transport.Send(Protocol.EncodeLeave())` 再 abort —— 满足强制约束 4。`Transport.Send` 在 `_stream == null` 时静默返回（Transport.cs:108-109），断线下是安全 no-op。

### 2. 断线解锁（Program.cs:189-201）

按 brief 原文**替换**了原 `transport.Disconnected` 处理器（原来只有一行日志），没有另起第二个订阅 → `# 设备已断开` 仍只打一次。三件套 `tracker.AbortTakeover()` + `supp.Release()` + `host.SetStatus("IDLE")` 一个不少 —— 满足强制约束 3（关闭 Task 6 审查的 Important：掉线卡 TAKEOVER / 重连装新 CursorModel 后状态不配套）。上面补了 3 行注释说明来由（Task 6 审查、ledger Ruling 22）。

### 3. 逃逸键（MessageHost.cs:38-61 + Program.cs:281-291）

- `MessageHost` 加了 `public event Action Escape` 和 `ProcessCmdKey` / `ProcessDialogKey` 两个重写（Ctrl+Alt+Esc 组合在含修饰键时 WinForms 分发路径不一致，两个都重写）。均按 brief 原文。
- `Main` 里 `host.Escape` 处理器（Program.cs:281-291）：日志 → `transport.Send(Protocol.EncodeLeave())`（约束 4，含 brief 的解释注释）→ `AbortTakeover` → `Release` → `SetStatus("IDLE")`。
- `host.Escape +=` 在 `Application.Run(host)`（Program.cs:304）之前订阅。

### 4. Step 3：Injector.java 审计（确认，零改动）

- **socket EOF 立即退出**：`int type = in.read(); if (type < 0) break;`（Injector.java:59-60）；`readExact` 对 `r <= 0` 返回 false 也 break（Injector.java:141-143）。✅ 已满足
- **IOException 退出而非无限重试**：整个读循环包在 `try { … } catch (Exception e) { System.out.println("INJECTOR loop end: " + e); }`（Injector.java:57-69），随后 `dev.close(); sock.close(); System.out.println("INJECTOR exit");`（71-73）。✅ 已满足
- 结论：无需改代码，故未提交 Injector.java（与"只提交两个文件"的约束一致）。

---

## 构建验证

```
> powershell -ExecutionPolicy Bypass -File build/build-agent.ps1
编译 9 个源文件...
编译成功: G:\pc-kvm\dist\pc-kvm.exe  (26.0 KB)
已复制 pckvm.jar 到 dist\
EXIT=0
```

`csc.exe`（4.0.30319）无任何警告输出 → **零警告**，退出码 0。**未运行** `dist\pc-kvm.exe`（按任务指令，运行时验证留给用户监督下进行）。

**行数**：`Program.cs` 最终 **315 行**（≤ 320 上限，但只剩 5 行余量；任务描述称基线为 225 行，实际基线是 249 行——净增 66 行，比预估的 ~36 行多，因为 brief 代码块自带大量注释）。`MessageHost.cs` 63 行。

---

## 恢复路径审计（七处 `supp.Release()` 触达点）

| # | 路径 | 位置（file:line，引原文） | 触发条件 |
|---|------|--------------------------|----------|
| 1 | LeaveTakeover 处理器（正常退出） | `Program.cs:98` — `supp.Release();` | 用户把虚拟光标推回与 PC 相邻的边（`OnTakeoverMove` 判定回程，EdgeTracker.cs:113-123） |
| 2 | OnFormClosing | `Program.cs:293` — `host.FormClosing += delegate { supp.Release(); };` | 窗体关闭（关机/退出流程的第一站） |
| 3 | Application.ApplicationExit | `Program.cs:296` — `supp.Release();`（ApplicationExit 处理器首行） | 托盘"退出"→`Application.Exit()`；与 #2 构成双保险 |
| 4 | guard 定时器 → CheckForeground → 前台丢失 | `Program.cs:237` — `guard.Tick += delegate { supp.CheckForeground(); };`；内部 `Suppressor.cs:107-108` — `# 前台被抢走，强制解除抑制` / `Release();`，随后 `Suppressor.cs:109-110` 触发 `ForegroundLost`；其处理器 `Program.cs:103` — `supp.ForegroundLost += delegate { tracker.AbortTakeover(); host.SetStatus("IDLE"); };` | UAC 安全桌面、锁屏、任何程序抢前台；每 250ms 检查一次 |
| 5 | 心跳超时（本任务） | `Program.cs:270` — `supp.Release();`（heartbeat.Tick 内，259-273） | TAKEOVER 态下 `!transport.IsConnected`（262）或 PONG 超 2 秒（262 `age > 2.0`） |
| 6 | 逃逸键（本任务） | `Program.cs:289` — `supp.Release();`（host.Escape 处理器内，281-291）；事件源 `MessageHost.cs:42/52` 的 ProcessCmdKey/ProcessDialogKey | 用户按 Ctrl+Alt+Esc |
| 7 | Disconnected 处理器（本任务） | `Program.cs:198` — `supp.Release();`（transport.Disconnected 内，192-201，Takeover 守卫在 195） | TCP ReadLoop 返回（EOF/IO 异常/对端重置），在 Transport 的 accept 线程上触发 |

### 交互安全性分析

- **双重 Release 是否安全：安全。** `Suppressor.Release()`（Suppressor.cs:89-99）第一句无条件 `ClipCursor(IntPtr.Zero)`，随后 `if (!IsEngaged) return;` —— 幂等；即使两次并发执行，两次 `ClipCursor(null)` 都是进程级"解除钳制"，效果相同；`SetForegroundWindow(_prevForeground)` 重复执行也只是把前台还给同一个窗口。`AbortTakeover()`（EdgeTracker.cs:55-59）只是两行状态赋值，同样幂等。
- **线程模型：** 路径 1-6 全部在 UI 线程（Raw Input 经宿主窗口 WndProc 派发；两个 Timer 都是 WinForms Timer；按键走窗体消息）。单线程消息循环串行派发，这些处理器之间**不可能并发**（无一处调用 `Application.DoEvents` 或开模态循环）。唯一在别的线程上的是路径 7（Transport 的 accept 线程）。
- **路径 7 与 UI 路径并发（如拔线瞬间同时心跳到期/按逃逸键）：** 最坏情形是两边同时进 abort——`AbortTakeover` 与 `Release` 均如上幂等；两边可能各发一次 `EncodeLeave`，设备侧收到两条 LEAVE 也只是把同一状态再清零，无害。两线程间**无共享锁**（`_sendLock` 只在 `Transport.Send` 内部短临界区），不存在死锁。收敛结果与单路径触发完全一致。
- **顺序竞态：** 断线时 Disconnected 先触发则 tracker 已回 IDLE，心跳 1 秒后的 TAKEOVER 守卫（259）使安全网整体跳过；心跳先触发（`_client.Connected` 有滞后、仍报 true 时靠 PONG 超时兜住）则 Disconnected 的 Takeover 守卫（195）同样跳过。两条路径谁先谁后都收敛。
- **逃逸键在抑制生效期间可达：** `Engage()` 成功的判定就是 `GetForegroundWindow() == _own`（Suppressor.cs:70-75），且 guard 每 250ms 复核（Suppressor.cs:104-105）。也就是说 **IsEngaged 为 true 期间本窗口持有前台**，Windows 把键盘发给前台窗口 → Ctrl+Alt+Esc 必然进入本窗口消息队列、被 ProcessCmdKey/ProcessDialogKey 捕获。若前台恰好已被抢走（键收不到），guard 在 250ms 内独立兜底。两道防线互补，不存在"既收不到键、又没人解锁"的窗口。

---

## 自查发现 / 关切（留给审查员）

1. **设备侧 MSG_LEAVE 尚未实现（重要，跨任务）**：brief 与约束 4 的理由都假设"设备侧处理 MSG_LEAVE 时会 buttonsDown=0 并 KeyState.releaseAll"，但当前 `Injector.java:112` 注明 `MSG_ENTER / MSG_LEAVE / MSG_CONFIG 由后续任务接管` —— 现在 MSG_LEAVE 帧会被 `payloadLength` 消化（返回 0）但 `handle()` 不做任何清理。PC 侧现在把 LEAVE 发对了（向前兼容），但"逃逸时按住的键在手机上永久残留"的防护要等设备侧任务实现后真正生效。**本任务被禁止改 Injector.java，故未动**，请控制器裁决归属（大概率 Task 9）。
2. **路径 7 的 `host.SetStatus("IDLE")` 跑在 accept 线程**：`Label.Text` 跨线程赋值。实践中安全（Text 走 `SetWindowText`→跨线程 SendMessage 合法阻塞；`CheckForIllegalCrossThreadCalls` 仅在调试器附加时抛），且既有代码同样在 accept 线程上改共享状态（Connected 处理器 new CursorModel，Program.cs:185）。但这是 brief 原文强制的三件套之一，故照抄未改；如审查认为需要 `host.BeginInvoke` 包装，请另开裁决。
3. **IDLE 态按逃逸键的副作用（轻微）**：Escape 处理器不检查 `tracker.Current`，IDLE 时按下会 `AbortTakeover()`（把 `Armed` 置 false，需光标离开边缘安全带 12px 后才重新武装）并 `ClipCursor(IntPtr.Zero)`。行为良性、按 brief 原文未加守卫。
4. **行数余量只剩 5 行**（315/320）：后续任务若还要往 `Main` 加东西会立即触顶，可能需要提取方法。

## 未验证项

- brief Step 4 的五条运行时实测（拔线解锁、`pkill Injector`、逃逸键、UAC 抢占、正常往返）**全部未执行**——任务指令明确禁止运行 `dist\pc-kvm.exe`（手机连着、用户正在用机器，误触发 Engage 会真锁光标）。留给用户在审查通过后监督执行。

---

# 修轮 1（审查 Important）—— 断线解锁的 SetStatus marshal 回 UI 线程

**审查结论：Approved**（1 条 Important 本轮修复；4 条 Minor 控制方已记账，本轮不动：lastPongTicks 陈旧、ForegroundLost 不发 LEAVE（归 Task 9）、IDLE 态逃逸键无害、行数预算已由 Ruling 26 改为 360）。

## 改了什么

`src/agent/Program.cs` 的 `transport.Disconnected` 处理器（现 Program.cs:195-207）：按控制方裁决逐字替换 TAKEOVER 分支——

- `tracker.AbortTakeover()` 与 `supp.Release()` **保持同步、保持在 marshal 之前**（释放抑制绝不延后到被 marshal 的委托里）；
- `host.SetStatus("IDLE")` 改为 `host.BeginInvoke((MethodInvoker)delegate { host.SetStatus("IDLE"); });`，并带上控制方给的解释注释（accept 线程直接改 Label.Text 是非法跨线程访问）。

只动这一处：未改 `Connected` 处理器、未碰其它任何文件。

## 验证

```
> powershell -ExecutionPolicy Bypass -File build/build-agent.ps1
编译 9 个源文件...
编译成功: G:\pc-kvm\dist\pc-kvm.exe  (26.0 KB)
已复制 pckvm.jar 到 dist\
EXIT=0
```

`csc.exe` 4.0.30319 零警告、exit 0。`MethodInvoker` 来自 `System.Windows.Forms`（Program.cs:5 已 using），C# 5 语法合法。修复后 Program.cs **321 行**（新红线 360，Ruling 26）。静态复核：该处理器现在是"同步 abort/release → 异步 SetStatus"，断线路径上光标解锁不再依赖任何 UI 线程调度；若 UI 线程恰在忙，`BeginInvoke` 只是排队，accept 线程立即返回继续等重连。运行时行为（真实拔线测试）仍留待用户监督验证（同前，未运行 exe）。

## 提交

- `a3e0205` `fix(agent): 断线解锁的 SetStatus marshal 回 UI 线程（审查 Important 修轮 1）`（仅 `src/agent/Program.cs`，+7/−1）

