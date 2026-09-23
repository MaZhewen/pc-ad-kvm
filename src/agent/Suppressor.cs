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
