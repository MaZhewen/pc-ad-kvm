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
        static volatile int _phoneRotation = 0;
        static volatile bool _geometryChanged = false;

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT p);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X; public int Y; }

        const int SM_XVIRTUALSCREEN = 76;
        const int SM_YVIRTUALSCREEN = 77;
        const int SM_CXVIRTUALSCREEN = 78;
        const int SM_CYVIRTUALSCREEN = 79;

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int nIndex);

        const int VK_NUMLOCK = 0x90;

        [DllImport("user32.dll")]
        static extern short GetKeyState(int vKey);

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();

            MessageHost host = new MessageHost();
            IntPtr hwnd = host.Handle;   // 触发句柄创建
            host.Show();
            host.Hide();   // 空闲时不遮挡 PC；接管前再显示以持有前台

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
                delegate(bool on) { host.SetCursorHidden(on); },
                delegate { host.ShowForCapture(); }, delegate { host.HideAfterCapture(); });

            // transport 需在 MouseMoved 处理器之前就绪（C# 局部变量在声明点之后才可见）
            Transport transport = new Transport(DeviceLauncher.Port);
            if (!transport.Start())
            {
                log.WriteLine("# TCP 监听失败：" + transport.StartError);
                log.Close();
                MessageBox.Show("TCP 端口 " + DeviceLauncher.Port + " 监听失败：\n"
                    + transport.StartError.Message + "\n详细错误见 pc-kvm.log。",
                    "PC-KVM", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            DeviceLauncher.LocalPort = transport.ListeningPort;
            PointerSession pointer = new PointerSession(transport.Send);
            transport.MessageReceived += pointer.Receive;
            log.WriteLine("# TCP 监听 127.0.0.1:" + transport.ListeningPort
                + (transport.ListeningPort != DeviceLauncher.Port ? "（默认端口被占用，已自动切换）" : ""));

            // 阶段骨架：只把事件落到日志，后续任务接管这两个事件
            int mc = 0, kc = 0;
            // 修饰键位图（HID 键盘报告的 modifier 字节）。必须声明在 KeyChanged 订阅之前：
            // 处理器会捕获并修改它，而 C# 局部变量不支持前向引用（Task 4 与 Task 7 都栽过）
            byte modifiers = 0;
            CursorModel cursor = null;

            // 速度缩放器（含小数余量累积）。必须声明在 ri.MouseMoved 订阅之前：
            // 处理器会捕获它，而 C# 局部变量不支持前向引用（Task 4/7 都栽过）
            MouseScaler scaler = new MouseScaler(cfg.MouseSensitivity);
            // 入口边**运行时**从真实虚拟桌面推导，不写魔数：写死 3839 时，
            // 换一套显示器布局会让入口条件永远不可达——正是 Task 6 审查栽过的静默 bug。
            // edgeX 约定 = 触发侧最外侧"有效像素列"：右侧取 X+W-1（Windows 把光标钳在最后一列内），
            // 左侧取 X。edgeBottom 是**开区间**（EdgeTracker.cs 判定为 cursorY >= _edgeBottom 时拒绝），
            // 故直接传桌面高度对应的 Y+H。
            int screenX = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int screenY = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int screenW = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int screenH = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            // 尺寸是判成败的判据：它必须为正。原点**允许为负**（左/上方挂显示器时就是负的），
            // 所以原点不能拿 <= 0 当"失败"——只有尺寸坏了才整体回退，且回退时原点必须归零。
            if (screenW <= 0 || screenH <= 0)
            {
                screenX = 0; screenY = 0; screenW = 3840; screenH = 1080;
            }
            log.WriteLine("# 桌面几何 x=" + screenX + " y=" + screenY + " "
                          + screenW + "x" + screenH
                          + "，手机在" + (cfg.PhoneOnLeft ? "左" : "右") + "侧");

            EdgeTracker tracker = new EdgeTracker(
                edgeX: cfg.PhoneOnLeft ? screenX : screenX + screenW - 1,
                edgeTop: screenY, edgeBottom: screenY + screenH,
                phoneW: 2136, phoneH: 3200, phoneRight: !cfg.PhoneOnLeft);

            bool resumeAfterRotation = false;
            Timer rotationTimeout = new Timer();
            rotationTimeout.Interval = 8000;
            rotationTimeout.Tick += delegate
            {
                rotationTimeout.Stop();
                if (!resumeAfterRotation) return;
                resumeAfterRotation = false;
                if (tracker.Current != KvmState.Takeover) return;
                log.WriteLine("# 旋转后指针未就绪，安全退出接管");
                transport.SendControl(Protocol.EncodeLeave());
                tracker.AbortTakeover();
                supp.Release();
                host.SetStatus("IDLE");
            };

            // 后台守护集中到 Watchers（Ruling 26 的抽取，见该类头注释的线程纪律）。
            // ⚠️ 必须声明在 TrayUi 的 Install() 之前（它会调 watchers.Stop()），
            // 而 C# 局部变量不能前向引用（Task 4/7/8 都栽过这一类）。
            // 但 Start() 要等最后才调——过早启动重连监督会抢在首次建隧道之前动手。
            Watchers watchers = new Watchers(transport, tracker, supp, host,
                delegate(string s) { log.WriteLine(s); });
            watchers.GeometryQueried += delegate(int w, int h, int rotation)
            {
                if (w == _phoneW && h == _phoneH && rotation == _phoneRotation) return;
                host.BeginInvoke((MethodInvoker)delegate
                {
                    if (w == _phoneW && h == _phoneH && rotation == _phoneRotation) return;
                    bool keepTakeover = tracker.Current == KvmState.Takeover && supp.IsEngaged;
                    int oldW = _phoneW, oldH = _phoneH, oldRotation = _phoneRotation;
                    int oldX = cursor == null ? 0 : cursor.X;
                    int oldY = cursor == null ? 0 : cursor.Y;
                    _phoneW = w; _phoneH = h; _phoneRotation = rotation;
                    // Geometry is a state barrier. Clear stale motion before the
                    // new epoch so rotation cannot replay old coordinates/buttons.
                    transport.SendControl(Protocol.EncodeLeave());
                    if (tracker.Current == KvmState.Takeover && !keepTakeover)
                    {
                        tracker.AbortTakeover();
                        supp.Release();
                        host.SetStatus("IDLE");
                    }
                    if (cursor == null) cursor = new CursorModel(w, h);
                    cursor.SetBounds(w, h);
                    tracker.SetPhoneSize(w, h);
                    if (keepTakeover)
                    {
                        int mappedX, mappedY;
                        RotationMap.Map(oldX, oldY, oldW, oldH, oldRotation,
                                        w, h, rotation, out mappedX, out mappedY);
                        cursor.SetPosition(mappedX, mappedY);
                        tracker.RebaseTakeover(mappedX);
                        resumeAfterRotation = true;
                        rotationTimeout.Stop();
                        rotationTimeout.Start();
                        host.SetStatus("TAKEOVER → 手机（旋转中）");
                    }
                    else
                    {
                        cursor.Reset();
                        resumeAfterRotation = false;
                        rotationTimeout.Stop();
                    }
                    pointer.Configure(w, h, rotation);
                    _geometryChanged = false;
                    log.WriteLine("# 几何已应用 " + w + "x" + h + " rotation=" + rotation);
                });
            };

            tracker.EnterTakeover += delegate(short px, short py)
            {
                // 安全不变量 4：未连接时绝不夺取前台/锁光标——断线后 tracker 已回 IDLE，
                // 再推边缘会重新进 TAKEOVER，而 Task 8 的心跳恢复在未连接时直接 return，
                // 救不了"前台被夺+光标被钳"的状态
                if (!transport.IsConnected || !pointer.Ready)
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
                cursor.Reset();
                cursor.SetPosition(px, py);
                scaler.Reset();   // 归零的同时清掉小数余量，避免带着跨越前的零头
                pointer.Begin(px, py);
            };
            tracker.LeaveTakeover += delegate
            {
                supp.Release();
                host.SetStatus("IDLE");
                // 回程外推累积量：正常回程应 >= 阈值(40)；若日志里出现很小的值，
                // 说明仍有未被阈值挡住的回程路径
                log.WriteLine("# LEAVE takeover（回程外推累积 " + tracker.BackPush + "）");
                transport.SendControl(Protocol.EncodeLeave());
            };
            supp.ForegroundLost += delegate
            {
                // 与逃逸键/心跳失联同理：放弃路径必须补发 LEAVE，清手机侧 buttonsDown
                // 与按键槽位（若连接已断，Send 是安全 no-op）
                transport.SendControl(Protocol.EncodeLeave());
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
                        transport.SendControl(Protocol.EncodeLeave());
                        supp.Release();
                        tracker.AbortTakeover();
                        cursor.SetBounds(_phoneW, _phoneH);
                        tracker.SetPhoneSize(_phoneW, _phoneH);
                        log.WriteLine("# 几何已应用 " + _phoneW + "x" + _phoneH);
                        pointer.Configure(_phoneW, _phoneH, _phoneRotation);
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
                        // 缩放器输出的已是"应发送"的整数增量，仍要过 CursorModel 的钳制
                        short sdx = cursor.NextDx(scaler.ApplyX(e.Dx));
                        short sdy = cursor.NextDy(scaler.ApplyY(e.Dy));
                        if (sdx != 0 || sdy != 0)
                            pointer.Move(cursor.X, cursor.Y);
                        // 用原始增量判定回程方向（用户意图）；钳制后的 sdx 在 x=0 处
                        // 对负增量恒为 0，会让"刚入屏就推回"永远无法离开
                        tracker.OnTakeoverMove(pointer.ConfirmedX(cursor.X) ? e.Dx : 0,
                            e.Dy, cursor.X, cursor.Y);
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
                // 末尾的 numlock 位决定小键盘行为，而它来自"读操作系统"：读到过期值时症状是
                // "怎么切 numlock 都不出数字"，本行是唯一能一眼分辨的仪器（2026-09-23 教训）。
                log.WriteLine("KEY scancode=0x" + e.Scancode.ToString("X")
                    + (e.IsUp ? " UP" : " DOWN")
                    + (e.IsE0 ? "" : " numlock=" + (GetKeyState(VK_NUMLOCK) & 1)));

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

                // NumLock 关时把数字键盘普通码翻成 E0 形态（#5）。上面日志已经打过**原始**
                // scancode——日志必须记录真实观测，不能记录翻译后的值。
                int sendSc = e.Scancode;
                if (!e.IsE0)
                {
                    if ((GetKeyState(VK_NUMLOCK) & 1) == 0)
                    {
                        int nav = KeyMap.NumpadNavE0Scancode(sendSc & 0xFF);
                        if (nav == 0) return;        // 小键盘 5 在 NumLock 关时 = Clear，丢弃
                        if (nav > 0) { sendSc = nav; log.WriteLine("# 小键盘翻成 0x" + nav.ToString("X") + "（NumLock 关）"); }
                    }
                }
                transport.Send(Protocol.EncodeKey((ushort)sendSc, (byte)(e.IsUp ? 0 : 1), modifiers));
            };

            string jar = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pckvm.jar");
            if (!DeviceLauncher.Prepare(jar))
            {
                MessageBox.Show("adb 隧道/推送失败。确认手机已连接且 USB 调试已开。",
                    "PC-KVM", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            // 设备侧进程的所有权交给 Watchers：重连会换进程，退出时要 Kill 它。
            // 这里只负责"第一次拉起来"。
            watchers.AttachConfig(cfg);

            transport.Connected += delegate
            {
                pointer.Reset();
                log.WriteLine("# 设备已连接");
                int dw, dh, rotation;
                if (DeviceLauncher.QueryDisplay(out dw, out dh, out rotation))
                {
                    _phoneW = dw; _phoneH = dh; _phoneRotation = rotation;
                    log.WriteLine("# 屏幕几何 " + dw + "x" + dh + " rotation=" + rotation);
                }
                else
                {
                    _phoneW = 2136; _phoneH = 3200; _phoneRotation = 0;   // 查询失败时的保守回退
                    log.WriteLine("# 屏幕几何查询失败，回退 " + _phoneW + "x" + _phoneH);
                }
                cursor = new CursorModel(_phoneW, _phoneH);
                pointer.Configure(_phoneW, _phoneH, _phoneRotation);
                _geometryChanged = false;
                transport.Send(Protocol.EncodePing(1));
            };
            // 掉线时必须把 tracker 从 TAKEOVER 里拉出来（Task 6 审查 Important）：
            // 只打日志会让 Current 卡在 TAKEOVER、cursor 保持非 null、Armed 不复位，
            // 而重连时装的是全新 CursorModel，此后所有鼠标移动都会误走接管分支。
            transport.Disconnected += delegate
            {
                pointer.Reset();
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
                else if (type == Protocol.MsgPointerReady)
                {
                    log.WriteLine("# 绝对指针已就绪 epoch=" + Protocol.GetU32(payload, 0));
                    host.BeginInvoke((MethodInvoker)delegate
                    {
                        if (!resumeAfterRotation || !pointer.Ready) return;
                        resumeAfterRotation = false;
                        rotationTimeout.Stop();
                        if (tracker.Current != KvmState.Takeover || !supp.IsEngaged
                            || !transport.IsConnected) return;
                        pointer.Begin(cursor.X, cursor.Y);
                        host.SetStatus("TAKEOVER → 手机");
                        log.WriteLine("# 旋转后接管已恢复 phone(" + cursor.X + "," + cursor.Y + ")");
                    });
                }
            };

            // 托盘与生命周期集中到 TrayUi（Ruling 33）。必须在 Application.Run 之前 Install。
            TrayUi trayUi = new TrayUi(host, supp, watchers, transport, log, cfg);
            trayUi.Install();

            // 应用设置由组合根订阅处理（TrayUi 只弹对话框+写盘，见其 SettingsApplied 注释）。
            trayUi.SettingsApplied += delegate(Config c)
            {
                scaler.SetSensitivity(c.MouseSensitivity);   // 立即生效，不必重启
                // 正在接管就先干净地退出来（补发 LEAVE、解锁光标、还前台），
                // 否则换边会让 tracker 停在 TAKEOVER 却对着新的边界判定
                if (tracker.Current == KvmState.Takeover)
                {
                        transport.SendControl(Protocol.EncodeLeave());
                    tracker.AbortTakeover();
                    supp.Release();
                    host.SetStatus("IDLE");
                }
                tracker.SetEdge(c.PhoneOnLeft ? screenX : screenX + screenW - 1,
                                screenY, screenY + screenH, !c.PhoneOnLeft);
                log.WriteLine("# 设置已应用：手机在" + (c.PhoneOnLeft ? "左" : "右")
                              + "侧（edgeX=" + (c.PhoneOnLeft ? screenX : screenX + screenW - 1) + "）"
                              + " 速度=" + c.MouseSensitivity.ToString("F2")
                              + " 强杀adb=" + c.AllowKillAdb);
            };

            // Subscribe all connection handlers before a fast local injector can connect.
            if (!watchers.StartDevice())
                log.WriteLine("# 首次拉起注入器失败（重连监督会继续尝试）");
            watchers.Start();
            host.FormClosed += delegate { Application.ExitThread(); };
            Application.Run();
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
