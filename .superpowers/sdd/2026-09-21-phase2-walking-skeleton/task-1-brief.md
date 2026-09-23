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

