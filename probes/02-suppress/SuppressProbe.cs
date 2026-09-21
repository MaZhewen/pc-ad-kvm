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
