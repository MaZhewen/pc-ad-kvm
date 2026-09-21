# PC ↔ Android 键鼠共享 · 阶段二（端到端行走骨架）实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把阶段一验证过的三个探针产品化并接通，达成第一个真正的里程碑：**PC 的鼠标推到显示器边缘就跨到手机上、在手机屏上移动、再推回边缘收回**——全程无 APK、无安装、无全局键盘钩子。

**Architecture:** PC 侧一个绿色单文件 exe，用 Raw Input 采集、用 `AttachThreadInput` 夺取前台抑制本地输入、把事件编码成自定义协议经 TCP 发给手机；手机侧由 PC 用 `adb push` + `app_process` 拉起一个零安装进程，它把协议翻译成 HID 报告写进 `/dev/uhid`，系统据此绘制**真实系统光标**。传输走 `adb reverse` 隧道，完全不碰办公网。

**Tech Stack:**
- PC 侧：C# 5 / .NET Framework 4.8，Windows 自带 `csc.exe` 编译（无 SDK、无 NuGet、无下载）
- 手机侧：Java 8 语义（javac `--release 8`）→ `d8` 转 dex → `adb push` → `app_process`（无 APK）
- 设备通信：`adb` + TCP over `adb reverse`

**Spec:** `docs/superpowers/specs/2026-09-21-pc-android-kvm-design.md`
**阶段一实测约束:** `FINDINGS.md`（本计划的 Global Constraints 大量引自此处，实现者必须两者都读）

## Global Constraints

以下约束适用于**每一个**任务，不再逐条重复。**前四条是阶段一实测确立的，不是推演。**

- **抑制层必须使用 `AttachThreadInput` 绕行。** 朴素 `SetForegroundWindow` 从后台进程抢夺前台会失败（Win11 26200 实测）。顺序固定为：`AttachThreadInput(myThread, fgThread, true)` → `SetForegroundWindow(hwnd)` → `AttachThreadInput(myThread, fgThread, false)`。**detach 必须无条件执行**（放 `finally`），否则两条线程的输入队列会被永久挂在一起，产生极难排查的输入错乱。
- **UHID `CREATE2` 事件必须写满 `sizeof(struct uhid_event)` = 4376 字节**，内核一次接受（实测）。回退值 4102 / 4380 未被触发，不要用。
- **所有 `.ps1` 脚本必须存为 UTF-8 with BOM。** Windows PowerShell 5.1 会把无 BOM 的 UTF-8 中文注释按 GBK 解读并**静默破坏解析**（表现为变量变 null，且不报明确错误）。这是实测踩过的坑。
- **进入 `TAKEOVER` 前必须推角落归零。** UHID 虚拟设备销毁/重建后光标位置**持续存在**（实测），不归零会导致虚拟光标模型与系统真光标永久错位，且症状隐蔽（表现为"鼠标飘了"）。
- **禁止任何全局键盘钩子**（`SetWindowsHookEx(WH_KEYBOARD_LL)`）。本机运行网易青骓，键盘钩子特征与键盘记录器一致。
- **手机侧零安装**：无 APK、无图标、无权限弹窗、无 Shizuku、无残留。进程退出后 `/data/local/tmp` 不留文件。
- **PC 侧产物必须是绿色免安装单文件 exe**：不写注册表、不装服务、不开机自启。
- **不传画面、不镜像、PC 上不开镜像窗口。** 用户看的是手机自己那块屏。
- **C# 语言版本上限 = C# 5。** 编译器是 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`（自报 "for C# 5"）。**禁用**：字符串插值 `$""`、null 条件运算符 `?.`、表达式体成员 `=>`、`nameof`、自动属性初始化器、`using static`。**允许**：`var`、lambda、LINQ、`async/await`、泛型。
- **csc 开关一律用 `-` 前缀，不用 `/` 前缀。** 在 Git Bash 下 `/nologo` 会被 MSYS 路径转换吃成 `C:/.../Git/nologo`，产生误导性的 `error CS2001: 未能找到源文件`。脚本里传路径也要用 Windows 反斜杠形式。
- **设备侧 Java 只用 JDK 标准库**（`java.io` / `java.net` / `java.nio`），**不引用任何 `android.*` 类**——这是 javac 无需 `android.jar` 的前提。
- **固定端口**：TCP `27183`（与 scrcpy 默认一致，避免与常用端口冲突）。
- **开工前必查**：确认 `GameViewer.exe` 未在运行。它以约 5.7 次/秒注入 `VK_VOLUME_DOWN`，会污染任何输入相关测试且症状隐蔽（详见 `FINDINGS.md` 关联记录）。

### 目标环境（实测值，代码里的常量以此为准）

| 项 | 值 |
|---|---|
| 显示器 | `DISPLAY2`(主, 0,0, 1920×1080) + `DISPLAY1`(1920,0, 1920×1080) |
| 虚拟桌面 | 0,0 → 3840×1080 |
| 手机 | 小米 `25053RP5CC` / HyperOS 3.0 / Android 16 (SDK 36)，未 root，SELinux Enforcing |
| 手机屏幕 | **2136 × 3200**（竖屏），密度 400 |
| javac | `C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin\javac.exe` |
| d8 | `tools\r8.jar`（已存在，约 17 MB，被 `.gitignore` 排除） |
| adb | `D:\software\platform-tools\adb.exe` v37.0.1 |

---

## 文件结构

```
G:\pc-kvm\
├── src\
│   ├── agent\                    PC 侧 C#（编译成单个 green exe）
│   │   ├── Program.cs            入口、托盘图标、装配各部件
│   │   ├── RawInput.cs           Raw Input 采集（只负责产出事件）
│   │   ├── EdgeTracker.cs        状态机 + 边缘判定 + 虚拟光标与坐标映射
│   │   ├── Suppressor.cs         夺前台（AttachThreadInput）+ ClipCursor
│   │   ├── Protocol.cs           协议编解码（纯函数，无 I/O）
│   │   ├── Transport.cs          TCP 服务端 + 连接生命周期 + 心跳
│   │   └── DeviceLauncher.cs     adb reverse + push + app_process 拉起与清理
│   └── injector\                 手机侧 Java（编译成 dex → jar）
│       ├── Injector.java         main：socket 循环 + 指令分发
│       ├── HidDescriptor.java    组合 HID 报告描述符（鼠标 + 键盘）
│       └── UhidDevice.java       /dev/uhid 打开、CREATE2、写 INPUT2
├── build\
│   ├── build-agent.ps1           编译 PC 侧，产出单文件 exe
│   └── build-injector.ps1        javac + d8 + 打包 + push
└── docs\superpowers\...
```

**为什么 PC 侧要拆成 7 个文件而不是一个**：`Suppressor` 持有 `ClipCursor` 这个能把用户鼠标锁死的系统级副作用，`Transport` 持有 socket 生命周期，`Protocol` 是纯函数。把这几样混在一个文件里，改协议时会碰到锁光标和网络代码，改抑制时会碰到协议——出错代价不对称。分开后每个文件的修改风险是可独立评估的。

**手机侧为什么只有 3 个文件**：它是要被 push 到 `/data/local/tmp` 的一次性产物，越薄越好，且必须在设备上零依赖运行。

---

### Task 1: PC 侧骨架 —— 构建脚本、托盘、Raw Input 采集

**产出**：一个能编译运行的单文件 `pc-kvm.exe`，托盘有图标，移动鼠标时控制台/日志打出真实增量。**这是产品化的采集层**，取代阶段一的 `probes/01-rawinput`。

**Files:**
- Create: `src/agent/RawInput.cs`
- Create: `src/agent/Program.cs`
- Create: `build/build-agent.ps1`

**Interfaces:**
- Consumes: 无（首个任务）
- Produces:
  - `RawInput` 类：构造 `RawInput(IntPtr hwnd)`；事件 `public event Action<RawMouseEvent> MouseMoved`；`public event Action<RawKeyEvent> KeyChanged`；`public void Register()`；字段 `public static bool LastRegisterOk` 与 `public static int RegisterError`
  - `struct RawMouseEvent { public int Dx, Dy; public ushort ButtonFlags; public short WheelDelta; }`
  - `struct RawKeyEvent { public int Scancode; public bool IsUp; public bool IsE0; }`
  - `build/build-agent.ps1`：从 `src/agent/*.cs` 编译出 `dist/pc-kvm.exe`

- [ ] **Step 1: 创建构建脚本**

创建 `build/build-agent.ps1`。**必须存为 UTF-8 with BOM**（见 Global Constraints）。

```powershell
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$src  = Join-Path $root 'src\agent'
$dist = Join-Path $root 'dist'
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) { throw "找不到 csc.exe: $csc" }
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Force -Path $dist | Out-Null }

$sources = Get-ChildItem -Path $src -Filter '*.cs' | ForEach-Object { $_.FullName }
if ($sources.Count -eq 0) { throw "src\agent 下没有 .cs 文件" }

Write-Host "编译 $($sources.Count) 个源文件..." -ForegroundColor Cyan
& $csc -nologo -target:winexe -platform:x64 -optimize+ `
       -out:"$dist\pc-kvm.exe" `
       -r:System.Windows.Forms.dll `
       -r:System.Drawing.dll `
       $sources

if ($LASTEXITCODE -ne 0) { throw "编译失败" }
$exe = Get-Item "$dist\pc-kvm.exe"
Write-Host ("编译成功: {0}  ({1:N1} KB)" -f $exe.FullName, ($exe.Length / 1KB)) -ForegroundColor Green
```

- [ ] **Step 2: 创建 Raw Input 采集**

创建 `src/agent/RawInput.cs`。**关键：`RAWMOUSE` 里的 `_pad` 字段不能省**——C 定义中 `ulButtons` 是与匿名结构体的联合，`usFlags` 之后有 2 字节隐式填充；C# 默认布局会把 `usButtonFlags` 放到偏移 2 而非 4，导致后续所有字段错位。

```csharp
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PcKvm
{
    public struct RawMouseEvent
    {
        public int Dx;
        public int Dy;
        public ushort ButtonFlags;
        public short WheelDelta;
    }

    public struct RawKeyEvent
    {
        public int Scancode;   // MakeCode | (E0 ? 0xE000 : 0)
        public bool IsUp;
        public bool IsE0;
    }

    /// <summary>Raw Input 采集。只负责把系统事件翻译成结构体，不做任何判定。</summary>
    public class RawInput : NativeWindow
    {
        const int WM_INPUT = 0x00FF;
        const uint RIDEV_INPUTSINK = 0x00000100;
        const uint RID_INPUT = 0x10000003;
        const uint RIM_TYPEMOUSE = 0;
        const uint RIM_TYPEKEYBOARD = 1;

        public const ushort RI_MOUSE_WHEEL = 0x0400;

        public static bool LastRegisterOk = false;
        public static int RegisterError = 0;

        public event Action<RawMouseEvent> MouseMoved;
        public event Action<RawKeyEvent> KeyChanged;

        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        // _pad 必须保留，见方法注释
        [StructLayout(LayoutKind.Sequential)]
        struct RAWMOUSE
        {
            public ushort usFlags;
            public ushort _pad;
            public ushort usButtonFlags;
            public ushort usButtonData;
            public uint ulRawButtons;
            public int lLastX;
            public int lLastY;
            public uint ulExtraInformation;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RAWKEYBOARD
        {
            public ushort MakeCode;
            public ushort Flags;
            public ushort Reserved;
            public ushort VKey;
            public uint Message;
            public uint ExtraInformation;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] p, uint n, uint cb);

        [DllImport("user32.dll")]
        static extern uint GetRawInputData(IntPtr h, uint cmd, IntPtr data, ref uint size, uint hdrSize);

        public RawInput(IntPtr hwnd)
        {
            AssignHandle(hwnd);
        }

        /// <summary>注册鼠标与键盘，RIDEV_INPUTSINK 使窗口非焦点时也能收到事件。</summary>
        public void Register()
        {
            RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[2];
            rid[0].usUsagePage = 0x01; rid[0].usUsage = 0x02;   // Generic Desktop / Mouse
            rid[0].dwFlags = RIDEV_INPUTSINK; rid[0].hwndTarget = Handle;
            rid[1].usUsagePage = 0x01; rid[1].usUsage = 0x06;   // Generic Desktop / Keyboard
            rid[1].dwFlags = RIDEV_INPUTSINK; rid[1].hwndTarget = Handle;

            LastRegisterOk = RegisterRawInputDevices(rid, 2, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            RegisterError = LastRegisterOk ? 0 : Marshal.GetLastWin32Error();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT)
            {
                try { Dispatch(m.LParam); }
                catch { /* 单个坏事件不能打断消息泵 */ }
            }
            base.WndProc(ref m);
        }

        void Dispatch(IntPtr hRaw)
        {
            uint size = 0;
            uint hdrSize = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
            GetRawInputData(hRaw, RID_INPUT, IntPtr.Zero, ref size, hdrSize);
            if (size == 0) return;

            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                uint got = GetRawInputData(hRaw, RID_INPUT, buf, ref size, hdrSize);
                if (got != size) return;

                RAWINPUTHEADER hdr = (RAWINPUTHEADER)Marshal.PtrToStructure(buf, typeof(RAWINPUTHEADER));
                IntPtr payload = new IntPtr(buf.ToInt64() + hdrSize);

                if (hdr.dwType == RIM_TYPEMOUSE)
                {
                    RAWMOUSE ms = (RAWMOUSE)Marshal.PtrToStructure(payload, typeof(RAWMOUSE));
                    RawMouseEvent e = new RawMouseEvent();
                    e.Dx = ms.lLastX;
                    e.Dy = ms.lLastY;
                    e.ButtonFlags = ms.usButtonFlags;
                    e.WheelDelta = (ms.usButtonFlags & RI_MOUSE_WHEEL) != 0 ? (short)ms.usButtonData : (short)0;
                    Action<RawMouseEvent> h = MouseMoved;
                    if (h != null) h(e);   // C# 5 没有 ?. ，用局部变量 + null 判断
                }
                else if (hdr.dwType == RIM_TYPEKEYBOARD)
                {
                    RAWKEYBOARD kb = (RAWKEYBOARD)Marshal.PtrToStructure(payload, typeof(RAWKEYBOARD));
                    RawKeyEvent e = new RawKeyEvent();
                    e.IsUp = (kb.Flags & 0x01) != 0;
                    e.IsE0 = (kb.Flags & 0x02) != 0;
                    e.Scancode = kb.MakeCode | (e.IsE0 ? 0xE000 : 0);
                    Action<RawKeyEvent> h = KeyChanged;
                    if (h != null) h(e);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }
    }
}
```

- [ ] **Step 3: 创建入口与托盘**

创建 `src/agent/Program.cs`。

```csharp
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PcKvm
{
    /// <summary>隐藏主窗体：只作为 Raw Input 的消息宿主与托盘载体，不显示任何 UI。</summary>
    class MessageHost : Form
    {
        public MessageHost()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            Opacity = 0;
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);   // 永不显示
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();

            MessageHost host = new MessageHost();
            IntPtr hwnd = host.Handle;   // 触发句柄创建

            RawInput ri = new RawInput(hwnd);
            ri.Register();
            if (!RawInput.LastRegisterOk)
            {
                MessageBox.Show("RegisterRawInputDevices 失败, err=" + RawInput.RegisterError
                    + "\n程序将退出。", "PC-KVM", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            StreamWriter log = new StreamWriter(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pc-kvm.log"),
                true, System.Text.Encoding.UTF8);
            log.AutoFlush = true;
            log.WriteLine("# 启动 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            // 阶段骨架：只把事件落到日志，后续任务接管这两个事件
            int mc = 0, kc = 0;
            ri.MouseMoved += delegate(RawMouseEvent e)
            {
                mc++;
                if (mc % 50 == 0)   // 降频，避免日志爆炸
                    log.WriteLine("MOUSE dx=" + e.Dx + " dy=" + e.Dy
                        + " btn=0x" + e.ButtonFlags.ToString("X4") + " wheel=" + e.WheelDelta);
            };
            ri.KeyChanged += delegate(RawKeyEvent e)
            {
                kc++;
                log.WriteLine("KEY scancode=0x" + e.Scancode.ToString("X")
                    + (e.IsUp ? " UP" : " DOWN"));
            };

            NotifyIcon tray = new NotifyIcon();
            tray.Icon = SystemIcons.Application;
            tray.Text = "PC-KVM（阶段二骨架）";
            tray.Visible = true;
            MenuItem quit = new MenuItem("退出");
            quit.Click += delegate { Application.Exit(); };
            tray.ContextMenu = new ContextMenu(new MenuItem[] { quit });

            Application.ApplicationExit += delegate
            {
                log.WriteLine("# 退出");
                tray.Visible = false;
                log.Close();
            };

            Application.Run(host);
        }
    }
}
```

- [ ] **Step 4: 编译**

Run: `powershell -ExecutionPolicy Bypass -File build/build-agent.ps1`

Expected: 输出 `编译成功: G:\pc-kvm\dist\pc-kvm.exe  (约 20 KB)`，`$LASTEXITCODE` 为 0，无警告。**若报 `error CS2001: 未能找到源文件`，多半是路径被 MSYS 转换了——确认脚本里用的是 `$csc ... $sources`（Windows 路径数组），不是在 bash 里手敲相对路径。**

- [ ] **Step 5: 运行验证**

Run: `dist\pc-kvm.exe`

操作与判定：
1. 托盘出现图标，标题 `PC-KVM（阶段二骨架）` ✅
2. 移动鼠标 —— `dist\pc-kvm.log` 里出现 `MOUSE dx=.. dy=..` 行 ✅
3. **把焦点切到记事本再移动鼠标** —— 日志仍在增长（这是 `RIDEV_INPUTSINK` 的作用，阶段一已验证） ✅
4. 敲键盘 —— 出现 `KEY scancode=0x..  DOWN/UP` 行 ✅
5. 托盘右键 → 退出，进程消失，`pc-kvm.log` 末尾写入 `# 退出` ✅

- [ ] **Step 6: 提交**

```bash
cd /g/pc-kvm
git add build/build-agent.ps1 src/agent/RawInput.cs src/agent/Program.cs .gitignore
git commit -m "feat(agent): PC 侧骨架 —— 构建脚本、托盘、Raw Input 采集"
```

同时在 `.gitignore` 追加 `dist/` 与 `*.log`（若尚无）。**不要重写整个 .gitignore**，只追加缺失的行。

---

### Task 2: 设备侧注入器 —— UHID 设备与组合 HID 报告描述符

**产出**：一个 dex jar，push 到手机后用 `app_process` 跑起来，**手机上出现真实鼠标光标**。此阶段它从标准输入读简单文本指令，便于手工测试。

**Files:**
- Create: `src/injector/HidDescriptor.java`
- Create: `src/injector/UhidDevice.java`
- Create: `src/injector/Injector.java`
- Create: `build/build-injector.ps1`

**Interfaces:**
- Consumes: 无
- Produces:
  - `HidDescriptor.MOUSE_KEYBOARD`：`static final int[]`，组合 HID 报告描述符
  - `UhidDevice`：构造 `UhidDevice(String name, int[] descriptor)`，抛 `IOException`；`public void sendMouse(byte buttons, byte dx, byte dy, byte wheel)`；`public void sendKeyboard(byte modifiers, byte[] keys6)`；`public void close()`
  - `Injector.main(String[])`：从 stdin 逐行读指令，格式 `M dx dy buttons wheel` / `K mods k1 k2 k3 k4 k5 k6` / `Q`
  - `build/build-injector.ps1`：javac → d8 → 打包 → `adb push` 到 `/data/local/tmp/pckvm.jar`

**背景（实现者必读）**：

1. **UHID 事件必须写满 4376 字节。** 不写满内核会拒（阶段一实测确认 4376 一次通过）。
2. **偏移表（`__attribute__((packed))`，必须手写字节偏移，不要用 `ByteBuffer` 默认对齐）**：
   - `uhid_event`: `type`(u32 @0) + union(@4)
   - `UHID_CREATE2 = 11`，`UHID_INPUT2 = 12`
   - `create2`: `name[128]`@4, `phys[64]`@132, `uniq[64]`@196, `rd_size`(u16)@260, `bus`(u16)@262, `vendor`(u32)@264, `product`(u32)@268, `version`(u32)@272, `country`(u32)@276, `rd_data[4096]`@280
   - `input2`: `size`(u16)@4, `data[4096]`@6
   - 全部小端。
3. **`report id` 必须在报告的第一个字节。** 描述符里声明了两个 Report ID（鼠标 = 1，键盘 = 2），因此 `input2` 的 data 首字节是 report id，`size` 字段要含它（鼠标 5 字节，键盘 9 字节）。

- [ ] **Step 1: 编写 HID 报告描述符**

创建 `src/injector/HidDescriptor.java`：

```java
/** 组合 HID 报告描述符：Report ID 1 = 相对鼠标，Report ID 2 = 6 键位键盘。 */
public class HidDescriptor {

    // 鼠标报告负载（不含 report id）：[buttons(1B)][dx(1B)][dy(1B)][wheel(1B)]
    public static final int MOUSE_REPORT_ID = 1;
    public static final int MOUSE_REPORT_SIZE = 5;   // report id + 4

    // 键盘报告负载（不含 report id）：[modifiers(1B)][reserved(1B)][key1..key6(6B)]
    public static final int KEYBOARD_REPORT_ID = 2;
    public static final int KEYBOARD_REPORT_SIZE = 9; // report id + 8

    public static final int[] MOUSE_KEYBOARD = {
        // ---- 鼠标 ----
        0x05, 0x01,        // Usage Page (Generic Desktop)
        0x09, 0x02,        // Usage (Mouse)
        0xA1, 0x01,        // Collection (Application)
        0x85, 0x01,        //   Report ID (1)
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
        0xC0,              // End Collection

        // ---- 键盘 ----
        0x05, 0x01,        // Usage Page (Generic Desktop)
        0x09, 0x06,        // Usage (Keyboard)
        0xA1, 0x01,        // Collection (Application)
        0x85, 0x02,        //   Report ID (2)
        0x05, 0x07,        //   Usage Page (Keyboard/Keypad)
        0x19, 0xE0,        //   Usage Minimum (224 = LeftCtrl)
        0x29, 0xE7,        //   Usage Maximum (231 = RightGUI)
        0x15, 0x00,        //   Logical Minimum (0)
        0x25, 0x01,        //   Logical Maximum (1)
        0x95, 0x08,        //   Report Count (8)
        0x75, 0x01,        //   Report Size (1)
        0x81, 0x02,        //   Input (Data,Var,Abs)   -- 8 个修饰键位
        0x95, 0x01,        //   Report Count (1)
        0x75, 0x08,        //   Report Size (8)
        0x81, 0x01,        //   Input (Const)          -- 保留字节
        0x95, 0x06,        //   Report Count (6)
        0x75, 0x08,        //   Report Size (8)
        0x15, 0x00,        //   Logical Minimum (0)
        0x25, 0x65,        //   Logical Maximum (101)
        0x05, 0x07,        //   Usage Page (Keyboard/Keypad)
        0x19, 0x00,        //   Usage Minimum (0)
        0x29, 0x65,        //   Usage Maximum (101)
        0x81, 0x00,        //   Input (Data,Ary,Abs)   -- 6 个按键槽位
        0xC0               // End Collection
    };
}
```

- [ ] **Step 2: 编写 UHID 设备封装**

创建 `src/injector/UhidDevice.java`：

```java
import java.io.IOException;
import java.io.RandomAccessFile;

/** 封装 /dev/uhid：创建一个虚拟 HID 设备并持续写入输入报告。 */
public class UhidDevice {

    static final int UHID_CREATE2 = 11;
    static final int UHID_INPUT2  = 12;
    static final int UHID_EVENT_SIZE = 4376;   // sizeof(struct uhid_event)，内核一次接受（实测）

    static final int OFF_NAME    = 4;
    static final int OFF_PHYS    = 132;
    static final int OFF_UNIQ    = 196;
    static final int OFF_RD_SIZE = 260;
    static final int OFF_BUS     = 262;
    static final int OFF_VENDOR  = 264;
    static final int OFF_PRODUCT = 268;
    static final int OFF_VERSION = 272;
    static final int OFF_COUNTRY = 276;
    static final int OFF_RD_DATA = 280;

    static final int OFF_INPUT_SIZE = 4;
    static final int OFF_INPUT_DATA = 6;

    static final int BUS_USB = 0x03;

    private final RandomAccessFile dev;

    public UhidDevice(String name, int[] descriptor) throws IOException {
        dev = new RandomAccessFile("/dev/uhid", "rw");
        byte[] ev = new byte[UHID_EVENT_SIZE];
        putU32(ev, 0, UHID_CREATE2);
        putStr(ev, OFF_NAME, name, 128);
        putStr(ev, OFF_PHYS, "pc-kvm", 64);
        putStr(ev, OFF_UNIQ, "", 64);
        putU16(ev, OFF_RD_SIZE, descriptor.length);   // 真实描述符长度，不是缓冲容量
        putU16(ev, OFF_BUS, BUS_USB);
        putU32(ev, OFF_VENDOR, 0x1234);
        putU32(ev, OFF_PRODUCT, 0x5679);
        putU32(ev, OFF_VERSION, 1);
        putU32(ev, OFF_COUNTRY, 0);
        for (int i = 0; i < descriptor.length; i++) {
            ev[OFF_RD_DATA + i] = (byte) descriptor[i];
        }
        dev.write(ev);
    }

    /** 鼠标报告：[reportId][buttons][dx][dy][wheel] */
    public void sendMouse(byte buttons, byte dx, byte dy, byte wheel) throws IOException {
        byte[] ev = new byte[UHID_EVENT_SIZE];
        putU32(ev, 0, UHID_INPUT2);
        putU16(ev, OFF_INPUT_SIZE, HidDescriptor.MOUSE_REPORT_SIZE);
        ev[OFF_INPUT_DATA + 0] = (byte) HidDescriptor.MOUSE_REPORT_ID;
        ev[OFF_INPUT_DATA + 1] = buttons;
        ev[OFF_INPUT_DATA + 2] = dx;
        ev[OFF_INPUT_DATA + 3] = dy;
        ev[OFF_INPUT_DATA + 4] = wheel;
        dev.write(ev);
    }

    /** 键盘报告：[reportId][modifiers][reserved][6 个 key slot] */
    public void sendKeyboard(byte modifiers, byte[] keys6) throws IOException {
        byte[] ev = new byte[UHID_EVENT_SIZE];
        putU32(ev, 0, UHID_INPUT2);
        putU16(ev, OFF_INPUT_SIZE, HidDescriptor.KEYBOARD_REPORT_SIZE);
        ev[OFF_INPUT_DATA + 0] = (byte) HidDescriptor.KEYBOARD_REPORT_ID;
        ev[OFF_INPUT_DATA + 1] = modifiers;
        ev[OFF_INPUT_DATA + 2] = 0;   // reserved，必须为 0
        for (int i = 0; i < 6; i++) {
            ev[OFF_INPUT_DATA + 3 + i] = i < keys6.length ? keys6[i] : 0;
        }
        dev.write(ev);
    }

    public void close() {
        try { dev.close(); } catch (IOException e) { /* 关 fd 时内核自动销毁设备 */ }
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
        int len = Math.min(raw.length, cap - 1);   // 保留结尾 NUL
        System.arraycopy(raw, 0, b, off, len);
    }
}
```

- [ ] **Step 3: 编写命令行入口**

创建 `src/injector/Injector.java`。此阶段从 **stdin 逐行读文本指令**，便于 `adb shell` 手工喂：

```java
import java.io.BufferedReader;
import java.io.InputStreamReader;

/**
 * 阶段二 Task 2 的命令行版本：从 stdin 读文本指令驱动虚拟 HID 设备。
 *   M <dx> <dy> <buttons> <wheel>   发送鼠标报告（dx/dy 会被夹到 -127..127）
 *   K <mods> <k1> <k2> <k3> <k4> <k5> <k6>   发送键盘报告
 *   Q                                退出
 * 后续任务会把 stdin 换成 socket，本类保留为手工调试入口。
 */
public class Injector {

    public static void main(String[] args) throws Exception {
        System.out.println("INJECTOR start");
        UhidDevice dev;
        try {
            dev = new UhidDevice("PC-KVM Virtual Input", HidDescriptor.MOUSE_KEYBOARD);
        } catch (Exception e) {
            System.out.println("INJECTOR FAIL open /dev/uhid: " + e);
            return;
        }
        System.out.println("INJECTOR ready");

        BufferedReader in = new BufferedReader(new InputStreamReader(System.in));
        String line;
        while ((line = in.readLine()) != null) {
            line = line.trim();
            if (line.length() == 0) continue;
            if (line.equals("Q")) break;

            String[] t = line.split("\\s+");
            try {
                if (t[0].equals("M") && t.length >= 5) {
                    dev.sendMouse((byte) clampToByte(parse(t[3])),
                                  (byte) clamp127(parse(t[1])),
                                  (byte) clamp127(parse(t[2])),
                                  (byte) clamp127(parse(t[4])));
                } else if (t[0].equals("K") && t.length >= 8) {
                    byte[] keys = new byte[6];
                    for (int i = 0; i < 6; i++) keys[i] = (byte) parse(t[i + 2]);
                    dev.sendKeyboard((byte) parse(t[1]), keys);
                } else {
                    System.out.println("INJECTOR bad line: " + line);
                }
            } catch (Exception ex) {
                System.out.println("INJECTOR err on [" + line + "]: " + ex);
            }
        }

        dev.close();
        System.out.println("INJECTOR exit");
    }

    /** 把任意整数夹到有符号 8 位范围，HID 报告的轴是 int8。 */
    static int clamp127(int v) {
        if (v > 127) return 127;
        if (v < -127) return -127;
        return v;
    }

    static int clampToByte(int v) {
        if (v > 127) return 127;
        if (v < -128) return -128;
        return v;
    }

    static int parse(String s) { return Integer.parseInt(s); }
}
```

- [ ] **Step 4: 编写构建脚本**

创建 `build/build-injector.ps1`。**必须存为 UTF-8 with BOM**（PowerShell 5.1 会把无 BOM 的 UTF-8 中文注释按 GBK 误读，静默破坏解析——实测踩过）。

```powershell
$ErrorActionPreference = 'Stop'
$root  = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$src   = Join-Path $root 'src\injector'
$r8    = Join-Path $root 'tools\r8.jar'
$javac = 'C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin\javac.exe'
$java  = 'C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin\java.exe'

foreach ($p in @($javac, $java, $r8)) {
    if (-not (Test-Path $p)) { throw "缺少: $p" }
}

$out = Join-Path $env:TEMP 'pckvm-injector-classes'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force -Path $out | Out-Null

$sources = Get-ChildItem -Path $src -Filter '*.java' | ForEach-Object { $_.FullName }

Write-Host "javac（--release 8，会打印弃用警告，正常）..." -ForegroundColor Cyan
& $javac --release 8 -nowarn -d $out $sources
if ($LASTEXITCODE -ne 0) { throw "javac 失败" }

Write-Host "d8 转 dex..." -ForegroundColor Cyan
$classes = Get-ChildItem -Path $out -Filter '*.class' | ForEach-Object { $_.FullName }
& $java -cp $r8 com.android.tools.r8.D8 --release --min-api 28 --output $out $classes
if ($LASTEXITCODE -ne 0) { throw "d8 失败" }

$dex = Join-Path $out 'classes.dex'
if (-not (Test-Path $dex)) { throw "未生成 classes.dex" }

$jar = Join-Path $env:TEMP 'pckvm.jar'
if (Test-Path $jar) { Remove-Item -Force $jar }
$zip = Join-Path $out 'payload.zip'
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $dex -DestinationPath $zip -Force
Move-Item $zip $jar

Write-Host "推送到设备..." -ForegroundColor Cyan
& adb push $jar /data/local/tmp/pckvm.jar
if ($LASTEXITCODE -ne 0) { throw "adb push 失败" }

Write-Host "完成: /data/local/tmp/pckvm.jar" -ForegroundColor Green
```

- [ ] **Step 5: 构建**

Run: `powershell -ExecutionPolicy Bypass -File build/build-injector.ps1`

Expected: javac（含 `--release 8` 弃用警告，正常）→ d8 → `完成: /data/local/tmp/pckvm.jar`。**若 d8 报缺库，停下报告——不要自行下载 `android.jar`**（那会把下载量从 1 个 jar 变成 3 个，属范围变更）。本任务的 Java 无 lambda、无默认方法、无 try-with-resources，不需要脱糖。

- [ ] **Step 6: 手工验证 —— 手机出现光标并能被指令驱动**

终端 A（保持运行）：

```powershell
adb shell "CLASSPATH=/data/local/tmp/pckvm.jar app_process / Injector"
```

Expected: 打印 `INJECTOR start` / `INJECTOR ready`，**手机屏幕上出现鼠标光标**。

终端 B 逐条喂指令，每条之后看手机：

```powershell
adb shell "echo 'M 20 20 0 0' >> /dev/null"   # 占位，实际用下面的交互方式
```

**更好用的方式**：在终端 A 直接敲（stdin 是通的）：

```
M 30 0 0 0      → 光标向右跳 30 像素
M 0 30 0 0      → 向下 30
M -30 -30 0 0   → 左上
M 0 0 1 0       → 左键按下（图标/按钮应显示按下态）
M 0 0 0 0       → 左键抬起
M 0 0 0 1       → 滚轮下滚一格
```

判定：光标按指令移动、按钮生效、滚轮生效 → **通过**。

- [ ] **Step 7: 清理并提交**

```bash
cd /g/pc-kvm
adb shell rm -f /data/local/tmp/pckvm.jar
adb shell "ps -A | grep app_process"   # 确认无残留（若有，kill 掉）
git add build/build-injector.ps1 src/injector/
git commit -m "feat(injector): 设备侧 UHID 注入器 —— 组合 HID 描述符 + 命令行驱动"
```

---

### Task 3: 协议与传输 —— PC 侧 TCP 服务端 + 手机侧 socket 客户端

**产出**：PC 端启动后监听 `127.0.0.1:27183` 的 TCP；`adb reverse` 打通后，手机侧进程连上来，双方能互发消息。**此任务不接采集也不接注入**，只把管道打通并用 PING/PONG 证明。

**Files:**
- Create: `src/agent/Protocol.cs`
- Create: `src/agent/Transport.cs`
- Create: `src/agent/DeviceLauncher.cs`
- Create: `src/injector/Injector.java`（修改：stdin 循环改为 socket 循环，保留 stdin 调试模式）
- Modify: `src/agent/Program.cs`

**Interfaces:**
- Consumes: Task 2 的 `Injector`
- Produces:
  - `Protocol`：`public const byte MsgEnter=0x01, MsgMove=0x02, MsgButton=0x03, MsgScroll=0x04, MsgKey=0x05, MsgLeave=0x06, MsgConfig=0x07, MsgPing=0x08, MsgPong=0x09;`
    - `public static byte[] EncodeEnter(short x, short y)`
    - `public static byte[] EncodeMove(short dx, short dy)`
    - `public static byte[] EncodeButton(byte btn, byte down)`
    - `public static byte[] EncodeScroll(short dx, short dy)`
    - `public static byte[] EncodeKey(ushort scancode, byte down, byte mods)`
    - `public static byte[] EncodeLeave()`
    - `public static byte[] EncodePing(uint seq)` / `EncodePong(uint seq)`
    - `public static bool TryDecode(byte[] buf, int len, out byte type, out byte[] payload)`
  - `Transport`：`public Transport(int port)`；`public bool Start()`；`public bool IsConnected { get; }`；`public event Action Connected` / `Disconnected`；`public void Send(byte[] frame)`；`public void Stop()`
  - `DeviceLauncher`：`public static bool Prepare(string jarLocalPath)`（`adb reverse` + `push`）；`public static Process Start()`（`app_process` 拉起）；`public static void Cleanup()`

**背景**：`adb reverse tcp:27183 tcp:27183` 让**手机的** `127.0.0.1:27183` 转发到 **PC 的** `127.0.0.1:27183`。所以 **PC 是服务端、手机是客户端**。PC 必须先开始监听，再拉起手机进程。

- [ ] **Step 1: 编写协议编解码**

创建 `src/agent/Protocol.cs`：

```csharp
using System;

namespace PcKvm
{
    /// <summary>协议编解码。纯函数，不做任何 I/O，便于单独推理与测试。</summary>
    public static class Protocol
    {
        public const byte MsgEnter  = 0x01;   // int16 x, int16 y
        public const byte MsgMove   = 0x02;   // int16 dx, int16 dy
        public const byte MsgButton = 0x03;   // uint8 btn, uint8 down
        public const byte MsgScroll = 0x04;   // int16 dx, int16 dy
        public const byte MsgKey    = 0x05;   // uint16 scancode, uint8 down, uint8 mods
        public const byte MsgLeave  = 0x06;   // 无负载
        public const byte MsgConfig = 0x07;   // uint8 edge, uint16 pcEdgeLen, uint16 phoneW, uint16 phoneH
        public const byte MsgPing   = 0x08;   // uint32 seq
        public const byte MsgPong   = 0x09;   // uint32 seq

        public static byte[] EncodeEnter(short x, short y)
        {
            byte[] b = new byte[5];
            b[0] = MsgEnter;
            PutI16(b, 1, x); PutI16(b, 3, y);
            return b;
        }

        public static byte[] EncodeMove(short dx, short dy)
        {
            byte[] b = new byte[5];
            b[0] = MsgMove;
            PutI16(b, 1, dx); PutI16(b, 3, dy);
            return b;
        }

        public static byte[] EncodeButton(byte btn, byte down)
        {
            byte[] b = new byte[3];
            b[0] = MsgButton; b[1] = btn; b[2] = down;
            return b;
        }

        public static byte[] EncodeScroll(short dx, short dy)
        {
            byte[] b = new byte[5];
            b[0] = MsgScroll;
            PutI16(b, 1, dx); PutI16(b, 3, dy);
            return b;
        }

        public static byte[] EncodeKey(ushort scancode, byte down, byte mods)
        {
            byte[] b = new byte[5];
            b[0] = MsgKey;
            PutU16(b, 1, scancode); b[3] = down; b[4] = mods;
            return b;
        }

        public static byte[] EncodeLeave()
        {
            return new byte[] { MsgLeave };
        }

        public static byte[] EncodeConfig(byte edge, ushort pcEdgeLen, ushort phoneW, ushort phoneH)
        {
            byte[] b = new byte[8];
            b[0] = MsgConfig; b[1] = edge;
            PutU16(b, 2, pcEdgeLen); PutU16(b, 4, phoneW); PutU16(b, 6, phoneH);
            return b;
        }

        public static byte[] EncodePing(uint seq) { return EncodeU32(MsgPing, seq); }
        public static byte[] EncodePong(uint seq) { return EncodeU32(MsgPong, seq); }

        static byte[] EncodeU32(byte type, uint v)
        {
            byte[] b = new byte[5];
            b[0] = type;
            PutU32(b, 1, v);
            return b;
        }

        /// <summary>所有消息都是定长的，按 type 查表得长度；返回 0 表示类型未知。</summary>
        public static int PayloadLength(byte type)
        {
            switch (type)
            {
                case MsgEnter: return 4;
                case MsgMove: return 4;
                case MsgButton: return 2;
                case MsgScroll: return 4;
                case MsgKey: return 4;
                case MsgLeave: return 0;
                case MsgConfig: return 7;
                case MsgPing: return 4;
                case MsgPong: return 4;
                default: return -1;
            }
        }

        public static short GetI16(byte[] b, int off)
        {
            return (short)(b[off] | (b[off + 1] << 8));
        }

        public static ushort GetU16(byte[] b, int off)
        {
            return (ushort)(b[off] | (b[off + 1] << 8));
        }

        public static uint GetU32(byte[] b, int off)
        {
            return (uint)(b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24));
        }

        static void PutI16(byte[] b, int off, short v) { PutU16(b, off, (ushort)v); }
        static void PutU16(byte[] b, int off, ushort v)
        {
            b[off] = (byte)(v & 0xFF);
            b[off + 1] = (byte)((v >> 8) & 0xFF);
        }
        static void PutU32(byte[] b, int off, uint v)
        {
            b[off] = (byte)(v & 0xFF);
            b[off + 1] = (byte)((v >> 8) & 0xFF);
            b[off + 2] = (byte)((v >> 16) & 0xFF);
            b[off + 3] = (byte)((v >> 24) & 0xFF);
        }
    }
}
```

- [ ] **Step 2: 编写传输层**

创建 `src/agent/Transport.cs`：

```csharp
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace PcKvm
{
    /// <summary>TCP 服务端。单一连接，断线后自动等待重连。</summary>
    public class Transport
    {
        readonly int _port;
        TcpListener _listener;
        TcpClient _client;
        NetworkStream _stream;
        Thread _acceptThread;
        volatile bool _running;
        readonly object _sendLock = new object();

        public event Action Connected;
        public event Action Disconnected;
        public event Action<byte, byte[]> MessageReceived;   // (type, payload)

        public bool IsConnected { get { return _client != null && _client.Connected; } }

        public Transport(int port) { _port = port; }

        public bool Start()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Start();
            }
            catch (Exception)
            {
                return false;
            }
            _running = true;
            _acceptThread = new Thread(AcceptLoop);
            _acceptThread.IsBackground = true;
            _acceptThread.Start();
            return true;
        }

        void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    TcpClient c = _listener.AcceptTcpClient();
                    c.NoDelay = true;          // 输入转发对延迟敏感，禁用 Nagle
                    _client = c;
                    _stream = c.GetStream();
                    Action conn = Connected;
                    if (conn != null) conn();
                    ReadLoop();
                    Action disc = Disconnected;
                    if (disc != null) disc();
                }
                catch (Exception)
                {
                    // 对端断开或监听器被 Stop，回到等待下一次连接
                }
                finally
                {
                    try { if (_stream != null) _stream.Close(); } catch (Exception) { }
                    try { if (_client != null) _client.Close(); } catch (Exception) { }
                    _stream = null;
                    _client = null;
                }
            }
        }

        void ReadLoop()
        {
            byte[] head = new byte[1];
            while (_running)
            {
                if (!ReadExact(head, 1)) return;
                int plen = Protocol.PayloadLength(head[0]);
                if (plen < 0) return;                      // 未知类型，认为流已错位
                byte[] payload = new byte[plen];
                if (plen > 0 && !ReadExact(payload, plen)) return;
                Action<byte, byte[]> h = MessageReceived;
                if (h != null) h(head[0], payload);
            }
        }

        bool ReadExact(byte[] buf, int n)
        {
            int got = 0;
            while (got < n)
            {
                int r;
                try { r = _stream.Read(buf, got, n - got); }
                catch (Exception) { return false; }
                if (r <= 0) return false;
                got += r;
            }
            return true;
        }

        /// <summary>线程安全发送。连接不存在时静默丢弃——调用方不应因对端断开而崩溃。</summary>
        public void Send(byte[] frame)
        {
            NetworkStream s = _stream;
            if (s == null) return;
            lock (_sendLock)
            {
                try { s.Write(frame, 0, frame.Length); }
                catch (Exception) { /* 对端已断开，下一次 Connected 会重建 */ }
            }
        }

        public void Stop()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch (Exception) { }
            try { if (_client != null) _client.Close(); } catch (Exception) { }
        }
    }
}
```

- [ ] **Step 3: 编写设备拉起器**

创建 `src/agent/DeviceLauncher.cs`：

```csharp
using System.Diagnostics;
using System.IO;

namespace PcKvm
{
    /// <summary>通过 adb 建立反向隧道并拉起设备侧进程。零安装：不推送 APK，只 push 一个 jar。</summary>
    public static class DeviceLauncher
    {
        public const int Port = 27183;
        const string RemoteJar = "/data/local/tmp/pckvm.jar";

        public static bool Prepare(string localJarPath)
        {
            if (!File.Exists(localJarPath)) return false;
            if (RunAdb("reverse tcp:" + Port + " tcp:" + Port) != 0) return false;
            if (RunAdb("push \"" + localJarPath + "\" " + RemoteJar) != 0) return false;
            return true;
        }

        /// <summary>拉起设备侧进程。返回的 Process 由调用方负责 Kill——它是 PC 端唯一能关掉它的手段。</summary>
        public static Process Start()
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "adb";
            psi.Arguments = "shell \"CLASSPATH=" + RemoteJar + " app_process / Injector\"";
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            Process p = Process.Start(psi);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            return p;
        }

        public static void Cleanup(Process p)
        {
            if (p != null)
            {
                try { if (!p.HasExited) p.Kill(); } catch (System.Exception) { }
            }
            RunAdb("shell rm -f " + RemoteJar);
            RunAdb("reverse --remove tcp:" + Port);
        }

        static int RunAdb(string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "adb";
            psi.Arguments = args;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            try
            {
                Process p = Process.Start(psi);
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(5000);
                return p.ExitCode;
            }
            catch (System.Exception)
            {
                return -1;
            }
        }
    }
}
```

- [ ] **Step 4: 把设备侧改成 socket 循环**

修改 `src/injector/Injector.java`：把 stdin 循环换成 **socket 客户端**，连 `127.0.0.1:27183`；**保留 stdin 调试模式**（命令行传 `--stdin` 时走原路径）。

```java
import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.Socket;

public class Injector {

    static final int PORT = 27183;
    static final byte MSG_ENTER = 0x01, MSG_MOVE = 0x02, MSG_BUTTON = 0x03,
                      MSG_SCROLL = 0x04, MSG_KEY = 0x05, MSG_LEAVE = 0x06,
                      MSG_CONFIG = 0x07, MSG_PING = 0x08, MSG_PONG = 0x09;

    /** 当前按下的鼠标按钮位图（bit0 左, bit1 右, bit2 中）。 */
    static int buttonsDown = 0;

    public static void main(String[] args) throws Exception {
        if (args.length > 0 && args[0].equals("--stdin")) {
            StdinMode.run();
            return;
        }

        System.out.println("INJECTOR start");
        UhidDevice dev;
        try {
            dev = new UhidDevice("PC-KVM Virtual Input", HidDescriptor.MOUSE_KEYBOARD);
        } catch (Exception e) {
            System.out.println("INJECTOR FAIL open /dev/uhid: " + e);
            return;
        }
        System.out.println("INJECTOR ready");

        // 连不上就重试：PC 端可能比我们先起好，也可能刚重连
        Socket sock = null;
        for (int i = 0; i < 100 && sock == null; i++) {
            try {
                sock = new Socket();
                sock.connect(new InetSocketAddress("127.0.0.1", PORT), 1000);
                sock.setTcpNoDelay(true);
            } catch (Exception e) {
                sock = null;
                Thread.sleep(100);
            }
        }
        if (sock == null) {
            System.out.println("INJECTOR FAIL cannot connect to PC");
            dev.close();
            return;
        }
        System.out.println("INJECTOR connected");

        InputStream in = sock.getInputStream();
        OutputStream out = sock.getOutputStream();

        try {
            while (true) {
                int type = in.read();
                if (type < 0) break;
                int plen = payloadLength(type);
                if (plen < 0) break;
                byte[] p = new byte[plen];
                if (plen > 0 && !readExact(in, p, plen)) break;
                handle(dev, out, (byte) type, p);
            }
        } catch (Exception e) {
            System.out.println("INJECTOR loop end: " + e);
        }

        dev.close();
        sock.close();
        System.out.println("INJECTOR exit");
    }

    static void handle(UhidDevice dev, OutputStream out, byte type, byte[] p) throws Exception {
        if (type == MSG_MOVE) {
            int dx = i16(p, 0), dy = i16(p, 1 + 1);
            // int8 轴：超过 ±127 要拆成多条报告，否则会丢位移
            while (dx != 0 || dy != 0) {
                int sx = clamp127(dx), sy = clamp127(dy);
                dev.sendMouse((byte) buttonsDown, (byte) sx, (byte) sy, (byte) 0);
                dx -= sx; dy -= sy;
            }
        } else if (type == MSG_BUTTON) {
            int btn = p[0] & 0xFF, down = p[1] & 0xFF;
            int bit = btnToBit(btn);
            if (down != 0) buttonsDown |= bit; else buttonsDown &= ~bit;
            dev.sendMouse((byte) buttonsDown, (byte) 0, (byte) 0, (byte) 0);
        } else if (type == MSG_SCROLL) {
            int dy = i16(p, 2);
            dev.sendMouse((byte) buttonsDown, (byte) 0, (byte) 0, (byte) clamp127(dy));
        } else if (type == MSG_KEY) {
            KeyState.apply(dev, u16(p, 0), p[2] != 0, p[3]);
        } else if (type == MSG_PING) {
            int seq = i32(p, 0);
            byte[] r = new byte[5];
            r[0] = MSG_PONG;
            r[1] = (byte) (seq & 0xFF);
            r[2] = (byte) ((seq >>> 8) & 0xFF);
            r[3] = (byte) ((seq >>> 16) & 0xFF);
            r[4] = (byte) ((seq >>> 24) & 0xFF);
            out.write(r);
            out.flush();
        }
        // MSG_ENTER / MSG_LEAVE / MSG_CONFIG 由后续任务接管
    }

    static int btnToBit(int btn) {
        if (btn == 1) return 1;   // 左
        if (btn == 2) return 2;   // 右
        if (btn == 3) return 4;   // 中
        return 0;
    }

    static int payloadLength(int type) {
        switch (type) {
            case MSG_ENTER:  return 4;
            case MSG_MOVE:   return 4;
            case MSG_BUTTON: return 2;
            case MSG_SCROLL: return 4;
            case MSG_KEY:    return 4;
            case MSG_LEAVE:  return 0;
            case MSG_CONFIG: return 7;
            case MSG_PING:   return 4;
            case MSG_PONG:   return 4;
            default:         return -1;
        }
    }

    static boolean readExact(InputStream in, byte[] buf, int n) throws Exception {
        int got = 0;
        while (got < n) {
            int r = in.read(buf, got, n - got);
            if (r <= 0) return false;
            got += r;
        }
        return true;
    }

    static int clamp127(int v) {
        if (v > 127) return 127;
        if (v < -127) return -127;
        return v;
    }

    static int i16(byte[] b, int off) {
        return (short) ((b[off] & 0xFF) | ((b[off + 1] & 0xFF) << 8));
    }

    static int u16(byte[] b, int off) {
        return (b[off] & 0xFF) | ((b[off + 1] & 0xFF) << 8);
    }

    static int i32(byte[] b, int off) {
        return (b[off] & 0xFF) | ((b[off + 1] & 0xFF) << 8)
             | ((b[off + 2] & 0xFF) << 16) | ((b[off + 3] & 0xFF) << 24);
    }
}
```

同时创建 `src/injector/StdinMode.java`（把 Task 2 的 stdin 循环原样搬进来，类名改 `StdinMode`，方法改 `public static void run()`），以及 `src/injector/KeyState.java`：

```java
/** 维护按下的修饰键与 6 个按键槽位，并在变化时发出键盘报告。 */
public class KeyState {

    static int modifiers = 0;
    static int[] slots = new int[6];

    /** scancode 是 Windows 形式（低字节 MakeCode，E0 置 0xE000），此处先只处理非 E0 的普通键。 */
    public static void apply(UhidDevice dev, int scancode, boolean down, int mods) throws Exception {
        modifiers = mods & 0xFF;
        int usage = ScancodeMap.toHidUsage(scancode);
        if (usage < 0) return;   // 未映射的键直接忽略，不破坏报告

        if (down) {
            if (!contains(usage)) {
                int free = freeSlot();
                if (free >= 0) slots[free] = usage;
            }
        } else {
            for (int i = 0; i < 6; i++) if (slots[i] == usage) slots[i] = 0;
        }
        byte[] keys = new byte[6];
        for (int i = 0; i < 6; i++) keys[i] = (byte) slots[i];
        dev.sendKeyboard((byte) modifiers, keys);
    }

    static boolean contains(int usage) {
        for (int i = 0; i < 6; i++) if (slots[i] == usage) return true;
        return false;
    }

    static int freeSlot() {
        for (int i = 0; i < 6; i++) if (slots[i] == 0) return i;
        return -1;
    }
}
```

以及 `src/injector/ScancodeMap.java`：Windows scancode → HID Usage ID（Keyboard/Keypad page `0x07`）映射表。**本任务只需覆盖字母、数字、常用符号与修饰键**（完整表在 Task 9 补齐）：

```java
/** Windows scancode → HID Usage ID（Keyboard/Keypad page 0x07）。 */
public class ScancodeMap {

    /** 返回 -1 表示未映射。scancode 低字节为 MakeCode。 */
    public static int toHidUsage(int scancode) {
        int mk = scancode & 0xFF;
        switch (mk) {
            case 0x01: return 0x29;   // Esc
            case 0x02: return 0x1E;   // 1
            case 0x03: return 0x1F;   // 2
            case 0x04: return 0x20;   // 3
            case 0x05: return 0x21;   // 4
            case 0x06: return 0x22;   // 5
            case 0x07: return 0x23;   // 6
            case 0x08: return 0x24;   // 7
            case 0x09: return 0x25;   // 8
            case 0x0A: return 0x26;   // 9
            case 0x0B: return 0x27;   // 0
            case 0x0E: return 0x2A;   // Backspace
            case 0x0F: return 0x2B;   // Tab
            case 0x10: return 0x14;   // Q
            case 0x11: return 0x1A;   // W
            case 0x12: return 0x08;   // E
            case 0x13: return 0x15;   // R
            case 0x14: return 0x17;   // T
            case 0x15: return 0x1C;   // Y
            case 0x16: return 0x18;   // U
            case 0x17: return 0x0C;   // I
            case 0x18: return 0x12;   // O
            case 0x19: return 0x13;   // P
            case 0x1A: return 0x2F;   // [
            case 0x1B: return 0x30;   // ]
            case 0x1C: return 0x28;   // Enter
            case 0x1E: return 0x04;   // A
            case 0x1F: return 0x16;   // S
            case 0x20: return 0x07;   // D
            case 0x21: return 0x09;   // F
            case 0x22: return 0x0A;   // G
            case 0x23: return 0x0B;   // H
            case 0x24: return 0x0D;   // J
            case 0x25: return 0x0E;   // K
            case 0x26: return 0x0F;   // L
            case 0x27: return 0x33;   // ;
            case 0x28: return 0x34;   // '
            case 0x29: return 0x35;   // `
            case 0x2B: return 0x31;   // backslash
            case 0x2C: return 0x1D;   // Z
            case 0x2D: return 0x1B;   // X
            case 0x2E: return 0x06;   // C
            case 0x2F: return 0x19;   // V
            case 0x30: return 0x05;   // B
            case 0x31: return 0x11;   // N
            case 0x32: return 0x10;   // M
            case 0x33: return 0x36;   // ,
            case 0x34: return 0x37;   // .
            case 0x35: return 0x38;   // /
            case 0x39: return 0x2C;   // Space
            default:   return -1;
        }
    }
}
```

- [ ] **Step 5: 在 Program.cs 里接上传输并验证 PING/PONG**

修改 `src/agent/Program.cs`，在托盘装配之前插入：

```csharp
            Transport transport = new Transport(DeviceLauncher.Port);
            if (!transport.Start())
            {
                MessageBox.Show("TCP 端口 " + DeviceLauncher.Port + " 监听失败，程序退出。",
                    "PC-KVM", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string jar = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pckvm.jar");
            if (!DeviceLauncher.Prepare(jar))
            {
                MessageBox.Show("adb 隧道/推送失败。确认手机已连接且 USB 调试已开。",
                    "PC-KVM", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            System.Diagnostics.Process devProc = DeviceLauncher.Start();

            transport.Connected += delegate
            {
                log.WriteLine("# 设备已连接");
                transport.Send(Protocol.EncodePing(1));
            };
            transport.Disconnected += delegate { log.WriteLine("# 设备已断开"); };
            transport.MessageReceived += delegate(byte type, byte[] payload)
            {
                if (type == Protocol.MsgPong)
                    log.WriteLine("# PONG seq=" + Protocol.GetU32(payload, 0));
            };
```

并在 `ApplicationExit` 里加上 `transport.Stop(); DeviceLauncher.Cleanup(devProc);`。

同时把 `build/build-agent.ps1` 的最后加一行，把 `%TEMP%\pckvm.jar` 复制到 `dist\pckvm.jar`（PC 端运行时从 exe 同目录找它）：

```powershell
Copy-Item "$env:TEMP\pckvm.jar" "$dist\pckvm.jar" -Force
Write-Host "已复制 pckvm.jar 到 dist\" -ForegroundColor Green
```

- [ ] **Step 6: 端到端验证**

Run（先确保手机已连接、`adb devices` 有设备）:
```powershell
powershell -ExecutionPolicy Bypass -File build/build-injector.ps1
powershell -ExecutionPolicy Bypass -File build/build-agent.ps1
dist\pc-kvm.exe
```

Expected：`dist\pc-kvm.log` 出现 `# 设备已连接` 与 `# PONG seq=1`；手机无任何图标出现（零安装）。

- [ ] **Step 7: 提交**

```bash
cd /g/pc-kvm
git add src/agent/Protocol.cs src/agent/Transport.cs src/agent/DeviceLauncher.cs \
        src/agent/Program.cs src/injector/ build/build-agent.ps1
git commit -m "feat: 协议与传输 —— PC 侧 TCP 服务端 + 手机侧 socket 客户端 + adb reverse 隧道"
```

---

### Task 4: 最小闭环 —— PC 鼠标直接驱动手机光标

**产出**：移动 PC 鼠标，**手机屏幕上的光标同步移动**。此任务**不做边缘判定、不做抑制**——PC 鼠标照常工作，同时手机光标跟着动。这是第一个可见成果。

**Files:**
- Modify: `src/agent/Program.cs`

**Interfaces:**
- Consumes: Task 1 的 `RawInput`、Task 3 的 `Transport` / `Protocol`
- Produces: 无新接口（纯接线）

- [ ] **Step 1: 接线**

在 `src/agent/Program.cs` 的 `ri.MouseMoved += ...` 处理器里，把已有日志之外追加发送：

```csharp
            ri.MouseMoved += delegate(RawMouseEvent e)
            {
                transport.Send(Protocol.EncodeMove((short)e.Dx, (short)e.Dy));

                short wheel = (short)(e.WheelDelta / 120);   // Windows 一格 = 120，HID 一格 = 1
                if (wheel != 0) transport.Send(Protocol.EncodeScroll((short)0, wheel));

                if (e.ButtonFlags != 0)
                {
                    EmitButton(transport, e.ButtonFlags, 0x0001, 1);   // 左
                    EmitButton(transport, e.ButtonFlags, 0x0004, 2);   // 右
                    EmitButton(transport, e.ButtonFlags, 0x0010, 3);   // 中
                }
            };
```

并在 `Program` 里加这个辅助方法（放在 `Main` 之后）：

```csharp
        /// <summary>把 Windows 的 down/up 位对翻译成协议的单次按钮事件。</summary>
        static void EmitButton(Transport t, ushort flags, ushort downBit, byte btn)
        {
            ushort upBit = (ushort)(downBit << 1);
            if ((flags & downBit) != 0) t.Send(Protocol.EncodeButton(btn, 1));
            else if ((flags & upBit) != 0) t.Send(Protocol.EncodeButton(btn, 0));
        }
```

- [ ] **Step 2: 验证**

Run: `dist\pc-kvm.exe`（前置：Task 3 的构建已跑过）

判定：
1. 移动鼠标 → **手机光标跟着动**，方向一致 ✅
2. 快速甩动鼠标 → 手机光标不应"少走"（验证 int8 拆分逻辑生效） ✅
3. 滚轮上下一格 → 手机内容滚动 ✅
4. 左键点击 → 手机上对应位置被点击 ✅
5. 右键/中键 → 同样生效 ✅
6. **PC 自己一切照常**（此任务无抑制） ✅

- [ ] **Step 3: 提交**

```bash
cd /g/pc-kvm
git add src/agent/Program.cs
git commit -m "feat: 最小闭环 —— 移动 PC 鼠标即驱动手机光标"
```

---

### Task 5: 推角落归零

**产出**：每次进入接管状态前，把手机光标**硬顶到 `(0,0)`**，使 PC 端的虚拟光标模型与系统真光标强制对齐。

**为什么必须做**：阶段一实测确认，UHID 虚拟设备销毁/重建后**光标位置持续存在**。不归零会导致两边永久错位，且症状隐蔽（表现为"鼠标飘了"，而不是明显故障）。

**Files:**
- Create: `src/agent/CursorModel.cs`
- Modify: `src/injector/Injector.java`
- Modify: `src/agent/Program.cs`

**Interfaces:**
- Consumes: Task 3 的 `Protocol` / `Transport`
- Produces:
  - `CursorModel`：`public CursorModel(short phoneW, short phoneH)`；`public int X { get; }` / `public int Y { get; }`；`public void Reset()`（把模型置 0,0）；`public short NextDx(int target)` / `NextDy(int target)`（返回**钳制后**应发的增量，并同步更新模型）
  - 协议新增：`Protocol.EncodeHome()` → 类型 `0x0A`，无负载。设备侧收到后把光标推回 `(0,0)`。

- [ ] **Step 1: 协议加 HOME 消息**

在 `Protocol.cs` 加常量与方法：

```csharp
        public const byte MsgHome = 0x0A;   // 无负载：让设备把光标硬顶到 (0,0)
```

```csharp
        public static byte[] EncodeHome()
        {
            return new byte[] { MsgHome };
        }
```

并在 `PayloadLength` 的 `switch` 里加 `case MsgHome: return 0;`。

- [ ] **Step 2: 设备侧实现 HOME**

在 `Injector.java` 加常量 `static final byte MSG_HOME = 0x0A;`（放进已有的常量声明行组），在 `payloadLength` 加 `case MSG_HOME: return 0;`，并在 `handle` 里加分支：

```java
        } else if (type == MSG_HOME) {
            // 8 位相对轴每报告最多走 127，按屏幕尺寸算够用的次数硬顶到左上角。
            // 3200 像素需要 ceil(3200/127)=26 次；取 40 次留余量，代价是几十毫秒。
            for (int i = 0; i < 40; i++) {
                dev.sendMouse((byte) buttonsDown, (byte) -127, (byte) -127, (byte) 0);
            }
        }
```

- [ ] **Step 3: 编写光标模型**

创建 `src/agent/CursorModel.cs`：

```csharp
namespace PcKvm
{
    /// <summary>
    /// PC 端持有的虚拟光标（坐标系 = 手机屏幕像素）。
    /// 职责：把任意目标增量钳制成"不会越界"的实际发送量，并同步更新自身位置。
    /// 之所以要钳制：设备侧的系统光标会停在屏幕边缘，若我们照发全额增量，两边就会越走越远。
    /// </summary>
    public class CursorModel
    {
        readonly int _w;
        readonly int _h;

        public int X { get; private set; }
        public int Y { get; private set; }

        public CursorModel(int phoneW, int phoneH)
        {
            _w = phoneW;
            _h = phoneH;
            X = 0;
            Y = 0;
        }

        /// <summary>归零。必须在每次进入接管、且设备侧已执行 HOME 之后调用。</summary>
        public void Reset()
        {
            X = 0;
            Y = 0;
        }

        /// <summary>直接定位（用于跨越入屏）。不发送任何东西，只改模型。</summary>
        public void SetPosition(int x, int y)
        {
            X = Clamp(x, 0, _w - 1);
            Y = Clamp(y, 0, _h - 1);
        }

        /// <summary>返回实际应发送的 dx（已钳制到不越界），并按它更新模型。</summary>
        public short NextDx(int want)
        {
            int nx = Clamp(X + want, 0, _w - 1);
            int actual = nx - X;
            X = nx;
            return (short)actual;
        }

        /// <summary>返回实际应发送的 dy（已钳制到不越界），并按它更新模型。</summary>
        public short NextDy(int want)
        {
            int ny = Clamp(Y + want, 0, _h - 1);
            int actual = ny - Y;
            Y = ny;
            return (short)actual;
        }

        static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
```

- [ ] **Step 4: 在 Program.cs 接入归零流程**

**本任务先做一个可验证的最小接法**：程序启动、设备连上后，自动执行一次 HOME 并归零模型，然后进入"鼠标事件走模型"的模式。

在 `transport.Connected` 处理器里追加：

```csharp
            cursor = new CursorModel(2136, 3200);   // 目标机手机分辨率，后续任务改成从 CONFIG 协商
            cursorResetPending = true;
```

新增两个字段在 `Main` 开头：

```csharp
            CursorModel cursor = null;
            bool cursorResetPending = false;
```

并把 `MouseMoved` 处理器里的 `EncodeMove` 那行改成：

```csharp
                if (cursorResetPending)
                {
                    transport.Send(Protocol.EncodeHome());
                    cursor.Reset();
                    cursorResetPending = false;
                }
                short sdx = cursor.NextDx(e.Dx);
                short sdy = cursor.NextDy(e.Dy);
                if (sdx != 0 || sdy != 0)
                    transport.Send(Protocol.EncodeMove(sdx, sdy));
```

- [ ] **Step 5: 验证 —— 反复归零不漂移**

Run: `dist\pc-kvm.exe`

判定（**这一条是本任务的全部意义**）：
1. 移动鼠标，手机光标动 ✅
2. **把手机光标推到屏幕任意位置，然后重启 `pc-kvm.exe`** → 手机光标应**回到左上角**并跟随鼠标 ✅
3. **反复重启 5 次**，每次都归零、每次都从左上角开始，位置不累积漂移 ✅
4. 把手机光标**推到屏幕右下角**后再重启 → 仍能归零到左上角（证明 40 次 −127 的位移量足够覆盖整屏） ✅

- [ ] **Step 6: 提交**

```bash
cd /g/pc-kvm
git add src/agent/CursorModel.cs src/agent/Protocol.cs src/agent/Program.cs src/injector/Injector.java
git commit -m "feat: 推角落归零 —— 修复 UHID 光标位置跨设备重建持续存在导致的模型错位"
```

---

### Task 6: 边缘状态机与坐标映射

**产出**：光标推到配置的外侧边缘时进入 `TAKEOVER`，虚拟光标映射到手机屏；在手机屏上把光标推回邻接边缘时退出 `TAKEOVER`。**此任务仍不做抑制**（PC 键盘鼠标照常工作），先把状态机与几何跑通。

**Files:**
- Create: `src/agent/EdgeTracker.cs`
- Modify: `src/agent/Program.cs`

**Interfaces:**
- Consumes: Task 5 的 `CursorModel`
- Produces:
  - `EdgeTracker`：`public enum State { Idle, Takeover }`
    - `public EdgeTracker(int edgeX, int edgeTop, int edgeBottom, int phoneW, int phoneH, bool phoneIsRightOfPc)`
    - `public State Current { get; }`
    - `public event Action<short, short> EnterTakeover`（入屏点，手机屏坐标）
    - `public event Action LeaveTakeover`
    - `public void OnMouseMoved(int dx, int dy, int cursorX, int cursorY)` → 返回 `bool handled`（true 表示该事件已被接管、不应再转发给设备）
    - `public void OnIdleMove(int cursorX, int cursorY)`

- [ ] **Step 1: 编写 EdgeTracker**

创建 `src/agent/EdgeTracker.cs`：

```csharp
using System;

namespace PcKvm
{
    public enum KvmState { Idle, Takeover }

    /// <summary>
    /// 边缘跨越状态机与坐标映射。
    /// 只在 IDLE 态读真实光标位置（此时未被 ClipCursor 冻结）；进入 TAKEOVER 后 PC 光标不再可用，
    /// 一切位置都由 CursorModel 承担。
    /// </summary>
    public class EdgeTracker
    {
        readonly int _edgeX;            // 触发边界的 x（屏幕像素）
        readonly int _edgeTop;
        readonly int _edgeBottom;
        readonly int _phoneW;
        readonly int _phoneH;
        readonly bool _phoneRight;      // true = 手机在 PC 右侧（此时从手机左边缘入屏）

        public KvmState Current { get; private set; }
        public bool Armed { get; private set; }   // 回程冷却：离开边缘安全带后才重新武装

        public event Action<short, short> EnterTakeover;
        public event Action LeaveTakeover;

        public EdgeTracker(int edgeX, int edgeTop, int edgeBottom,
                           int phoneW, int phoneH, bool phoneRight)
        {
            _edgeX = edgeX;
            _edgeTop = edgeTop;
            _edgeBottom = edgeBottom;
            _phoneW = phoneW;
            _phoneH = phoneH;
            _phoneRight = phoneRight;
            Current = KvmState.Idle;
            Armed = true;
        }

        /// <summary>IDLE 态下、每次鼠标事件调用。cursorX/Y 为真实光标位置。</summary>
        public void OnIdleMove(int dx, int dy, int cursorX, int cursorY)
        {
            const int SAFE = 12;   // 安全带宽度，防止回来瞬间被弹回去

            if (!Armed)
            {
                bool away = _phoneRight
                    ? cursorX < _edgeX - SAFE
                    : cursorX > _edgeX + SAFE;
                if (away) Armed = true;
                return;
            }

            if (cursorY < _edgeTop || cursorY >= _edgeBottom) return;

            bool atEdge;
            bool pushingOut;
            if (_phoneRight)
            {
                atEdge = cursorX >= _edgeX;
                pushingOut = dx > 0;
            }
            else
            {
                atEdge = cursorX <= _edgeX;
                pushingOut = dx < 0;
            }
            if (!atEdge || !pushingOut) return;

            // 入屏点用比例映射，保证 PC 边缘顶端 → 手机顶端
            int span = _edgeBottom - _edgeTop;
            int phoneY = (int)((long)(cursorY - _edgeTop) * _phoneH / span);
            if (phoneY < 0) phoneY = 0;
            if (phoneY > _phoneH - 1) phoneY = _phoneH - 1;
            int phoneX = _phoneRight ? 0 : _phoneW - 1;

            Current = KvmState.Takeover;
            Armed = false;
            Action<short, short> h = EnterTakeover;
            if (h != null) h((short)phoneX, (short)phoneY);
        }

        /// <summary>TAKEOVER 态下、每次鼠标事件调用，传入游标模型算出的实际增量。</summary>
        public void OnTakeoverMove(int actualDx, int actualDy, int vx, int vy)
        {
            if (Current != KvmState.Takeover) return;

            // 回程判定：虚拟光标撞到与 PC 相邻的那条边
            bool backAtEdge = _phoneRight ? vx <= 0 : vx >= _phoneW - 1;
            if (!backAtEdge) return;

            // 必须仍在继续往外推，否则光标停在边上就会立刻回程
            bool pushingBack = _phoneRight ? actualDx < 0 : actualDx > 0;
            if (!pushingBack) return;

            Current = KvmState.Idle;
            Armed = false;          // 回到 IDLE 后先解除武装，等光标离开安全带
            Action h = LeaveTakeover;
            if (h != null) h();
        }
    }
}
```

- [ ] **Step 2: 在 Program.cs 里接入状态机**

把 `MouseMoved` 处理器整体替换为：

```csharp
            ri.MouseMoved += delegate(RawMouseEvent e)
            {
                if (tracker.Current == KvmState.Idle)
                {
                    POINT p;
                    GetCursorPos(out p);
                    tracker.OnIdleMove(e.Dx, e.Dy, p.X, p.Y);
                }
                else
                {
                    short sdx = cursor.NextDx((int)(e.Dx * sensitivity));
                    short sdy = cursor.NextDy((int)(e.Dy * sensitivity));
                    if (sdx != 0 || sdy != 0)
                        transport.Send(Protocol.EncodeMove(sdx, sdy));
                    tracker.OnTakeoverMove(sdx, sdy, cursor.X, cursor.Y);
                }

                short wheel = (short)(e.WheelDelta / 120);
                if (wheel != 0) transport.Send(Protocol.EncodeScroll((short)0, wheel));

                if (e.ButtonFlags != 0)
                {
                    EmitButton(transport, e.ButtonFlags, 0x0001, 1);
                    EmitButton(transport, e.ButtonFlags, 0x0004, 2);
                    EmitButton(transport, e.ButtonFlags, 0x0010, 3);
                }
            };
```

在 `Main` 里加字段与装配（**放在 transport 装配之后**）：

```csharp
            double sensitivity = 1.0;
            // 目标机几何：手机挂在 DISPLAY1（1920,0,1920x1080）的右侧，故边界 x = 3840
            EdgeTracker tracker = new EdgeTracker(
                edgeX: 3840, edgeTop: 0, edgeBottom: 1080,
                phoneW: 2136, phoneH: 3200, phoneRight: true);

            tracker.EnterTakeover += delegate(short px, short py)
            {
                log.WriteLine("# ENTER takeover at phone(" + px + "," + py + ")");
                transport.Send(Protocol.EncodeHome());
                cursor.Reset();
                cursor.SetPosition(px, py);
                transport.Send(Protocol.EncodeEnter(px, py));
            };
            tracker.LeaveTakeover += delegate
            {
                log.WriteLine("# LEAVE takeover");
                transport.Send(Protocol.EncodeLeave());
            };
```

需要 `using System.Runtime.InteropServices;` 与在本类里声明：

```csharp
        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT p);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X; public int Y; }
```

**注意**：`EncodeHome` 之后 `cursor.SetPosition(px, py)` —— 设备侧执行 HOME 时把光标顶到 `(0,0)`，随后 `EncodeEnter` 只是把设备侧的光标位置"告知"用于后续对齐；**Task 5 之后设备侧尚未处理 ENTER**，所以本任务的实际效果是：进入接管时光标从手机左上角开始。这是可接受的中间状态，Task 7 会补齐。

- [ ] **Step 3: 验证**

Run: `dist\pc-kvm.exe`

判定：
1. 把鼠标推到**最右边显示器（DISPLAY1）的右边缘并继续往右推** → `pc-kvm.log` 出现 `# ENTER takeover at phone(0,<y>)`，且 `y` 随你在边缘的高度变化 ✅
2. 继续移动鼠标 → 手机上光标移动；**PC 光标不动**（此时 PC 光标被 Windows 钳在边缘） ✅
3. 把手机光标**推回左边缘并继续往左推** → 日志出现 `# LEAVE takeover` ✅
4. **回到 IDLE 后立刻再往右推** → **不应立刻再次 ENTER**（回程冷却生效），需先把光标往左移开一段再推 ✅
5. 在**左边那块显示器（DISPLAY2）的最左边缘**推 → **不应触发**（手机挂在右侧） ✅

- [ ] **Step 4: 提交**

```bash
cd /g/pc-kvm
git add src/agent/EdgeTracker.cs src/agent/Program.cs
git commit -m "feat: 边缘状态机与坐标映射 —— 比例入屏、回程冷却、虚拟光标钳制"
```

---

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
            uint fgThread = GetWindowThreadProcessId(_prevForeground, out uint _);

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

- [ ] **Step 2: 在 Program.cs 接入**

把 `MessageHost` 改成**可见但极小的置顶窗口**（夺取前台需要有真实窗口，且用户要能看见当前状态）：

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

```csharp
            Suppressor supp = new Suppressor(hwnd, delegate(string s) { log.WriteLine(s); });

            tracker.EnterTakeover += delegate(short px, short py)
            {
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

### Task 8: 边界情况 —— 心跳、断线解锁、逃逸键

**产出**：手机掉线、adb 断开、进程被杀等异常下，**用户永远不会被困在锁死状态**。这三条是"能日常用"与"偶尔抽风"的分界。

**Files:**
- Modify: `src/agent/Program.cs`
- Modify: `src/injector/Injector.java`

**Interfaces:**
- Consumes: Task 7 的 `Suppressor`、Task 3 的 `Transport`
- Produces: 无新接口

- [ ] **Step 1: 加心跳**

PC 侧每 1 秒发一次 PING，记录最后收到 PONG 的时间；超过 2 秒未收到则视为断线。

在 `Main` 里加：

```csharp
            uint pingSeq = 0;
            long lastPongTicks = DateTime.UtcNow.Ticks;
            transport.MessageReceived += delegate(byte type, byte[] payload)
            {
                if (type == Protocol.MsgPong)
                    lastPongTicks = DateTime.UtcNow.Ticks;
            };

            Timer heartbeat = new Timer();
            heartbeat.Interval = 1000;
            heartbeat.Tick += delegate
            {
                if (!transport.IsConnected) return;
                pingSeq++;
                transport.Send(Protocol.EncodePing(pingSeq));

                double sincePong = (DateTime.UtcNow - new DateTime(lastPongTicks)).TotalSeconds;
                if (sincePong > 2.0 && tracker.Current == KvmState.Takeover)
                {
                    log.WriteLine("# 心跳超时（" + sincePong.ToString("F1") + "s），强制解除抑制");
                    tracker.AbortTakeover();
                    supp.Release();
                    host.SetStatus("IDLE");
                }
            };
            heartbeat.Start();
```

同时在 `transport.Disconnected` 处理器里加：

```csharp
            transport.Disconnected += delegate
            {
                log.WriteLine("# 设备已断开");
                if (tracker.Current == KvmState.Takeover)
                {
                    tracker.AbortTakeover();
                    supp.Release();
                    host.SetStatus("IDLE");
                }
            };
```

- [ ] **Step 2: 加紧急逃逸键**

在 `MessageHost` 上挂键盘处理。**注意**：抑制生效时焦点在本窗口，所以这个键一定能收到。

```csharp
        public event Action Escape;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Alt | Keys.Escape))
            {
                Action h = Escape;
                if (h != null) h();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Alt | Keys.Escape))
            {
                Action h = Escape;
                if (h != null) h();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }
```

（两个都重写是因为 WinForms 对含修饰键的组合在不同路径下分发不一致。）

在 `Main` 里接上：

```csharp
            host.Escape += delegate
            {
                log.WriteLine("# 逃逸键触发");
                tracker.AbortTakeover();
                supp.Release();
                host.SetStatus("IDLE");
            };
```

- [ ] **Step 3: 设备侧在 stdin/socket 关闭时正确退出**

`Injector.java` 已经在内层循环退出后调 `dev.close()` 并打印 `INJECTOR exit`。补一条：**socket 读到 EOF 应立即退出**，不要留在后台。已有代码满足（`in.read()` 返回 -1 → `break`）。**额外补一个防御**：捕获 `IOException` 时也退出循环而非无限重试。已有 `try/catch` 覆盖。**本步骤只需确认这两点，不需改代码**——若发现不符，按上述要求修正。

- [ ] **Step 4: 验证（三条异常路径，逐条实测）**

Run: `dist\pc-kvm.exe`

1. **拔线解锁**：进入 `TAKEOVER` 后，**直接拔掉手机数据线** → 2 秒内小窗回到 `IDLE`，鼠标解锁 ✅
2. **杀设备进程**：进入 `TAKEOVER` 后，在另一个终端执行 `adb shell "pkill -f Injector"` → 心跳超时后回 `IDLE` ✅
3. **逃逸键**：进入 `TAKEOVER` 后按 `Ctrl+Alt+Esc` → 立即回 `IDLE` ✅
4. **UAC 抢占**：进入 `TAKEOVER` 后，触发一个 UAC 弹窗（例如运行 `powershell Start-Process cmd -Verb RunAs`）→ 250ms 内检测到前台丢失并解除 ✅
5. **正常流程不受影响**：完整走一遍跨越→收回，仍正常 ✅

- [ ] **Step 5: 提交**

```bash
cd /g/pc-kvm
git add src/agent/Program.cs src/injector/Injector.java
git commit -m "feat: 边界情况 —— 心跳超时、断线解锁、紧急逃逸键、前台丢失保护"
```

---

### Task 9: 键盘支持

**产出**：`TAKEOVER` 期间按 PC 键盘，字符出现在**手机上**（而不是 PC 上）。

**Files:**
- Modify: `src/agent/Program.cs`
- Modify: `src/injector/ScancodeMap.java`

**Interfaces:**
- Consumes: Task 3 的 `Protocol.EncodeKey`、Task 2 的 `UhidDevice.sendKeyboard`
- Produces: 无新接口

- [ ] **Step 1: 补齐 scancode 映射表**

修改 `src/injector/ScancodeMap.java`，把 Task 3 的骨架表补全：方向键、Home/End/PgUp/PgDn/Insert/Delete、F1–F12、左右 Ctrl/Shift/Alt/Win（E0 前缀）、小键盘（E0 前缀的数字区）、`-=`、`[]`、`;'`、`` ` ``、`\,./`。

E0 前缀键的写法（示例，按同样方式补齐其余）：

```java
    public static int toHidUsage(int scancode) {
        boolean e0 = (scancode & 0xE000) == 0xE000;
        int mk = scancode & 0xFF;

        if (e0) {
            switch (mk) {
                case 0x1C: return 0x58;   // 小键盘 Enter
                case 0x1D: return 0xE4;   // 右 Ctrl
                case 0x35: return 0x54;   // 小键盘 /
                case 0x37: return 0x46;   // PrintScreen
                case 0x38: return 0xE6;   // 右 Alt
                case 0x47: return 0x4A;   // Home
                case 0x48: return 0x52;   // Up
                case 0x49: return 0x4B;   // PgUp
                case 0x4B: return 0x50;   // Left
                case 0x4D: return 0x4F;   // Right
                case 0x4F: return 0x4D;   // End
                case 0x50: return 0x51;   // Down
                case 0x51: return 0x4E;   // PgDn
                case 0x52: return 0x49;   // Insert
                case 0x53: return 0x4C;   // Delete
                case 0x5B: return 0xE3;   // 左 Win
                case 0x5C: return 0xE7;   // 右 Win
                case 0x5D: return 0x65;   // Menu
                default:   return -1;
            }
        }
        // ... 原有的非 E0 表，再加上：
        switch (mk) {
            case 0x0C: return 0x2D;   // -
            case 0x0D: return 0x2E;   // =
            case 0x1A: return 0x2F;   // [
            case 0x1B: return 0x30;   // ]
            case 0x27: return 0x33;   // ;
            case 0x28: return 0x34;   // '
            case 0x29: return 0x35;   // `
            case 0x2B: return 0x31;   // backslash
            case 0x33: return 0x36;   // ,
            case 0x34: return 0x37;   // .
            case 0x35: return 0x38;   // /
            case 0x3A: return 0x39;   // CapsLock
            case 0x3B: return 0x3A; case 0x3C: return 0x3B; case 0x3D: return 0x3C;
            case 0x3E: return 0x3D; case 0x3F: return 0x3E; case 0x40: return 0x3F;
            case 0x41: return 0x40; case 0x42: return 0x41; case 0x43: return 0x42;
            case 0x44: return 0x43;                       // F1-F10
            case 0x57: return 0x44; case 0x58: return 0x45;   // F11, F12
            case 0x45: return 0x53;   // NumLock
            case 0x46: return 0x48;   // ScrollLock
            default:   return -1;
        }
    }
```

**修饰键注意**：左右 Shift/Ctrl/Alt 在 HID 里是报告中的独立位（`0x02/0x20` = 左/右 Shift，`0x01/0x10` = 左/右 Ctrl，`0x04/0x40` = 左/右 Alt，`0x08/0x80` = 左/右 GUI），**不是** 6 个按键槽位里的普通键。因此 `ScancodeMap.toHidUsage` 对修饰键应返回 `-1`，由 **PC 侧**把它们折算进 `mods` 字节随每条 KEY 消息下发。

- [ ] **Step 2: PC 侧维护修饰键状态**

在 `src/agent/Program.cs` 加：

```csharp
        /// <summary>把 Windows scancode 映射为 HID 修饰位（不是普通键）。返回 0 表示不是修饰键。</summary>
        static byte ModifierBit(int scancode, bool isE0)
        {
            int mk = scancode & 0xFF;
            if (!isE0)
            {
                if (mk == 0x2A) return 0x02;   // 左 Shift
                if (mk == 0x36) return 0x20;   // 右 Shift
                if (mk == 0x1D) return 0x01;   // 左 Ctrl
                if (mk == 0x38) return 0x04;   // 左 Alt
                if (mk == 0x5B) return 0x08;   // 左 Win
            }
            else
            {
                if (mk == 0x1D) return 0x10;   // 右 Ctrl
                if (mk == 0x38) return 0x40;   // 右 Alt
                if (mk == 0x5C) return 0x80;   // 右 Win
            }
            return 0;
        }
```

把 `KeyChanged` 处理器替换为：

```csharp
            ri.KeyChanged += delegate(RawKeyEvent e)
            {
                byte bit = ModifierBit(e.Scancode, e.IsE0);
                if (bit != 0)
                {
                    if (e.IsUp) modifiers &= (byte)~bit; else modifiers |= bit;
                    // 修饰键变化也要发一条报告，否则手机侧修饰态不更新
                    if (tracker.Current == KvmState.Takeover)
                        transport.Send(Protocol.EncodeKey((ushort)e.Scancode, (byte)(e.IsUp ? 0 : 1), modifiers));
                    return;
                }

                if (tracker.Current != KvmState.Takeover) return;   // IDLE 态不转发，PC 正常用
                transport.Send(Protocol.EncodeKey((ushort)e.Scancode, (byte)(e.IsUp ? 0 : 1), modifiers));
            };
```

并在 `Main` 里加字段：

```csharp
            byte modifiers = 0;
```

**注意**：`modifiers` 被 lambda 捕获并修改，C# 5 下需要它是**局部变量而非字段**（lambda 捕获局部变量是 C# 3 特性，可以）。若编译器报错，改为用一个 `byte[] modifiersBox = new byte[1];` 包装。

- [ ] **Step 3: 设备侧在退出接管时清空按键**

在 `Injector.java` 的 `handle` 里给 `MSG_LEAVE` 加分支——**防止修饰键卡在按下态**（这是遥控类软件的经典 bug：接管期间按下 Ctrl，退出时没抬起，之后手机上一直是 Ctrl 生效）：

```java
        } else if (type == MSG_LEAVE) {
            buttonsDown = 0;
            dev.sendMouse((byte) 0, (byte) 0, (byte) 0, (byte) 0);
            KeyState.releaseAll(dev);
        }
```

在 `KeyState.java` 加：

```java
    /** 全部抬起：清修饰键与所有按键槽位，各发一条报告。 */
    public static void releaseAll(UhidDevice dev) throws Exception {
        modifiers = 0;
        for (int i = 0; i < 6; i++) slots[i] = 0;
        dev.sendKeyboard((byte) 0, new byte[6]);
    }
```

- [ ] **Step 4: 验证**

Run: `dist\pc-kvm.exe`，打开记事本点进去确认能打字。

1. 进入 `TAKEOVER`，在手机上打开一个可输入的应用（如备忘录），**用 PC 键盘打字** → 字符出现在**手机**上 ✅
2. `Shift+A` → 出大写 `A` ✅
3. `Ctrl+A` 全选、`Ctrl+C/V` 复制粘贴 ✅
4. 方向键、`Home`/`End`、`Backspace`/`Delete` ✅
5. **修饰键不粘连**：按住 `Ctrl` 进入再退出接管 → 退出后手机上不应有 Ctrl 残留（在手机上再打字验证） ✅
6. **IDLE 态键盘照常**：退出接管后，在 PC 记事本打字 → 正常输入 ✅

- [ ] **Step 5: 提交**

```bash
cd /g/pc-kvm
git add src/agent/Program.cs src/injector/ScancodeMap.java src/injector/KeyState.java src/injector/Injector.java
git commit -m "feat: 键盘支持 —— scancode 映射、修饰键状态、退出时清空按键"
```

---

## 自审记录

**Spec 覆盖检查**（对照 `2026-09-21-pc-android-kvm-design.md`）：

| Spec 要求 | 覆盖任务 |
|---|---|
| 第 4 节 架构：PC 侧 7 部件 + 手机侧零安装 | Task 1–3（文件结构即按此拆分） |
| 第 6 节 状态机 IDLE ⇄ TAKEOVER | Task 6 |
| 第 6 节 边缘判定「贴边且继续外推」 | Task 6 Step 1 |
| 第 6 节 回程冷却 | Task 6 Step 1（`Armed` 字段） |
| 第 7 节 PC 端持有虚拟光标 + 钳制 | Task 5（`CursorModel`） |
| 第 7 节 推角落归零 | Task 5 |
| 第 7 节 两套比例（去程比例映射 / 接管期 1:1） | Task 6 Step 1（比例入屏）+ Task 6 Step 2（`sensitivity` 默认 1.0） |
| 第 7 节 协议 9 种消息 | Task 3（+ Task 5 加 `MsgHome` = 第 10 种） |
| 第 7 节 `LEAVE` 由 PC 端发起 | Task 6 Step 1（`OnTakeoverMove` 在 PC 侧判定）+ Task 9 Step 3 |
| 第 7 节 键盘 scancode → HID Usage ID | Task 9 |
| 第 8 节 抑制机制（焦点小窗 + ClipCursor） | Task 7 |
| 第 8 节 不用全局钩子 | Task 7（全程无 `SetWindowsHookEx`） |
| 第 9 节 ① 断线必须立刻解除抑制 | Task 8 Step 1 |
| 第 9 节 ② 前台被抢走就漏键 | Task 7 的 `CheckForeground` + Task 8 Step 1 |
| 第 9 节 ③ 紧急逃逸键 | Task 8 Step 2 |
| 第 10 节 验证 5（推角落归零） | Task 5 Step 5 |
| 第 10 节 验证 6（端到端闭环） | Task 4（最小闭环）+ Task 6/7/8/9 逐步补全 |
| 第 1 节 不传画面、PC 上不开镜像窗口 | 全程无视频代码，Task 7 的小窗是状态指示器而非镜像 |

**未覆盖项（明确留给后续，非遗漏）**：

- **`CONFIG` 消息未接线**：Task 3 定义了它，但 DeviceLauncher/Program 里手机分辨率与边缘配置目前是**硬编码常量**（`2136`/`3200`/`3840`）。这是刻意的——单机单人场景下先跑通，多配置支持属 YAGNI。若后续要支持"手机挂在左侧"或换手机，需接线 `CONFIG`。
- **上/下边缘未实现**：Spec 第 3 节的实测几何决定了手机只能挂在双屏的最外侧左右，`EdgeTracker` 只实现 left/right。上下边缘留待有实际需求时再加。
- **无线（Wi-Fi）传输**：Spec 第 7 节说"先用 USB 跑通，网络问题留到最后"。本计划全程走 adb，无线不在范围内。
- **`UHID_START` 事件未读取**：阶段一发现不读也能工作。读取它能让设备侧感知就绪/销毁，但非必需。

**占位符扫描**：已通过。无 TBD / TODO / "类似 Task N" / 空泛的"适当处理错误"。每个代码步骤都给出了完整可编译的代码。

**类型一致性检查**：

- `Protocol.PayloadLength` 的返回长度与 `Encode*` 产生的帧长逐项核对：Enter 4+1=5 ✅ Move 5 ✅ Button 3 ✅ Scroll 5 ✅ Key 5 ✅ Leave 1 ✅ Config 8 ✅ Ping/Pong 5 ✅ Home 1 ✅
- PC 侧 `Protocol.MsgXxx`（byte 常量）与手机侧 `Injector.MSG_XXX`（byte 常量）取值一一对应 ✅
- `CursorModel.NextDx/NextDy` 返回 `short`，`Protocol.EncodeMove(short,short)` 消费 `short` ✅
- `EdgeTracker.EnterTakeover` 是 `Action<short,short>`，`Program` 里的处理器签名 `delegate(short px, short py)` 匹配 ✅
- `Suppressor.Engage()` 返回 `bool`，`Program` 里 `if (!supp.Engage())` 匹配 ✅
- `KeyState.apply(UhidDevice, int, boolean, int)` 与 `Injector.handle` 里的 `KeyState.apply(dev, u16(p,0), p[2]!=0, p[3])` 匹配 ✅
- `HidDescriptor.MOUSE_REPORT_SIZE`(5) 与 `UhidDevice.sendMouse` 写入的字节数（report id + buttons + dx + dy + wheel = 5）一致 ✅
- `HidDescriptor.KEYBOARD_REPORT_SIZE`(9) 与 `sendKeyboard` 写入的字节数（report id + mods + reserved + 6 = 9）一致 ✅
- `EdgeTracker.AbortTakeover()` 在 Task 7 引入并被 `Program` 调用；Task 6 的类定义中未包含，Task 7 Step 2 明确要求补上 ✅

**已知的实现风险（实现者需留意）**：

1. **Task 7 是唯一有系统级副作用的任务**，`ClipCursor` 失控会锁死用户鼠标。三条安全不变量必须原样实现。
2. **Task 6 的中间状态**：Task 5 之后设备侧尚未处理 `ENTER`，所以进入接管时光标从手机左上角开始而非入屏点。这是刻意的中间状态，不要为此提前实现 `ENTER`。
3. **`sensitivity` 为 1.0 只是起点**。Raw Input 的 `lLastX/lLastY` 是 mickeys 而非像素，Windows 指针加速不作用于它，真实手感需在 Task 6 验证时手调后写回常量。
