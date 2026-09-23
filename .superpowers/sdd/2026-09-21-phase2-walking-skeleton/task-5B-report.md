# Task 5B 报告：手机显示几何 —— 运行时读取 + 轮询检测旋转

**状态: DONE**
**提交: 0193f06** `fix(agent): 显示几何改为运行时读取 + 轮询检测旋转，修复硬编码竖屏导致的虚拟边界`（分支 `phase2-skeleton`）

## 实现内容

严格按 `task-5B-brief.md` 逐字落地，无即兴改动：

### 1. `src/agent/DeviceLauncher.cs`（+86 行，不改已有 `RunAdb`/`Prepare`/`Cleanup`/`Start`）
- `static int RunAdbCapture(string args, out string stdout)`：跑 adb 取回 stdout，失败返回 -1。
- `public static bool QueryDisplay(out int w, out int h)`：跑 `wm size; dumpsys window displays | grep mRotation`，失败返回 false。
- `public static bool ParseDisplaySize(string adbOutput, out int w, out int h)`：纯函数，无 I/O。Override size 优先于 Physical size，`ROTATION_90/270` 时宽高互换，未知旋转值（如 `ROTATION_LEFT`）解析为 -1 → 整体返回 false。
- 私有辅助 `ParseWxH` / `ParseRotation`。

### 2. `src/agent/CursorModel.cs`
- `readonly int _w/_h` → 去掉 `readonly`。
- 追加 `SetBounds(int w, int h)`：更新边界并把 X/Y 钳进新边界。

### 3. `src/agent/Program.cs`（152 → 186 行，红线 250 未超）
- (a) `Program` 类加三个 `static volatile` 字段：`_phoneW` / `_phoneH` / `_geometryChanged`。
- (b) `transport.Connected` 处理器：连接时 `DeviceLauncher.QueryDisplay` 读真实逻辑尺寸，失败回退 `2136x3200`；建 `CursorModel(_phoneW, _phoneH)`；置 `_geometryChanged = true`。
- (c) 托盘装配（`NotifyIcon tray = ...`）之前启动后台轮询线程：每 2 秒 `Sleep` → 未连接 `continue`（静默）→ `QueryDisplay` 失败 `continue`（静默，不刷日志）→ 尺寸变化才更新字段并置标志。`IsBackground = true`。
- (d) `MouseMoved` 里原 `if (cursorResetPending) {...}` 整段替换为 `if (_geometryChanged) {...}`（SetBounds → 日志 → HOME → Reset）；`bool cursorResetPending` 字段声明删除。外层 `if (cursor != null)` 与 `mc++`/降频日志块按控制器的消歧保持原样。
- 顺带修掉 Task 5 审查挂起的 deferred minor：跨线程未同步的普通 bool 换成了 `volatile bool`（brief 明示这是 Ruling 4 的提前执行）。

## 验证（离线两项，均通过）

### 1. 编译

命令（`G:\pc-kvm` 下）：

```
powershell -ExecutionPolicy Bypass -File build/build-agent.ps1
```

实际输出（乱码为控制台代码页显示问题，内容即脚本原文）：

```
编译 6 个源文件...
编译成功: G:\pc-kvm\dist\pc-kvm.exe  (18.5 KB)
已复制 pckvm.jar 到 dist\
EXITCODE=0
```

csc 零警告（无任何 warning 行输出），`$LASTEXITCODE` = 0。

### 2. 纯解析测试（10 向量）

测试程序 `C:\Users\mazhewen\AppData\Local\Temp\geotest\Main.cs`（ASCII only，C# 5，`using PcKvm;`），与 `src\agent\DeviceLauncher.cs` 一起编译：

```
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -target:exe `
    -out:"$env:TEMP\geotest\geotest.exe" `
    "$env:TEMP\geotest\Main.cs" "G:\pc-kvm\src\agent\DeviceLauncher.cs"
```

（注：第一次编译因 Main.cs 缺 `using PcKvm;` 报 CS0103，补上后即过——这是测试程序问题，非产品代码问题。）

运行 `geotest.exe` 实际 stdout：

```
vector 1: PASS  input=<Physical size: 2136x3200\nmRotation=ROTATION_90\n>  expected=3200x2136  actual=3200x2136
vector 2: PASS  input=<Physical size: 2136x3200\nmRotation=ROTATION_0\n>  expected=2136x3200  actual=2136x3200
vector 3: PASS  input=<Physical size: 2136x3200\nmRotation=ROTATION_270\n>  expected=3200x2136  actual=3200x2136
vector 4: PASS  input=<Physical size: 2136x3200\nOverride size: 1080x1920\nmRotation=ROTATION_0\n>  expected=1080x1920  actual=1080x1920
vector 5: PASS  input=<Override size: 1080x1920\nPhysical size: 2136x3200\nmRotation=ROTATION_90\n>  expected=1920x1080  actual=1920x1080
vector 6: PASS  input=<Physical size: 2136x3200\r\nmRotation=ROTATION_90\r\n>  expected=3200x2136  actual=3200x2136
vector 7: PASS  input=<>  expected=false  actual=false
vector 8: PASS  input=<Physical size: 2136x3200\n>  expected=false  actual=false
vector 9: PASS  input=<error: no devices/emulators found\n>  expected=false  actual=false
vector 10: PASS  input=<Physical size: 2136x3200\nmRotation=ROTATION_LEFT\n>  expected=false  actual=false
ALL 10 VECTORS PASS
```

退出码 0。真机 adb 步骤按控制器指示全部跳过（手机离线充电中）。

## 自审发现

1. **代码与 brief 逐行一致**：三段新增代码均从 brief 原文复制，未改签名、数值、字符串。git diff 复核通过。
2. **C# 5 合规**：`out` 参数全部先声明后传（`int a, b; ... out a`）；无 `?.`、`$""`、表达式体成员；`System.Threading.Thread` 全限定（Program.cs 无 `using System.Threading`）。
3. **`volatile` 只修饰静态字段**，轮询线程写 / UI 线程（MouseMoved）读的竞态按设计靠 volatile 可见性兜底。
4. **轮询无条件运行**，只判 `IsConnected`，没有提前造 Task 6 的状态机；掉线静默 continue。
5. **行数红线**：Program.cs 186/250 行，安全。
6. **已知但按 brief 保留的固有局限（非本任务引入）**：
   - `RunAdbCapture` 先 `ReadToEnd()` stdout 再读 stderr，若进程 stderr 输出量大（>pipe 缓冲）理论上可能死锁到 WaitForExit(5000) 超时——adb 这两条命令输出极小，且此写法是 brief 逐字要求，不改。
   - `WaitForExit(5000)` 超时后仍读 `p.ExitCode`：若进程 5 秒未退出，`ExitCode` 会抛 `InvalidOperationException`（HasExited=false），被 `catch (System.Exception)` 吞掉返回 -1，行为安全（返回失败）。
   - `QueryDisplay` 失败时 `w/h` 已被置 0——"失败不改动 out"的注释指 ParseDisplaySize 内部先赋 0 后返回，与 brief 原文一致。
7. **并发环境观察**：提交时发现 `docs/superpowers/plans/2026-09-21-phase2-walking-skeleton.md` 在我开始后被外部进程修改（初始 `git status` 干净，编辑期间出现改动）——是控制器/计划维护者所为，与本任务无关，未纳入提交，仍留在工作区。
8. git 提示这些源文件 LF→CRLF 警告：仓库既有状态如此（非本次引入），未处理。

## 未做（按 brief 明示范围外，等手机回线验收）

- 真机横/竖屏光标行为复验（Task 5 复验项）
- 旋转时 `# 几何已应用` + 重新归零对齐的实机验证
