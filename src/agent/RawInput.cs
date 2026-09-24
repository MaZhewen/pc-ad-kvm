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
