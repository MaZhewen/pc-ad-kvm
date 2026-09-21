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
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")]
    static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    IntPtr _savedForeground = IntPtr.Zero;
    POINT  _savedCursor;
    bool   _engaged = false;
    int    _countdown = 0;
    string _keys = "";
    string _lastEngageLog = "";

    Label  _status;
    Label  _keysLabel;
    Button _btnGo;
    Button _btnRelease;
    Timer  _timer;

    public SuppressProbe()
    {
        Text = "Suppress Probe — 后台夺取前台验证";
        Width = 780; Height = 380;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(20, 20);

        _status = new Label();
        _status.Dock = DockStyle.Top;
        _status.Height = 190;
        _status.Font = new Font("Consolas", 9.5f);
        _status.Text = "步骤：\n"
            + "1. 打开记事本并拖到屏幕右侧（别与探针重叠），点进去打几个字\n"
            + "2. 点击下方「3 秒后尝试夺取前台并进入抑制」\n"
            + "3. 倒计时 3 秒内【点击记事本】让它保持前台（模拟正在打字）\n"
            + "4. 倒计时结束，探针从后台尝试夺取前台，上方显示策略A/B成败\n"
            + "5. 若夺取成功【不要再点任何窗口】直接打字：字符应只出现在下方\n"
            + "   「已吃掉按键」列表里，记事本中一个字符都不该有；鼠标应被锁住\n"
            + "6. 点「强制解除抑制」或按 Ctrl+Alt+U 退出，回记事本打字应恢复";

        _keysLabel = new Label();
        _keysLabel.Dock = DockStyle.Top;
        _keysLabel.Height = 50;
        _keysLabel.Font = new Font("Consolas", 9.5f);
        _keysLabel.ForeColor = Color.DarkBlue;
        _keysLabel.Text = "已吃掉按键: (尚未进入抑制模式)";

        _btnGo = new Button();
        _btnGo.Text = "3 秒后尝试夺取前台并进入抑制";
        _btnGo.Dock = DockStyle.Bottom;
        _btnGo.Height = 40;
        _btnGo.Click += delegate { StartCountdown(); };

        _btnRelease = new Button();
        _btnRelease.Text = "强制解除抑制";
        _btnRelease.Dock = DockStyle.Bottom;
        _btnRelease.Height = 40;
        _btnRelease.Click += delegate { Release(); };

        // 后添加的先停靠：_status 最顶，_keysLabel 其下；_btnRelease 最底，_btnGo 其上
        Controls.Add(_btnGo);
        Controls.Add(_btnRelease);
        Controls.Add(_keysLabel);
        Controls.Add(_status);

        _timer = new Timer();
        _timer.Interval = 1000;
        _timer.Tick += OnTick;

        KeyPreview = true;
        KeyDown += OnKeyDown;
        KeyPress += OnKeyPress;
    }

    void StartCountdown()
    {
        if (_engaged || _countdown > 0) return;
        _keys = "";
        _keysLabel.Text = "已吃掉按键: ";
        _countdown = 3;
        _status.Text = "倒计时 3 …\n立即点击记事本，让它获得前台焦点！\n"
            + "当前前台窗口: " + TitleOf(GetForegroundWindow());
        _timer.Start();
    }

    void OnTick(object sender, EventArgs e)
    {
        _countdown--;
        if (_countdown > 0)
        {
            _status.Text = "倒计时 " + _countdown + " …\n立即点击记事本，让它获得前台焦点！\n"
                + "当前前台窗口: " + TitleOf(GetForegroundWindow());
        }
        else
        {
            _timer.Stop();
            Engage();
        }
    }

    void Engage()
    {
        if (_engaged) return;

        _savedForeground = GetForegroundWindow();
        GetCursorPos(out _savedCursor);

        // 无边框置顶（TopMost 只保证浮在最上，不等于抢到前台——这正是要测的）
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        Opacity = 0.65;
        BackColor = Color.DarkRed;
        _status.ForeColor = Color.White;
        _keysLabel.ForeColor = Color.Yellow;

        StringBuilder log = new StringBuilder();
        log.Append("原前台窗口: " + TitleOf(_savedForeground) + "\n");

        // 策略 A：直接 SetForegroundWindow，再用 GetForegroundWindow 核对
        SetForegroundWindow(Handle);
        bool aOk = (GetForegroundWindow() == Handle);
        log.Append("策略A(直接SetForegroundWindow): " + (aOk ? "成功" : "失败") + "\n");

        // 策略 B：AttachThreadInput 绕行（仅当 A 失败才有意义）
        string bResult;
        if (aOk)
        {
            bResult = "跳过(A已成功，无需绕行)";
        }
        else
        {
            IntPtr fg = GetForegroundWindow();
            uint fgPid;
            uint fgThread = GetWindowThreadProcessId(fg, out fgPid);
            uint myThread = GetCurrentThreadId();
            bool attached = AttachThreadInput(myThread, fgThread, true);
            SetForegroundWindow(Handle);
            AttachThreadInput(myThread, fgThread, false);
            bool bOk = (GetForegroundWindow() == Handle);
            bResult = (bOk ? "成功" : "失败")
                + (attached ? "" : " (AttachThreadInput本身返回false)");
        }
        log.Append("策略B(AttachThreadInput绕行): " + bResult + "\n");

        bool grabbed = (GetForegroundWindow() == Handle);
        log.Append("最终前台窗口: " + TitleOf(GetForegroundWindow())
            + (grabbed ? "（本探针）" : "（非本探针）") + "\n");

        // 只有真的抢到前台才锁光标：没抢到时锁鼠标会让用户够不到解除按钮
        string clipResult;
        if (grabbed)
        {
            ActiveControl = null; // 防止空格/回车误触按钮
            POINT c;
            GetCursorPos(out c);
            RECT r;
            r.Left = c.X; r.Top = c.Y; r.Right = c.X + 1; r.Bottom = c.Y + 1;
            clipResult = ClipCursor(ref r) ? "成功" : "失败 err=" + Marshal.GetLastWin32Error();
        }
        else
        {
            clipResult = "未执行(夺取失败，避免鼠标无处释放)";
        }
        log.Append("ClipCursor: " + clipResult + "\n");
        _lastEngageLog = log.ToString();

        _engaged = true;
        _status.Text = "【抑制模式 " + (grabbed ? "ON" : "ON-但夺取失败") + "】\n"
            + _lastEngageLog
            + (grabbed
                ? "→ 不要点任何窗口，直接打字！字符应只进下方列表。Ctrl+Alt+U 或按钮解除。"
                : "→ 焦点方案在此机器上不成立。点「强制解除抑制」退出并记录结论。");
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Control && e.Alt && e.KeyCode == Keys.U) { Release(); e.Handled = true; return; }
        if (IsNonCharKey(e.KeyCode)) LogKey("[" + e.KeyCode + "]");
    }

    void OnKeyPress(object sender, KeyPressEventArgs e)
    {
        char c = e.KeyChar;
        if (c == '\r') LogKey("[Enter]");
        else if (c == '\b') LogKey("[Back]");
        else if (c == ' ') LogKey("[Space]");
        else if (c >= ' ') LogKey(c.ToString());
    }

    static bool IsNonCharKey(Keys k)
    {
        switch (k)
        {
            case Keys.ShiftKey: case Keys.ControlKey: case Keys.Menu:
            case Keys.LWin: case Keys.RWin:
            case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right:
            case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown:
            case Keys.Insert: case Keys.Delete: case Keys.Escape: case Keys.Tab:
            case Keys.CapsLock: case Keys.NumLock: case Keys.Scroll:
            case Keys.F1: case Keys.F2: case Keys.F3: case Keys.F4:
            case Keys.F5: case Keys.F6: case Keys.F7: case Keys.F8:
            case Keys.F9: case Keys.F10: case Keys.F11: case Keys.F12:
            case Keys.PrintScreen: case Keys.Pause:
                return true;
        }
        return false;
    }

    void LogKey(string k)
    {
        _keys += k + " ";
        if (_keys.Length > 300) _keys = _keys.Substring(_keys.Length - 300);
        _keysLabel.Text = "已吃掉按键: " + _keys;
    }

    void Release()
    {
        // ClipCursor 解除放在最前面：任何情况下都不能让用户鼠标锁死
        ClipCursor(IntPtr.Zero);

        _timer.Stop();
        _countdown = 0;

        if (_engaged)
        {
            _engaged = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            TopMost = false;
            Opacity = 1.0;
            BackColor = SystemColors.Control;
            _status.ForeColor = SystemColors.ControlText;
            _keysLabel.ForeColor = Color.DarkBlue;
            SetCursorPos(_savedCursor.X, _savedCursor.Y);
            if (_savedForeground != IntPtr.Zero) SetForegroundWindow(_savedForeground);
        }

        _status.Text = "【抑制模式 OFF】\n"
            + "光标已解锁，焦点已还原到: " + TitleOf(_savedForeground) + "\n"
            + "当前前台窗口: " + TitleOf(GetForegroundWindow()) + "\n"
            + "→ 现在去记事本打字，字符应恢复正常。可再次点按钮重新验证。\n"
            + "---- 上次夺取结果（请记录到 FINDINGS）----\n"
            + (_lastEngageLog.Length > 0 ? _lastEngageLog : "(本轮尚未进入过抑制)\n");
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
