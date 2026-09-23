using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PcKvm
{
    /// <summary>
    /// 主窗体：Raw Input 的消息宿主、托盘载体，兼作状态指示窗。
    /// 必须是可见的真实窗口——隐藏窗口无法持有前台，抑制器夺取前台依赖它。
    /// </summary>
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

        // ---- 抑制期间隐藏 PC 光标 ----
        // 抑制生效时鼠标在控制手机，而 PC 光标被 ClipCursor 钳在状态条里的一个像素上——
        // 留着它只是个让人困惑的箭头。做法是给**本窗口**挂一张全透明光标：Windows 按
        // "光标下面那个窗口"决定画什么，所以只有压在我们窗口上时不可见，光标一离开
        // （或本进程死掉、系统释放裁剪）就自动恢复成正常箭头。
        // **刻意不用全局 `ShowCursor(false)`**：那是桌面级显示计数器，app 被硬杀时不会
        // 自动恢复，而 `taskkill /F /IM pc-kvm.exe` 正是本项目写明的应急退路——不能跟它打架。
        IntPtr _blankHandle;
        System.Windows.Forms.Cursor _blank;

        /// <summary>hidden=true 时隐藏 PC 光标（仅在本窗口范围内生效）。
        /// 透明光标造不出来时保持默认光标，绝不抛异常——这是 UX 增强，不是安全路径。</summary>
        public void SetCursorHidden(bool hidden)
        {
            if (_blank == null)
            {
                if (!hidden) return;
                int n = 32, plane = (n * n) / 8;
                byte[] andBits = new byte[plane], xorBits = new byte[plane];
                for (int i = 0; i < plane; i++) andBits[i] = 0xFF;   // AND=1 → 输出=屏幕本身 → 全透明
                _blankHandle = CreateCursor(GetModuleHandle(null), 0, 0, n, n, andBits, xorBits);
                if (_blankHandle == IntPtr.Zero) return;
                _blank = new System.Windows.Forms.Cursor(_blankHandle);
            }
            System.Windows.Forms.Cursor c = hidden ? _blank : Cursors.Default;
            this.Cursor = c;
            _status.Cursor = c;
            // 显式施加一次：光标此刻已被钳进本窗口且不再移动，未必还会收到 WM_SETCURSOR
            SetCursor(c.Handle);
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr CreateCursor(IntPtr hInst, int xHotSpot, int yHotSpot,
            int nWidth, int nHeight, byte[] pvANDPlane, byte[] pvXORPlane);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")]
        static extern IntPtr SetCursor(IntPtr h);

        /// <summary>紧急逃逸键 Ctrl+Alt+Esc。抑制生效时本窗口持有前台，此键一定能收到。</summary>
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
    }
}
