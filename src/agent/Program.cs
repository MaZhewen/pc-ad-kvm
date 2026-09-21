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
            ri.MouseMoved += delegate(RawMouseEvent e)
            {
                mc++;
                if (mc % 50 == 0)   // 降频，避免日志爆炸
                    log.WriteLine("MOUSE dx=" + e.Dx + " dy=" + e.Dy
                        + " btn=0x" + e.ButtonFlags.ToString("X4") + " wheel=" + e.WheelDelta);

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
                transport.Send(Protocol.EncodePing(1));
            };
            transport.Disconnected += delegate { log.WriteLine("# 设备已断开"); };
            transport.MessageReceived += delegate(byte type, byte[] payload)
            {
                if (type == Protocol.MsgPong)
                    log.WriteLine("# PONG seq=" + Protocol.GetU32(payload, 0));
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
