# PC ↔ Android 键鼠共享 · 阶段一（前置验证）实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用三个独立的最小探针，把整套方案里仅剩的三个未知量钉死：Raw Input 能否提供所需数据、焦点小窗能否吃掉键盘、手机侧零安装能否自建 HID 设备。

**Architecture:** 三个探针彼此独立、可单独运行和验证。它们不是产品代码，是**一次性实验**——每个探针只回答一个是/否问题，并把答案（含实测数字）写回实验记录。产品化实现是阶段二的事。

**Tech Stack:**
- PC 侧：C# 5 / .NET Framework 4.8，用 Windows 自带的 `csc.exe` 编译（**无 SDK、无 NuGet、无下载**）
- 手机侧：Java 8 语义（javac `--release 8`）→ `d8` 转 dex → `adb push` → `app_process` 拉起
- 设备通信：`adb`（USB）

**Spec:** `docs/superpowers/specs/2026-09-21-pc-android-kvm-design.md`

## Global Constraints

以下约束适用于**每一个**任务，不再逐条重复：

- **目标平台**：Windows 11 x64 + 小米 `25053RP5CC`（HyperOS 3.0 `OS3.0.304.0.WOTCNXM` / Android 16 / SDK 36），设备**未 root**，SELinux **Enforcing**
- **禁止任何全局键盘钩子**（`SetWindowsHookEx(WH_KEYBOARD_LL)`）——本机运行网易青骓，键盘钩子特征与键盘记录器一致
- **不传画面、不镜像、不在 PC 上开镜像窗口**
- **PC 侧产物必须是绿色免安装单文件 exe**：不写注册表、不装服务、不开机自启
- **手机侧零安装**：无 APK、无图标、无权限弹窗、无残留；`app_process` 起的进程退出后设备上不留东西
- **C# 语言版本上限 = C# 5**。禁止使用：字符串插值 `$""`、null 条件运算符 `?.`、表达式体成员 `=>`、`nameof`、自动属性初始化器、`using static`。**允许**：`var`、lambda、LINQ、`async/await`（C# 5 已有）、泛型、匿名类型
- **csc 开关一律用 `-` 前缀，不用 `/` 前缀**。在 Git Bash 下 `/nologo` 会被 MSYS 路径转换吃成 `C:/.../Git/nologo`，产生 `error CS2001: 未能找到源文件` 这种误导性错误
- **设备侧 Java 只用 JDK 标准库**（`java.io` / `java.net` / `java.nio`），**不引用任何 `android.*` 类**——这样 javac 无需 `android.jar` 即可编译
- 显示器几何：双屏 `DISPLAY2`(主, 0,0, 1920×1080) + `DISPLAY1`(1920,0, 1920×1080)，DPI 96
- 手机屏幕：2136 × 3200（竖屏），密度 400

---

## 文件结构

```
G:\pc-kvm\
├── probes\
│   ├── 01-rawinput\
│   │   ├── RawInputProbe.cs        探针 1：Raw Input 数据完备性
│   │   └── run.ps1                 构建 + 运行
│   ├── 02-suppress\
│   │   ├── SuppressProbe.cs        探针 2：焦点小窗键盘抑制
│   │   └── run.ps1
│   └── 03-uhid\
│       ├── UhidProbe.java          探针 3：设备侧 UHID 自建
│       ├── build.ps1               javac + d8 + push
│       └── run.ps1                 启动 + 喂指令
├── tools\
│   └── r8.jar                      下载而来，仅构建期使用
├── FINDINGS.md                     三个探针的实测结论（本计划的产物）
└── docs\superpowers\specs\...      设计文档
```

**为什么按探针分目录而不是按技术分层**：三个探针生命周期独立——可能探针 1 通过后永远不再运行，而探针 3 会被反复迭代。放一起会让"删掉一个"变成考古工作。

---

## Task 1: Raw Input 数据完备性探针（验证 2）

**回答的问题**：`RIDEV_INPUTSINK` 注册的 Raw Input，能否在**窗口非焦点**时收到鼠标相对增量 `lLastX`/`lLastY` 与键盘 scancode？多个指点设备（本机有两个虚拟显示驱动）会不会产生干扰？

**Files:**
- Create: `probes/01-rawinput/RawInputProbe.cs`
- Create: `probes/01-rawinput/run.ps1`

**Interfaces:**
- Consumes: 无（本计划第一个任务）
- Produces: 一个可运行的 `RawInputProbe.exe`；实测结论写入 `FINDINGS.md`。后续任务不依赖此任务的代码，只依赖其结论

**背景（实现者必读）**：Raw Input 的 `RAWMOUSE` 结构体在 C# 里**不能**用朴素的 `LayoutKind.Sequential` 直接映射。C 定义中 `ulButtons` 是一个与匿名结构体 `{usButtonFlags; usButtonData}` 的联合，成员 `usFlags` 之后有 2 字节隐式填充。C# 默认布局会把 `usButtonFlags` 放到偏移 2 而非 4，导致后续所有字段错位。**必须显式补一个 `_pad` 字段。**

- [ ] **Step 1: 创建探针源码**

创建 `probes/01-rawinput/RawInputProbe.cs`：

```csharp
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

class RawInputProbe : Form
{
    const int WM_INPUT = 0x00FF;
    const uint RIDEV_INPUTSINK = 0x00000100;
    const uint RID_INPUT = 0x10000003;
    const uint RIM_TYPEMOUSE = 0;
    const uint RIM_TYPEKEYBOARD = 1;

    // 按钮位（usButtonFlags）
    const ushort RI_MOUSE_LEFT_BUTTON_DOWN   = 0x0001;
    const ushort RI_MOUSE_LEFT_BUTTON_UP     = 0x0002;
    const ushort RI_MOUSE_RIGHT_BUTTON_DOWN  = 0x0004;
    const ushort RI_MOUSE_RIGHT_BUTTON_UP    = 0x0008;
    const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;
    const ushort RI_MOUSE_MIDDLE_BUTTON_UP   = 0x0020;
    const ushort RI_MOUSE_WHEEL              = 0x0400;

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint   dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTHEADER
    {
        public uint   dwType;
        public uint   dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    // 关键：_pad 不能省。见任务说明。
    [StructLayout(LayoutKind.Sequential)]
    struct RAWMOUSE
    {
        public ushort usFlags;
        public ushort _pad;
        public ushort usButtonFlags;
        public ushort usButtonData;
        public uint   ulRawButtons;
        public int    lLastX;
        public int    lLastY;
        public uint   ulExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint   Message;
        public uint   ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterRawInputDevices(
        RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll")]
    static extern uint GetRawInputData(
        IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    StreamWriter _log;
    long _mouseCount, _keyCount, _startTicks;
    Label _status;

    public RawInputProbe()
    {
        Text = "RawInput Probe  —  把焦点切到别的窗口，继续动鼠标/敲键盘";
        Width = 780; Height = 260;
        StartPosition = FormStartPosition.Manual;
        Location = new System.Drawing.Point(20, 20);

        _status = new Label();
        _status.Dock = DockStyle.Fill;
        _status.Font = new System.Drawing.Font("Consolas", 11f);
        _status.Text = "初始化...";
        Controls.Add(_status);

        string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rawinput-probe.log");
        _log = new StreamWriter(logPath, false, Encoding.UTF8);
        _log.AutoFlush = true;
        _startTicks = Stopwatch.GetTimestamp();
        _log.WriteLine("# RawInput 探针日志");
        _log.WriteLine("# t_ms\tsrc\tdevice\tlLastX\tlLastY\tflags\tbtn\twheel\tvkey\tmakecode\tscancode");
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[2];
        // 通用桌面控制: UsagePage 0x01, 鼠标 Usage 0x02
        rid[0].usUsagePage = 0x01; rid[0].usUsage = 0x02;
        rid[0].dwFlags = RIDEV_INPUTSINK; rid[0].hwndTarget = this.Handle;
        // 通用桌面控制: 键盘 Usage 0x06
        rid[1].usUsagePage = 0x01; rid[1].usUsage = 0x06;
        rid[1].dwFlags = RIDEV_INPUTSINK; rid[1].hwndTarget = this.Handle;

        bool ok = RegisterRawInputDevices(rid, 2, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            _status.Text = "RegisterRawInputDevices 失败, Win32Error=" + err;
            _log.WriteLine("# FATAL: RegisterRawInputDevices failed, err=" + err);
            return;
        }
        _status.Text = "已注册。RAWMOUSE size=" + Marshal.SizeOf(typeof(RAWMOUSE))
                     + " (期望 24)。请切到别的窗口操作鼠标键盘。";
        _log.WriteLine("# RAWMOUSE size=" + Marshal.SizeOf(typeof(RAWMOUSE)) + " (期望 24)");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_INPUT)
        {
            try { HandleRawInput(m.LParam); }
            catch (Exception ex) { _log.WriteLine("# EXC " + ex.Message); }
        }
        base.WndProc(ref m);
    }

    void HandleRawInput(IntPtr hRawInput)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
        GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0) return;

        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            uint got = GetRawInputData(hRawInput, RID_INPUT, buf, ref size, headerSize);
            if (got != size) return;

            RAWINPUTHEADER hdr = (RAWINPUTHEADER)Marshal.PtrToStructure(buf, typeof(RAWINPUTHEADER));
            IntPtr payload = new IntPtr(buf.ToInt64() + headerSize);
            long tMs = (Stopwatch.GetTimestamp() - _startTicks) * 1000 / Stopwatch.Frequency;

            string fgTitle = ForegroundTitle();

            if (hdr.dwType == RIM_TYPEMOUSE)
            {
                RAWMOUSE ms = (RAWMOUSE)Marshal.PtrToStructure(payload, typeof(RAWMOUSE));
                _mouseCount++;

                int wheel = 0;
                if ((ms.usButtonFlags & RI_MOUSE_WHEEL) != 0)
                    wheel = (short)ms.usButtonData;

                _log.WriteLine(tMs + "\tMOUSE\t" + hdr.hDevice.ToInt64().ToString("X")
                    + "\t" + ms.lLastX + "\t" + ms.lLastY
                    + "\t0x" + ms.usFlags.ToString("X4")
                    + "\t0x" + ms.usButtonFlags.ToString("X4")
                    + "\t" + wheel + "\t\t\t" + fgTitle);

                _status.Text = "鼠标事件=" + _mouseCount + "  键盘事件=" + _keyCount
                    + "\n最近: lLastX=" + ms.lLastX + " lLastY=" + ms.lLastY
                    + " btnFlags=0x" + ms.usButtonFlags.ToString("X4")
                    + "\n当前前台窗口: " + fgTitle;
            }
            else if (hdr.dwType == RIM_TYPEKEYBOARD)
            {
                RAWKEYBOARD kb = (RAWKEYBOARD)Marshal.PtrToStructure(payload, typeof(RAWKEYBOARD));
                _keyCount++;
                // Flags bit0 = RI_KEY_BREAK(抬起), bit1 = RI_KEY_E0
                bool isUp = (kb.Flags & 0x01) != 0;
                bool e0 = (kb.Flags & 0x02) != 0;
                int scancode = kb.MakeCode | (e0 ? 0xE000 : 0);

                _log.WriteLine(tMs + "\tKEY\t" + hdr.hDevice.ToInt64().ToString("X")
                    + "\t\t\t\t\t\t" + kb.VKey + "\t" + kb.MakeCode
                    + "\t" + scancode + (isUp ? " UP" : " DOWN") + "\t" + fgTitle);

                _status.Text = "鼠标事件=" + _mouseCount + "  键盘事件=" + _keyCount
                    + "\n最近: VKey=" + kb.VKey + " MakeCode=" + kb.MakeCode
                    + " scancode=0x" + scancode.ToString("X") + (isUp ? " UP" : " DOWN")
                    + "\n当前前台窗口: " + fgTitle;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    string ForegroundTitle()
    {
        IntPtr h = GetForegroundWindow();
        if (h == IntPtr.Zero) return "(none)";
        StringBuilder sb = new StringBuilder(256);
        GetWindowText(h, sb, sb.Capacity);
        string s = sb.ToString();
        if (s.Length == 0) s = "(untitled)";
        return s.Replace("\t", " ");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _log.WriteLine("# 结束: 鼠标事件=" + _mouseCount + " 键盘事件=" + _keyCount);
        _log.Close();
        base.OnFormClosed(e);
    }

    [STAThread]
    static void Main()
    {
        Application.Run(new RawInputProbe());
    }
}
```

- [ ] **Step 2: 创建构建运行脚本**

创建 `probes/01-rawinput/run.ps1`：

```powershell
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) { throw "找不到 csc.exe: $csc" }

Write-Host "编译中..." -ForegroundColor Cyan
# 注意：全部用 - 前缀开关。用 / 前缀会被 MSYS 路径转换破坏。
& $csc -nologo -target:winexe -platform:x64 -optimize+ `
       -out:"$here\RawInputProbe.exe" `
       -r:System.Windows.Forms.dll `
       -r:System.Drawing.dll `
       "$here\RawInputProbe.cs"

if ($LASTEXITCODE -ne 0) { throw "编译失败" }
Write-Host "编译成功: $here\RawInputProbe.exe" -ForegroundColor Green
Write-Host "启动探针..." -ForegroundColor Cyan
& "$here\RawInputProbe.exe"
```

- [ ] **Step 3: 编译并运行**

Run: `powershell -ExecutionPolicy Bypass -File probes/01-rawinput/run.ps1`

Expected: 窗口弹出，标题栏下方显示 `已注册。RAWMOUSE size=24 (期望 24)`。**如果 size 不是 24，立刻停止——结构体布局错了，后面所有数据都是垃圾。**

- [ ] **Step 4: 执行验证操作（人工，约 1 分钟）**

按顺序做，每步之间停顿一下便于读日志：

1. 鼠标在探针窗口内移动 2 秒
2. **把焦点切到记事本**（点任务栏或 Alt+Tab），在记事本窗口上移动鼠标 3 秒
3. 仍在记事本中，敲 `abcdef` 和 `Shift+A`
4. 在记事本中滚轮上下各滚 3 格
5. 左右中键各点一次

- [ ] **Step 5: 判定结果并记录**

Run: `powershell -Command "Get-Content probes\01-rawinput\rawinput-probe.log -TotalCount 60"`

**判定标准（全部满足才算通过）：**

| 检查项 | 期望 |
|---|---|
| `RAWMOUSE size` | `24` |
| 步骤 2（焦点在记事本）的 MOUSE 行 | `lLastX`/`lLastY` **有非零值** ← **这是本探针的核心问题** |
| 每行末尾的前台窗口名 | 步骤 2 时应为 `记事本` 或类似，证明确实是非焦点状态 |
| 步骤 3 的 KEY 行 | 有 6 个字母 + Shift，`VKey` 有值 |
| 步骤 4 的 MOUSE 行 | `wheel` 列有 ±120 的倍数 |
| 步骤 5 的 MOUSE 行 | 有 `0x0001/0x0002`（左）、`0x0004/0x0008`（右）、`0x0010/0x0020`（中） |

把结论写入 `FINDINGS.md`（见 Task 4），格式：

```markdown
## 验证 2：Raw Input 数据完备性

**结论**：通过 / 失败
- RAWMOUSE 结构体大小：24 ✅
- 非焦点时收到鼠标相对增量：是 / 否
- 非焦点时收到键盘 scancode：是 / 否
- 多指点设备干扰：无 / 有（描述）
- 实测备注：（例如滚轮单位、e0 扩展键处理）
```

- [ ] **Step 6: 提交**

```bash
cd /g/pc-kvm
git add probes/01-rawinput/
git commit -m "probe: Raw Input 数据完备性探针（验证 2）"
```

---

## Task 2: 焦点小窗键盘抑制探针（验证 1）

**回答的问题**：一个获得焦点的置顶无边框窗口，能否**真正阻止**按键送达原本的前台应用？`ClipCursor` 锁鼠标后，原应用还会不会收到鼠标事件？焦点能否可靠还原？

**这是整个方案最自创的一环**——"用前台焦点代替全局钩子"没有先例可抄，必须实测。**若此探针失败，架构第 8 节（抑制机制）作废，必须改用 `WH_KEYBOARD_LL`（回到青骓风险）或内核驱动，需重开决策。**

**Files:**
- Create: `probes/02-suppress/SuppressProbe.cs`
- Create: `probes/02-suppress/run.ps1`

**Interfaces:**
- Consumes: 无
- Produces: 实测结论写入 `FINDINGS.md`。产品化抑制器（阶段二）依赖此结论的**可行性判定**，不依赖具体代码

**背景（实现者必读）**：抑制的三步是 ① `GetForegroundWindow()` 存下原前台窗口 ② 显示置顶无边框窗口并用 `SetForegroundWindow` 抢焦点 ③ `ClipCursor` 把光标锁进 1×1 区域。恢复时逆序，并且 `ClipCursor(IntPtr.Zero)` **必须**放在 `finally` 或窗体关闭回调里——否则探针崩溃时用户的鼠标会永久锁死（重启才能解）。

- [ ] **Step 1: 创建探针源码**

创建 `probes/02-suppress/SuppressProbe.cs`：

```csharp
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

class SuppressProbe : Form
{
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    static extern bool ClipCursor(ref RECT lpRect);
    [DllImport("user32.dll")]
    static extern bool ClipCursor(IntPtr lpRect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    IntPtr _savedForeground = IntPtr.Zero;
    POINT  _savedCursor;
    bool   _engaged = false;

    Label _status;
    Button _btn;

    public SuppressProbe()
    {
        Text = "Suppress Probe — 先把记事本打开并让它在最前";
        Width = 720; Height = 260;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(20, 20);

        _status = new Label();
        _status.Dock = DockStyle.Top;
        _status.Height = 150;
        _status.Font = new Font("Consolas", 10f);
        _status.Text = "步骤：\n"
            + "1. 打开记事本，点进去，随便打几个字（确认能打字）\n"
            + "2. 按 Ctrl+Alt+K 进入「抑制模式」\n"
            + "3. 在记事本上疯狂打字 —— 字符【不应该】出现\n"
            + "4. 同时试试移动鼠标 —— 光标【应该】被锁住不动\n"
            + "5. 按 Ctrl+Alt+U 退出，再打字 —— 字符【应该】恢复正常";
        Controls.Add(_status);

        _btn = new Button();
        _btn.Text = "强制解除抑制";
        _btn.Dock = DockStyle.Bottom;
        _btn.Click += delegate { Release(); };
        Controls.Add(_btn);

        KeyPreview = true;
        KeyDown += OnKeyDown;
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Control && e.Alt && e.KeyCode == Keys.K) { Engage(); e.Handled = true; }
        else if (e.Control && e.Alt && e.KeyCode == Keys.U) { Release(); e.Handled = true; }
    }

    void Engage()
    {
        if (_engaged) return;

        _savedForeground = GetForegroundWindow();
        GetCursorPos(out _savedCursor);

        // 无边框置顶，覆盖屏幕中央一小块（不遮住记事本便于肉眼观察）
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        Opacity = 0.65;
        BackColor = Color.DarkRed;
        Bounds = new Rectangle(Bounds.Left, Bounds.Top, Bounds.Width, Bounds.Height);

        Show();
        BringToFront();
        Activate();
        SetForegroundWindow(Handle);

        // 把光标锁在窗口中心 1x1 像素
        POINT c;
        GetCursorPos(out c);
        RECT r;
        r.Left = c.X; r.Top = c.Y; r.Right = c.X + 1; r.Bottom = c.Y + 1;
        bool clipped = ClipCursor(ref r);

        _engaged = true;
        _status.Text = "【抑制模式 ON】\n"
            + "原前台窗口: " + TitleOf(_savedForeground) + "\n"
            + "当前前台窗口: " + TitleOf(GetForegroundWindow()) + "\n"
            + "ClipCursor: " + (clipped ? "成功" : "失败 err=" + Marshal.GetLastWin32Error()) + "\n"
            + "→ 现在去记事本上打字，字符不应出现。Ctrl+Alt+U 解除。";
    }

    void Release()
    {
        // ClipCursor 解除放在最前面：任何情况下都不能让用户鼠标锁死
        ClipCursor(IntPtr.Zero);

        if (_engaged)
        {
            _engaged = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            TopMost = false;
            Opacity = 1.0;
            BackColor = SystemColors.Control;
            SetCursorPos(_savedCursor.X, _savedCursor.Y);
            if (_savedForeground != IntPtr.Zero) SetForegroundWindow(_savedForeground);
        }

        _status.Text = "【抑制模式 OFF】\n"
            + "光标已解锁，焦点已还原到: " + TitleOf(_savedForeground) + "\n"
            + "当前前台窗口: " + TitleOf(GetForegroundWindow()) + "\n"
            + "→ 现在去记事本打字，字符应恢复正常。Ctrl+Alt+K 重新进入。";
    }

    string TitleOf(IntPtr h)
    {
        if (h == IntPtr.Zero) return "(none)";
        StringBuilder sb = new StringBuilder(256);
        GetWindowText(h, sb, sb.Capacity);
        string s = sb.ToString();
        return s.Length == 0 ? "(untitled)" : s;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        ClipCursor(IntPtr.Zero);   // 兜底：绝不留下锁死的光标
        base.OnFormClosing(e);
    }

    [STAThread]
    static void Main()
    {
        Application.Run(new SuppressProbe());
    }
}
```

- [ ] **Step 2: 创建构建运行脚本**

创建 `probes/02-suppress/run.ps1`：

```powershell
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

Write-Host "编译中..." -ForegroundColor Cyan
& $csc -nologo -target:winexe -platform:x64 -optimize+ `
       -out:"$here\SuppressProbe.exe" `
       -r:System.Windows.Forms.dll `
       -r:System.Drawing.dll `
       "$here\SuppressProbe.cs"

if ($LASTEXITCODE -ne 0) { throw "编译失败" }
Write-Host "编译成功，启动..." -ForegroundColor Green
& "$here\SuppressProbe.exe"
```

- [ ] **Step 3: 编译并运行**

Run: `powershell -ExecutionPolicy Bypass -File probes/02-suppress/run.ps1`

Expected: 窗口弹出，显示五步操作说明。编译无错误。

- [ ] **Step 4: 执行验证操作（人工，约 1 分钟）**

1. 打开记事本，点进去，打几个字确认能打字
2. 点一下探针窗口让它获得焦点 → 按 `Ctrl+Alt+K`
3. **点回记事本**，疯狂打字
4. 尝试移动鼠标
5. 按 `Ctrl+Alt+U`（若按键进不去探针，就点探针的「强制解除抑制」按钮）
6. 回到记事本再打字

- [ ] **Step 5: 判定结果并记录**

**判定标准：**

| 检查项 | 期望 | 若不符 |
|---|---|---|
| 步骤 3：字符**没有**出现在记事本 | ✅ 必须 | ❌ **架构第 8 节作废** |
| 步骤 4：鼠标光标**卡住不动** | ✅ 必须 | ❌ `ClipCursor` 需要改成 `SetCapture` 方案 |
| 步骤 5：按住 Ctrl+Alt 时字符**不出现** | ⚠️ 期望能抑制；系统级热键（Win 键）漏出去是**已知可接受** | 记录哪些漏了 |
| 步骤 6：写字恢复正常 | ✅ 必须 | ❌ 恢复逻辑有 bug |
| 探针面板显示的"当前前台窗口" | 进入抑制后应为本探针标题 | — |

**特别注意**：步骤 3 里**从记事本按 Ctrl+Alt+K/U 是收不到的**（抑制期间按键进探针窗口不假，但焦点在记事本时探针收不到 K/U 组合——正确做法是先让探针获得焦点再按键）。这是设计上的正常现象，记录到备注里。

写入 `FINDINGS.md`：

```markdown
## 验证 1：焦点小窗键盘抑制

**结论**：通过 / 失败
- 字符是否被成功拦截：是 / 否
- 是否有按键漏到记事本（列出）：无 / 有（…）
- ClipCursor 是否锁住光标：是 / 否
- 焦点还原是否成功：是 / 否
- 系统级热键（Win / Ctrl+Alt+Del）行为：
- 实测备注：
```

- [ ] **Step 6: 提交**

```bash
cd /g/pc-kvm
git add probes/02-suppress/
git commit -m "probe: 焦点小窗键盘抑制探针（验证 1）"
```

---

## Task 3: 设备侧 UHID 自建探针（验证 4）

**回答的问题**：在应用进程之外（`app_process` + shell uid），能否打开 `/dev/uhid`、创建自定义 HID 设备、并让 Android 渲染出真实光标？

**这是本计划中风险最高、也最有价值的一步。** scrcpy 的 `--mouse=uhid` 已经间接证明可行，但那是 scrcpy 的代码。我们必须自建复现一遍——包括自己写 HID 报告描述符。

**成功的话，手机侧"零安装"这条路成立。失败的话退回装 APK（仍用 UHID 注入），牺牲零安装但保住光标。**

**Files:**
- Create: `probes/03-uhid/UhidProbe.java`
- Create: `probes/03-uhid/build.ps1`
- Create: `probes/03-uhid/run.ps1`
- Create: `tools/r8.jar`（下载）

**Interfaces:**
- Consumes: 无
- Produces: 一个能跑在设备上的 `uhidprobe.jar`，以及 `build.ps1` / `run.ps1` 这套构建-部署-驱动流程。**阶段二的产品实现直接复用这套流程和 HID 描述符构造代码**

**背景（实现者必读，四条关键事实）：**

1. **不要引用任何 `android.*` 类。** 只用 `java.io` / `java.net` / `java.nio`，这样 javac 不需要 `android.jar` 就能编译。
2. **`/dev/uhid` 在目标设备上可直接写**（已实测）：`crw-rw-rw-`，且 shell 用户属 `3011(uhid)` 组。
3. **写事件必须写满 `sizeof(struct uhid_event)` = 4376 字节**。内核的 `uhid_char_write` 会校验长度。结构体是 `__attribute__((packed))` 的，布局如下（**Java 里手写字节偏移，不要用 `ByteBuffer` 的默认对齐**）：
   - `uhid_event`: `type`(u32, offset 0) + union(offset 4)
   - `UHID_CREATE2` = 11，`UHID_INPUT2` = 12，`UHID_DESTROY` = 2
   - `create2`: `name[128]`@4, `phys[64]`@132, `uniq[64]`@196, `rd_size`(u16)@260, `bus`(u16)@262, `vendor`(u32)@264, `product`(u32)@268, `version`(u32)@272, `country`(u32)@276, `rd_data[4096]`@280
   - `input2`: `size`(u16)@4, `data[4096]`@6
4. **创建成功后，必须持续调用 `ioctl(fd, UHID_START)`……不需要。** 实际上内核会通过 `read()` 返回 `UHID_START` 事件通知设备已就绪。**为简化探针，我们先不 read，直接开始写 input 事件**——如果内核要求先处理 START，会在 Step 5 暴露出来并据此修正。

- [ ] **Step 1: 编写 Java 探针**

创建 `probes/03-uhid/UhidProbe.java`：

```java
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.RandomAccessFile;

/**
 * UHID 探针：在应用进程之外创建自定义 HID 鼠标设备。
 * 只用 JDK 标准库，不引用 android.*。
 *
 * 用法: app_process / UhidProbe <mode>
 *   mode = create  创建设备并保持存活，之后每 200ms 写一次向右下的相对位移
 *   mode = once    创建一个极简鼠标，写 20 次位移后退出
 */
public class UhidProbe {

    static final int UHID_CREATE2 = 11;
    static final int UHID_INPUT2  = 12;
    static final int UHID_DESTROY = 2;

    static final int UHID_EVENT_SIZE = 4376;   // sizeof(struct uhid_event)

    // 偏移
    static final int OFF_NAME     = 4;
    static final int OFF_PHYS     = 132;
    static final int OFF_UNIQ     = 196;
    static final int OFF_RD_SIZE  = 260;
    static final int OFF_BUS      = 262;
    static final int OFF_VENDOR   = 264;
    static final int OFF_PRODUCT  = 268;
    static final int OFF_VERSION  = 272;
    static final int OFF_COUNTRY  = 276;
    static final int OFF_RD_DATA  = 280;

    static final int OFF_INPUT_SIZE = 4;
    static final int OFF_INPUT_DATA = 6;

    static final int BUS_USB = 0x03;

    /**
     * 极简相对鼠标的 HID 报告描述符。
     * 用途 0x01(Generic Desktop) / 0x02(Mouse)
     * 报告: [buttons(1B)] [dx(1B)] [dy(1B)] [wheel(1B)] = 4 字节
     */
    static final int[] REPORT_DESCRIPTOR = {
        0x05, 0x01,        // Usage Page (Generic Desktop)
        0x09, 0x02,        // Usage (Mouse)
        0xA1, 0x01,        // Collection (Application)
        0x09, 0x01,        //   Usage (Pointer)
        0xA1, 0x00,        //   Collection (Physical)
        0x05, 0x09,        //     Usage Page (Button)
        0x19, 0x01,        //     Usage Minimum (1)
        0x29, 0x03,        //     Usage Maximum (3)
        0x15, 0x00,        //     Logical Minimum (0)
        0x25, 0x01,        //     Logical Maximum (1)
        0x95, 0x03,        //     Report Count (3)
        0x75, 0x01,        //     Report Size (1)
        0x81, 0x02,        //     Input (Data,Var,Abs)   -- 3 个按钮位
        0x95, 0x01,        //     Report Count (1)
        0x75, 0x05,        //     Report Size (5)
        0x81, 0x01,        //     Input (Const)          -- 5 位填充
        0x05, 0x01,        //     Usage Page (Generic Desktop)
        0x09, 0x30,        //     Usage (X)
        0x09, 0x31,        //     Usage (Y)
        0x15, 0x81,        //     Logical Minimum (-127)
        0x25, 0x7F,        //     Logical Maximum (127)
        0x75, 0x08,        //     Report Size (8)
        0x95, 0x02,        //     Report Count (2)
        0x81, 0x06,        //     Input (Data,Var,Rel)   -- dx, dy
        0x09, 0x38,        //     Usage (Wheel)
        0x15, 0x81,        //     Logical Minimum (-127)
        0x25, 0x7F,        //     Logical Maximum (127)
        0x75, 0x08,        //     Report Size (8)
        0x95, 0x01,        //     Report Count (1)
        0x81, 0x06,        //     Input (Data,Var,Rel)   -- wheel
        0xC0,              //   End Collection
        0xC0               // End Collection
    };

    public static void main(String[] args) throws Exception {
        String mode = args.length > 0 ? args[0] : "once";
        System.out.println("UHIDPROBE start mode=" + mode);

        RandomAccessFile dev;
        try {
            dev = new RandomAccessFile("/dev/uhid", "rw");
        } catch (Exception e) {
            System.out.println("UHIDPROBE FAIL open /dev/uhid: " + e);
            return;
        }
        System.out.println("UHIDPROBE open /dev/uhid OK");

        byte[] ev = new byte[UHID_EVENT_SIZE];
        buildCreate2(ev, "PC-KVM Probe Mouse");
        int n = writeAll(dev, ev);
        System.out.println("UHIDPROBE CREATE2 wrote " + n + " bytes");

        if ("create".equals(mode)) {
            // 每 200ms 往右下推一点，方便肉眼确认光标在动
            int iter = 0;
            while (iter < 200) {
                byte[] inp = new byte[UHID_EVENT_SIZE];
                buildInput2(inp, (byte) 0, (byte) 3, (byte) 3, (byte) 0);
                int w = writeAll(dev, inp);
                if (w <= 0) {
                    System.out.println("UHIDPROBE INPUT2 write failed at iter=" + iter);
                    break;
                }
                Thread.sleep(200);
                iter++;
            }
            System.out.println("UHIDPROBE done " + iter + " moves");
        } else {
            byte[] inp = new byte[UHID_EVENT_SIZE];
            buildInput2(inp, (byte) 0, (byte) 5, (byte) 5, (byte) 0);
            writeAll(dev, inp);
            System.out.println("UHIDPROBE wrote one move");
        }

        dev.close();
        System.out.println("UHIDPROBE exit");
    }

    static void buildCreate2(byte[] ev, String name) {
        putU32(ev, 0, UHID_CREATE2);
        putStr(ev, OFF_NAME, name, 128);
        putStr(ev, OFF_PHYS, "pc-kvm", 64);
        putStr(ev, OFF_UNIQ, "", 64);
        putU16(ev, OFF_RD_SIZE, REPORT_DESCRIPTOR.length);
        putU16(ev, OFF_BUS, BUS_USB);
        putU32(ev, OFF_VENDOR, 0x1234);
        putU32(ev, OFF_PRODUCT, 0x5678);
        putU32(ev, OFF_VERSION, 1);
        putU32(ev, OFF_COUNTRY, 0);
        for (int i = 0; i < REPORT_DESCRIPTOR.length; i++) {
            ev[OFF_RD_DATA + i] = (byte) REPORT_DESCRIPTOR[i];
        }
    }

    static void buildInput2(byte[] ev, byte buttons, byte dx, byte dy, byte wheel) {
        putU32(ev, 0, UHID_INPUT2);
        putU16(ev, OFF_INPUT_SIZE, 4);           // 报告长度
        ev[OFF_INPUT_DATA + 0] = buttons;
        ev[OFF_INPUT_DATA + 1] = dx;
        ev[OFF_INPUT_DATA + 2] = dy;
        ev[OFF_INPUT_DATA + 3] = wheel;
    }

    static int writeAll(RandomAccessFile dev, byte[] buf) {
        try {
            dev.write(buf);
            return buf.length;
        } catch (IOException e) {
            System.out.println("UHIDPROBE write IOException: " + e);
            return -1;
        }
    }

    static void putU32(byte[] b, int off, int v) {
        b[off]     = (byte) (v & 0xFF);
        b[off + 1] = (byte) ((v >>> 8) & 0xFF);
        b[off + 2] = (byte) ((v >>> 16) & 0xFF);
        b[off + 3] = (byte) ((v >>> 24) & 0xFF);
    }

    static void putU16(byte[] b, int off, int v) {
        b[off]     = (byte) (v & 0xFF);
        b[off + 1] = (byte) ((v >>> 8) & 0xFF);
    }

    static void putStr(byte[] b, int off, String s, int cap) {
        byte[] raw;
        try { raw = s.getBytes("UTF-8"); } catch (Exception e) { raw = s.getBytes(); }
        int len = Math.min(raw.length, cap - 1);
        System.arraycopy(raw, 0, b, off, len);
    }
}
```

- [ ] **Step 2: 下载 d8（r8.jar）**

Run:
```bash
mkdir -p /g/pc-kvm/tools
curl -L --ssl-no-revoke --fail -o /g/pc-kvm/tools/r8.jar \
  "https://maven.google.com/com/android/tools/r8/8.9.35/r8-8.9.35.jar"
ls -la /g/pc-kvm/tools/r8.jar
```

Expected: 文件存在，约 30–40 MB。若该版本号 404，去 `https://maven.google.com/web/index.html#com.android.tools:r8` 查最新版本号后替换。

**注意**：`--ssl-no-revoke` 是本办公网必需的（网络无法访问证书吊销服务器，会报 `CRYPT_E_NO_REVOCATION_CHECK`）。证书链与域名校验仍然生效。

- [ ] **Step 3: 编写设备侧构建脚本**

创建 `probes/03-uhid/build.ps1`：

```powershell
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $here)
$javac = "C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin\javac.exe"
$java  = "C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin\java.exe"
$r8    = Join-Path $root "tools\r8.jar"

foreach ($p in @($javac, $java, $r8)) {
    if (-not (Test-Path $p)) { throw "缺少: $p" }
}

$out = Join-Path $here "classes"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force -Path $out | Out-Null

Write-Host "javac 编译中（--release 8，会打印弃用警告，正常）..." -ForegroundColor Cyan
& $javac --release 8 -d $out (Join-Path $here "UhidProbe.java")
if ($LASTEXITCODE -ne 0) { throw "javac 失败" }

$jar = Join-Path $here "uhidprobe.jar"
Write-Host "d8 转 dex..." -ForegroundColor Cyan
& $java -cp $r8 com.android.tools.r8.D8 `
    --release --min-api 28 --output $out (Join-Path $out "UhidProbe.class")
if ($LASTEXITCODE -ne 0) { throw "d8 失败" }

$dex = Join-Path $out "classes.dex"
if (-not (Test-Path $dex)) { throw "未生成 classes.dex" }

# 打成含 classes.dex 的 jar（app_process 可直接吃）
$zip = Join-Path $out "payload.zip"
Compress-Archive -Path $dex -DestinationPath $zip -Force
if (Test-Path $jar) { Remove-Item -Force $jar }
Move-Item $zip $jar
Write-Host "生成: $jar" -ForegroundColor Green

Write-Host "推送到设备..." -ForegroundColor Cyan
& adb push $jar /data/local/tmp/uhidprobe.jar
if ($LASTEXITCODE -ne 0) { throw "adb push 失败" }
Write-Host "完成。运行: powershell -File `"$here\run.ps1`"" -ForegroundColor Green
```

- [ ] **Step 4: 编写运行脚本**

创建 `probes/03-uhid/run.ps1`：

```powershell
param([string]$Mode = "create")
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "启动 UHID 探针 (mode=$Mode)..." -ForegroundColor Cyan
Write-Host "预期: 手机上出现一个鼠标光标，并且它会自己缓慢向右下移动。Ctrl+C 结束。" -ForegroundColor Yellow
Write-Host ""
& adb shell "CLASSPATH=/data/local/tmp/uhidprobe.jar app_process / UhidProbe $Mode"
```

- [ ] **Step 5: 构建并推送**

Run: `powershell -ExecutionPolicy Bypass -File probes/03-uhid/build.ps1`

Expected: 依次输出 javac（含 `--release 8` 弃用警告，正常）、d8、`生成: ...uhidprobe.jar`、`adb push` 成功。**无编译错误。**

- [ ] **Step 6: 运行并观察**

Run: `powershell -ExecutionPolicy Bypass -File probes/03-uhid/run.ps1 -Mode create`

Expected 输出：
```
UHIDPROBE start mode=create
UHIDPROBE open /dev/uhid OK
UHIDPROBE CREATE2 wrote 4376 bytes
```

同时**看着手机屏幕**：应出现一个鼠标光标，并每 200ms 向右下挪一点（共 200 次，约 40 秒）。

- [ ] **Step 7: 判定结果并记录**

| 现象 | 判定 |
|---|---|
| `open /dev/uhid OK` | ✅ 权限与 SELinux 都没拦 |
| `CREATE2 wrote 4376 bytes` | ✅ 内核接受了事件长度 |
| **手机上出现光标** | ✅ **HID 描述符正确，Android 认了这个设备** |
| **光标自行移动** | ✅ 报告写入生效 |
| 报 `IOException: ... EINVAL` | ❌ 事件长度不对 → 试 `UHID_EVENT_SIZE` 改成 4102 / 4380 再试 |
| 报 `Permission denied` | ❌ SELinux 拦了 → 用 `adb shell dmesg` 过滤 `avc` 看拒绝记录 |
| 无光标但无报错 | ❌ 描述符有问题 → 用 `adb shell dumpsys input | grep -A20 Probe` 看设备是否被识别 |

写入 `FINDINGS.md`：

```markdown
## 验证 4：设备侧 UHID 自建（零安装）

**结论**：通过 / 失败
- open /dev/uhid：成功 / 失败（错误码）
- CREATE2 写入字节数（实际被接受的）：4376 / 其他
- 手机是否出现光标：是 / 否
- 光标是否按预期移动：是 / 否
- app_process 启动方式是否稳定：是 / 否
- 实测备注（含 dumpsys input 里的设备名）：
```

- [ ] **Step 8: 清理并提交**

```bash
cd /g/pc-kvm
adb shell rm -f /data/local/tmp/uhidprobe.jar
git add probes/03-uhid/ tools/ .gitignore
git commit -m "probe: 设备侧 UHID 自建探针（验证 4）"
```

`.gitignore` 内容（`tools/r8.jar` 太大不入库，靠 build 脚本按需下载）：
```
tools/r8.jar
probes/**/classes/
probes/**/*.exe
probes/**/*.log
```

---

## Task 4: 汇总结论并决定是否进入阶段二

**Files:**
- Create: `FINDINGS.md`

**Interfaces:**
- Consumes: Task 1、2、3 的实测结论
- Produces: 阶段二的准入门判定

- [ ] **Step 1: 汇总 FINDINGS.md**

把三个任务各自的结论合并成 `FINDINGS.md`，顶部加一个决策表：

```markdown
# 前置验证结论汇总

| 验证 | 问题 | 结论 | 对阶段二的影响 |
|---|---|---|---|
| 2 | Raw Input 数据完备性 | ✅/❌ | ❌ 则采集方案作废 |
| 1 | 焦点小窗键盘抑制 | ✅/❌ | ❌ 则必须改用 WH_KEYBOARD_LL（青骓风险）或内核驱动 |
| 4 | 设备侧 UHID 自建 | ✅/❌ | ❌ 则退回装 APK，放弃"手机零安装" |

## 准入判定
- 三项全过 → 进入阶段二（端到端行走骨架）
- 验证 1 失败 → 重开抑制机制决策，spec 第 8 节需改写
- 验证 4 失败 → spec 第 6 节架构图需改写（引入 APK）
```

- [ ] **Step 2: 提交**

```bash
cd /g/pc-kvm
git add FINDINGS.md
git commit -m "docs: 前置验证结论汇总与阶段二准入判定"
```

---

## 自审记录

**Spec 覆盖检查**（对照 `2026-09-21-pc-android-kvm-design.md`）：

| Spec 要求 | 覆盖任务 |
|---|---|
| 第 10 节 验证 1（焦点小窗抑制） | Task 2 |
| 第 10 节 验证 2（Raw Input） | Task 1 |
| 第 10 节 验证 4（设备侧零安装 UHID） | Task 3 |
| 第 10 节 验证 3（adb 通路） | 已完成（spec 第 5 节记录） |
| 第 10 节 验证 5（推角落归零） | ⚠️ **不在本计划**，属阶段二（需先有传输层） |
| 第 10 节 验证 6（端到端闭环） | ⚠️ **不在本计划**，属阶段二 |
| 第 7 节 组合 HID 描述符（键盘+鼠标） | ⚠️ 探针只做鼠标，键盘描述符属阶段二 |
| 第 8 节 抑制机制 | Task 2 只验可行性，产品化属阶段二 |
| 第 9 节 三个边界情况 | ⚠️ **不在本计划**，属阶段二 |

**说明**：验证 5、6 及所有产品化工作属于阶段二。本计划的范围是**三个 fail-fast 闸门**——它们的结果可能改写 spec 第 6、8 节，因此阶段二的计划必须在拿到这三个结论之后再写。

**占位符扫描**：已通过。无 TBD / TODO / "类似上文" / 空泛的"适当处理错误"。

**类型一致性检查**：
- `UHID_EVENT_SIZE`(4376) 在 Task 3 与 spec 第 6 节说明一致
- 偏移常量 `OFF_*` 与 Step 1 说明中的偏移表逐项一致
- `RAWMOUSE` 的 `_pad` 字段在 Task 1 的说明、代码、判定标准（size=24）三处一致
- 后续任务未引用前序任务的函数签名（三个探针彼此独立），无跨任务签名漂移风险
