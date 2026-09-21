using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
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
        static volatile int _phoneW = 0;
        static volatile int _phoneH = 0;
        static volatile bool _geometryChanged = false;

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT p);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X; public int Y; }

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

            // transport 需在 MouseMoved 处理器之前就绪（C# 局部变量在声明点之后才可见）
            Transport transport = new Transport(DeviceLauncher.Port);
            if (!transport.Start())
            {
                MessageBox.Show("TCP 端口 " + DeviceLauncher.Port + " 监听失败，程序退出。",
                    "PC-KVM", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 阶段骨架：只把事件落到日志，后续任务接管这两个事件
            int mc = 0, kc = 0;
            CursorModel cursor = null;

            // 声明必须早于 ri.MouseMoved 订阅（C# 局部变量不能前向引用——Task 4 踩过）
            double sensitivity = 1.0;
            // 目标机几何：手机挂在 DISPLAY1（1920,0,1920x1080）的右侧，故边界 x = 3840
            EdgeTracker tracker = new EdgeTracker(
                edgeX: 3840, edgeTop: 0, edgeBottom: 1080,
                phoneW: 2136, phoneH: 3200, phoneRight: true);   // 占位初值，真值经 SetPhoneSize 灌入

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

            ri.MouseMoved += delegate(RawMouseEvent e)
            {
                mc++;
                if (mc % 50 == 0)
                    log.WriteLine("MOUSE dx=" + e.Dx + " dy=" + e.Dy
                        + " btn=0x" + e.ButtonFlags.ToString("X4") + " wheel=" + e.WheelDelta);

                // 未连接时 cursor 为 null：此时绝不能让状态机跑起来，
                // 否则推到边缘会触发 EnterTakeover → cursor.Reset() 空引用（异常被
                // RawInput 的 catch{} 吞掉，表现为状态机卡死在 TAKEOVER 且无任何报错）
                if (cursor != null)
                {
                    if (_geometryChanged)
                    {
                        _geometryChanged = false;
                        cursor.SetBounds(_phoneW, _phoneH);
                        tracker.SetPhoneSize(_phoneW, _phoneH);
                        log.WriteLine("# 几何已应用 " + _phoneW + "x" + _phoneH);
                        transport.Send(Protocol.EncodeHome());
                        cursor.Reset();
                    }

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
                }

                short wheel = (short)(e.WheelDelta / 120);   // Windows 一格 = 120，HID 一格 = 1
                if (wheel != 0) transport.Send(Protocol.EncodeScroll((short)0, wheel));

                if (e.ButtonFlags != 0)
                {
                    EmitButton(transport, e.ButtonFlags, 0x0001, 1);   // 左
                    EmitButton(transport, e.ButtonFlags, 0x0004, 2);   // 右
                    EmitButton(transport, e.ButtonFlags, 0x0010, 3);   // 中
                }
            };
            ri.KeyChanged += delegate(RawKeyEvent e)
            {
                kc++;
                log.WriteLine("KEY scancode=0x" + e.Scancode.ToString("X")
                    + (e.IsUp ? " UP" : " DOWN"));
            };

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
                int dw, dh;
                if (DeviceLauncher.QueryDisplay(out dw, out dh))
                {
                    _phoneW = dw; _phoneH = dh;
                    log.WriteLine("# 屏幕几何 " + dw + "x" + dh);
                }
                else
                {
                    _phoneW = 2136; _phoneH = 3200;   // 查询失败时的保守回退
                    log.WriteLine("# 屏幕几何查询失败，回退 " + _phoneW + "x" + _phoneH);
                }
                cursor = new CursorModel(_phoneW, _phoneH);
                _geometryChanged = true;   // 首次鼠标移动时消费：SetBounds + HOME 归零
                transport.Send(Protocol.EncodePing(1));
            };
            transport.Disconnected += delegate { log.WriteLine("# 设备已断开"); };
            transport.MessageReceived += delegate(byte type, byte[] payload)
            {
                if (type == Protocol.MsgPong)
                    log.WriteLine("# PONG seq=" + Protocol.GetU32(payload, 0));
            };

            System.Threading.Thread rotPoll = new System.Threading.Thread(delegate()
            {
                while (true)
                {
                    System.Threading.Thread.Sleep(2000);
                    if (!transport.IsConnected) continue;
                    int w, h;
                    if (!DeviceLauncher.QueryDisplay(out w, out h)) continue;   // 掉线时静默跳过，不刷日志
                    if (w != _phoneW || h != _phoneH)
                    {
                        _phoneW = w; _phoneH = h;
                        _geometryChanged = true;
                    }
                }
            });
            rotPoll.IsBackground = true;
            rotPoll.Start();

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
                transport.Stop();
                DeviceLauncher.Cleanup(devProc);
                log.Close();
            };

            Application.Run(host);
        }

        /// <summary>把 Windows 的 down/up 位对翻译成协议的单次按钮事件。</summary>
        static void EmitButton(Transport t, ushort flags, ushort downBit, byte btn)
        {
            ushort upBit = (ushort)(downBit << 1);
            if ((flags & downBit) != 0) t.Send(Protocol.EncodeButton(btn, 1));
            else if ((flags & upBit) != 0) t.Send(Protocol.EncodeButton(btn, 0));
        }
    }
}
