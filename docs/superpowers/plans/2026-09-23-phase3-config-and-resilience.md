# 阶段三实施计划 —— 配置化 · NumLock · 断联自愈

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让用户能配置鼠标速度与手机所在侧、修掉 NumLock 状态不一致、给 exe 一个像样的图标，
并让 adb 断联后能自愈——同时把 `Program.cs` 从"只剩 1 行"的死局里救出来。

**Architecture:** 先做一个纯搬迁任务（`Watchers.cs`）腾出行数预算，再建一个极简的 INI 配置层
（`Config.cs` + `SettingsForm.cs`），然后五项功能各自独立落地。每项功能遵守既有纪律：
纯映射/纯逻辑放独立文件并带离线用例；`Program.cs` 只做装配。

**Tech Stack:** C# 5（`csc.exe 4.0.30319`，WinForms）/ Java 8（PyCharm JBR `javac`，设备侧走
`d8` → jar → `app_process`）/ UHID 虚拟输入设备 / adb 隧道

**Spec:** `docs/superpowers/specs/2026-09-23-phase3-config-and-resilience-design.md`

## Global Constraints

以下每一条对所有任务生效，不再逐个任务重复：

- **C# 5 语言上限**（本机唯一编译器 `csc.exe 4.0.30319`）：**禁止**内联 `out` 声明（`out int x`）、
  `?.`、`$"..."`、`nameof`、`using static`、表达式体成员。out 变量必须先声明后使用。
- **PC 侧绿色免安装单文件 exe**：所有 `.cs` 编进同一个 exe；`.ico` 用 `/win32icon:` **嵌入**；
  `pc-kvm.ini` 只在用户改过设置后才生成，exe 单独拷走必须照常工作。
- **手机侧零安装**：只 push 一个 jar 到 `/data/local/tmp`，不留 APK、不留图标。
- **禁止任何全局键盘钩子**：代码库中不得出现 `SetWindowsHookEx` / `WH_KEYBOARD_LL`。
  Raw Input（`RegisterRawInputDevices`）是被动观察，允许。
- **行数口径一律用 `wc -l`**：`Measure-Object -Line` 对 CRLF+BOM 文件不可信（Ruling 26 已栽过）。
- **每次构建后必须核实产物真的换了**：用 ASCII 扫二进制找新符号，不要只看时间戳
  （交接 §六：曾让用户白测一轮旧 exe）。
- **`.ps1` 文件必须带 UTF-8 BOM**：无 BOM 时非 ASCII 内容会被按 ANSI 误读。用 Write 工具新建的
  `.ps1` 一律先查前三字节（应为 `efbbbf`），必要时用
  `[IO.File]::WriteAllText($p, $c, (New-Object Text.UTF8Encoding($true)))` 补。
- **不要从 Git Bash 跑含 `/路径` 参数的命令**：MSYS 会改写它以 `/` 开头的参数（Ruling 12）。
  跑 `csc` / `adb shell` 这类一律用 PowerShell 或 `MSYS_NO_PATHCONV=1`。
- **手机侧判活判据是 `dumpsys input | grep -i PC-KVM`**，不是 `ps -A | grep Injector`
  （`ps -A` 只显示进程名 `app_process`，不含命令行——ledger 记过这个误报）。

## 开工前置（必须由控制方先做，不属任何任务）

1. **`pc-kvm.exe` 必须先退掉**：它此刻在跑（PID 会变），会锁住 `dist\pc-kvm.exe` 导致编译失败。
   **正确做法**：挂一个后台任务等进程消失后自动构建并回报，不要要求用户"退掉后通知我"——
   交接 §六 明写"协调会漏，本次就因此白测过一轮"。
2. **手机在线**：`adb devices` 必须见到设备。若掉线，先 `adb kill-server`，确认无 `adb.exe`
   残留进程，再 `adb start-server`。
3. **基线**：`git log --oneline -1` 应为 `8e510e5`（spec 提交）；`git status` 应干净。

---

## File Structure

| 文件 | 动作 | 职责 |
|---|---|---|
| `src/agent/Watchers.cs` | 新建 | 后台守护集中地：几何轮询线程 + 前台守卫 Timer + 心跳 Timer + 断联重连监督（任务 1、8） |
| `src/agent/TrayUi.cs` | 新建 | 托盘图标/菜单、设置入口、退出清理（任务 1；任务 3/5/8 就地扩展） |
| `src/agent/Config.cs` | 新建 | `pc-kvm.ini` 读写。`Parse` 纯函数、`Load`/`Save` 碰磁盘（任务 2） |
| `src/agent/SettingsForm.cs` | 新建 | 设置对话框：速度滑块 + 左/右 + 强杀 adb 开关（任务 3） |
| `src/agent/MouseScaler.cs` | 新建 | 原始增量 → 缩放增量，**带小数余量累积**（任务 4） |
| `src/agent/KeyMap.cs` | 修改 | 追加 NumLock 关时的数字键盘翻译（任务 7） |
| `src/agent/EdgeTracker.cs` | 修改 | edge 字段去 `readonly`；新增 `SetEdge`（任务 5） |
| `src/agent/DeviceLauncher.cs` | 修改 | `Prepare` 拆成 `EnsureTunnel`/`PushJar`；新增 `DeviceVisible`/`RestartAdbServer`/`KillAllAdb`（任务 8） |
| `src/agent/Program.cs` | 修改 | 只做装配：接配置、取 NumLock、换图标、把守护交给 `Watchers` |
| `src/injector/ScancodeMap.java` | 修改 | **只改注释**（现有注释是错的，见任务 7） |
| `build/build-agent.ps1` | 修改 | 加 `/win32icon:assets\pc-kvm.ico`（任务 6） |
| `assets/pc-kvm.ico` | 新建 | 多尺寸图标（任务 6，二进制入仓） |
| `tests/edge-tracker/Main.cs` | 修改 | 追加左挂镜像用例（任务 5） |
| `tests/agent-logic/{Main.cs,run.ps1}` | 新建 | 一个 harness 覆盖 `Config`/`MouseScaler`/`KeyMap`（任务 2、4、7） |

**为什么 `Config`/`MouseScaler`/`KeyMap` 共用一个 harness**：三者都是 PC 侧纯逻辑、无 UI 无 I/O，
编译输入是同一批文件，合成一个 runner 才不用维护三份几乎相同的 `.ps1`。
`tests/edge-tracker/` 与 `tests/scancode-map/` 保持原样不动（它们已经在跑，动了就是无谓的风险）。

---

### Task 1: P0 —— 抽 `Watchers.cs` + `TrayUi.cs`（纯搬迁）

**为什么必须先做**：`Program.cs` 实测 **359/360** 行（`wc -l`），Ruling 26 明文承诺
"此后若再超 360 必须抽取，不得再上调"。任务 3/5/6/7/8 都要往里加东西。

**为什么是两块而不是一块（Ruling 33，飞行前扫描的修正）**：初稿只搬三个 watcher（62 行），
实测估算后 `Program.cs` 会在本计划末尾落到 **约 371 行**——**踩破 360 红线 11 行**，
而任务 5/6/7/8 里都写着"≤360"的自检，那条自检必然踩空。精确测量（`wc -l` 口径）：

| 块 | 行号 | 行数 |
|---|---|---|
| 几何轮询线程 | 252-268 | 17 |
| 前台守卫定时器 | 278-282 | 5 |
| 心跳定时器 | 284-323 | 40 |
| 托盘 | 270-276 | 7 |
| 逃逸键 | 325-335 | 11 |
| 生命周期（FormClosing + ApplicationExit） | 337-346 | 10 |
| **合计** | | **90** |

搬 90 行、补回约 17 行 → `359−90+17 = 286`；本计划后续功能共加约 60 行 → **约 346，余量 14** ✅。
只搬 62 行则落在约 371 ❌。两块分别成文件是因为它们属**不同风险域**：
`Watchers` 管"后台存活性检查"（含 adb 进程 churn，审查者点名的风险区），
`TrayUi` 管"界面与生命周期"。合成一个文件会让 `Watchers` 变成杂物间。

**Files:**
- Create: `src/agent/Watchers.cs`
- Create: `src/agent/TrayUi.cs`
- Modify: `src/agent/Program.cs`（删六段、加接线）

**Interfaces:**
- Consumes: `Transport`、`EdgeTracker`、`Suppressor`、`MessageHost`、`Protocol`、`DeviceLauncher`（都已存在）
- Produces:
  - `Watchers(Transport transport, EdgeTracker tracker, Suppressor supp, MessageHost host, Action<string> log)`
  - `void Watchers.Start()`
  - `event Action<int,int> Watchers.GeometryQueried` —— **每次成功查到几何都抛（含未变化）**
  - `TrayUi(MessageHost host, Suppressor supp, Watchers watchers, Transport transport, Action<string> log, Process devProc)`
  - `void TrayUi.Install()`
  - `NotifyIcon TrayUi.Tray { get; }`（任务 3 往里加菜单项；**属性名是 `Tray` 不是 `NotifyIcon`**）

- [ ] **Step 1: 新建 `src/agent/Watchers.cs`**

**搬迁纪律**：逻辑逐字照抄，只改三处接口适应：
① 几何轮询的回调改为 `GeometryQueried` 事件；② 心跳里对 `Program` 静态字段的引用改为本类字段；
③ PONG 时间戳订阅搬进来（`Program.cs` 那个只负责打日志的订阅**保留**，两者不重复打日志）。

```csharp
using System;
using System.Windows.Forms;

namespace PcKvm
{
    /// <summary>
    /// Program.cs 的后台守护集中地（Ruling 26 承诺的抽取）。
    /// 理由不是行数好看：Program.cs 只剩 1 行预算，而"后台存活性检查"与"装配"本来就是两回事，
    /// 且这一族正是审查者点名的风险区（adb 进程 churn）。
    ///
    /// 线程纪律（不可违反）：
    ///  - guard 与 heartbeat **必须是 WinForms Timer**（UI 线程 tick）：它们的路径会碰
    ///    host/tracker/supp。换 System.Threading.Timer 会引入跨线程调用（Task 7 审查遗留隐患）。
    ///  - 几何轮询与重连监督是**后台线程**，它们**绝不直接碰控件或写日志**，
    ///    一律经 Report()/BeginInvoke 回到 UI 线程。
    /// </summary>
    public class Watchers
    {
        readonly Transport _transport;
        readonly EdgeTracker _tracker;
        readonly Suppressor _supp;
        readonly MessageHost _host;
        readonly Action<string> _log;

        long _lastPongTicks = DateTime.UtcNow.Ticks;
        uint _pingSeq;
        volatile bool _stop;            // 退出时置位，让后台线程自己收工
        System.Threading.Thread _geoPoll;
        Timer _guard;
        Timer _heartbeat;

        /// <summary>几何轮询每次**成功**查到都抛（含未变化值）。
        /// 消费方自行比较后决定是否应用——这样 Watchers 不持有第二份几何状态，
        /// 不会与 Program.cs 的副本漂移（那正是 Task 5B 踩过的"窄撕裂窗口"同类问题）。</summary>
        public event Action<int, int> GeometryQueried;

        public Watchers(Transport transport, EdgeTracker tracker, Suppressor supp,
                        MessageHost host, Action<string> log)
        {
            _transport = transport;
            _tracker = tracker;
            _supp = supp;
            _host = host;
            _log = log;

            // PONG 时间戳由 Watchers 自持。Program.cs 里那个 MessageReceived 订阅只负责
            // 打 "# PONG seq=" 一行，两者互不重复（Task 8 审查要求"不要新增订阅造成重复日志"）。
            _transport.MessageReceived += delegate(byte type, byte[] payload)
            {
                if (type == Protocol.MsgPong) _lastPongTicks = DateTime.UtcNow.Ticks;
            };

            // 逃逸键 Ctrl+Alt+Esc（安全网之一）。**放在 Watchers 而不是 TrayUi**（见 R6）：
            // 它要 tracker/supp/transport 三样，而 Watchers 的构造器本来就有；
            // 更要紧的是它做的四件事与上面 HeartbeatTick 里那条安全网**逐字相同**，
            // 放隔壁才能让"放弃序列"只有一处需要维护。
            _host.Escape += delegate
            {
                _log("# 逃逸键触发");
                // 放弃路径也必须通知设备侧清状态：接管期间若按着鼠标键/修饰键再逃逸，
                // 不补发 LEAVE 会让手机侧 buttonsDown 与按键槽位永久残留（leave 才会清）。
                // 设备侧处理 MSG_LEAVE 时会 buttonsDown=0 并 KeyState.releaseAll。
                _transport.Send(Protocol.EncodeLeave());
                _tracker.AbortTakeover();
                _supp.Release();
                _host.SetStatus("IDLE");
            };
        }

        public void Start()
        {
            _geoPoll = new System.Threading.Thread(delegate()
            {
                while (!_stop)
                {
                    System.Threading.Thread.Sleep(2000);
                    if (_stop) return;
                    if (!_transport.IsConnected) continue;   // 掉线时静默跳过，不刷日志
                    int w, h;
                    if (!DeviceLauncher.QueryDisplay(out w, out h)) continue;
                    Action<int, int> g = GeometryQueried;
                    if (g != null) g(w, h);
                }
            });
            _geoPoll.IsBackground = true;
            _geoPoll.Start();

            // 前台守卫：前台被抢走（UAC 安全桌面、锁屏、点击激活了别的窗口）时立即放弃抑制
            _guard = new Timer();
            _guard.Interval = 250;
            _guard.Tick += delegate { _supp.CheckForeground(); };
            _guard.Start();

            _heartbeat = new Timer();
            _heartbeat.Interval = 1000;
            _heartbeat.Tick += delegate { HeartbeatTick(); };
            _heartbeat.Start();
        }

        /// <summary>退出前调用（由 TrayUi 的 ApplicationExit 处理器调用）。
        /// 必须在 log.Close() 之前——否则后台线程会写进一个已关闭的 StreamWriter。
        /// 不 Join 线程：几何轮询是 IsBackground，最多 2 秒后自己看到 _stop 退出，
        /// 而进程此刻已经在收尾，不值得为它多等。</summary>
        public void Stop()
        {
            _stop = true;
            if (_guard != null) _guard.Stop();
            if (_heartbeat != null) _heartbeat.Stop();
        }

        void HeartbeatTick()
        {
            // 先做安全网：处于 TAKEOVER 时，「没连接」与「PONG 超时」都要立刻解除抑制。
            // 原写法把 `if (!IsConnected) return;` 放在最前，会在掉线后让安全网整体短路——
            // 用户此时再推到边缘会重新进入 TAKEOVER 并夺取前台，而没有东西能救他
            //（Task 7 安全不变量 4 / Ruling 18b，第二轮跨任务扫描发现）。
            if (_tracker.Current == KvmState.Takeover)
            {
                double age = (DateTime.UtcNow - new DateTime(_lastPongTicks)).TotalSeconds;
                if (!_transport.IsConnected || age > 2.0)
                {
                    _log("# 心跳失联（" + age.ToString("F1") + "s, connected="
                         + _transport.IsConnected + "），强制解除抑制");
                    // 放弃路径要通知设备侧清 buttonsDown/按键槽位（Ruling 25）；连接已断时 Send 是安全 no-op
                    _transport.Send(Protocol.EncodeLeave());
                    _tracker.AbortTakeover();
                    _supp.Release();
                    _host.SetStatus("IDLE");
                }
            }

            if (!_transport.IsConnected) return;
            _pingSeq++;
            _transport.Send(Protocol.EncodePing(_pingSeq));
        }
    }
}
```

- [ ] **Step 1b: 新建 `src/agent/TrayUi.cs`**

同样**逐字照抄**（托盘块 `:270-276` 与生命周期块 `:337-346`）。

> ⚠️ **逃逸键（`:325-335`）不在这里** —— 它搬进 `Watchers.cs` 的构造函数，理由见 Step 1 的
> 代码注释与 R6。初稿把"逃逸键"写进本步骤的文字却漏了代码，照抄会让 Ctrl+Alt+Esc
> 整条逃生通道失效（`host.Escape` 零订阅者）。**这是计划缺陷，已被实现者拦下。**

**两个新类都必须写成 `internal`（即不写修饰符）**，不能是 `public class`：
`MessageHost` 是 `class MessageHost : Form`（internal），而 public 类的 public 构造器
不能接收 internal 参数类型 → **CS0051 编译失败**。既然约束是"不得修改既有文件"，
降级新类就是唯一解；单程序集内零运行时差异。
（后续任务不受影响：`Config`/`MouseScaler`/`SettingsForm` 只吃 public 类型。）
`StreamWriter` 直接传进来（而不是 `Action<string>`），因为这里要用它的 `Close()`。

```csharp
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PcKvm
{
    /// <summary>
    /// 托盘图标、设置入口与退出清理（阶段三 P0 抽取的第二块，见计划任务 1 的 Ruling 33）。
    /// 与 Watchers 分家的理由：Watchers 管"后台存活性检查"（含 adb 进程 churn），
    /// 这里管"界面与生命周期"，属两个不同的风险域；合成一个文件会让 Watchers 变成杂物间。
    /// 本类不参与任何输入或状态机逻辑。
    /// </summary>
    public class TrayUi
    {
        readonly MessageHost _host;
        readonly Suppressor _supp;
        readonly Watchers _watchers;
        readonly Transport _transport;
        readonly StreamWriter _log;
        readonly Process _devProc;

        public NotifyIcon Tray { get; private set; }

        public TrayUi(MessageHost host, Suppressor supp, Watchers watchers,
                      Transport transport, StreamWriter log, Process devProc)
        {
            _host = host;
            _supp = supp;
            _watchers = watchers;
            _transport = transport;
            _log = log;
            _devProc = devProc;
        }

        /// <summary>建托盘、装菜单、订阅生命周期事件。必须在 Application.Run 之前调用。</summary>
        public void Install()
        {
            Tray = new NotifyIcon();
            Tray.Icon = SystemIcons.Application;   // 任务 6 换成 exe 自带的那份
            Tray.Text = "PC-KVM";
            Tray.Visible = true;

            MenuItem quit = new MenuItem("退出");
            quit.Click += delegate { Application.Exit(); };
            // 任务 3 会在这两行之间插入「设置…」
            Tray.ContextMenu = new ContextMenu(new MenuItem[] { quit });

            _host.FormClosing += delegate { _supp.Release(); };
            Application.ApplicationExit += delegate
            {
                // 顺序要紧：先停后台监督（否则它会与 _log.Close() 抢），再释放抑制、收设备。
                _watchers.Stop();
                _supp.Release();
                _log.WriteLine("# 退出");
                Tray.Visible = false;
                _transport.Stop();
                DeviceLauncher.Cleanup(_devProc);
                _log.Close();
            };
        }
    }
}
```

- [ ] **Step 2: 修改 `src/agent/Program.cs` —— 删掉六段被搬走的代码**

删除以下六段（**逐字删除，不要顺手改动别处**；行号是本次的实测值，实施时按内容定位）：

| 段 | 原行号 | 起止 |
|---|---|---|
| 几何轮询线程 | 252-268 | `System.Threading.Thread rotPoll = ...` 到 `rotPoll.Start();` |
| 托盘 | 270-276 | `NotifyIcon tray = new NotifyIcon();` 到 `tray.ContextMenu = ...;` |
| 前台守卫定时器 | 278-282 | 注释 `// 前台守卫：...` 到 `guard.Start();` |
| 心跳 | 284-323 | `// ---- Task 8 安全网 ...` 到 `heartbeat.Start();`，**含其中那个只更新 `lastPongTicks` 的 `transport.MessageReceived` 订阅**（已搬进 Watchers） |
| 逃逸键 | 325-335 | `host.Escape += delegate` 整块 —— **搬进 `Watchers.cs` 的构造函数**（不是 `TrayUi.cs`，见 R6） |
| 生命周期 | 337-346 | `host.FormClosing += delegate` 与 `Application.ApplicationExit += delegate` 两块 |

> ⚠️ 删除**生命周期**那一段时注意：它里面的 `tray.Visible = false;` 与 `Transport`/`DeviceLauncher`
> 调用都已在 `TrayUi.Install()` 里落地；`Application.Run(host);` 这一行**不要删**。

- [ ] **Step 3: 在 `Program.cs` 里接上 `Watchers`**

**声明位置是承重的，照抄下面这段的位置，不要挪**：`watchers` 必须在
`Application.ApplicationExit` 订阅**之前**声明（任务 8 会在那个处理器里调 `watchers.Stop()` 与
`watchers.DeviceProcess`），而 C# 局部变量不支持前向引用——Task 4/7/8 都在这一类上返工过。

**所以分两处放**：

① 构造 + 订阅几何事件，放在 `EdgeTracker tracker = new EdgeTracker(...)` **紧接着的后面**
（构造只依赖 `transport/tracker/supp/host/log`，这些都是更早声明的局部量）：

```csharp
            // 后台守护集中到 Watchers（Ruling 26 的抽取，见该类头注释的线程纪律）。
            // ⚠️ 必须声明在 TrayUi 的 Install() 之前（它会调 watchers.Stop()），
            // 而 C# 局部变量不能前向引用（Task 4/7/8 都栽过这一类）。
            // 但 Start() 要等最后才调——过早启动重连监督会抢在首次建隧道之前动手。
            Watchers watchers = new Watchers(transport, tracker, supp, host,
                delegate(string s) { log.WriteLine(s); });
            watchers.GeometryQueried += delegate(int w, int h)
            {
                // 与原实现等价：只有真的变了才应用。比较集中在此，Watchers 不再持第二份状态。
                // ⚠️ 此处理器跑在**几何轮询线程**上，所以里面**不许有任何 I/O**
                //（Task 1 审查 Important 1：初稿在这里加过一行 log.WriteLine，退出时
                // 轮询线程可能在 log.Close() 之后才抛出事件 → 后台线程未捕获异常终结进程）。
                // 只允许字段赋值。
                if (w == _phoneW && h == _phoneH) return;
                _phoneW = w; _phoneH = h;
                _geometryChanged = true;
            };
```

② `TrayUi` 的构造与 `Install()`，放在**原托盘那段的位置**（`DeviceLauncher.Start()` 之后，
因为要传 `devProc` 进去）：

```csharp
            // 托盘与生命周期集中到 TrayUi（Ruling 33）。必须在 Application.Run 之前 Install。
            TrayUi trayUi = new TrayUi(host, supp, watchers, transport, log, devProc);
            trayUi.Install();
```

③ `watchers.Start();` —— 放在 `Application.Run(host);` **那一行之前**（即 `Main` 的最后）：

```csharp
            watchers.Start();
            Application.Run(host);
```

> 注意：原实现在轮询里比较、变化时只置 `_geometryChanged`，**不打日志**；而 `MouseMoved`
> 消费时才打 `# 几何已应用 WxH`。初稿曾在这里加一行 `# 检测到几何变化`，
> **已在 Task 1 审查后被删除**（R8）：它跑在几何轮询线程上，而 `Stop()` 只置标志不 Join，
> 于是退出时一个正卡在 `QueryDisplay`（adb 调用，最长 5s）里的轮询迭代可能在 `log.Close()`
> **之后**写一个已关闭的 `StreamWriter` → 后台线程未捕获异常**终结进程**。
> 那行的诊断价值边际很小（消费点本来就有 `# 几何已应用`），故删除而非加 marshal。

- [ ] **Step 4: 编译并核实行数真的降了**

Run（PowerShell，**不要用 Git Bash**）：
```powershell
pwsh -File G:\pc-kvm\build\build-agent.ps1
```
Expected: `编译成功`、exit 0、**零警告**。

然后：
```bash
cd /g/pc-kvm && wc -l src/agent/Program.cs
```
Expected: **约 286 行**（原 359，移出实测 90 行、加回约 17 行）。若仍在 320 以上说明没删干净
——**不要就此继续**，那意味着后面几个任务的自检会踩空 360 红线（这正是 Ruling 33 的成因）。

- [ ] **Step 5: 跑既有两个 harness，确认搬迁没碰坏纯逻辑**

```powershell
pwsh -File G:\pc-kvm\tests\edge-tracker\run.ps1
pwsh -File G:\pc-kvm\tests\scancode-map\run.ps1
```
Expected: `TOTAL: pass=15 fail=0` 与 `TOTAL: 45/45 passed, 0 failed`，均 exit 0。

- [ ] **Step 6: 逐条核对搬迁等价性（审查者也要做这一步）**

对着 `git diff` 逐项确认，**任何一项不符就报 BLOCKED，不要自行"顺手修好"**：

| 检查项 | 期望 |
|---|---|
| 守卫间隔 | 仍是 250ms |
| 心跳间隔 | 仍是 1000ms |
| 心跳安全网的**位置** | 仍在 `if (!IsConnected) return;` **之前** |
| 超时判据 | 仍是 `age > 2.0` 秒 |
| 几何轮询周期 | 仍是 2000ms |
| 掉线时轮询行为 | 仍 `continue` 静默跳过、不刷日志 |
| 放弃路径三件事 | 仍都做：`Send(EncodeLeave)` → `AbortTakeover()` → `Release()` → `SetStatus("IDLE")` |
| Timer 类型 | 仍是 `System.Windows.Forms.Timer`（全文搜 `System.Threading.Timer` 应**零命中**） |
| 托盘菜单项 | 仍只有「退出」（「设置…」是任务 3 加的） |
| 逃逸键四件事 | 仍都做：`Send(EncodeLeave)` → `AbortTakeover()` → `Release()` → `SetStatus("IDLE")`；且 `# 逃逸键触发` 仍**最先**打。核实：`rg -n 'host\.Escape\s*\+=' src/agent/*.cs` 必须**恰 1 处**且在 `Watchers.cs`，`Program.cs` 里**零命中** |
| `FormClosing` | 仍只调 `supp.Release()` |
| 退出顺序 | `Release` → 写 `# 退出` → 托盘隐藏 → `transport.Stop` → `Cleanup` → `log.Close`，**顺序不变** |

**有意与原文不同的地方（两处，都写在这里免得审查者当成回归）**：

1. 退出路径的**最前面**多了一步 `watchers.Stop()`。这是**必须的**：`Watchers` 现在有后台线程，
   不在 `log.Close()` 之前停掉它，就可能写进一个已关闭的 `StreamWriter` 而抛异常。
   原实现的后台线程只在几何轮询里（不写日志），所以以前不需要这一步。
2. 托盘文本由 `"PC-KVM（阶段二骨架）"` 改为 `"PC-KVM"`。那个"阶段二骨架"标签已经过时
   （现在做阶段三），Step 3 的意图也是 `"PC-KVM"`。**初稿漏列了这一条**，由实现者发现。

> 初稿还有第三处（几何变化日志行），已在 Task 1 审查后**删除**——理由见 Step 3 末尾的注（R8）。

- [ ] **Step 7: Commit**

```bash
cd /g/pc-kvm
git add src/agent/Watchers.cs src/agent/TrayUi.cs src/agent/Program.cs
git commit -F - <<'EOF'
refactor: 抽 Watchers.cs + TrayUi.cs —— 后台守护与托盘/生命周期（Ruling 26 的承诺）

Program.cs 实测 359/360 行（wc -l 口径），只剩 1 行预算，而阶段三的配置化、
NumLock 翻译、图标、重连监督都要往里加东西。Ruling 26 已明文承诺
"此后若再超 360 必须抽取，不得再上调"，本任务兑现它。

**为什么是两个文件而不是一个（飞行前扫描修正了初稿）**：初稿只搬三个 watcher（实测 62 行），
按实测值推演，本计划末尾 Program.cs 会落到约 371 行——踩破 360 红线 11 行，而任务 5/6/7/8
里都写着 "≤360" 的自检。实测各块：几何轮询 17 + 前台守卫 5 + 心跳 40 + 托盘 7 + 逃逸键 11
+ 生命周期 10 = 90 行；搬走并补回约 17 行接线后 Program.cs 约 286 行，本计划后续功能
共加约 60 行 → 约 346，余量 14。
分两个文件是因为它们属**不同风险域**：Watchers 管"后台存活性检查"（含 adb 进程 churn，
审查者点名的风险区），TrayUi 管"界面与生命周期"。合成一个会让 Watchers 变成杂物间。

纯搬迁，零逻辑改动。三处接口适应：
1. 几何轮询的回调改为 GeometryQueried 事件（每次成功查询都抛，消费方比较）——
   这样 Watchers 不持有第二份几何状态，不会与 Program.cs 的副本漂移。
2. 心跳原来引用 Program 的静态字段（_phoneW 等），改为本类字段。
3. PONG 时间戳订阅搬进来；Program.cs 里那个只打日志的订阅保留，两者不重复打日志。

有意新增两处（已在计划里声明，免得被当成回归）：
- 几何变化时多打一行 "# 检测到几何变化 WxH"。阶段二排查几何问题时正是因为分不清
  "没查到"与"没变化"而多花了一轮。
- 退出路径最前面加了一步 watchers.Stop()：现在 Watchers 有后台线程，不在 log.Close()
  之前停掉它就可能写进已关闭的 StreamWriter。原实现的后台线程不写日志，故以前不需要。

线程纪律写进了类头注释：guard/heartbeat 必须是 WinForms Timer（tick 会碰 host/tracker/supp），
几何轮询是后台线程且绝不直接碰控件。

Co-Authored-By: Claude Code <noreply@anthropic.com>
EOF
```

---

### Task 2: `Config.cs` —— INI 读写（含离线 harness 骨架）

**Files:**
- Create: `src/agent/Config.cs`
- Create: `tests/agent-logic/Main.cs`
- Create: `tests/agent-logic/run.ps1`

**Interfaces:**
- Produces:
  - 常量 `Config.SensitivityMin/Max`（0.10/3.00）、`Config.ReconnectSecondsMin/Max`（2/60）
  - 字段 `double MouseSensitivity`（默认 0.50）、`bool PhoneOnLeft`（默认 false）、
    `bool AllowKillAdb`（默认 false）、`int ReconnectSeconds`（默认 5）
  - `static Config Config.Parse(string text, List<string> warnings)` —— **纯函数**
  - `static Config Config.Load(Action<string> log)` —— 读 `<exe目录>/pc-kvm.ini`，文件不存在则返回默认值
  - `bool Config.Save()` —— 写回，失败返回 false（不抛）

- [ ] **Step 1: 写失败用例 —— `tests/agent-logic/Main.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using PcKvm;

static class AgentLogicTest
{
    static int _pass = 0, _fail = 0;

    static void Check(string name, bool ok, string detail)
    {
        if (ok) { _pass++; Console.WriteLine("PASS " + name + "  " + detail); }
        else { _fail++; Console.WriteLine("FAIL " + name + "  " + detail); }
    }

    static Config P(string text)
    {
        List<string> w = new List<string>();
        return Config.Parse(text, w);
    }

    static void Main()
    {
        // ---- C1: 空文本 / null → 全默认 ----
        Config c = P("");
        Check("C1 空文本=默认", c.MouseSensitivity == 0.50 && !c.PhoneOnLeft
            && !c.AllowKillAdb && c.ReconnectSeconds == 5,
            "sens=" + c.MouseSensitivity + " left=" + c.PhoneOnLeft
            + " kill=" + c.AllowKillAdb + " rec=" + c.ReconnectSeconds);

        // ---- C2: 完整读取 ----
        c = P("MouseSensitivity=1.25\nPhoneSide=Left\nAllowKillAdb=true\nReconnectSeconds=12\n");
        Check("C2 完整读取", c.MouseSensitivity == 1.25 && c.PhoneOnLeft
            && c.AllowKillAdb && c.ReconnectSeconds == 12,
            "sens=" + c.MouseSensitivity + " left=" + c.PhoneOnLeft
            + " kill=" + c.AllowKillAdb + " rec=" + c.ReconnectSeconds);

        // ---- C3: 注释、空行、空白、大小写、CRLF ----
        c = P("; 注释\r\n\r\n  mouseSENSITIVITY = 0.75  \r\n# 另一种注释\r\nphoneside=right\r\n");
        Check("C3 注释/空白/大小写/CRLF", c.MouseSensitivity == 0.75 && !c.PhoneOnLeft,
            "sens=" + c.MouseSensitivity + " left=" + c.PhoneOnLeft);

        // ---- C4: 越界与非法值一律回退默认，且**不影响其它项** ----
        List<string> w4 = new List<string>();
        c = Config.Parse("MouseSensitivity=99\nReconnectSeconds=abc\nPhoneSide=Left\n", w4);
        Check("C4 坏值回退且不牵连", c.MouseSensitivity == 0.50 && c.ReconnectSeconds == 5
            && c.PhoneOnLeft && w4.Count == 2,
            "sens=" + c.MouseSensitivity + " rec=" + c.ReconnectSeconds
            + " left=" + c.PhoneOnLeft + " warnings=" + w4.Count);

        // ---- C5: 小数用不变文化解析（避免逗号小数点地区踩坑） ----
        c = P("MouseSensitivity=0.35\n");
        Check("C5 小数点为句点", c.MouseSensitivity == 0.35, "sens=" + c.MouseSensitivity);

        // ---- C6: 未知键被忽略但不致命 ----
        List<string> w6 = new List<string>();
        c = Config.Parse("NotAKey=1\nMouseSensitivity=2.00\n", w6);
        Check("C6 未知键忽略", c.MouseSensitivity == 2.00 && w6.Count == 1,
            "sens=" + c.MouseSensitivity + " warnings=" + w6.Count);

        // ---- C7: 边界值恰好合法 ----
        Check("C7 边界合法", P("MouseSensitivity=0.10\n").MouseSensitivity == 0.10
            && P("MouseSensitivity=3.00\n").MouseSensitivity == 3.00
            && P("ReconnectSeconds=2\n").ReconnectSeconds == 2
            && P("ReconnectSeconds=60\n").ReconnectSeconds == 60,
            "0.10/3.00/2/60 均应被接受");

        // ---- C8: 恰好越界的边界值要被拒 ----
        Check("C8 边界越界被拒", P("MouseSensitivity=0.09\n").MouseSensitivity == 0.50
            && P("MouseSensitivity=3.01\n").MouseSensitivity == 0.50
            && P("ReconnectSeconds=1\n").ReconnectSeconds == 5
            && P("ReconnectSeconds=61\n").ReconnectSeconds == 5,
            "0.09/3.01/1/61 均应回退");

        Console.WriteLine("TOTAL: pass=" + _pass + " fail=" + _fail);
        if (_fail > 0) Environment.Exit(1);
    }
}
```

- [ ] **Step 2: 写 runner —— `tests/agent-logic/run.ps1`**

（写完**立刻查前三字节**，必须是 `efbbbf`；不是就用 PowerShell 的
`[IO.File]::WriteAllText($p, $c, (New-Object Text.UTF8Encoding($true)))` 补 BOM。）

```powershell
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$src  = Join-Path $root 'src\agent'
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) { throw "找不到 csc.exe: $csc" }

# 每次从零编译仓库当前源码，绝不复用 %TEMP% 里的预编译产物
#（本项目栽过一次"验的不是用户实际会跑的那一份"）
$out = Join-Path $env:TEMP 'pckvm-tests\agent-logic'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force -Path $out | Out-Null

$sources = @(
    (Join-Path $PSScriptRoot 'Main.cs'),
    (Join-Path $src 'Config.cs'),
    (Join-Path $src 'MouseScaler.cs'),
    (Join-Path $src 'KeyMap.cs')
)
# 尚不存在的源文件先跳过（本任务只测 Config；后续任务逐个补上）
$sources = $sources | Where-Object { Test-Path $_ }
if ($sources.Count -lt 2) { throw "待编译的源文件不足" }

Write-Host "编译 PC 侧纯逻辑 harness（$($sources.Count) 个文件）..." -ForegroundColor Cyan
& $csc -nologo -target:exe -platform:x64 -out:"$out\agent-logic.exe" $sources
if ($LASTEXITCODE -ne 0) { throw "编译失败" }

& "$out\agent-logic.exe"
if ($LASTEXITCODE -ne 0) { throw "PC 侧纯逻辑用例未全过" }

Write-Host "PC 侧纯逻辑: 全部用例通过" -ForegroundColor Green
```

- [ ] **Step 3: 跑 runner 确认失败**

```powershell
pwsh -File G:\pc-kvm\tests\agent-logic\run.ps1
```
Expected: **编译失败** —— `error CS0246: 找不到类型或命名空间名称"Config"`（`Config.cs` 还没建）。
这就是 RED。

- [ ] **Step 4: 实现 `src/agent/Config.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PcKvm
{
    /// <summary>
    /// exe 同目录 pc-kvm.ini 的读写。解析与磁盘 I/O 分开：Parse 是纯函数（可离线测），
    /// Load/Save 才碰文件。
    ///
    /// 设计原则：**配置坏掉绝不影响可用性**。解析失败 / 键缺失 / 值越界一律回退默认值，
    /// 回退明细经 warnings 交调用方记**一行**日志。绝不弹模态框
    ///（本项目栽过"模态 MessageBox 永久阻塞"的坑）。
    ///
    /// 默认值 = 阶段二行为：PhoneSide=Right、AllowKillAdb=false 时，
    /// 把 exe 单独拷到一台新机器上跑起来与阶段二完全一致。
    /// </summary>
    public class Config
    {
        public const double SensitivityMin = 0.10;
        public const double SensitivityMax = 3.00;
        public const int ReconnectSecondsMin = 2;
        public const int ReconnectSecondsMax = 60;

        public const string FileName = "pc-kvm.ini";

        public double MouseSensitivity = 0.50;
        public bool PhoneOnLeft = false;      // false = 手机在 PC 右侧（阶段二行为）
        public bool AllowKillAdb = false;
        public int ReconnectSeconds = 5;

        /// <summary>纯函数：解析 INI 文本。warnings 收到每一条回退/忽略的说明（可为 null）。</summary>
        public static Config Parse(string text, List<string> warnings)
        {
            Config c = new Config();
            if (text == null) return c;

            string[] lines = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string s = lines[i].Trim();
                if (s.Length == 0 || s[0] == ';' || s[0] == '#') continue;
                int eq = s.IndexOf('=');
                if (eq <= 0)
                {
                    Warn(warnings, "无法解析的行（已忽略）: " + s);
                    continue;
                }
                string k = s.Substring(0, eq).Trim();
                string v = s.Substring(eq + 1).Trim();

                if (Same(k, "MouseSensitivity"))
                {
                    double d;
                    // 必须用 InvariantCulture：本机是中文区，但配置文件里的小数点是句点
                    if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)
                        && d >= SensitivityMin && d <= SensitivityMax)
                        c.MouseSensitivity = d;
                    else
                        Warn(warnings, "MouseSensitivity=" + v + " 无效（需 "
                             + SensitivityMin + "–" + SensitivityMax + "），用默认 " + c.MouseSensitivity);
                }
                else if (Same(k, "PhoneSide"))
                {
                    if (Same(v, "Left")) c.PhoneOnLeft = true;
                    else if (Same(v, "Right")) c.PhoneOnLeft = false;
                    else Warn(warnings, "PhoneSide=" + v + " 无效（需 Left/Right），用默认 Right");
                }
                else if (Same(k, "AllowKillAdb"))
                {
                    bool b;
                    if (TryParseBool(v, out b)) c.AllowKillAdb = b;
                    else Warn(warnings, "AllowKillAdb=" + v + " 无效（需 true/false），用默认 false");
                }
                else if (Same(k, "ReconnectSeconds"))
                {
                    int n;
                    if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)
                        && n >= ReconnectSecondsMin && n <= ReconnectSecondsMax)
                        c.ReconnectSeconds = n;
                    else
                        Warn(warnings, "ReconnectSeconds=" + v + " 无效（需 "
                             + ReconnectSecondsMin + "–" + ReconnectSecondsMax + "），用默认 "
                             + c.ReconnectSeconds);
                }
                else
                {
                    Warn(warnings, "未知配置项（已忽略）: " + k);
                }
            }
            return c;
        }

        /// <summary>读 exe 同目录的 pc-kvm.ini。文件不存在 = 首次运行，直接返回默认值（不生成文件）。</summary>
        public static Config Load(Action<string> log)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
            List<string> warnings = new List<string>();
            Config c;
            try
            {
                if (!File.Exists(path)) return new Config();
                c = Parse(File.ReadAllText(path), warnings);
            }
            catch (Exception ex)
            {
                if (log != null) log("# 配置读取失败，全部用默认值：" + ex.Message);
                return new Config();
            }
            if (log != null && warnings.Count > 0)
                for (int i = 0; i < warnings.Count; i++) log("# 配置: " + warnings[i]);
            return c;
        }

        /// <summary>写回。失败返回 false（不抛）——调用方应记一行日志，但不要因此中断程序。</summary>
        public bool Save()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("; PC-KVM 配置。改完保存即可，不需要重启 exe（下次按键事件即生效）。");
                sb.AppendLine("; 删掉本文件 = 全部回到默认值。exe 单独拷走也能跑。");
                sb.AppendLine("; MouseSensitivity: " + SensitivityMin + "–" + SensitivityMax + "，默认 0.50");
                sb.AppendLine("; PhoneSide: Left | Right，默认 Right");
                sb.AppendLine("; AllowKillAdb: true | false，默认 false（会打断本机其它用 adb 的工具）");
                sb.AppendLine("; ReconnectSeconds: " + ReconnectSecondsMin + "–" + ReconnectSecondsMax + "，默认 5");
                sb.AppendLine();
                sb.AppendLine("MouseSensitivity=" + MouseSensitivity.ToString("F2", CultureInfo.InvariantCulture));
                sb.AppendLine("PhoneSide=" + (PhoneOnLeft ? "Left" : "Right"));
                sb.AppendLine("AllowKillAdb=" + (AllowKillAdb ? "true" : "false"));
                sb.AppendLine("ReconnectSeconds=" + ReconnectSeconds);
                // 带 BOM：这个文件是给人用记事本改的，BOM 让任何编辑器都能正确识别编码
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static void Warn(List<string> warnings, string s)
        {
            if (warnings != null) warnings.Add(s);
        }

        static bool Same(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        static bool TryParseBool(string v, out bool result)
        {
            result = false;
            if (Same(v, "true") || v == "1") { result = true; return true; }
            if (Same(v, "false") || v == "0") { result = false; return true; }
            return false;
        }
    }
}
```

- [ ] **Step 5: 跑 runner 确认全过**

```powershell
pwsh -File G:\pc-kvm\tests\agent-logic\run.ps1
```
Expected: `C1`–`C8` 全 `PASS`，`TOTAL: pass=8 fail=0`，exit 0。

- [ ] **Step 6: 在 `tests/README.md` 补一行**

在依赖表加一行：

```markdown
| `agent-logic` | 同 `edge-tracker`（`csc.exe`）；覆盖 `Config` / `MouseScaler` / `KeyMap` |
```
并在"跑法"那段补上：
```powershell
pwsh -File tests\agent-logic\run.ps1
```

- [ ] **Step 7: Commit**

```bash
cd /g/pc-kvm
git add src/agent/Config.cs tests/agent-logic tests/README.md
git commit -F - <<'EOF'
feat: 配置层 —— exe 旁 pc-kvm.ini 的解析与读写

阶段二整个项目没有任何配置机制（端口、几何、phoneRight、sensitivity 全是字面量），
本任务补上它，#1 速度与 #2 手机在哪侧共用。

- Parse 是纯函数（可离线测），Load/Save 才碰磁盘，故能在 harness 里直接喂文本。
- 坏文件策略：解析失败/值越界/未知键一律回退默认值，回退明细经 warnings 交给调用方
  记一行日志。绝不弹模态框——本项目栽过"模态 MessageBox 永久阻塞"。
- 默认值 = 阶段二行为（PhoneSide=Right、AllowKillAdb=false）：exe 单独拷到新机器
  跑起来与阶段二一致，硬约束"绿色免安装单文件"不破。
- 写文件带 BOM：它是给人用记事本改的。
- 小数解析显式用 InvariantCulture。

离线用例 8 条（C1–C8）：空文本、完整读取、注释/空白/大小写/CRLF、坏值回退且不牵连
其它项、小数句点、未知键忽略、边界值恰好合法、恰好越界被拒。
新建 tests/agent-logic harness（后续任务往里补 MouseScaler 与 KeyMap 用例）。

Co-Authored-By: Claude Code <noreply@anthropic.com>
EOF
```

---

### Task 3: `SettingsForm.cs` + 托盘「设置…」

**Files:**
- Create: `src/agent/SettingsForm.cs`
- Modify: `src/agent/TrayUi.cs`（加「设置…」菜单项 + `SettingsApplied` 事件 + 吃下 `Config`）
- Modify: `src/agent/Program.cs`（读配置 + 订阅 `SettingsApplied`）

**Interfaces:**
- Consumes: `Config`（任务 2）
- Produces:
  - `SettingsForm(Config current)` 构造
  - 属性 `double MouseSensitivity`、`bool PhoneOnLeft`、`bool AllowKillAdb`
  - `event Action<Config> TrayUi.SettingsApplied`
  - `TrayUi` 构造函数**多加一个末位参数** `Config cfg`（任务 1 里还没有它，本任务加）

- [ ] **Step 1: 实现 `src/agent/SettingsForm.cs`**

```csharp
using System;
using System.Drawing;
using System.Windows.Forms;

namespace PcKvm
{
    /// <summary>
    /// 设置对话框。托盘右键「设置…」打开。
    ///
    /// 为什么是对话框而不是托盘菜单项：鼠标速度必须**边调边感受**，
    /// 菜单项的「速度 +」「速度 −」做不到这一点。
    ///
    /// 滑块内部用整数 10–300 表示 0.10–3.00；拖动后吸附到 0.05 的整数倍（与 spec 一致）。
    /// </summary>
    public class SettingsForm : Form
    {
        const int MinHundredths = 10;    // 0.10
        const int MaxHundredths = 300;   // 3.00
        const int StepHundredths = 5;    // 0.05

        readonly TrackBar _speed;
        readonly Label _speedValue;
        readonly RadioButton _left;
        readonly RadioButton _right;
        readonly CheckBox _killAdb;

        public double MouseSensitivity { get; private set; }
        public bool PhoneOnLeft { get; private set; }
        public bool AllowKillAdb { get; private set; }

        public SettingsForm(Config current)
        {
            Text = "PC-KVM 设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(360, 230);

            Label l1 = new Label();
            l1.Text = "手机上的鼠标速度";
            l1.Location = new Point(16, 16);
            l1.AutoSize = true;
            Controls.Add(l1);

            _speedValue = new Label();
            _speedValue.Location = new Point(280, 16);
            _speedValue.AutoSize = true;
            Controls.Add(_speedValue);

            _speed = new TrackBar();
            _speed.Minimum = MinHundredths;
            _speed.Maximum = MaxHundredths;
            _speed.SmallChange = StepHundredths;
            _speed.LargeChange = StepHundredths * 4;
            _speed.TickFrequency = StepHundredths * 10;
            _speed.Location = new Point(12, 40);
            _speed.Width = 330;
            int init = (int)Math.Round(current.MouseSensitivity * 100);
            if (init < MinHundredths) init = MinHundredths;
            if (init > MaxHundredths) init = MaxHundredths;
            _speed.Value = init;
            _speed.ValueChanged += delegate { OnSpeedChanged(); };
            Controls.Add(_speed);

            Label l2 = new Label();
            l2.Text = "手机在显示器的";
            l2.Location = new Point(16, 105);
            l2.AutoSize = true;
            Controls.Add(l2);

            _left = new RadioButton();
            _left.Text = "左侧";
            _left.Location = new Point(140, 103);
            _left.AutoSize = true;
            Controls.Add(_left);

            _right = new RadioButton();
            _right.Text = "右侧";
            _right.Location = new Point(220, 103);
            _right.AutoSize = true;
            Controls.Add(_right);
            if (current.PhoneOnLeft) _left.Checked = true; else _right.Checked = true;

            _killAdb = new CheckBox();
            _killAdb.Text = "断联时允许强杀 adb 进程（最后一招）";
            _killAdb.Location = new Point(16, 135);
            _killAdb.AutoSize = true;
            _killAdb.Checked = current.AllowKillAdb;
            Controls.Add(_killAdb);

            Label l3 = new Label();
            l3.Text = "会打断本机其它正在用 adb 的工具（如 InputShare）。默认关闭。";
            l3.ForeColor = SystemColors.GrayText;
            l3.Location = new Point(34, 158);
            l3.AutoSize = true;
            Controls.Add(l3);

            Button ok = new Button();
            ok.Text = "确定";
            ok.DialogResult = DialogResult.OK;
            ok.Location = new Point(180, 190);
            ok.Click += delegate { Capture(); };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(266, 190);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            OnSpeedChanged();   // 初始显示数值
        }

        void OnSpeedChanged()
        {
            int v = _speed.Value;
            // 吸附到 0.05 的整数倍（spec 的步进要求）。重赋值会再触发一次 ValueChanged，
            // 那一轮 v 已等于吸附值、直接走下面的显示逻辑，不会无限递归。
            int snapped = ((v + (StepHundredths / 2)) / StepHundredths) * StepHundredths;
            if (snapped < MinHundredths) snapped = MinHundredths;
            if (snapped > MaxHundredths) snapped = MaxHundredths;
            if (snapped != v) { _speed.Value = snapped; return; }
            _speedValue.Text = ((double)v / 100.0).ToString("F2");
        }

        void Capture()
        {
            MouseSensitivity = (double)_speed.Value / 100.0;
            PhoneOnLeft = _left.Checked;
            AllowKillAdb = _killAdb.Checked;
        }
    }
}
```

- [ ] **Step 2: 在 `TrayUi.cs` 里加「设置…」，并把"应用设置"交回 `Program.cs`**

**先读配置**：在 `Program.cs` 的 `Main` 里、`log` 创建**之后**加一行（`cfg` 会被后面的
`scaler`、`tracker`、`TrayUi` 构造使用，所以必须声明得足够早）：

```csharp
            Config cfg = Config.Load(delegate(string s) { log.WriteLine(s); });
```

并把 `trayUi` 的构造点改为多传一个 `cfg`：

```csharp
            TrayUi trayUi = new TrayUi(host, supp, watchers, transport, log, devProc, cfg);
            trayUi.Install();
```

**设计要点（Ruling 33 的连带修正）**：托盘菜单现在住在 `TrayUi.cs` 里，而"应用设置"要用到
`scaler` / `tracker` / `supp` / `transport` —— 那些都是 `Program.cs` 的局部量。所以
`TrayUi` **只负责弹对话框与写盘**，应用行为通过事件交回组合根。

在 `TrayUi.cs` 里加：

```csharp
        /// <summary>设置对话框点了确定、且配置已更新并写盘之后抛出。
        /// 应用行为（改速度、换跨越边）由 Program.cs 订阅处理——它才持有 scaler/tracker/supp。
        /// 本类只做界面与持久化，不碰运行时状态。</summary>
        public event Action<Config> SettingsApplied;
```

在 `Install()` 里，把「退出」那两行之间插入「设置…」：

```csharp
            MenuItem settings = new MenuItem("设置…");
            settings.Click += delegate
            {
                using (SettingsForm f = new SettingsForm(_cfg))
                {
                    if (f.ShowDialog(_host) != DialogResult.OK) return;
                    _cfg.MouseSensitivity = f.MouseSensitivity;
                    _cfg.AllowKillAdb = f.AllowKillAdb;
                    _cfg.PhoneOnLeft = f.PhoneOnLeft;
                    if (!_cfg.Save())
                        _log.WriteLine("# 配置写盘失败（设置本次仍生效，只是下次启动会丢）");
                }
                Action<Config> h = SettingsApplied;
                if (h != null) h(_cfg);
            };
            MenuItem quit = new MenuItem("退出");
            quit.Click += delegate { Application.Exit(); };
            Tray.ContextMenu = new ContextMenu(new MenuItem[] { settings, quit });
```

这要求 `TrayUi` 也拿到 `Config`：把构造函数改为
`TrayUi(MessageHost host, Suppressor supp, Watchers watchers, Transport transport, StreamWriter log, Process devProc, Config cfg)`
并在字段区加 `readonly Config _cfg;`（构造里赋值）。**任务 1 里先不加这个参数**——
它是任务 3 才引入的，任务 3 的实现者负责同时改构造函数与 `Program.cs` 的调用点。

在 `Program.cs` 里，`trayUi.Install();` 之后加订阅（本任务先只处理速度之外的两项，
速度在任务 4 接、跨越边在任务 5 接）：

```csharp
            trayUi.SettingsApplied += delegate(Config c)
            {
                log.WriteLine("# 设置已应用：速度=" + c.MouseSensitivity.ToString("F2")
                              + " 手机在" + (c.PhoneOnLeft ? "左" : "右") + "侧"
                              + " 强杀adb=" + c.AllowKillAdb);
            };
```

- [ ] **Step 3: 编译**

```powershell
pwsh -File G:\pc-kvm\build\build-agent.ps1
```
Expected: `编译成功`、exit 0、零警告。

- [ ] **Step 4: 核实 `Program.cs` 仍在下限内**

```bash
cd /g/pc-kvm && wc -l src/agent/Program.cs
```
Expected: **≤ 360**。若超了，停下来报 BLOCKED——Ruling 26 不允许再上调红线。

- [ ] **Step 5: 用机器可核的手段代替 GUI 清单（R10）**

> ⚠️ **原 Step 5 的"启动 exe 手工看托盘/对话框"已作废，改为下面的机器检查。**
> 三条理由：①子代理看不见也点不了 GUI；②手机不在线时 `DeviceLauncher.Prepare` 会失败并弹
> **模态 MessageBox 永久阻塞**（本项目有记录的同类事故）；③跑起来的 app 会锁住
> `dist\pc-kvm.exe`，后面 4 个任务都编不了。
> GUI 那几项**并入任务 4/5 的真机验收**（那时本来就要用户在场，而设置对话框正是给速度与
> 手机侧用的）。

**顺带补一个 spec 缺口**：spec §11 要求给 `Config` 写「**写回往返**」用例，而已有的 8 条
（C1–C8）**完全没覆盖 `Save()`**。故本步骤在 `tests/agent-logic/Main.cs` 追加两条：

- **C9 —— `Save()` → `Load()` 往返**：构造一个四项全非默认的 `Config`，`Save()` 后
  `Config.Load(null)`，断言四个值原样回来。`Save`/`Load` 用的是
  `AppDomain.CurrentDomain.BaseDirectory`，在 harness 里就是 `%TEMP%\pckvm-tests\agent-logic\`
  输出目录，故**自包含**；测完删掉自己写的文件，保证重跑干净。
- **C10 —— `Save()` 写出的文件是 UTF-8 带 BOM 且键名正确**：读前三字节断言
  `0xEF,0xBB,0xBF`；断言文本含 `MouseSensitivity=1.25` 与 `PhoneSide=Left`。

runner 的计数从 `pass=8 fail=0` 变为 **`pass=10 fail=0`**（报实际值）。

**另外两条机器检查（原始输出贴进报告）**：

```powershell
rg -n 'MenuItem' src/agent/TrayUi.cs
# 期望：恰两个菜单项（设置… 与 退出）
rg -n 'Config.Load|SettingsApplied' src/agent/Program.cs src/agent/TrayUi.cs
# 期望：1 次 Load 调用、1 处事件声明、1 处订阅
```

**以下原 GUI 清单已移交用户**（并入任务 4/5 的真机验收，那时用户在场；**实现者不要执行、也不要启动 exe**）：

- [ ] 托盘右键能看到「设置…」与「退出」两项
- [ ] 设置对话框打开后，滑块显示当前值（首次应为 `0.50`）
- [ ] 拖动滑块时右侧数值实时变化，且只出现在 `0.05` 的倍数上（试 `0.10`、`1.35`、`3.00`）
- [ ] 选「左侧」+ 勾选强杀 → 确定 → `dist\pc-kvm.ini` 被创建，内容含 `PhoneSide=Left`、`AllowKillAdb=true`
- [ ] 再打开设置，控件状态与上次一致（配置真的被读回）
- [ ] 把 ini 里的 `MouseSensitivity` 改成 `abc`，重启 exe，日志出现一行 `# 配置: MouseSensitivity=abc 无效...`，程序照常启动

- [ ] **Step 6: Commit**

```bash
cd /g/pc-kvm
# 四个文件都要提交：本任务同时改了 TrayUi.cs（加菜单项与事件），且 R10 追加了两条用例。
# 初稿这里只列了两个文件——那会提交出一棵编译不过的树（实现者拦下并改用全部四个）。
git add src/agent/SettingsForm.cs src/agent/TrayUi.cs src/agent/Program.cs tests/agent-logic/Main.cs
git commit -F - <<'EOF'
feat: 设置对话框 —— 速度滑块 + 手机在左/右 + 强杀 adb 开关

做成对话框而不是托盘菜单项的理由：鼠标速度必须边调边感受，
菜单项的「速度 +」「速度 −」做不到这一点。

- 滑块内部用整数 10–300 表示 0.10–3.00，拖动后吸附到 0.05 的倍数（spec 的步进要求）。
- 确定 = 应用 + 写回 pc-kvm.ini；写盘失败只记一行日志，不影响本次生效。
- 托盘菜单从只有「退出」变为「设置…」+「退出」。
- Program.cs 启动时 Config.Load 一次；坏配置的提示由 Load 自己打日志。

本任务只改配置值；"手机在哪侧"对边界的影响在任务 5 落地（那里会调 tracker.SetEdge）。

Co-Authored-By: Claude Code <noreply@anthropic.com>
EOF
```

---

### Task 4: #1 —— 鼠标速度（含小数余量累积）

**Files:**
- Create: `src/agent/MouseScaler.cs`
- Modify: `src/agent/Program.cs`（用配置值 + 换成 `MouseScaler`）
- Modify: `tests/agent-logic/Main.cs`（追加用例）

**Interfaces:**
- Consumes: `Config.MouseSensitivity`（任务 2）
- Produces:
  - `MouseScaler(double sensitivity)`；`void SetSensitivity(double s)`
  - `int MouseScaler.ApplyX(int rawDx)` / `int ApplyY(int rawDy)` —— 返回**本次应发送**的整数增量
  - `void MouseScaler.Reset()`

- [ ] **Step 1: 追加失败用例到 `tests/agent-logic/Main.cs`**

在 `Console.WriteLine("TOTAL: ...")` **之前**插入：

```csharp
        // ---- S1: 系数 1.0 时逐位透传 ----
        MouseScaler ms = new MouseScaler(1.0);
        Check("S1 系数1透传", ms.ApplyX(7) == 7 && ms.ApplyX(-3) == -3 && ms.ApplyY(11) == 11,
            "7/-3/11");

        // ---- S2: 低系数下小位移【不丢】——这正是原实现 (int)(dx*s) 的病 ----
        // 系数 0.5 时连推 3 次 dx=1 应累计出 1 像素（1*0.5=0.5，第三次到 1.5 → 1，
        // 余 0.5）。原写法 (int)(1*0.5)=0 → 三次全丢，表现为"慢速走不动"。
        ms = new MouseScaler(0.5);
        int a1 = ms.ApplyX(1), a2 = ms.ApplyX(1), a3 = ms.ApplyX(1);
        Check("S2 低系数小位移不丢", a1 == 0 && a2 == 1 && a3 == 0,
            "三次 dx=1 @0.5 → " + a1 + "/" + a2 + "/" + a3 + "（期望 0/1/0）");

        // ---- S3: 反向对称（负方向同样累积，不能单向吞掉） ----
        ms = new MouseScaler(0.5);
        int b1 = ms.ApplyX(-1), b2 = ms.ApplyX(-1);
        Check("S3 负方向对称", b1 == 0 && b2 == -1,
            "-1/-1 @0.5 → " + b1 + "/" + b2 + "（期望 0/-1）");

        // ---- S4: 两轴独立 ----
        ms = new MouseScaler(0.5);
        ms.ApplyX(1); ms.ApplyX(1);          // X 余量到 1
        Check("S4 两轴互不干扰", ms.ApplyY(1) == 0,
            "X 累积不应影响 Y");

        // ---- S5: 高系数（放大） ----
        ms = new MouseScaler(2.0);
        Check("S5 放大", ms.ApplyX(3) == 6 && ms.ApplyY(-4) == -8, "3*2=6 / -4*2=-8");

        // ---- S6: Reset 清掉小数余量 ----
        ms = new MouseScaler(0.5);
        ms.ApplyX(1);                         // 余 0.5
        ms.Reset();
        Check("S6 Reset 清残差", ms.ApplyX(1) == 0,
            "Reset 后单次 dx=1 @0.5 应为 0（若残差未清会是 1）");

        // ---- S7: SetSensitivity 立即生效 ----
        ms = new MouseScaler(0.5);
        ms.SetSensitivity(2.0);
        Check("S7 改系数立即生效", ms.ApplyX(3) == 6, "改为 2.0 后 3*2=6");

        // ---- S8: 长时间小位移不漂移（累积误差必须闭合） ----
        // 200 次 dx=1 @0.5 恰好应发出 100 像素，一个不多一个不少。
        ms = new MouseScaler(0.5);
        int sum = 0;
        for (int i = 0; i < 200; i++) sum += ms.ApplyX(1);
        Check("S8 长期不漂移", sum == 100, "200 次 dx=1 @0.5 共发 " + sum + "（期望 100）");
```

- [ ] **Step 2: 跑 runner 确认失败**

```powershell
pwsh -File G:\pc-kvm\tests\agent-logic\run.ps1
```
Expected: 编译失败 `error CS0246: ... "MouseScaler"`。RED。

- [ ] **Step 3: 实现 `src/agent/MouseScaler.cs`**

```csharp
using System;

namespace PcKvm
{
    /// <summary>
    /// 原始鼠标增量 → 实际发送增量的缩放器，**带小数余量累积**。
    ///
    /// 为什么不能直接 (int)(dx * sensitivity)：系数小于 1 时小位移会被截断成 0
    /// （dx=1、系数 0.5 → 0 像素），表现为**慢速移动发涩、细调走不动**——
    /// 而那正是低灵敏度下最常用的场景，等于把滑块往低调就直接废掉这个功能。
    /// 故每轴保留一个 double 残差，把不足 1 像素的部分带到下一次。
    ///
    /// 纯逻辑、无 I/O、无 UI，故独立成文件（Program.cs 只剩 1 行预算，Ruling 26 禁令）
    /// 且可离线测（tests/agent-logic）。
    /// </summary>
    public class MouseScaler
    {
        double _sensitivity;
        double _resX;
        double _resY;

        public MouseScaler(double sensitivity)
        {
            _sensitivity = sensitivity;
        }

        public void SetSensitivity(double sensitivity)
        {
            _sensitivity = sensitivity;
        }

        /// <summary>进入接管/几何变化时清零残差，避免带着上一次的零头。</summary>
        public void Reset()
        {
            _resX = 0;
            _resY = 0;
        }

        public int ApplyX(int rawDx)
        {
            _resX += rawDx * _sensitivity;
            // 向零截断：与"发送整数增量"的语义一致（不能用 Floor，那会让负方向多走一格）
            int send = (int)Math.Truncate(_resX);
            _resX -= send;
            return send;
        }

        public int ApplyY(int rawDy)
        {
            _resY += rawDy * _sensitivity;
            int send = (int)Math.Truncate(_resY);
            _resY -= send;
            return send;
        }
    }
}
```

- [ ] **Step 4: 跑 runner 确认全过**

```powershell
pwsh -File G:\pc-kvm\tests\agent-logic\run.ps1
```
Expected: `C1`–`C8`、`S1`–`S8` 全 `PASS`，`TOTAL: pass=16 fail=0`，exit 0。

- [ ] **Step 5: 在 `Program.cs` 里接线**

把原来的 `double sensitivity = 1.0;`（`Program.cs:68` 附近）整行**删除**，改为在它附近创建缩放器：

```csharp
            // 速度缩放器（含小数余量累积）。必须声明在 ri.MouseMoved 订阅之前：
            // 处理器会捕获它，而 C# 局部变量不支持前向引用（Task 4/7 都栽过）
            MouseScaler scaler = new MouseScaler(cfg.MouseSensitivity);
```

把 `MouseMoved` 里接管分支的两行：

```csharp
                        short sdx = cursor.NextDx((int)(e.Dx * sensitivity));
                        short sdy = cursor.NextDy((int)(e.Dy * sensitivity));
```

改为：

```csharp
                        // 缩放器输出的已是"应发送"的整数增量，仍要过 CursorModel 的钳制
                        short sdx = cursor.NextDx(scaler.ApplyX(e.Dx));
                        short sdy = cursor.NextDy(scaler.ApplyY(e.Dy));
```

在 `tracker.EnterTakeover` 处理器里，`cursor.Reset();` 那一行**之后**加：

```csharp
                scaler.Reset();   // 归零的同时清掉小数余量，避免带着跨越前的零头
```

在**任务 3 建立的那个 `trayUi.SettingsApplied` 处理器**里，把日志那行之前加上速度的应用
（`scaler` 是 `Program.cs` 的局部量，所以只能写在这里，不能写进 `TrayUi.cs`）：

```csharp
            trayUi.SettingsApplied += delegate(Config c)
            {
                scaler.SetSensitivity(c.MouseSensitivity);   // 立即生效，不必重启
                log.WriteLine("# 设置已应用：速度=" + c.MouseSensitivity.ToString("F2")
                              + " 手机在" + (c.PhoneOnLeft ? "左" : "右") + "侧"
                              + " 强杀adb=" + c.AllowKillAdb);
            };
```

- [ ] **Step 6: 编译 + 核实行数 + 跑既有 harness**

```powershell
pwsh -File G:\pc-kvm\build\build-agent.ps1
pwsh -File G:\pc-kvm\tests\edge-tracker\run.ps1
```
Expected: 编译零警告 exit 0；`TOTAL: pass=15 fail=0`。

```bash
cd /g/pc-kvm && wc -l src/agent/Program.cs
```
Expected: ≤ 360。

- [ ] **Step 7: 真机验收（需用户在场）**

前提：手机在线；用户已退出旧的 `pc-kvm.exe` 并重新启动新的。

1. 进接管，鼠标推到底 → 手机光标**能到物理边缘**
2. 打开设置，把速度调到 `0.20` → 确定 → **不重启**，立刻试移动：应明显变慢，且**慢速细推能一格一格动**（不是"推半天不动然后猛跳"）
3. 调到 `3.00` → 应明显变快
4. 调回 `1.00` → 手感与阶段二一致
5. 退出设置对话框后确认**仍能正常接管**（设置窗口抢过前台，不应留下任何抑制残留）

- [ ] **Step 8: Commit**

```bash
cd /g/pc-kvm
git add src/agent/MouseScaler.cs src/agent/Program.cs tests/agent-logic/Main.cs
git commit -F - <<'EOF'
feat(#1): 鼠标速度可配置 —— 含小数余量累积（修一个手感缺陷）

Program.cs:68 本来就有 double sensitivity = 1.0 并在 :148-149 用它，只是硬编码、
没有任何途径改。本任务把它接到配置层（配置在任务 2 已就位）。

同时修一个**手感缺陷**：原写法 (int)(e.Dx * sensitivity) 在系数 < 1 时会把小位移
截断成 0（dx=1、系数 0.5 → 0 像素），表现为慢速移动发涩、细调走不动——而那正是
低灵敏度下最常用的场景，等于把滑块往低调就直接废掉这个功能。
改为每轴维护 double 残差、向零截断后把零头带到下一次（不能用 Floor，那会让负方向
多走一格，破坏反向对称）。

- 缩放同时作用于"发给手机的增量"与"虚拟光标模型"（cursor.NextDx 收的是缩放后值），
  两者仍自洽。
- 不受影响：回程阈值（单位是原始 mickey）、入屏比例映射（由绝对坐标算）。
- 进接管时 scaler.Reset()，避免带着跨越前的零头。
- 设置对话框改速度立即生效（SetSensitivity），不必重启。

离线用例 8 条（S1–S8）：透传、低系数小位移不丢、负方向对称、两轴独立、放大、
Reset 清残差、改系数立即生效、200 次小位移不漂移。

Co-Authored-By: Claude Code <noreply@anthropic.com>
EOF
```

---

### Task 5: #2 —— 手机在哪侧（运行时入口边 + 左挂回归）

**Files:**
- Modify: `src/agent/EdgeTracker.cs`（4 个 edge 字段去 `readonly`，新增 `SetEdge`）
- Modify: `src/agent/Program.cs`（运行时推导入口边 + 设置回调里 `SetEdge`）
- Modify: `tests/edge-tracker/Main.cs`（追加左挂镜像用例）

**Interfaces:**
- Consumes: `Config.PhoneOnLeft`（任务 2）
- Produces: `void EdgeTracker.SetEdge(int edgeX, int edgeTop, int edgeBottom, bool phoneRight)`

- [ ] **Step 1: 追加失败用例到 `tests/edge-tracker/Main.cs`**

在 `Console.WriteLine("TOTAL: ...")` **之前**插入。**先加工具函数**（放在 `NewTracker()` 旁边）：

```csharp
    // 左挂镜像：手机在 PC 左侧 → 从手机【右】边缘入屏（phoneX = phoneW-1 = 3199），
    // 与 PC 相邻的那条边是手机的逻辑右缘。PC 侧 _edgeX = 0（桌面左缘可达列）。
    static EdgeTracker NewTrackerLeft()
    {
        return new EdgeTracker(edgeX: 0, edgeTop: 0, edgeBottom: 1080,
            phoneW: 3200, phoneH: 2136, phoneRight: false);
    }
```

再插入用例：

```csharp
        // ---- L1（左挂镜像）: 从桌面左缘入屏，落点为手机右边缘 x=3199，y 仍比例映射 ----
        // 与 T1 严格镜像：cursorY=540 → phoneY = 540*2136/1080 = 1068
        {
            EdgeTracker t = NewTrackerLeft();
            int en = 0; short px = -1, py = -1;
            t.EnterTakeover += delegate(short x, short y) { en++; px = x; py = y; };
            t.OnIdleMove(-5, 0, 0, 540);          // 在左缘、继续向左推
            Check("L1", t.Current == KvmState.Takeover && en == 1 && px == 3199 && py == 1068,
                "state=" + t.Current + " en=" + en + " px=" + px + " py=" + py
                + " (expect Takeover,1,3199,1068)");
        }

        // ---- L2（左挂）: 左缘但朝屏内推 → 不触发 ----
        {
            EdgeTracker t = NewTrackerLeft();
            int en = 0;
            t.EnterTakeover += delegate(short x, short y) { en++; };
            t.OnIdleMove(5, 0, 0, 540);           // 向右推 = 朝屏内
            Check("L2", t.Current == KvmState.Idle && en == 0,
                "state=" + t.Current + " en=" + en + " (expect Idle,0)");
        }

        // ---- L3（左挂）: 离开左缘一列（x=1）→ 不触发 ----
        {
            EdgeTracker t = NewTrackerLeft();
            int en = 0;
            t.EnterTakeover += delegate(short x, short y) { en++; };
            t.OnIdleMove(-5, 0, 1, 540);
            Check("L3", t.Current == KvmState.Idle && en == 0,
                "state=" + t.Current + " en=" + en + " (expect Idle,0)");
        }

        // ---- L4（左挂）: 入口处微小【右】向抖动不得回程（根因 B 的左挂镜像） ----
        // T11 是右挂版的 -1 抖动；左挂的"向外推"是 +dx，故抖动是 +1。
        // 入屏瞬间 _lastVx 被置为 phoneX = 3199（手机右缘），所以 +1 会被计入外推，
        // 但 1 < 40 阈值，必须仍留在接管态。这条钉住"阈值在左挂下同样生效"。
        {
            EdgeTracker t = NewTrackerLeft();
            CursorModel c = new CursorModel(3200, 2136);
            int lv = 0;
            t.LeaveTakeover += delegate { lv++; };
            t.OnIdleMove(-5, 0, 0, 540);            // 进接管，_lastVx = 3199
            c.SetPosition(3199, 1068);              // ⚠️ 必须显式定位：Program.cs 在 EnterTakeover 里
                                                    // 做的是 Reset() + SetPosition(px,py)，
                                                    // 而 CursorModel 的初值是 (0,0)。
                                                    // 初稿漏了这一句 → 4 条 L 用例失败。
            Feed(t, c, 1, 0);                       // 入口处向右抖 1 mickey
            Check("L4", t.Current == KvmState.Takeover && lv == 0 && t.BackPush == 1,
                "state=" + t.Current + " leave=" + lv + " backPush=" + t.BackPush
                + " (expect Takeover,0,1)");
        }

        // ---- L5（左挂）: "走到边界"不算外推，在边界上继续外推 ≥40 才回程 ----
        // 分三步，与 T12/T13 的右挂语义严格镜像。
        {
            EdgeTracker t = NewTrackerLeft();
            CursorModel c = new CursorModel(3200, 2136);
            int lv = 0;
            t.LeaveTakeover += delegate { lv++; };
            t.OnIdleMove(-5, 0, 0, 540);            // 进接管，vx=3199
            c.SetPosition(3199, 1068);              // 同上：必须显式定位（初稿漏了，L5 会失败）
            Feed(t, c, -120, 0);                    // 往屏内走
            Check("L5a", t.Current == KvmState.Takeover && c.X == 3079,
                "state=" + t.Current + " vx=" + c.X + " (expect Takeover,3079)");
            Feed(t, c, 120, 0);                     // 又滑回右缘：这是"走到边界"，不得算外推
            Check("L5b", t.Current == KvmState.Takeover && c.X == 3199 && t.BackPush == 0,
                "state=" + t.Current + " vx=" + c.X + " backPush=" + t.BackPush
                + " (expect Takeover,3199,0)");
            Feed(t, c, 20, 0);                      // 在边界上外推 20
            Feed(t, c, 0, 3);                       // 纯纵向事件：既不累积也不清零
            Feed(t, c, 25, 0);                      // 20 + 25 = 45 >= 40
            Check("L5c", t.Current == KvmState.Idle && lv == 1,
                "state=" + t.Current + " leave=" + lv + " (expect Idle,1)");
        }

        // ---- L6（左挂）: SetEdge 切换后立刻解除武装（防止换边瞬间被弹过去） ----
        {
            EdgeTracker t = NewTracker();
            t.OnIdleMove(5, 0, 3839, 540);         // 右挂下先进入接管
            Check("L6a", t.Current == KvmState.Takeover, "state=" + t.Current);
            t.AbortTakeover();
            t.SetEdge(0, 0, 1080, false);           // 切成左挂
            bool armed = t.Armed;
            int en = 0;
            t.EnterTakeover += delegate(short x, short y) { en++; };
            t.OnIdleMove(-5, 0, 0, 540);            // 此刻光标恰在新边缘
            Check("L6b", !armed && t.Current == KvmState.Idle && en == 0,
                "armedAfterSetEdge=" + armed + " state=" + t.Current + " en=" + en
                + " (expect False, Idle, 0)");
        }
```

- [ ] **Step 2: 跑 runner 确认失败**

```powershell
pwsh -File G:\pc-kvm\tests\edge-tracker\run.ps1
```
Expected: 编译失败 `error CS1061: "EdgeTracker" 不包含"SetEdge"的定义`。RED。

- [ ] **Step 3: 实现 `EdgeTracker.SetEdge`**

把 `EdgeTracker.cs:14-19` 的四个字段去掉 `readonly`：

```csharp
        int _edgeX;                     // 触发侧最外侧有效像素列的 x（右挂 = 桌面宽-1，左挂 = 0）。
                                        // 约定：不是"边界外那条虚线"，而是光标真实可达的最后一列——
                                        // Windows 把光标钳在最后一列内，GetCursorPos 永远到不了桌面宽。
                                        // 左右两侧在此约定下天然对称（atEdge/安全带判定无需分侧特判）。
        int _edgeTop;
        int _edgeBottom;
        int _phoneW;                    // 可变：旋转/尺寸变化时由 SetPhoneSize 更新
        int _phoneH;
        bool _phoneRight;               // true = 手机在 PC 右侧（此时从手机左边缘入屏）
```

并按如下注释更新构造函数的 doc：

```csharp
        /// <summary>phoneW/phoneH 为占位初值（竖屏 2136x3200），非权威常量；
        /// 真值在连接后首次鼠标移动、及每次旋转变化时经 SetPhoneSize 灌入（Task 5B 几何轮询）。
        /// edgeX 约定：触发侧最外侧有效像素列的 x（右挂传桌面宽-1，左挂传 0），见字段注释。</summary>
```

在 `SetPhoneSize` **之后**新增：

```csharp
        /// <summary>运行时改变跨越边（阶段三 #2：手机在左/右可配置）。
        /// **刻意不重建 tracker**：EnterTakeover/LeaveTakeover 的订阅挂在对象上，
        /// 重建会丢订阅（那是"接不到事件"的静默失效）。故只改字段。
        /// 同时解除武装：换边后光标很可能正好落在新边缘上，等它离开安全带再重新武装，
        /// 免得用户刚点完"确定"就被弹进接管。</summary>
        public void SetEdge(int edgeX, int edgeTop, int edgeBottom, bool phoneRight)
        {
            _edgeX = edgeX;
            _edgeTop = edgeTop;
            _edgeBottom = edgeBottom;
            _phoneRight = phoneRight;
            Armed = false;
            _backPush = 0;
        }
```

- [ ] **Step 4: 跑 runner 确认全过**

```powershell
pwsh -File G:\pc-kvm\tests\edge-tracker\run.ps1
```
Expected: `T1`–`T15`（15 条）、`L1`–`L6b`（9 条）全 `PASS`，`TOTAL: pass=24 fail=0`，exit 0。

- [ ] **Step 5: 在 `Program.cs` 里改用运行时推导的入口边**

在 `Program.cs` 的 `POINT` 结构体附近加：

```csharp
        const int SM_XVIRTUALSCREEN = 76;
        const int SM_YVIRTUALSCREEN = 77;
        const int SM_CXVIRTUALSCREEN = 78;
        const int SM_CYVIRTUALSCREEN = 79;

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int nIndex);
```

把 `EdgeTracker tracker = new EdgeTracker(edgeX: 3839, edgeTop: 0, edgeBottom: 1080, ...)`
那一整块（`Program.cs:69-74` 附近，**含那两行写死坐标的注释**）替换为：

```csharp
            // 入口边**运行时**从真实虚拟桌面推导，不写魔数：写死 3839 时，
            // 换一套显示器布局会让入口条件永远不可达——正是 Task 6 审查栽过的静默 bug。
            // edgeX 约定 = 触发侧最外侧"有效像素列"：右侧取 W-1（Windows 把光标钳在最后一列内），
            // 左侧取 0。edgeBottom 是**开区间**（EdgeTracker.cs 判定为 cursorY >= _edgeBottom 时拒绝），
            // 故直接传桌面高度。
            int screenX = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int screenY = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int screenW = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int screenH = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            // ⚠️ 原点也要读（R12）。只用尺寸会把原点默认成 (0,0)，而**任何把显示器挂在主屏
            // 左侧或上方的布局**原点都不是 (0,0)：此时左边缘应是 `SM_X` 而非 0、右边缘应是
            // `SM_X + W - 1` 而非 `W - 1`，且 `cursorX >= W - 1` 可能永远不可达 —— 正是本功能
            // 要防的"入口条件永远不可达"。只堵尺寸变化是不够的。
            // 尺寸是判成败的判据（必须为正）；**原点允许为负**，故不能用 <= 0 判失败。
            if (screenW <= 0 || screenH <= 0)
            {
                screenX = 0; screenY = 0; screenW = 3840; screenH = 1080;
            }
            log.WriteLine("# 桌面几何 x=" + screenX + " y=" + screenY + " "
                          + screenW + "x" + screenH
                          + "，手机在" + (cfg.PhoneOnLeft ? "左" : "右") + "侧");

            EdgeTracker tracker = new EdgeTracker(
                edgeX: cfg.PhoneOnLeft ? screenX : screenX + screenW - 1,
                edgeTop: screenY, edgeBottom: screenY + screenH,
                phoneW: 2136, phoneH: 3200, phoneRight: !cfg.PhoneOnLeft);
```

- [ ] **Step 6: 在 `SettingsApplied` 处理器里接上 `SetEdge`**

任务 3 建的 `SettingsApplied` 处理器现在必须处理"换了哪一侧"。**注意 `TrayUi` 已经把
`_cfg.PhoneOnLeft` 改好并写盘了**，处理器只需负责运行时那部分（`tracker` 换边）：

把任务 4 之后的处理器整体改为：

```csharp
            trayUi.SettingsApplied += delegate(Config c)
            {
                scaler.SetSensitivity(c.MouseSensitivity);   // 立即生效，不必重启
                // 正在接管就先干净地退出来（补发 LEAVE、解锁光标、还前台），
                // 否则换边会让 tracker 停在 TAKEOVER 却对着新的边界判定
                if (tracker.Current == KvmState.Takeover)
                {
                    transport.Send(Protocol.EncodeLeave());
                    tracker.AbortTakeover();
                    supp.Release();
                    host.SetStatus("IDLE");
                }
                tracker.SetEdge(c.PhoneOnLeft ? screenX : screenX + screenW - 1,
                                screenY, screenY + screenH, !c.PhoneOnLeft);
                log.WriteLine("# 设置已应用：手机在" + (c.PhoneOnLeft ? "左" : "右")
                              + "侧（edgeX=" + (c.PhoneOnLeft ? 0 : screenW - 1) + "）"
                              + " 速度=" + c.MouseSensitivity.ToString("F2")
                              + " 强杀adb=" + c.AllowKillAdb);
            };
```

> `screenW` / `screenH` / `tracker` / `scaler` / `transport` / `supp` / `host` 都是 `Main` 的
> 局部量，被这个回调捕获——C# 闭包合法（`supp` 更是早就为 Task 8 提前声明在 transport 之前）。
> 那次"先 AbortTakeover 再 SetEdge"的顺序不是讲究：`SetEdge` 会把 `Armed` 置 false，
> 而 `AbortTakeover` 也会；两者都跑没有副作用，但**先退接管**是为了不让 `Release()` 漏掉。

- [ ] **Step 7: 编译 + 核实行数 + 跑全部 harness**

```powershell
pwsh -File G:\pc-kvm\build\build-agent.ps1
pwsh -File G:\pc-kvm\tests\edge-tracker\run.ps1
pwsh -File G:\pc-kvm\tests\scancode-map\run.ps1
pwsh -File G:\pc-kvm\tests\agent-logic\run.ps1
```
Expected: 编译零警告 exit 0；`fail=0`（21 条）、`45/45`、`fail=0`（16 条）。

```bash
cd /g/pc-kvm && wc -l src/agent/Program.cs
```
Expected: ≤ 360。

- [ ] **Step 8: 真机验收（需用户在场；**这一步不可省，它关掉挂账 A3**）**

用户把手机摆在**左侧**，设置里选「左侧」，确定。然后：

1. 鼠标往屏幕**最左**推 → 进接管，手机光标出现在**手机右侧边缘**、y 与 PC 光标高度对应
2. 在手机屏上移动 → 能到四个物理边缘（尤其**右**边缘，那是新配置下的"邻接边"）
3. 继续**向右**推 ≥1cm → 回 PC
4. **横屏**下重复 1–3，全过
5. **竖屏**下重复 1–3，全过 ← **这一条是 A3 的判据**：若竖屏下回程方向反了，
   说明旋转让邻接边翻了，必须停下来报 BLOCKED 并把现象写进 ledger（代码层面解不了，
   要另开设计）
6. 切回「右侧」，重复 1–3 确认没回归

- [ ] **Step 9: Commit**

```bash
cd /g/pc-kvm
git add src/agent/EdgeTracker.cs src/agent/Program.cs tests/edge-tracker/Main.cs
git commit -F - <<'EOF'
feat(#2): 手机在左/右可配置；入口边改运行时推导

- 入口边不再写死 3839/0/1080，改为按 SM_CXVIRTUALSCREEN/CYVIRTUALSCREEN 推导：
  右侧取 W-1（Windows 把光标钳在最后一列内）、左侧取 0、edgeBottom 直接取桌面高度
  （EdgeTracker 的判定是开区间，已对着源码核实）。不这么做的话换显示器布局会让入口
  条件永远不可达——正是 Task 6 审查栽过的静默 bug。
- EdgeTracker 的 4 个 edge 字段去掉 readonly，新增 SetEdge()。
  **刻意不重建 tracker**：EnterTakeover/LeaveTakeover 的订阅挂在对象上，重建会丢订阅
  （静默失效）。SetEdge 同时解除武装，免得用户刚点完"确定"就被弹进接管。
- 设置里切换侧别时，若正在接管则先干净退出（补发 LEAVE、解锁光标、还前台）。

补齐**左挂回归**：此前 harness 15 条用例全是 phoneRight:true，而 EdgeTracker 有 6 处
左挂分支，上一轮只由人眼核过对称性、没有任何自动化覆盖。新增 L1–L6：入口落点
phoneX=3199、朝屏内推不触发、离边缘一列不触发、入口抖动不回程（右挂 T11 的镜像）、
在手机右边缘外推 ≥40 回程、SetEdge 后解除武装。共 **24** 条全过（原 15 + 新 9）。

⚠️ A3（旋转时邻接边是否翻转）仍需真机横竖屏各测一次，见任务 8 的验收清单第 5 项。

Co-Authored-By: Claude Code <noreply@anthropic.com>
EOF
```

---

### Task 6: #3 —— 图标

**Files:**
- Create: `assets/pc-kvm.ico`
- Modify: `build/build-agent.ps1`
- Modify: `src/agent/Program.cs`（托盘图标）

**Interfaces:** 无代码接口；产出是资源与一行托盘改动。

- [ ] **Step 1: 生成 `assets/pc-kvm.ico`**

要求：
- **多尺寸**：16×16、24×24、32×32、48×48、64×64、128×128、256×256（256 用 PNG 压缩存）
- **概念**：一台显示器剪影 + 一台手机剪影并排，中间一支跨越两者的箭头（表达"键鼠跨越"）
- **配色**：深色与浅色背景下都要看得清 → 主体用中灰/蓝色，**不要**纯黑或纯白描边；
  16×16 下必须仍能辨认出"两块屏幕 + 一支箭头"（小尺寸下简化，去掉细节）

生成后**核实**（不要只信"我生成好了"）：

```powershell
$b = [IO.File]::ReadAllBytes('G:\pc-kvm\assets\pc-kvm.ico')
"大小: $($b.Length) 字节  前4字节: $((($b[0..3]) | ForEach-Object { $_.ToString('x2') }) -join '')"
# ICO 头必须是 00 00 01 00；偏移 4-5 字节是图像数量（小端），应 >= 6（多尺寸）
"图像数量: $([BitConverter]::ToUInt16($b, 4))"
```
Expected: 前 4 字节 `00000100`；图像数量 ≥ 6。

- [ ] **Step 2: 在 `build-agent.ps1` 里加 `/win32icon:`**

把原来那段 `& $csc ...` 改为（**只加一行**，其余不动）：

```powershell
$icon = Join-Path $root 'assets\pc-kvm.ico'
if (-not (Test-Path $icon)) { throw "找不到图标: $icon" }

Write-Host "编译 $($sources.Count) 个源文件..." -ForegroundColor Cyan
& $csc -nologo -target:winexe -platform:x64 -optimize+ `
       -win32icon:"$icon" `
       -out:"$dist\pc-kvm.exe" `
       -r:System.Windows.Forms.dll `
       -r:System.Drawing.dll `
       $sources
```

- [ ] **Step 3: 编译并核实图标真的嵌进去了**

```powershell
pwsh -File G:\pc-kvm\build\build-agent.ps1
Write-Host "`n=== 从 exe 提取图标并核实 ===" -ForegroundColor Cyan
Add-Type -AssemblyName System.Drawing
$ico = [System.Drawing.Icon]::ExtractAssociatedIcon('G:\pc-kvm\dist\pc-kvm.exe')
"提取到的尺寸: $($ico.Width)x$($ico.Height)"
```

Expected: `编译成功` exit 0；能提取到图标，尺寸 32×32（非 0，也非系统默认图标）。

- [ ] **Step 4: 托盘图标改用同一资源**

把 `TrayUi.cs` 里 `Install()` 内的 `Tray.Icon = SystemIcons.Application;` 那一行
（任务 1 里我们保留过它）替换为：

```csharp
            // 用 exe 自己嵌入的图标（/win32icon 那份），不再是 SystemIcons.Application
            // ——那个 Windows 通用图标是用户看到的"丑"的主要来源。取不到则回退，绝不抛。
            try
            {
                Tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception)
            {
                Tray.Icon = null;
            }
            if (Tray.Icon == null) Tray.Icon = SystemIcons.Application;
```

- [ ] **Step 5: 编译 + 核实行数**

```powershell
pwsh -File G:\pc-kvm\build\build-agent.ps1
```
```bash
cd /g/pc-kvm && wc -l src/agent/Program.cs
```
Expected: 零警告 exit 0；行数 ≤ 360。

- [ ] **Step 6: 人工验收（不需要手机）**

1. 资源管理器里看 `dist\pc-kvm.exe` → 显示的是新图标（**不是**通用 exe 图标）
2. 启动 exe → 托盘图标**也是**新图标
3. 把任务栏/资源管理器切到浅色与深色主题各看一次 → 都能看清

- [ ] **Step 7: Commit**

```bash
cd /g/pc-kvm
# 托盘那行在 TrayUi.cs（Task 1 把托盘从 Program.cs 搬走了），不在 Program.cs。
# 初稿这里写的是 src/agent/Program.cs —— 实现者按任务上下文判断并提交了 TrayUi.cs，判断正确。
git add assets/pc-kvm.ico assets/pc-kvm-preview-256.png assets/pc-kvm-preview-16.png \
        build/build-agent.ps1 src/agent/TrayUi.cs
git commit -F - <<'EOF'
feat(#3): exe 与托盘图标

- assets/pc-kvm.ico：多尺寸 16/24/32/48/64/128/256（256 用 PNG 压缩），
  概念为显示器 + 手机剪影加一支跨越箭头；深浅色背景下都可辨认，16px 下做了简化。
- build-agent.ps1 加 /win32icon 嵌入（**单文件 exe 硬约束不破**：图标是嵌入资源，
  不是外挂文件；编译前先断言图标存在，缺了就 fail-fast）。
- 托盘图标从 SystemIcons.Application（Windows 通用图标，用户看到的"丑"主要来自它）
  换成 exe 自己嵌入的那份，取不到则回退，绝不抛。

Co-Authored-By: Claude Code <noreply@anthropic.com>
EOF
```

---

### Task 7: #5 —— 数字键盘与 NumLock

**Files:**
- Modify: `src/agent/KeyMap.cs`（追加翻译函数）
- Modify: `src/agent/Program.cs`（取 NumLock 状态 + 转发前翻译）
- Modify: `src/injector/ScancodeMap.java`（**只改注释**）
- Modify: `tests/agent-logic/Main.cs`（追加用例）
- Modify: `tests/scancode-map/TestMain.java`（追加交叉验证用例）

**Interfaces:**
- Consumes: `KeyMap.ModifierBit`（已存在）
- Produces: `static int KeyMap.NumpadNavE0Scancode(int mk)` ——
  返回 `0xE000|mk`（应发这个 scancode，设备侧既有 E0 表会映成导航 usage）；
  返回 **0** = 该键在 NumLock 关时无对应（小键盘 5 = Clear），调用方**丢弃**；
  返回 **-1** = 不属于受 NumLock 影响的数字键盘键，调用方**原样发送**。

- [ ] **Step 1: 追加失败用例到 `tests/agent-logic/Main.cs`**

```csharp
        // ---- N1: NumLock 关时数字键盘应翻成 E0 形态（设备侧既有 E0 表会映成导航 usage） ----
        // 实测依据（2026-09-23 真机日志）：物理数字键盘不受 NumLock 影响恒发普通码
        //（小键盘 7 = 0x47，导航区独立 Home = 0xE047）。所以"关"这一侧必须由我们显式翻译，
        // 否则手机永远是小键盘模式 = 与 PC 键盘的 NumLock 灯相反。
        Check("N1 翻译成 E0 形态",
            KeyMap.NumpadNavE0Scancode(0x47) == 0xE047
            && KeyMap.NumpadNavE0Scancode(0x48) == 0xE048
            && KeyMap.NumpadNavE0Scancode(0x49) == 0xE049
            && KeyMap.NumpadNavE0Scancode(0x4B) == 0xE04B
            && KeyMap.NumpadNavE0Scancode(0x4D) == 0xE04D
            && KeyMap.NumpadNavE0Scancode(0x4F) == 0xE04F
            && KeyMap.NumpadNavE0Scancode(0x50) == 0xE050
            && KeyMap.NumpadNavE0Scancode(0x51) == 0xE051
            && KeyMap.NumpadNavE0Scancode(0x52) == 0xE052
            && KeyMap.NumpadNavE0Scancode(0x53) == 0xE053,
            "0x47..0x53（除 0x4C）都应翻成 0xE0xx");

        // ---- N2: 小键盘 5（NumLock 关 = Clear）无 HID 对应，必须返回 0 让调用方丢弃 ----
        Check("N2 小键盘5 丢弃", KeyMap.NumpadNavE0Scancode(0x4C) == 0,
            "0x4C → " + KeyMap.NumpadNavE0Scancode(0x4C) + "（期望 0）");

        // ---- N3: 不受 NumLock 影响的键返回 -1（原样发送） ----
        Check("N3 不受影响的原样发", KeyMap.NumpadNavE0Scancode(0x37) == -1
            && KeyMap.NumpadNavE0Scancode(0x4A) == -1
            && KeyMap.NumpadNavE0Scancode(0x4E) == -1
            && KeyMap.NumpadNavE0Scancode(0x35) == -1
            && KeyMap.NumpadNavE0Scancode(0x1E) == -1,
            "0x37(*) 0x4A(-) 0x4E(+) 0x35(/) 与普通字母键都应返回 -1");

        // ---- N4（钉子）: 这条用例是为了防止有人"顺手"把翻译改成返回 usage 而不是 scancode。
        // 返回的必须是 scancode（带 0xE000 标志），因为线上协议送的就是 scancode，
        // 设备侧 scancode→usage 的翻译已经存在（ScancodeMap 的 E0 表）。
        Check("N4 返回的是 scancode 不是 usage",
            (KeyMap.NumpadNavE0Scancode(0x47) & 0xE000) == 0xE000,
            "0x47 → 0x" + KeyMap.NumpadNavE0Scancode(0x47).ToString("X"));

        // ---- N5（回归钉子）: 假 Shift E0 0x2A 不得被当成修饰键 ----
        // 实测日志里出现过 0xE02A + 0xE049（部分键盘给导航键区发的兼容前缀）。
        // KeyMap 的 E0 分支只含 0x5B/0x1D/0x38/0x5C，故 0x2A 必须返回 0，
        // 否则会往手机注入一次幽灵 Shift。这条钉住它，免得以后有人"补全"E0 分支时引入回归。
        Check("N5 假Shift 不得当修饰键", KeyMap.ModifierBit(0x2A, true) == 0,
            "ModifierBit(0x2A, isE0=true) = " + KeyMap.ModifierBit(0x2A, true) + "（期望 0）");
        Check("N5b 真左Shift 仍是修饰键", KeyMap.ModifierBit(0x2A, false) == 0x02,
            "ModifierBit(0x2A, isE0=false) = 0x" + KeyMap.ModifierBit(0x2A, false).ToString("X2")
            + "（期望 0x02）");
```

- [ ] **Step 2: 追加交叉验证用例到 `tests/scancode-map/TestMain.java`**

在 `System.out.println("TOTAL: ...")` **之前**插入：

```java
        // NumLock 关时，PC 侧（KeyMap.NumpadNavE0Scancode）会把数字键盘普通码翻成 E0 形态，
        // 期望设备侧这些 E0 码映到【导航】usage。下面逐条钉住这个跨语言契约——
        // 若哪天有人改了 E0 表，这里会立刻红。
        check("NumLock关 小键盘7→Home", ScancodeMap.toHidUsage(0xE047), 0x4A);
        check("NumLock关 小键盘8→Up", ScancodeMap.toHidUsage(0xE048), 0x52);
        check("NumLock关 小键盘9→PgUp", ScancodeMap.toHidUsage(0xE049), 0x4B);
        check("NumLock关 小键盘4→Left", ScancodeMap.toHidUsage(0xE04B), 0x50);
        check("NumLock关 小键盘6→Right", ScancodeMap.toHidUsage(0xE04D), 0x4F);
        check("NumLock关 小键盘1→End", ScancodeMap.toHidUsage(0xE04F), 0x4D);
        check("NumLock关 小键盘2→Down", ScancodeMap.toHidUsage(0xE050), 0x51);
        check("NumLock关 小键盘3→PgDn", ScancodeMap.toHidUsage(0xE051), 0x4E);
        check("NumLock关 小键盘0→Insert", ScancodeMap.toHidUsage(0xE052), 0x49);
        check("NumLock关 小键盘.→Delete", ScancodeMap.toHidUsage(0xE053), 0x4C);
        check("NumLock关 小键盘5→无对应", ScancodeMap.toHidUsage(0xE04C), -1);
        // 对照：NumLock 开时走普通码那一段，必须是数字键盘 usage
        check("NumLock开 小键盘7→KP7", ScancodeMap.toHidUsage(0x47), 0x5F);
        check("NumLock开 小键盘0→KP0", ScancodeMap.toHidUsage(0x52), 0x62);
```

- [ ] **Step 3: 跑两个 runner 确认失败**

```powershell
pwsh -File G:\pc-kvm\tests\agent-logic\run.ps1
pwsh -File G:\pc-kvm\tests\scancode-map\run.ps1
```
Expected:
- `agent-logic`：**编译失败** `error CS0117: "KeyMap" 不包含"NumpadNavE0Scancode"`。这是 RED。
- `scancode-map`：**应当场全过**（`TOTAL: 58/58`）。这 13 条是**跨语言契约的钉子**，不是新功能——
  设备侧这次一行逻辑都不改，所以这里没有可红的余地。
  ⚠️ **若有一条不过，不要改用例去迁就**：那说明设备侧 E0 表与 spec §8.2 的预期不符
  （多半是某条 E0 映射缺失），必须停下来把实际情况写进报告并报 BLOCKED。这条纪律是本项目
  用真实教训换来的——Task 6 曾因为"用实现者的心智模型当测试输入"而让入口条件在真机上永远不可达。

- [ ] **Step 4: 实现 `KeyMap.NumpadNavE0Scancode`**

在 `KeyMap.cs` 的 `ModifierBit` **之后**追加：

```csharp
        /// <summary>
        /// NumLock **关**时，数字键盘的普通 scancode 应改送哪个 scancode。
        ///
        /// 实测依据（2026-09-23 真机日志，铁证）：物理数字键盘**不受 NumLock 影响，
        /// 恒发同一个普通 scancode**——小键盘 7 是 0x47，而导航区那颗独立的 Home 才是 0xE047。
        /// 是 Windows 依据 NumLock 把 0x47 解释成 NUMPAD7 还是 HOME。
        /// 原 ScancodeMap 注释写成"NumLock 关时同样这些物理键发 E0 前缀"是**错的**，
        /// 于是 0x47→0x5F 无条件生效 → 手机永远小键盘模式 → 与 PC 键盘的 NumLock 灯相反。
        ///
        /// 修法：把"NumLock 关"的小键盘码翻成它**自己的 E0 形态**，交给设备侧既有的
        /// E0 表去映成 Home/PgUp/… —— 那张表的"关"列与导航键区本来就逐项相同，
        /// 于是**设备侧零改动**。返回的是 scancode 而非 usage，因为线上协议送的就是 scancode。
        ///
        /// 返回 0 = 该键在 NumLock 关时无 HID 对应（小键盘 5 = Clear），调用方**丢弃**。
        /// 返回 -1 = 不受 NumLock 影响，调用方**原样发送**。
        /// </summary>
        public static int NumpadNavE0Scancode(int mk)
        {
            switch (mk)
            {
                case 0x47:   // 7 → Home
                case 0x48:   // 8 → Up
                case 0x49:   // 9 → PgUp
                case 0x4B:   // 4 → Left
                case 0x4D:   // 6 → Right
                case 0x4F:   // 1 → End
                case 0x50:   // 2 → Down
                case 0x51:   // 3 → PgDn
                case 0x52:   // 0 → Insert
                case 0x53:   // . → Delete
                    return 0xE000 | mk;
                case 0x4C:   // 5（NumLock 关）= Clear，HID 无对应
                    return 0;
                default:     // 0x37 * / 0x4A - / 0x4E + / 0x35 斜杠：不受 NumLock 影响
                    return -1;
            }
        }
```

- [ ] **Step 5: 修正 `ScancodeMap.java` 的错误注释（不改任何逻辑）**

把 `ScancodeMap.java:103-109` 那段注释：

```java
            // ↓ 数字键盘（NumLock 开着时的形态；NumLock 关时同样这些物理键发 E0 前缀，
            // 走上面 E0 表的 Home/End/箭头那一组）。原来整段缺失 → 返回 -1 → 注入器静默丢弃。
```

改为（**只改这一段文字，代码一行不动**）：

```java
            // ↓ 数字键盘（NumLock **开**着时的形态）。原来整段缺失 → 返回 -1 → 注入器静默丢弃。
            // （2026-09-23 更正：本节原写着"NumLock 关时同样这些物理键发 E0 前缀，走上面 E0 表"，
            //  那是**错的**。真机实测：物理数字键盘不受 NumLock 影响，恒发普通码——小键盘 7 是
            //  0x47，导航区那颗独立的 Home 才是 0xE047。NumLock 关的翻译由 PC 侧完成
            // （KeyMap.NumpadNavE0Scancode 把它翻成 0xE047 再送过来），此处只负责"开"的一侧。
            //  上面 E0 表的 0x47..0x53 那一组既服务导航键区，也服务 NumLock 关时被翻译过来的码。）
```

- [ ] **Step 6: 跑两个 runner 确认全过**

```powershell
pwsh -File G:\pc-kvm\tests\agent-logic\run.ps1
pwsh -File G:\pc-kvm\tests\scancode-map\run.ps1
```
Expected: `agent-logic` `TOTAL: pass=22 fail=0`（C 8 + S 8 + N1/N2/N3/N4 4 + N5/N5b 2）；
> ⚠️ **这个 22 是陈旧数字**（初稿写于 Task 3 加 C9/C10 之前）。实测正确值是 **24**
> （C1–C10 = 10，S1–S8 = 8，N 组 6）。实现者按 24 报、并在报告里指出了这处陈旧 —— 判断正确。
`scancode-map` `TOTAL: 58/58 passed, 0 failed`（45 + 13）。

- [ ] **Step 7: 在 `Program.cs` 里接线**

在 `GetSystemMetrics` 的 DllImport 附近加：

```csharp
        const int VK_NUMLOCK = 0x90;

        [DllImport("user32.dll")]
        static extern short GetKeyState(int vKey);
```

在 `ri.KeyChanged += delegate(RawKeyEvent e) { ... }` 里接线。**位置在状态门控之后**——
IDLE 态本就不转发，没必要为它付出 `GetKeyState` 调用；修饰键那一段已经提前 `return`，
不会走到这里：

把该处理器末尾这段（原来是 `if (tracker.Current != KvmState.Takeover) return;`
紧跟一行 `transport.Send(Protocol.EncodeKey((ushort)e.Scancode, ...));`）改为：

```csharp
                if (tracker.Current != KvmState.Takeover) return;   // IDLE 态不转发，PC 正常用

                // NumLock 关时把数字键盘普通码翻成 E0 形态（#5）。上面日志已经打过**原始**
                // scancode——日志必须记录真实观测，不能记录翻译后的值。
                int sendSc = e.Scancode;
                if (!e.IsE0)
                {
                    // GetKeyState 在 UI 线程调用（Raw Input 的 WM_INPUT 就在消息循环上），开销可忽略
                    if ((GetKeyState(VK_NUMLOCK) & 1) == 0)
                    {
                        int nav = KeyMap.NumpadNavE0Scancode(sendSc & 0xFF);
                        if (nav == 0) return;        // 小键盘 5 在 NumLock 关时 = Clear，丢弃
                        if (nav > 0) sendSc = nav;
                    }
                }
                transport.Send(Protocol.EncodeKey((ushort)sendSc, (byte)(e.IsUp ? 0 : 1), modifiers));
```

> 注意 `transport.Send` 那行的第一个实参从 `e.Scancode` 变成了 `sendSc`。修饰键分支里
> 那一处 `transport.Send(...)` **不用改**（它走的是上面提前 `return` 的分支）。

- [ ] **Step 8: 编译 + 核实行数 + 跑全部 harness**

```powershell
pwsh -File G:\pc-kvm\build\build-agent.ps1
pwsh -File G:\pc-kvm\tests\edge-tracker\run.ps1
pwsh -File G:\pc-kvm\tests\scancode-map\run.ps1
pwsh -File G:\pc-kvm\tests\agent-logic\run.ps1
```
Expected: 编译零警告 exit 0；三套用例 `fail=0`。

```bash
cd /g/pc-kvm && wc -l src/agent/Program.cs
```
Expected: ≤ 360。**若超了必须停下来报 BLOCKED**（Ruling 26）。

- [ ] **Step 9: 真机验收（需用户在场）**

1. PC NumLock **开**（灯亮）→ 接管中按小键盘 `7` → 手机上出现 **`7`**
2. PC NumLock **关**（灯灭）→ 接管中按小键盘 `7` → 手机上应是 **Home**（不是 `7`）
3. NumLock 关时按小键盘 `0` → 手机上应是 **Insert**
4. NumLock 关时按小键盘 `5` → 手机上**什么都不应发生**
5. NumLock 开/关各按一次 `*` `-` `+` `/` → 手机上始终是这四种符号（不受 NumLock 影响）
6. NumLock 关时按导航区那颗**独立 Home** → 手机上仍是 **Home**（不能被翻译逻辑影响）

- [ ] **Step 10: Commit**

```bash
cd /g/pc-kvm
git add src/agent/KeyMap.cs src/agent/Program.cs src/injector/ScancodeMap.java \
        tests/agent-logic/Main.cs tests/scancode-map/TestMain.java
git commit -F - <<'EOF'
fix(#5): NumLock 关时数字键盘仍被当数字键发出去（手机行为与 PC 键盘灯相反）

根因已实测确认，不是推测。让用户把 PC NumLock 关掉后按键，日志给出铁证：
  KEY scancode=0x47   DOWN/UP   ← 小键盘 7：普通 0x47，没有 E0 前缀
  KEY scancode=0xE047 DOWN/UP   ← 导航区那颗独立 Home：才是 E0 0x47
即**物理数字键盘不受 NumLock 影响，恒发同一个普通码**；是 Windows 依据 NumLock
把 0x47 解释成 NUMPAD7 还是 HOME。

而 ScancodeMap 里原本写着"NumLock 关时同样这些物理键发 E0 前缀，走上面 E0 表"——
**这句注释是错的**，于是 0x47→0x5F 无条件生效 → 手机永远是小键盘模式，
与 PC 键盘的 NumLock 灯状态相反。阶段二两次真机实测都是 NumLock 开着打数字，
关的那一侧从未被测过（FINDINGS.md:63 当年也明写"E0 处理分支未被实测覆盖"）。

修法（PC 侧翻译，设备侧零改动）：
- KeyMap.NumpadNavE0Scancode(mk)：NumLock 关时把数字键盘普通码翻成它**自己的 E0 形态**，
  交给设备侧既有的 E0 表映成 Home/PgUp/… ——那张表的"关"列与导航键区本来就逐项相同。
  返回 0 = 小键盘 5 = Clear（HID 无对应）→ 丢弃；返回 -1 = 不受影响 → 原样发送。
- Program.cs 转发前读 GetKeyState(VK_NUMLOCK)；日志照旧打**原始** scancode（日志必须记录
  真实观测，不能记录翻译后的值）。
- ScancodeMap.java **只改那段错误注释**，代码一行不动。
- 继续转发 NumLock 键本身，让 Android 自身状态跟随 PC 一并翻转（防御性一致）。

用例：agent-logic 新增 N1–N5b（含 N5 钉住"E0 0x2A 假 Shift 不得被当修饰键"），
scancode-map 新增 13 条跨语言契约（NumLock 关的 E0 码 → 导航 usage，逐条钉住）。

Co-Authored-By: Claude Code <noreply@anthropic.com>
EOF
```

---

### Task 8: #4 —— 断联自愈（分级升级）

**Files:**
- Modify: `src/agent/DeviceLauncher.cs`
- Modify: `src/agent/Watchers.cs`（新增重连监督线程 + `Stop`）
- Modify: `src/agent/Program.cs`（交出 `devProc` 所有权 + 退出路径）

**Interfaces:**
- Consumes: `Config.ReconnectSeconds`、`Config.AllowKillAdb`（任务 2）
- Produces:
  - `static bool DeviceLauncher.EnsureTunnel()`
  - `static bool DeviceLauncher.PushJar(string localJarPath)`
  - `static bool DeviceLauncher.DeviceVisible()`
  - `static bool DeviceLauncher.RestartAdbServer()`
  - `static bool DeviceLauncher.KillAllAdb()`
  - `bool Watchers.StartDevice()`、`void Watchers.AttachConfig(Config cfg)`、
    `Process Watchers.DeviceProcess { get; }`、`void Watchers.Stop()`

> ⚠️ **R13 —— 行数预案（实测比初稿预测紧得多，务必先读这一条）**
> 本计划初稿推演"收尾时 `Program.cs` 约 337 行、余量 23"，**实测已到 353 行**（Task 3 的 +13、
> Task 5 的 +36、Task 7 的 +19 都比估的高），**距 360 只剩 7 行**。
> 好消息是本任务的 `Program.cs` 增量很小（把 `Process devProc = DeviceLauncher.Start();` 换成
> `watchers.AttachConfig(cfg)` + `StartDevice()`，并把 `TrayUi` 构造里的 `devProc` 去掉，约 ±3 行），
> 主体在 `Watchers.cs`。
> **若本任务发现 `Program.cs` 需要超过那 7 行：必须先抽 `src/agent/InputRouter.cs`
> （把 `MouseMoved` 与 `KeyChanged` 两个处理器搬进去，实测约 81 行）来腾地方，
> 绝不允许上调 360 红线。** 这是 Ruling 26 的原话，也是 R1 当时就写好的下一步。
> 代价：本任务多一个前置纯搬迁步骤。

- [ ] **Step 1: 改 `DeviceLauncher.cs` —— 拆分与新增**

把 `Prepare` 替换为下面三块（`RunAdb` / `RunAdbCapture` **不动**）：

```csharp
        /// <summary>只建反向隧道。重连时用这个——不重推 jar（设备侧那份还在）。</summary>
        public static bool EnsureTunnel()
        {
            return RunAdb("reverse tcp:" + Port + " tcp:" + Port) == 0;
        }

        /// <summary>推 jar。首次启动、或设备侧被清过之后才需要。</summary>
        public static bool PushJar(string localJarPath)
        {
            if (!File.Exists(localJarPath)) return false;
            return RunAdb("push \"" + localJarPath + "\" " + RemoteJar) == 0;
        }

        /// <summary>首次启动：建隧道 + 推 jar。行为与拆分前完全一致。</summary>
        public static bool Prepare(string localJarPath)
        {
            if (!EnsureTunnel()) return false;
            return PushJar(localJarPath);
        }
```

在 `Cleanup` **之后**新增：

```csharp
        /// <summary>adb 现在看得见设备吗？判据是 `adb devices` 里出现一行以 "device" 结尾的条目。
        /// 注意要跳过第一行 "List of devices attached"。</summary>
        public static bool DeviceVisible()
        {
            string outp;
            if (RunAdbCapture("devices", out outp) != 0) return false;
            string[] lines = outp.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string s = lines[i].Trim();
                if (s.Length == 0) continue;
                if (s.EndsWith("\tdevice") || s.EndsWith(" device")) return true;
            }
            return false;
        }

        /// <summary>温和恢复：重启 adb server。不碰别的进程，代价最小。</summary>
        public static bool RestartAdbServer()
        {
            RunAdb("kill-server");
            return RunAdb("start-server") == 0;
        }

        /// <summary>
        /// 激进恢复：强杀**所有** adb 进程再拉起 server。
        /// ⚠️ 本机同时跑着 3 个 adb（两个 platform-tools + InputShare 自带的），
        /// 强杀会打断其它正在用 adb 的工具。故由 Config.AllowKillAdb 把关、默认关闭。
        /// 实测依据：ledger 记过"kill-server 单独不够，出现过第二个 adb 进程仍占着设备"。
        /// </summary>
        public static bool KillAllAdb()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "taskkill";
                psi.Arguments = "/F /IM adb.exe";
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                Process p = Process.Start(psi);
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(5000);
            }
            catch (System.Exception)
            {
                return false;
            }
            return RunAdb("start-server") == 0;
        }
```

- [ ] **Step 2: 在 `Watchers.cs` 里加重连监督**

**线程纪律（务必照做）**：监督线程是**后台线程**，它**绝不直接写日志、绝不直接碰控件**——
日志经 `Report()`（内部走 `_host.BeginInvoke` 转 UI 线程）打，状态条同理。

先在类里加字段与接口：

```csharp
        Config _cfg;
        System.Diagnostics.Process _devProc;
        System.Threading.Thread _reconnect;
        volatile bool _stop;
        int _failStreak;
        int _level;                 // 已升级到的档位：0 = 只做①，1 = 做过温和，2 = 做过激进
        string _lastReport = "";

        /// <summary>设备侧进程句柄。所有权归 Watchers：重连会换进程，退出时要 Kill 它。</summary>
        public System.Diagnostics.Process DeviceProcess { get { return _devProc; } }

        /// <summary>接入配置（ReconnectSeconds 每轮重读、AllowKillAdb 决定要不要上第③级）。</summary>
        public void AttachConfig(Config cfg) { _cfg = cfg; }

        /// <summary>拉起设备侧进程（首次启动用）。</summary>
        public bool StartDevice()
        {
            _devProc = DeviceLauncher.Start();
            return _devProc != null;
        }

        /// <summary>退出前调用：停止监督并 Kill 设备侧进程。必须在 log.Close() 之前调。</summary>
        public void Stop()
        {
            _stop = true;
            try { if (_devProc != null && !_devProc.HasExited) _devProc.Kill(); }
            catch (System.Exception) { }
        }
```

在 `Start()` 的末尾追加重连线程：

```csharp
            _reconnect = new System.Threading.Thread(ReconnectLoop);
            _reconnect.IsBackground = true;
            _reconnect.Start();
```

新增以下方法：

```csharp
        /// <summary>
        /// 断联自愈（阶段三 #4）。分级升级：
        ///   ① 重建隧道 + 重启注入器（每次未连接都做）
        ///   ② 连续 3 个周期仍不通 → adb kill-server + start-server（温和）
        ///   ③ 再连续 3 个周期仍不通 且 AllowKillAdb → taskkill 所有 adb + start-server（激进）
        /// "不通"的判据统一是：**该周期结束时 transport.IsConnected 仍为 false**。
        ///
        /// ⚠️ 覆盖边界（如实写在这里，别让后人以为它能修好一切）：
        /// 2026-09-23 现场实测到一次**设备级**掉线——adb devices 全空、Windows 侧 ADB Interface
        /// 状态 OK 却打不开、kill-server + start-server 已实测无效、且当时只有 1 个 adb 进程
        /// （故不是 ledger 记的"多 adb 抢设备"）。本级联能覆盖"隧道断/注入器死"，
        /// 不保证覆盖"adb 整个看不见设备"——那仍需人看一眼手机。
        /// </summary>
        void ReconnectLoop()
        {
            while (!_stop)
            {
                int secs = 5;
                if (_cfg != null && _cfg.ReconnectSeconds >= Config.ReconnectSecondsMin)
                    secs = _cfg.ReconnectSeconds;
                // 拆成 100ms 小睡，让 Stop() 能及时生效（最长等一个周期）
                for (int i = 0; i < secs * 10 && !_stop; i++)
                    System.Threading.Thread.Sleep(100);
                if (_stop) return;

                if (_transport.IsConnected)
                {
                    if (_failStreak != 0 || _level != 0)
                    {
                        _failStreak = 0; _level = 0;
                        Report("已连接");
                    }
                    continue;
                }

                _failStreak++;

                // ①：重建隧道 + 重启注入器。幂等——重启前先 Kill 旧的，
                // 否则设备侧会堆多个 app_process。
                TryRebuildLink();

                if (_transport.IsConnected)
                {
                    _failStreak = 0; _level = 0;
                    Report("已连接");
                    continue;
                }

                // ②：连续 3 个周期仍不通 → 温和恢复（只此一次）
                if (_failStreak >= 3 && _level < 1)
                {
                    _level = 1;
                    Report("adb server 重启中…");
                    DeviceLauncher.RestartAdbServer();
                    continue;
                }

                // ③：再 3 个周期仍不通 且 用户开了开关 → 激进恢复（只此一次）
                if (_failStreak >= 6 && _level < 2)
                {
                    _level = 2;
                    if (_cfg != null && _cfg.AllowKillAdb)
                    {
                        Report("强杀 adb 进程中…");
                        DeviceLauncher.KillAllAdb();
                    }
                    continue;
                }

                string why = DeviceLauncher.DeviceVisible() ? "隧道/注入器未就绪" : "adb 看不到设备";
                Report("等待设备…（已重试 " + _failStreak + " 次，" + why + "）");
            }
        }

        bool TryRebuildLink()
        {
            try
            {
                if (_devProc != null && !_devProc.HasExited) _devProc.Kill();
            }
            catch (System.Exception) { }
            _devProc = null;

            string jar = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pckvm.jar");
            if (!DeviceLauncher.EnsureTunnel()) return false;
            // jar 只推一次：设备侧通常还在。推失败也不致命（可能只是设备侧被清过）
            if (!DeviceLauncher.PushJar(jar))
                LogFromWorker("# 重连：jar 推送失败（继续尝试拉起注入器）");
            _devProc = DeviceLauncher.Start();
            return _devProc != null;
        }

        /// <summary>后台线程写日志的**唯一通道**：绝不直接碰 `_log`。
        /// 理由（Task 1 审查 Important 1 就是这个形状，R9）：`log` 的 StreamWriter 在退出时被
        /// `TrayUi` 的 ApplicationExit `Close()`，而后台线程此刻可能仍在跑（`Stop()` 只置标志、
        /// 不 Join）。一条写进已关闭 writer 的调用就是**后台线程上的未捕获异常 = 进程被终结**，
        /// 而不是干净退出。消息循环结束后 BeginInvoke 要么抛异常（被这里吞掉）、要么投递后
        /// 再也不会被泵出——两种情况都不会真的写到已关闭的 writer。</summary>
        void LogFromWorker(string s)
        {
            try { _host.BeginInvoke((MethodInvoker)delegate { _log(s); }); }
            catch (System.Exception) { }
        }

        /// <summary>状态变化上报。**只在状态真的变了**才写日志与改状态条——
        /// 每次重试都打会把有效信息淹没（本项目既栽过 2% 采样率，也栽过刷日志）。
        /// 去重比较与两处 UI 触碰全部在 UI 线程上做，故 `_lastReport` 只被 UI 线程读写，
        /// 后台线程**不碰**它。</summary>
        void Report(string s)
        {
            try
            {
                _host.BeginInvoke((MethodInvoker)delegate
                {
                    if (s == _lastReport) return;
                    _lastReport = s;
                    _log("# " + s);
                    _host.SetStatus(s);
                });
            }
            catch (System.Exception) { }
        }
```

- [ ] **Step 3: 改 `Program.cs` 交出 `devProc` 所有权**

把 `System.Diagnostics.Process devProc = DeviceLauncher.Start();`（`Program.cs:207` 附近）替换为：

```csharp
            // 设备侧进程的所有权交给 Watchers：重连会换进程，退出时要 Kill 它。
            // 这里只负责"第一次拉起来"。
            watchers.AttachConfig(cfg);
            if (!watchers.StartDevice())
                log.WriteLine("# 首次拉起注入器失败（重连监督会继续尝试）");
```

把 `TrayUi.cs` 的构造函数**去掉末位参数 `Process devProc`**（连同 `_devProc` 字段），
并把 `Install()` 里的 `DeviceLauncher.Cleanup(_devProc);` 改为用 `Watchers` 持有的那个：

```csharp
                DeviceLauncher.Cleanup(_watchers.DeviceProcess);   // Kill + rm jar + 拆 reverse
```

（`_watchers.Stop()` 已在任务 1 就写在 `ApplicationExit` 的最前面，位置正确，不用动。）

同时把 `Watchers.Stop()` 补上"顺手 Kill 设备侧进程"：

```csharp
        public void Stop()
        {
            _stop = true;
            if (_guard != null) _guard.Stop();
            if (_heartbeat != null) _heartbeat.Stop();
            try { if (_devProc != null && !_devProc.HasExited) _devProc.Kill(); }
            catch (System.Exception) { }
        }
```

并把 `Program.cs` 里 `trayUi` 的构造点去掉 `devProc` 实参：

```csharp
            TrayUi trayUi = new TrayUi(host, supp, watchers, transport, log, cfg);
            trayUi.Install();
```

- [ ] **Step 4: 编译 + 核实行数 + 跑 harness**

```powershell
pwsh -File G:\pc-kvm\build\build-agent.ps1
pwsh -File G:\pc-kvm\tests\edge-tracker\run.ps1
pwsh -File G:\pc-kvm\tests\scancode-map\run.ps1
pwsh -File G:\pc-kvm\tests\agent-logic\run.ps1
```
Expected: 编译零警告 exit 0；三套用例 `fail=0`。

```bash
cd /g/pc-kvm && wc -l src/agent/Program.cs
```
Expected: ≤ 360。

- [ ] **Step 5: 静态核实（不需要手机）**

逐条确认，**任何一条不符就报 BLOCKED**：

| 检查项 | 期望 |
|---|---|
| 全文搜 `SetWindowsHookEx` | **零命中**（全局键盘钩子硬约束） |
| `Watchers.cs` 里搜 `System.Threading.Timer` | **零命中**（心跳必须是 WinForms Timer） |
| 后台线程有没有**直接** `_host.SetStatus` | **没有**，全部在 `Report()` 的 `BeginInvoke` 内 |
| 后台线程有没有**直接** `_log(` | **没有**。`ReconnectLoop`/`TryRebuildLink` 里一处都不许有；日志一律走 `Report()` 或 `LogFromWorker()`（R9：写已关闭的 StreamWriter 会让后台线程未捕获异常终结进程） |
| 核实命令 | `rg -n '_log\(' src/agent/Watchers.cs` —— 允许的命中只有：构造函数里逃逸键那行、`HeartbeatTick` 里那行（两者都在 **UI 线程**上跑），以及 `LogFromWorker`/`Report` 的 `BeginInvoke` 内的两行 |
| `Report` 的日志条件 | 有 `if (s == _lastReport) return;`（只在状态变化时打） |
| `TryRebuildLink` 的幂等 | 起新进程前先 `Kill` 旧的 |

- [ ] **Step 6: 真机验收 —— 三档演练（需用户在场，按风险从低到高）**

准备：手机在线、用户已退出旧 exe 并启动新 exe、日志 `dist\pc-kvm.log` 打开着。

1. **基线**：日志出现 `# 设备已连接`；推右进接管能正常工作；退出接管
2. **①级（注入器死）**：接管中途由控制方执行
   `adb shell "pkill -f Injector"` → 手机上 2 秒内回 `IDLE`、鼠标解锁；
   随后监督线程应在 ≤ `ReconnectSeconds` 内重建并出现新的 `# 设备已连接`
3. **①级（隧道消失）**：`adb reverse --remove tcp:27183` → 同上，应自动重建
4. **②级（温和恢复）**：控制方 `adb kill-server` 并保持不启动 → 观察日志：
   约 3 个周期后出现 `# adb server 重启中…`，之后应恢复连接
5. **③级（激进，需先打开开关）**：设置里勾选「允许强杀 adb 进程」→ 确定；
   控制方制造持续不可连的局面（如拔线）→ 约 6 个周期后日志出现 `# 强杀 adb 进程中…`
6. **状态可见性**：断开期间状态条应显示 `等待设备…（已重试 n 次，…）`，且**日志不刷屏**
   （每个状态只出现一次，不是每轮一次）
7. **退出干净**：托盘退出 → 日志出现 `# 退出`；`adb shell "ls /data/local/tmp/pckvm.jar"` 应报不存在
   （Cleanup 删掉了）；`adb reverse --list` 为空
8. **A3（与任务 5 共用）**：手机在左侧时横屏、竖屏各测一次回程方向

- [ ] **Step 7: 把这轮的覆盖边界写进 ledger**

在 `.superpowers/sdd/2026-09-21-phase2-walking-skeleton/progress.md` 末尾追加一节，
内容必须包含：本任务落地了什么、**2026-09-23 那次设备级掉线的实测现象**（`adb devices` 全空、
ADB Interface 状态 OK 却打不开、`kill-server`+`start-server` 无效、当时只有 1 个 adb 进程）、
以及"**本自愈不覆盖该形态**"的明确结论。**这一条不能省**——不然下一个会话会以为 #4 已经修好一切。

- [ ] **Step 8: Commit**

```bash
cd /g/pc-kvm
git add src/agent/DeviceLauncher.cs src/agent/Watchers.cs src/agent/Program.cs \
        .superpowers/sdd/2026-09-21-phase2-walking-skeleton/progress.md
git commit -F - <<'EOF'
feat(#4): 断联自愈 —— 分级升级重建隧道/注入器

掉线的真形态（ledger A2）：注入器随 adb shell 会话一起死、adb reverse 隧道一并消失，
而 Transport 只会等重连——隧道没了就永远等不到，于是"线一抖就得重启 app"。

分级升级（判据统一为"该周期结束时 IsConnected 仍为 false"）：
  ① 重建隧道 + 重启注入器（每次未连接都做）
  ② 连续 3 个周期仍不通 → adb kill-server + start-server（温和，不碰别的进程）
  ③ 再 3 个周期仍不通 且 Config.AllowKillAdb → taskkill 所有 adb + start-server（激进）
第③级默认关闭：本机同时跑着 3 个 adb（两个 platform-tools + InputShare 自带的），
强杀会打断其它正在用 adb 的工具。真正管用的恢复手段是有破坏性的，故把风险最大的
那一步交给用户显式开启。

- DeviceLauncher.Prepare 拆成 EnsureTunnel / PushJar（重连不重推 jar）。
- 幂等：重启注入器前先 Kill 旧的，否则设备侧堆多个 app_process。
- 设备侧进程所有权从 Program 移到 Watchers（重连会换进程）。
- 状态可见：状态条显示等待/重试信息；日志**只在状态变化时**打一行（不刷屏）。
- 线程纪律：监督线程绝不直接写日志或碰控件，一律经 Report() 的 BeginInvoke 回 UI 线程。

⚠️ 覆盖边界（已同步写进 ledger）：2026-09-23 现场实测到一次**设备级**掉线——
adb devices 全空、Windows 侧 ADB Interface 状态 OK 却打不开、kill-server + start-server
**已实测无效**、且当时只有 1 个 adb 进程（故非"多 adb 抢设备"）。本自愈覆盖
"隧道断 / 注入器死"，**不保证**覆盖"adb 整个看不见设备"。

Co-Authored-By: Claude Code <noreply@anthropic.com>
EOF
```

---

## 收尾（全部任务完成后）

- [ ] 跑齐三套离线用例并**把输出原样贴进报告**（不要只说"全过"）
- [ ] `wc -l src/agent/Program.cs` ≤ 360
- [ ] 全文搜 `SetWindowsHookEx` 零命中
- [ ] 真机验收清单里**所有**项都由用户确认过，含 A3 的横竖屏
- [ ] 更新 `.superpowers/sdd/.../progress.md`：本阶段每个任务的 commit、真机证据、遗留项
- [ ] 整阶段终审（opus）→ 再谈合并到 master（对外动作，需用户点头）

## Self-Review

**Spec 覆盖核对**（逐节对 spec）：

| spec 节 | 覆盖它的任务 |
|---|---|
| §4 P0 抽 Watchers.cs | 任务 1 |
| §5 配置机制（ini + 对话框 + 坏文件策略） | 任务 2（ini）、任务 3（对话框） |
| §6 #1 速度 + 小数余量累积 | 任务 4 |
| §7 #2 手机在哪侧 + 左挂回归 | 任务 5 |
| §10 #3 图标 | 任务 6 |
| §8 #5 NumLock | 任务 7 |
| §9 #4 断联自愈 | 任务 8 |
| §11 测试策略 | 各任务内的用例 + 任务 8 Step 6 |
| §12 明确不做 | 无任务（正确——YAGNI 项不该有任务） |

**已知的、有意不覆盖的**：spec §13 的 A3（旋转 × 邻接边）不是代码任务，它是任务 5 Step 8
第 5 项与任务 8 Step 6 第 8 项里的**真机判据**；若竖屏下回程方向反了，执行者必须报 BLOCKED
而不是自己想办法改代码。

**类型一致性核对**（跨任务用到的名字）：`Config.MouseSensitivity` / `PhoneOnLeft` /
`AllowKillAdb` / `ReconnectSeconds` / `SensitivityMin` / `ReconnectSecondsMin`（任务 2 定义，
任务 3/4/5/8 使用，拼写一致）；`MouseScaler.ApplyX/ApplyY/Reset/SetSensitivity`（任务 4 定义并使用）；
`EdgeTracker.SetEdge`（任务 5 定义并使用）；`KeyMap.NumpadNavE0Scancode`（任务 7 定义并使用）；
`Watchers.GeometryQueried` / `StartDevice` / `AttachConfig` / `DeviceProcess` / `Stop` /
`Report`（任务 1/8 定义，任务 8 的 Program.cs 接线使用）。`Config.Load` 的返回类型是
`Config`（**不是** `bool`），任务 3 Step 2 的 `Config cfg = Config.Load(...)` 与之一致。

**自审改掉的 6 处**（写在这里，免得执行者以为计划是不可质疑的）：

1. **`watchers` 的前向引用**（最严重）：初稿把它放在 `Application.Run` 之前，而任务 8 要在
   `Application.ApplicationExit` 处理器里用它——那是**编译不过**的。已改为"构造放 `tracker` 之后、
   `Start()` 放最后"，并在两处都写明为什么不能挪。C# 局部变量不能前向引用这一类，
   Task 4/7/8 在阶段二都栽过。
2. **左挂用例 L4 是空转的**：初稿只写了进接管就断言 `BackPush == 0`，等于把 L1 又抄了一遍，
   那个断言恒真、测不出任何东西。已改为真的喂一次 `+1` 抖动并断言 `BackPush == 1`。
3. **左挂用例 L5 与设计相反**：初稿"进接管后连推 +127 三次"会**立刻**满足外推阈值而回程
   （入屏瞬间虚拟光标就落在邻接边上），断言必挂。已改为"往屏内走 → 滑回边界（不计外推）
   → 在边界上外推 45"这三步，与右挂 T12/T13 严格镜像。
4. **测试条数算错**：`edge-tracker` 是 24 条（15 + 9）不是 21；`agent-logic` 到任务 7 时是
   22 条不是 24。
5. **`scancode-map` 的 RED 判断错**：初稿说新用例会 FAIL，实际上设备侧 E0 表**本来就有**那些
   映射（含 `0x4C` 未定义 → `-1`，恰好与预期一致），所以应当场全过。已改为"这里没有可红的余地，
   若有一条不过说明设备侧与 spec 预期不符，必须报 BLOCKED 而不是改用例"。
6. **NumLock 翻译的位置**：初稿让它跑在状态门控**之前**，IDLE 态下每次按键都会白调一次
   `GetKeyState`。已移到门控之后。

**仍未解的**：`MouseSensitivity` 的默认 `0.50` 是拍出来的起点值，不是标定值。滑块能实时调，
所以它不敏感——真机验收第 2 项顺手标定即可，不必为它多开一轮。
