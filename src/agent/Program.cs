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

            // cfg 必须声明得足够早：后续的 scaler（Task 4）、EdgeTracker 构造（Task 5，
            // 读 cfg.PhoneOnLeft）与 TrayUi 构造都要用它——C# 局部变量不能前向引用。
            Config cfg = Config.Load(delegate(string s) { log.WriteLine(s); });

            // supp 必须声明在 log 之后、transport 装配块之前：Task 8 会在
            // transport.Disconnected 处理器与心跳定时器里调 supp.Release()，
            // C# 局部变量不能前向引用（Task 4 踩过）。它只依赖 hwnd 与 log。
            Suppressor supp = new Suppressor(hwnd, delegate(string s) { log.WriteLine(s); },
                delegate(bool on) { host.SetCursorHidden(on); });

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
            // 修饰键位图（HID 键盘报告的 modifier 字节）。必须声明在 KeyChanged 订阅之前：
            // 处理器会捕获并修改它，而 C# 局部变量不支持前向引用（Task 4 与 Task 7 都栽过）
            byte modifiers = 0;
            CursorModel cursor = null;

            // 声明必须早于 ri.MouseMoved 订阅（C# 局部变量不能前向引用——Task 4 踩过）
            double sensitivity = 1.0;
            // 目标机几何：手机挂在 DISPLAY1（1920,0,1920x1080）的右侧。
            // edgeX 约定 = 触发侧最外侧有效像素列：3840x1080 桌面上光标最远只能到 3839
            //（Windows 钳制），传 3840 会让入口条件永远不可达。
            EdgeTracker tracker = new EdgeTracker(
                edgeX: 3839, edgeTop: 0, edgeBottom: 1080,
                phoneW: 2136, phoneH: 3200, phoneRight: true);   // 占位初值，真值经 SetPhoneSize 灌入

            // 后台守护集中到 Watchers（Ruling 26 的抽取，见该类头注释的线程纪律）。
            // ⚠️ 必须声明在 TrayUi 的 Install() 之前（它会调 watchers.Stop()），
            // 而 C# 局部变量不能前向引用（Task 4/7/8 都栽过这一类）。
            // 但 Start() 要等最后才调——过早启动重连监督会抢在首次建隧道之前动手。
            Watchers watchers = new Watchers(transport, tracker, supp, host,
                delegate(string s) { log.WriteLine(s); });
            watchers.GeometryQueried += delegate(int w, int h)
            {
                if (w == _phoneW && h == _phoneH) return;
                _phoneW = w; _phoneH = h;
                _geometryChanged = true;
            };

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
                // 回程外推累积量：正常回程应 >= 阈值(40)；若日志里出现很小的值，
                // 说明仍有未被阈值挡住的回程路径
                log.WriteLine("# LEAVE takeover（回程外推累积 " + tracker.BackPush + "）");
                transport.Send(Protocol.EncodeLeave());
            };
            supp.ForegroundLost += delegate
            {
                // 与逃逸键/心跳失联同理：放弃路径必须补发 LEAVE，清手机侧 buttonsDown
                // 与按键槽位（若连接已断，Send 是安全 no-op）
                transport.Send(Protocol.EncodeLeave());
                tracker.AbortTakeover();
                host.SetStatus("IDLE");
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
                        // 用原始增量判定回程方向（用户意图）；钳制后的 sdx 在 x=0 处
                        // 对负增量恒为 0，会让"刚入屏就推回"永远无法离开
                        tracker.OnTakeoverMove(e.Dx, e.Dy, cursor.X, cursor.Y);
                    }
                }

                // 滚轮与鼠标键只在接管期转发：IDLE 态用户是在操作 PC，
                // 此时转发会在手机光标停留处产生误点击/误滚动（既有缺陷，Ruling 27）
                if (cursor != null && tracker.Current == KvmState.Takeover)
                {
                    // 接管期的按钮事件**不抽样**记录：MOUSE 行是每 50 事件一条（2% 采样），
                    // 而一次点击只有 2 个事件，几乎必然被漏掉——本项目正因此无法证伪
                    // "点击导致前台被抢"这个假设。按钮事件本身很稀疏，全记不构成负担。
                    if ((e.ButtonFlags & ~RawInput.RI_MOUSE_WHEEL) != 0)
                        log.WriteLine("# 接管中按钮 flags=0x" + e.ButtonFlags.ToString("X4")
                            + " phone(" + cursor.X + "," + cursor.Y + ")");

                    short wheel = (short)(e.WheelDelta / 120);   // Windows 一格 = 120，HID 一格 = 1
                    if (wheel != 0) transport.Send(Protocol.EncodeScroll((short)0, wheel));

                    if (e.ButtonFlags != 0)
                    {
                        EmitButton(transport, e.ButtonFlags, 0x0001, 1);   // 左
                        EmitButton(transport, e.ButtonFlags, 0x0004, 2);   // 右
                        EmitButton(transport, e.ButtonFlags, 0x0010, 3);   // 中
                    }
                }
            };
            ri.KeyChanged += delegate(RawKeyEvent e)
            {
                kc++;
                log.WriteLine("KEY scancode=0x" + e.Scancode.ToString("X")
                    + (e.IsUp ? " UP" : " DOWN"));

                byte bit = KeyMap.ModifierBit(e.Scancode, e.IsE0);
                if (bit != 0)
                {
                    if (e.IsUp) modifiers &= (byte)~bit; else modifiers |= bit;
                    // 修饰键变化也要发一条报告，否则手机侧修饰态不更新
                    if (tracker.Current == KvmState.Takeover)
                        transport.Send(Protocol.EncodeKey((ushort)e.Scancode, (byte)(e.IsUp ? 0 : 1), modifiers));
                    return;
                }

                if (tracker.Current != KvmState.Takeover) return;   // IDLE 态不转发，PC 正常用
                transport.Send(Protocol.EncodeKey((ushort)e.Scancode, (byte)(e.IsUp ? 0 : 1), modifiers));
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

            // 托盘与生命周期集中到 TrayUi（Ruling 33）。必须在 Application.Run 之前 Install。
            TrayUi trayUi = new TrayUi(host, supp, watchers, transport, log, devProc, cfg);
            trayUi.Install();

            // 应用设置由组合根订阅处理（TrayUi 只弹对话框+写盘，见其 SettingsApplied 注释）。
            // 本任务先只记账；速度在 Task 4 接（MouseScaler）、跨越边在 Task 5 接（SetEdge）。
            trayUi.SettingsApplied += delegate(Config c)
            {
                log.WriteLine("# 设置已应用：速度=" + c.MouseSensitivity.ToString("F2")
                              + " 手机在" + (c.PhoneOnLeft ? "左" : "右") + "侧"
                              + " 强杀adb=" + c.AllowKillAdb);
            };

            watchers.Start();
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
