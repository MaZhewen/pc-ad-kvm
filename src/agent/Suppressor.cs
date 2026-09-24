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
        // SetLastError = true 是必须的：没有它，Marshal.GetLastWin32Error() 读到的是
        // 上一次"声明了 SetLastError 的"调用留下的值，下面那些 `err=` 日志会打出垃圾数字——
        // 本项目已多次栽在"诊断仪器说谎"上（Measure-Object -Line、2% 采样、ps -A）。
        [DllImport("user32.dll", SetLastError = true)] static extern bool ClipCursor(ref RECT r);
        [DllImport("user32.dll")] static extern bool ClipCursor(IntPtr none);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll", SetLastError = true)] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr h, StringBuilder s, int n);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        readonly IntPtr _own;
        readonly Action<string> _log;
        // 抑制状态切换回调（参数 true=抑制生效）。挂在 Engage/Release 这一对唯一入口上，
        // 于是"隐藏 PC 光标"自动覆盖全部七条释放路径，不必在 Program.cs 里逐个补调用
        readonly Action<bool> _onSuppress;
        readonly Action _showCapture;
        readonly Action _hideCapture;
        IntPtr _prevForeground = IntPtr.Zero;

        // 抑制期间光标的真实位置被搬到了状态条上，退出时必须搬回去，
        // 否则每次退出接管 PC 光标都会留在左上角（相对旧行为的 UX 回归）
        int _savedX, _savedY;
        bool _hasSavedCursor;

        public bool IsEngaged { get; private set; }
        public event Action ForegroundLost;

        public Suppressor(IntPtr ownWindow, Action<string> log, Action<bool> onSuppress,
                          Action showCapture, Action hideCapture)
        {
            _own = ownWindow;
            _log = log;
            _onSuppress = onSuppress;
            _showCapture = showCapture;
            _hideCapture = hideCapture;
        }

        /// <summary>尝试夺取前台并锁定光标。返回 false 表示夺取失败，此时光标未被锁。</summary>
        public bool Engage()
        {
            if (IsEngaged) return true;

            _prevForeground = GetForegroundWindow();
            _showCapture();

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
                _hideCapture();
                return false;
            }

            POINT c;
            GetCursorPos(out c);

            // 钳制区必须是**我们自己的窗口**，不能是"进接管前光标所在的那个像素"。
            // 旧写法把光标钳在 (3839,y)——屏幕最右侧那一列，而本窗口在 (0,0) 220x28。
            // 鼠标点击会送给**光标下**的窗口并把它激活，于是接管期每一次点击都会
            // 激活该像素下的窗口 → 我们丢前台 → 250ms 守卫立刻解除抑制。
            // 真机实测：8 次「前台被抢走」、接管寿命 0–6 秒，用户报"点一下就回 PC"、
            // 且 20 次接管里 0 次成功转发按键（都被踢出后才打字）。
            // 钳进自家置顶状态条后，点击落在我们自己窗口上，前台保持不变。
            RECT r;
            if (!GetWindowRect(_own, out r))
            {
                // err 必须在任何其它 Win32 调用之前取——SetForegroundWindow 会覆盖 last-error
                int err = Marshal.GetLastWin32Error();
                GiveBackForeground();
                _hideCapture();
                _log("# 取本窗口矩形失败 err=" + err + "，不锁光标");
                return false;
            }
            r.Left += 1; r.Top += 1; r.Right -= 1; r.Bottom -= 1;   // 内缩 1px，避免压着边框
            if (r.Right - r.Left < 2 || r.Bottom - r.Top < 2)
            {
                int rw = r.Right - r.Left, rh = r.Bottom - r.Top;
                GiveBackForeground();
                _hideCapture();
                _log("# 本窗口矩形过小(" + rw + "x" + rh + ")，不锁光标");
                return false;
            }

            // 钳成"本窗口中心的那一个像素"，而不是整个窗口矩形：
            // 整个矩形会让光标在状态条里滑动（用户实测的可见副作用）；1x1 既把光标冻住，
            // 又保证点击落回我们自己的窗口——旧写法钳在屏幕最右列那个像素上（属于别的窗口），
            // 一点击就激活它、丢前台。
            int cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
            _savedX = c.X; _savedY = c.Y;
            _hasSavedCursor = true;
            // 显式把光标搬进钳制区中心，不依赖 ClipCursor 的隐式搬移行为
            SetCursorPos(cx, cy);

            RECT pin;
            pin.Left = cx; pin.Top = cy; pin.Right = cx + 1; pin.Bottom = cy + 1;
            bool clipped = ClipCursor(ref pin);
            if (!clipped)
            {
                int err = Marshal.GetLastWin32Error();
                SetCursorPos(_savedX, _savedY);
                _hasSavedCursor = false;
                GiveBackForeground();
                _hideCapture();
                _log("# ClipCursor 失败 err=" + err + "，不接管");
                return false;
            }
            // IsEngaged 必须在 _log 之前置位：日志写入若抛异常，光标已被钳住而
            // IsEngaged 仍为 false，守卫定时器（!IsEngaged 时提前返回）就被废掉了
            //（Task 7 审查 Minor A1，合并前必修）。
            IsEngaged = true;
            if (_onSuppress != null) _onSuppress(true);   // 隐藏 PC 光标（仅本窗口范围）
            _log("# 抑制已生效");

            return true;
        }

        /// <summary>恢复。ClipCursor 的解除必须在最前面，任何情况下都不能让光标留在锁死状态。</summary>
        public void Release()
        {
            ClipCursor(IntPtr.Zero);

            if (_hasSavedCursor)
            {
                // 把光标从状态条搬回进接管前的真实位置
                SetCursorPos(_savedX, _savedY);
                _hasSavedCursor = false;
            }

            if (!IsEngaged) { _hideCapture(); return; }
            IsEngaged = false;

            if (_prevForeground != IntPtr.Zero)
                SetForegroundWindow(_prevForeground);
            _prevForeground = IntPtr.Zero;

            // 还原 PC 光标放在**最后**：它是纯 UX，不能插在"解锁光标/还原位置/还前台"
            // 这条安全序列中间——万一它抛异常，前面几步已经做完了（Task 7 审查 C1 同一教训）
            if (_onSuppress != null) _onSuppress(false);
            _hideCapture();
        }

        /// <summary>由 UI 线程的 guard 定时器（250ms）调用：前台被抢走（UAC 安全桌面、锁屏、
        /// 点击激活了别的窗口等）时立即放弃抑制。**必须是 UI 线程**——ForegroundLost 处理器
        /// 会碰状态条控件，跨线程调用会抛异常（Task 7 审查 Minor 2）。</summary>
        public void CheckForeground()
        {
            if (!IsEngaged) return;
            IntPtr now = GetForegroundWindow();
            if (now == _own) return;

            // 顺序要紧：**先解锁、再让状态机放弃、最后才取证写日志。**
            // 理由：_log 是 AutoFlush 的 StreamWriter（磁盘满/被占用会抛 IOException），
            // TitleOf 的 GetWindowText 是**同步跨进程 SendMessage**（目标窗口挂死则阻塞 UI 线程）。
            // 这两者若排在 Release() 之前，一旦出事就会让光标留在钳住状态、守卫定时器随后也失效
            // ——正是本项目定义为最高风险的那类失效（Task 7 审查 C1/A1 同一类）。
            Release();
            Action h = ForegroundLost;
            if (h != null) h();

            // 抢走者的标题：真机上无法分辨是点击激活了别的窗口、还是第三方程序夺前台，
            // 打出标题即可一次定位（本项目曾有 2% 采样率日志导致无法证伪假设的教训）。
            // now 是 Release 之前抓的句柄，仍然有效。
            _log("# 前台被抢走，强制解除抑制（抢走者：" + TitleOf(now) + "）");
        }

        /// <summary>已夺取前台、但后续步骤失败时回滚：把前台还给用户原来的窗口。
        /// 不回滚会把键盘扣在本程序的状态条上（EnterTakeover 的调用方在 Engage 返回 false 时
        /// 只 AbortTakeover、不 Release），用户得自己点别处才能恢复。</summary>
        void GiveBackForeground()
        {
            if (_prevForeground != IntPtr.Zero)
                SetForegroundWindow(_prevForeground);
            _prevForeground = IntPtr.Zero;
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
