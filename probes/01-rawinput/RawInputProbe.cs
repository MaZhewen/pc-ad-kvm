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
