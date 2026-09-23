using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PcKvm
{
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
            host.Show();   // 必须真实显示，隐藏窗口无法持有前台

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

            // supp 必须声明在 log 之后、transport 装配块之前：Task 8 会在
            // transport.Disconnected 处理器与心跳定时器里调 supp.Release()，
            // C# 局部变量不能前向引用（Task 4 踩过）。它只依赖 hwnd 与 log。
            Suppressor supp = new Suppressor(hwnd, delegate(string s) { log.WriteLine(s); });

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
            // 目标机几何：手机挂在 DISPLAY1（1920,0,1920x1080）的右侧。
            // edgeX 约定 = 触发侧最外侧有效像素列：3840x1080 桌面上光标最远只能到 3839
            //（Windows 钳制），传 3840 会让入口条件永远不可达。
            EdgeTracker tracker = new EdgeTracker(
                edgeX: 3839, edgeTop: 0, edgeBottom: 1080,
                phoneW: 2136, phoneH: 3200, phoneRight: true);   // 占位初值，真值经 SetPhoneSize 灌入

            tracker.EnterTakeover += delegate(short px, short py)
            {
                // 安全不变量 4：未连接时绝不夺取前台/锁光标——断线后 tracker 已回 IDLE，
                // 再推边缘会重新进 TAKEOVER，而 Task 8 的心跳恢复在未连接时直接 return，
                // 救不了"前台被夺+光标被钳"的状态
                if (!transport.IsConnected)
                {
                    log.WriteLine("# 未连接设备，放弃这次跨越");
                    tracker.AbortTakeover();
                    return;
                }
                if (!supp.Engage())
                {
                    // 夺取失败就放弃这次跨越，保持 IDLE，绝不能让用户被困在锁死光标的状态里
                    tracker.AbortTakeover();
                    return;
                }
                host.SetStatus("TAKEOVER → 手机");
                log.WriteLine("# ENTER takeover at phone(" + px + "," + py + ")");
                transport.Send(Protocol.EncodeHome());
                cursor.Reset();
                cursor.SetPosition(px, py);
                transport.Send(Protocol.EncodeEnter(px, py));
            };
            tracker.LeaveTakeover += delegate
            {
                supp.Release();
                host.SetStatus("IDLE");
                log.WriteLine("# LEAVE takeover");
                transport.Send(Protocol.EncodeLeave());
            };
            supp.ForegroundLost += delegate { tracker.AbortTakeover(); host.SetStatus("IDLE"); };

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
                        // 用原始增量判定回程方向（用户意图）；钳制后的 sdx 在 x=0 处
                        // 对负增量恒为 0，会让"刚入屏就推回"永远无法离开
                        tracker.OnTakeoverMove(e.Dx, e.Dy, cursor.X, cursor.Y);
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
            // 掉线时必须把 tracker 从 TAKEOVER 里拉出来（Task 6 审查 Important）：
            // 只打日志会让 Current 卡在 TAKEOVER、cursor 保持非 null、Armed 不复位，
            // 而重连时装的是全新 CursorModel，此后所有鼠标移动都会误走接管分支。
            transport.Disconnected += delegate
            {
                log.WriteLine("# 设备已断开");
                if (tracker.Current == KvmState.Takeover)
                {
                    // 顺序要紧：AbortTakeover 与 Release 必须**同步**执行完（它们不碰控件），
                    // 只有最后那次 UI 触碰需要 marshal。
                    tracker.AbortTakeover();
                    supp.Release();
                    // 本处理器跑在 Transport 的 accept 线程上（Transport.cs 的 AcceptLoop），
                    // 直接改 Label.Text 是非法跨线程访问：不挂调试器时靠 SendMessage 侥幸不抛，
                    // 挂上调试器就 InvalidOperationException；UI 线程若被阻塞还会连带卡住
                    // accept 线程、拖慢重连。故只有这一句 marshal 回 UI 线程。
                    host.BeginInvoke((MethodInvoker)delegate { host.SetStatus("IDLE"); });
                }
            };
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

            // 前台守卫：前台被抢走（UAC 安全桌面、锁屏等）时立即放弃抑制
            Timer guard = new Timer();
            guard.Interval = 250;
            guard.Tick += delegate { supp.CheckForeground(); };
            guard.Start();

            // ---- Task 8 安全网：心跳失联强制解锁 + Ctrl+Alt+Esc 逃逸键 ----
            // 必须是 WinForms Timer（UI 线程）：Abort 路径会碰 host/tracker/supp，
            // 换 System.Threading.Timer 会引入跨线程调用（Task 7 审查遗留的隐患点）。
            uint pingSeq = 0;
            long lastPongTicks = DateTime.UtcNow.Ticks;
            transport.MessageReceived += delegate(byte type, byte[] payload)
            {
                if (type == Protocol.MsgPong)
                    lastPongTicks = DateTime.UtcNow.Ticks;
            };

            Timer heartbeat = new Timer();
            heartbeat.Interval = 1000;
            heartbeat.Tick += delegate
            {
                // 先做安全网：处于 TAKEOVER 时，「没连接」与「PONG 超时」都要立刻解除抑制。
                // 原写法把 `if (!transport.IsConnected) return;` 放在最前，会在掉线后让
                // 安全网整体短路——用户此时再推到边缘会重新进入 TAKEOVER 并夺取前台，
                // 而没有任何东西能把他救出来（见 Task 7 安全不变量 4，第二轮跨任务扫描发现）。
                if (tracker.Current == KvmState.Takeover)
                {
                    double age = (DateTime.UtcNow - new DateTime(lastPongTicks)).TotalSeconds;
                    if (!transport.IsConnected || age > 2.0)
                    {
                        log.WriteLine("# 心跳失联（" + age.ToString("F1") + "s, connected="
                                      + transport.IsConnected + "），强制解除抑制");
                        // 与逃逸键同理：放弃路径要通知设备侧清 buttonsDown/按键槽位。
                        // 若连接已断，Send 是安全 no-op（Transport.Send 在 _stream 为 null 时直接返回）。
                        transport.Send(Protocol.EncodeLeave());
                        tracker.AbortTakeover();
                        supp.Release();
                        host.SetStatus("IDLE");
                    }
                }

                if (!transport.IsConnected) return;
                pingSeq++;
                transport.Send(Protocol.EncodePing(pingSeq));
            };
            heartbeat.Start();

            host.Escape += delegate
            {
                log.WriteLine("# 逃逸键触发");
                // 放弃路径也必须通知设备侧清状态：接管期间若按着鼠标键/修饰键再逃逸，
                // 不补发 LEAVE 会让手机侧 buttonsDown 与按键槽位永久残留（leave 才会清）。
                // 设备侧处理 MSG_LEAVE 时会 buttonsDown=0 并 KeyState.releaseAll。
                transport.Send(Protocol.EncodeLeave());
                tracker.AbortTakeover();
                supp.Release();
                host.SetStatus("IDLE");
            };

            host.FormClosing += delegate { supp.Release(); };
            Application.ApplicationExit += delegate
            {
                supp.Release();
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
