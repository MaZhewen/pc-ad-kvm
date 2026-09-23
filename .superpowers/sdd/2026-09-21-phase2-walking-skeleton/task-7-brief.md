### Task 7: 抑制器 —— 夺取前台与锁定光标

**产出**：进入 `TAKEOVER` 时，PC 本地的键盘与鼠标被**本程序吃掉**，不再影响 PC 上的其它程序；退出时恢复。**这是整个方案里唯一有系统级副作用的部分，也是安全上最危险的部分。**

**Files:**
- Create: `src/agent/Suppressor.cs`
- Modify: `src/agent/Program.cs`

**Interfaces:**
- Consumes: 无
- Produces:
  - `Suppressor`：`public Suppressor(IntPtr ownWindow, Action<string> log)`
    - `public bool Engage()` → 返回是否成功夺取；失败时内部已确保未锁光标
    - `public void Release()`
    - `public bool IsEngaged { get; }`
    - `public event Action ForegroundLost` —— 前台被他人抢走时触发（安全退出用）

**安全不变量（必须原样实现，不得削弱）**：

1. `ClipCursor(IntPtr.Zero)` 必须是 `Release()` 的**第一条语句**，在任何条件判断之前。
2. `Release()` 必须在 `OnFormClosing` / `ApplicationExit` / 心跳超时 / 前台丢失 **四条路径**上都可达。
3. **夺取失败时绝不调 `ClipCursor`**——此时前台仍在别的程序，锁光标会让用户够不到本程序的窗口，只能 taskkill。
4. **未连接时绝不夺取前台**（第二轮跨任务扫描新增）。失效路径：手机掉线后 `tracker` 已回 IDLE，用户**再**把鼠标推到边缘会重新触发 `EnterTakeover` → 若此时 `Engage()` 成功，前台被夺 + 光标被钳在一个像素上，而 Task 8 的心跳恢复里有 `if (!transport.IsConnected) return;`，**救不了这个状态**——用户只剩 `Ctrl+Alt+Esc` 一条退路。故 `EnterTakeover` 的第一件事就是连接检查。

- [ ] **Step 1: 编写抑制器**

创建 `src/agent/Suppressor.cs`：

```csharp
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PcKvm
{
    /// <summary>
    /// 用"夺取前台"代替全局键盘钩子：Windows 只把键盘发给前台窗口，
    /// 因此只要本程序的窗口在前台，其它程序就收不到按键。
    /// 必须用 AttachThreadInput 绕行——朴素 SetForegroundWindow 从后台进程会失败（阶段一实测）。
    /// </summary>
    public class Suppressor
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint attach, uint attachTo, bool fAttach);
        [DllImport("user32.dll")] static extern bool ClipCursor(ref RECT r);
        [DllImport("user32.dll")] static extern bool ClipCursor(IntPtr none);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr h, StringBuilder s, int n);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        readonly IntPtr _own;
        readonly Action<string> _log;
        IntPtr _prevForeground = IntPtr.Zero;

        public bool IsEngaged { get; private set; }
        public event Action ForegroundLost;

        public Suppressor(IntPtr ownWindow, Action<string> log)
        {
            _own = ownWindow;
            _log = log;
        }

        /// <summary>尝试夺取前台并锁定光标。返回 false 表示夺取失败，此时光标未被锁。</summary>
        public bool Engage()
        {
            if (IsEngaged) return true;

            _prevForeground = GetForegroundWindow();

            // 必须在 UI 线程调用：AttachThreadInput 需要的是拥有窗口输入队列的那个线程
            uint myThread = GetCurrentThreadId();
            // 注意：C# 5 不支持内联 out 声明（`out uint _` 是 C# 7 语法，本机 csc 4.0.30319
            // 直接编不过）。必须先声明再传——本计划 Ruling 2 就是在这一行上抓到的。
            uint fgPid;
            uint fgThread = GetWindowThreadProcessId(_prevForeground, out fgPid);

            bool attached = false;
            if (fgThread != 0)
                attached = AttachThreadInput(myThread, fgThread, true);
            try
            {
                SetForegroundWindow(_own);
            }
            finally
            {
                if (attached) AttachThreadInput(myThread, fgThread, false);   // 无条件 detach
            }

            IntPtr now = GetForegroundWindow();
            if (now != _own)
            {
                _log("# 夺取前台失败（前台仍是 " + TitleOf(now) + "），不锁光标");
                return false;
            }

            POINT c;
            GetCursorPos(out c);
            RECT r;
            r.Left = c.X; r.Top = c.Y; r.Right = c.X + 1; r.Bottom = c.Y + 1;
            bool clipped = ClipCursor(ref r);
            _log(clipped ? "# 抑制已生效" : "# ClipCursor 失败 err=" + Marshal.GetLastWin32Error());

            IsEngaged = true;
            return true;
        }

        /// <summary>恢复。ClipCursor 的解除必须在最前面，任何情况下都不能让光标留在锁死状态。</summary>
        public void Release()
        {
            ClipCursor(IntPtr.Zero);

            if (!IsEngaged) return;
            IsEngaged = false;

            if (_prevForeground != IntPtr.Zero)
                SetForegroundWindow(_prevForeground);
            _prevForeground = IntPtr.Zero;
        }

        /// <summary>由心跳线程定时调用：前台被抢走（UAC 安全桌面、锁屏等）时立即放弃抑制。</summary>
        public void CheckForeground()
        {
            if (!IsEngaged) return;
            if (GetForegroundWindow() == _own) return;

            _log("# 前台被抢走，强制解除抑制");
            Release();
            Action h = ForegroundLost;
            if (h != null) h();
        }

        static string TitleOf(IntPtr h)
        {
            if (h == IntPtr.Zero) return "(none)";
            StringBuilder sb = new StringBuilder(256);
            GetWindowText(h, sb, sb.Capacity);
            string s = sb.ToString();
            return s.Length == 0 ? "(untitled)" : s;
        }
    }
}
```

- [ ] **Step 2: 接入（分两个 commit：先纯搬迁，再改写）**

**(a) 先做纯搬迁：把 `MessageHost` 类从 `src/agent/Program.cs` 整体剪到新文件 `src/agent/MessageHost.cs`，一个字都不改，单独一个 commit。**

理由（控制方 Ruling 20）：①一个 `Form` 子类不是装配代码，本就该独立成文件——这与"按风险域分文件"的设计初衷一致；②不搬的话 `Program.cs` 会撞破体量红线（见下方"体量预算"）。搬迁单独成 commit，审查者才能干净地确认"纯移动、零行为变化"。

**(b) 再在 `MessageHost.cs` 里把它改成可见但极小的置顶窗口**（夺取前台需要有真实窗口，且用户要能看见当前状态）：

**体量预算（控制方实算，必读）**：`Program.cs` 在 Task 6 结束时是 **230 行**。Task 7/8/9 还需往里加约 65 行纯接线（Task 7 约 +20、Task 8 约 +36、Task 9 约 +30）。因此：
- `MessageHost` 搬出后 `Program.cs` 约 215 行 → Task 7 后约 235 → Task 8 后约 271 → Task 9 后约 301。
- **故 Program.cs 的体量红线由 250 行上调为 320 行**（Ruling 20，理由与代价见 ledger）。本任务结束时若超过 320 行，报 `DONE_WITH_CONCERNS`，不要自行拆分。


```csharp
    class MessageHost : Form
    {
        Label _status;

        public MessageHost()
        {
            Text = "PC-KVM";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Location = new System.Drawing.Point(0, 0);
            Size = new System.Drawing.Size(220, 28);
            Opacity = 0.75;
            BackColor = System.Drawing.Color.DarkSlateBlue;

            _status = new Label();
            _status.Dock = DockStyle.Fill;
            _status.ForeColor = System.Drawing.Color.White;
            _status.Font = new System.Drawing.Font("Consolas", 9f);
            _status.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            _status.Text = "IDLE";
            Controls.Add(_status);
        }

        public void SetStatus(string s) { _status.Text = s; }
    }
```

在 `Main` 里装配抑制器并把状态显示到窗口：

```csharp
            MessageHost host = new MessageHost();
            IntPtr hwnd = host.Handle;
            host.Show();   // 必须真实显示，隐藏窗口无法持有前台
```

**声明位置（第三轮跨任务扫描修正，必读）**：`Suppressor supp = new Suppressor(hwnd, delegate(string s) { log.WriteLine(s); });` 这一行**不能**留在"transport 装配之后"。Task 8 会在 `transport.Disconnected` 处理器与心跳定时器里调 `supp.Release()`，而那两个订阅位于 `Main` **前段**的 transport 装配块内——**C# 局部变量不支持前向引用**（Task 4 正是因为这个把装配块整体上移过）。故 `supp` 必须声明在 `log` 这个 `StreamWriter` 创建**之后、transport 装配块之前**；它只依赖 `hwnd` 与 `log`，放在那里没有任何障碍。下面的处理器订阅仍按原位（transport 装配之后）放置。

```csharp
            tracker.EnterTakeover += delegate(short px, short py)
            {
                // 安全不变量 4：未连接时绝不夺取前台/锁光标（见上方不变量清单）
                if (!transport.IsConnected)
                {
                    log.WriteLine("# 未连接设备，放弃这次跨越");
                    tracker.AbortTakeover();
                    return;
                }
                if (!supp.Engage())
                {
                    // 夺取失败就放弃这次跨越，保持 IDLE，绝不能让用户被困在锁死光标的状态里
                    tracker.AbortTakeover();
                    return;
                }
                host.SetStatus("TAKEOVER → 手机");
                log.WriteLine("# ENTER takeover at phone(" + px + "," + py + ")");
                transport.Send(Protocol.EncodeHome());
                cursor.Reset();
                cursor.SetPosition(px, py);
                transport.Send(Protocol.EncodeEnter(px, py));
            };
            tracker.LeaveTakeover += delegate
            {
                supp.Release();
                host.SetStatus("IDLE");
                log.WriteLine("# LEAVE takeover");
                transport.Send(Protocol.EncodeLeave());
            };
            supp.ForegroundLost += delegate { tracker.AbortTakeover(); host.SetStatus("IDLE"); };
```

在 `EdgeTracker` 里补一个方法（Task 6 的类需要扩展）：

```csharp
        /// <summary>放弃跨越：回到 IDLE 并解除武装，等待用户把光标移离边缘。</summary>
        public void AbortTakeover()
        {
            Current = KvmState.Idle;
            Armed = false;
        }
```

在 `Application.ApplicationExit` 里加 `supp.Release();`，并在窗口关闭路径上也加。启动一个定时器做前台检查：

```csharp
            Timer guard = new Timer();
            guard.Interval = 250;
            guard.Tick += delegate { supp.CheckForeground(); };
            guard.Start();
```

- [ ] **Step 3: 验证（本任务危险，务必按顺序）**

**准备**：打开记事本并点进去，确认能打字。

Run: `dist\pc-kvm.exe`

判定：
1. **先测夺取失败路径**：不要碰鼠标，直接在 `pc-kvm.log` 里确认程序已启动且状态为 `IDLE`
2. 把鼠标推到右边缘 → 小窗标题变成 `TAKEOVER → 手机` ✅
3. **立刻在记事本里打字**（不要点击记事本！点击会夺走前台，抑制会按设计解除）—— **字符不应出现** ✅
4. 移动鼠标 —— **PC 光标应卡住不动** ✅
5. 把手机光标推回左边缘 → 小窗回到 `IDLE` ✅
6. **焦点恢复**：随便点一个窗口，应能正常操作 ✅
7. **锁死恢复演练**：在 `TAKEOVER` 状态下用任务管理器 `taskkill /F /IM pc-kvm.exe` → 鼠标应立刻解锁（系统在进程退出时释放 ClipCursor） ✅

**任何一步出现鼠标锁死**：`taskkill /F /IM pc-kvm.exe`，进程退出即解锁；最坏情况重启系统。

- [ ] **Step 4: 提交**

```bash
cd /g/pc-kvm
git add src/agent/Suppressor.cs src/agent/EdgeTracker.cs src/agent/Program.cs
git commit -m "feat: 抑制器 —— AttachThreadInput 夺前台 + ClipCursor，零全局钩子"
```

---

