# Task 7 报告：抑制器 —— 夺取前台与锁定光标

## 状态：DONE

## 实现内容

- **`src/agent/Suppressor.cs`（新建，UTF-8 BOM）**：按 brief 逐字实现。`AttachThreadInput` 绕行夺前台 → 确认前台已是本窗口 → `ClipCursor` 钳到 1 像素矩形；`Release()` 首句无条件 `ClipCursor(IntPtr.Zero)`；`CheckForeground()` 供前台守卫定时器调用；`uint fgPid;` 预声明（未重新引入 C# 7 内联 out）。
- **`src/agent/MessageHost.cs`（新建，UTF-8 BOM）**：`MessageHost` 从 `Program.cs` 移出并改写为可见置顶小窗（220x28、(0,0)、Opacity 0.75、DarkSlateBlue、状态 Label），代码按 brief verbatim（文件级加了一段 XML 注释说明"必须可见才能持有前台"）。独立的 `using System; using System.Drawing; using System.Windows.Forms;`。
- **`src/agent/EdgeTracker.cs`**：新增 `AbortTakeover()`（Current=Idle、Armed=false），插在 `OnIdleMove` 之前。
- **`src/agent/Program.cs`**：
  - 删除旧 `MessageHost` 隐藏窗体类（含 `SetVisibleCore` 覆写）。
  - `host.Show();` 加在句柄创建之后。
  - `Suppressor supp = new Suppressor(hwnd, ...)` 声明在 `log` 创建之后、transport 装配块之前（Ruling：声明位置，供 Task 8 前向引用）。
  - `EnterTakeover` 处理器：首句为 `if (!transport.IsConnected)` 连接检查（安全不变量 4），未连接/夺取失败均 `tracker.AbortTakeover()` 返回；成功后 `host.SetStatus("TAKEOVER → 手机")`。
  - `LeaveTakeover` 处理器：首句 `supp.Release();`。
  - `supp.ForegroundLost += delegate { tracker.AbortTakeover(); host.SetStatus("IDLE"); };`
  - 前台守卫：`Timer guard`（WinForms，250ms，Tick → `supp.CheckForeground()`），在 UI 线程触发，与 Engage 同线程。
  - 释放路径：`host.FormClosing += delegate { supp.Release(); };` + `Application.ApplicationExit` 首句 `supp.Release();`。

## 提交

- `734af75` `feat: 抑制器 —— AttachThreadInput 夺前台 + ClipCursor，零全局钩子`（**单 commit**，搬迁+改写同提交，每个 commit 树均可编译）
- 4 files changed, 200 insertions(+), 17 deletions(-)

## 构建输出

```
编译 9 个源文件...
编译成功: G:\pc-kvm\dist\pc-kvm.exe  (24.5 KB)
已复制 pckvm.jar 到 dist\
```

退出码 0，**零警告**（csc 无任何 warning 输出）。

## 无全局钩子

`rg "SetWindowsHookEx|WH_KEYBOARD_LL|WH_MOUSE_LL" src/agent/` → **零匹配**。

## 安全不变量审计

| # | 不变量 | 结论 | 证据 |
|---|--------|------|------|
| 1 | `ClipCursor(IntPtr.Zero)` 是 `Release()` 第一条语句 | ✅ | `Suppressor.cs:91` `ClipCursor(IntPtr.Zero);`（`Release()` 开括号在第 90 行，此前无任何条件/早退） |
| 2a | LeaveTakeover 可达 | ✅ | `Program.cs:98` `supp.Release();`（处理器首句） |
| 2b | 前台丢失路径可达 | ✅ | `Program.cs:225` `guard.Tick += delegate { supp.CheckForeground(); };` → `Suppressor.cs:108` `Release();`（CheckForeground 内部）；`Program.cs:103` ForegroundLost → AbortTakeover |
| 2c | ApplicationExit 可达 | ✅ | `Program.cs:231` `supp.Release();`（处理器首句） |
| 2d | 窗口关闭路径可达 | ✅ | `Program.cs:228` `host.FormClosing += delegate { supp.Release(); };` |
| 2e | 心跳超时路径 | ⏳ **Task 8** | 本任务按指示不实现；`supp` 声明位置已为其预留 |
| 3 | 夺取失败绝不调 `ClipCursor` | ✅ | `Suppressor.cs:74-78` `if (now != _own) { _log(...); return false; }` 位于 `Suppressor.cs:81` `bool clipped = ClipCursor(ref r);` 之前；全类唯一的钳制调用即 81 行 |
| 4 | 未连接绝不调 `Engage()` | ✅ | `Program.cs:77` `if (!transport.IsConnected)` 是 EnterTakeover 处理器首条语句；`supp.Engage()` 唯一调用点在 `Program.cs:83`，仅在该检查通过后可到达 |
| 附 | AttachThreadInput detach 无条件 | ✅ | `Suppressor.cs:64-68` try/finally，`finally` 内 `if (attached) AttachThreadInput(myThread, fgThread, false);` |

brief 列出的四条具名释放路径：OnFormClosing ✅（本任务）、ApplicationExit ✅（本任务）、前台丢失 ✅（本任务，guard 定时器 + CheckForeground）、心跳超时 ⏳（Task 8）。

## Program.cs 行数

**225 行**（红线 320，余量 95 行，够 Task 8/9 约 +66 行预算）。

## 变更文件

- `G:\pc-kvm\src\agent\Suppressor.cs`（新建，120 行）
- `G:\pc-kvm\src\agent\MessageHost.cs`（新建，38 行）
- `G:\pc-kvm\src\agent\EdgeTracker.cs`（+7 行：AbortTakeover）
- `G:\pc-kvm\src\agent\Program.cs`（净 -9 行：234 → 225）

## 自审发现

1. **EdgeTracker.cs 仍无 BOM**（与改动前一致）。它原就含中文注释且一直编译通过——csc 4.0 对无 BOM 文件按系统代码页（GBK）解读，注释会乱码但只是注释，无字符串字面量受影响。我没有改动它的编码，避免引入额外 diff。两个新文件均按项目硬约束写入 UTF-8 BOM（已验首三字节 EF BB BF）；Program.cs 的 BOM 经 Edit 后保留（已验）。
2. **`Timer` 无歧义**：Program.cs 未 `using System.Threading`，`Timer` 解析为 `System.Windows.Forms.Timer`，Tick 在 UI 线程触发，与 Engage（RawInput WndProc 线程）同线程，CheckForeground/Release 无线程安全问题。构建零警告佐证。
3. **AbortTakeover 的副作用是有意的**：`Armed=false` 要求用户把光标移出 12px 安全带后才重新武装，断线场景下不会在边缘反复触发 EnterTakeover 刷日志。
4. **未连接时 EnterTakeover 实际可触发的前提**：`cursor` 在断开后不置 null（只在 Connected 时赋值），故断线后推边缘确实会进 EnterTakeover——不变量 4 的连接检查正是为这条路径，已落实在首句。
5. brief Step 3 的运行时验证**未执行**（按指示：手机在线、用户在用机，真实 Engage 会钳住用户光标）。运行时验证留给用户在审查后监督进行。

## 无法验证项

- 夺取/钳制/释放的真实运行行为（含 taskkill 后系统释放 ClipCursor 的兜底演练）——需用户监督下运行 `dist\pc-kvm.exe` 验证，判定步骤见 brief Step 3。
