using System;
using System.Windows.Forms;

namespace PcKvm
{
    /// <summary>
    /// Program.cs 的后台守护集中地（Ruling 26 承诺的抽取）。
    /// 理由不是行数好看：Program.cs 只剩 1 行预算，而"后台存活性检查"与"装配"本来就是两回事，
    /// 且这一族正是审查者点名的风险区（adb 进程 churn）。
    /// 职责：后台存活性检查（几何轮询/前台守卫/心跳），以及 UI 触发的放弃路径（逃逸键）。
    ///
    /// 线程纪律（不可违反）：
    ///  - guard 与 heartbeat **必须是 WinForms Timer**（UI 线程 tick）：它们的路径会碰
    ///    host/tracker/supp。换 System.Threading.Timer 会引入跨线程调用（Task 7 审查遗留隐患）。
    ///  - 几何轮询与重连监督是**后台线程**（重连监督：任务 8 落地），它们**绝不直接碰控件或写日志**，
    ///    一律经 Report()/LogFromWorker()（任务 8 落地，内部走 _host.BeginInvoke）回到 UI 线程。
    /// </summary>
    // 注：非 public——MessageHost 是 internal，public 构造器会触发 CS0051；
    // 本类只在同一程序集内被 Program.cs 消费。
    class Watchers
    {
        readonly Transport _transport;
        readonly EdgeTracker _tracker;
        readonly Suppressor _supp;
        readonly MessageHost _host;
        readonly Action<string> _log;

        // 跨线程字段：transport 读线程写、UI 线程心跳读——与 _stop/_devProc 同一纪律。
        // 注：C# 不允许 volatile long（CS0677），故用 Interlocked 读/写达到同一语义
        //（读侧带栅栏免陈旧，写侧在 32 位进程下也免撕裂；陈旧方向只会让 age 偏大，有界自纠）。
        long _lastPongTicks = DateTime.UtcNow.Ticks;
        uint _pingSeq;
        volatile bool _stop;            // 退出时置位，让后台线程自己收工
        System.Threading.Thread _geoPoll;
        Timer _guard;
        Timer _heartbeat;

        // ---- 断联自愈（阶段三 #4，任务 8）----
        Config _cfg;
        // volatile：跨线程字段——重连监督线程写（TryRebuildLink/StartDevice），
        // UI 线程读（DeviceProcess 属性、TrayUi 退出清理、Stop() 的 Kill）；_stop 同理早已是 volatile。
        volatile System.Diagnostics.Process _devProc;
        System.Threading.Thread _reconnect;
        int _failStreak;
        int _level;                 // 已升级到的档位：0 = 只做①，1 = 做过温和，2 = 做过激进
        // 唯一的日志去重键 = 【状态种类】，不能是含递增计数的完整消息——
        // 否则每轮都会写一行（掉线一小时约 720 行）。只在 UI 线程读写
        // （ReportState 的 BeginInvoke 委托体内）。
        string _lastLoggedState = "";

        /// <summary>几何轮询每次**成功**查到都抛（含未变化值）。
        /// 消费方自行比较后决定是否应用——这样 Watchers 不持有第二份几何状态，
        /// 不会与 Program.cs 的副本漂移（那正是 Task 5B 踩过的"窄撕裂窗口"同类问题）。</summary>
        public event Action<int, int> GeometryQueried;

        public Watchers(Transport transport, EdgeTracker tracker, Suppressor supp,
                        MessageHost host, Action<string> log)
        {
            _transport = transport;
            _tracker = tracker;
            _supp = supp;
            _host = host;
            _log = log;

            // PONG 时间戳由 Watchers 自持。Program.cs 里那个 MessageReceived 订阅只负责
            // 打 "# PONG seq=" 一行，两者互不重复（Task 8 审查要求"不要新增订阅造成重复日志"）。
            _transport.MessageReceived += delegate(byte type, byte[] payload)
            {
                if (type == Protocol.MsgPong)
                    System.Threading.Interlocked.Exchange(ref _lastPongTicks, DateTime.UtcNow.Ticks);
            };

            // 逃逸键 Ctrl+Alt+Esc（Task 8 的安全网之一）。放在 Watchers 而不是 TrayUi：
            // 它要 tracker/supp/transport 三样，而 Watchers 的构造器本来就有；
            // 更要紧的是它做的四件事与上面 HeartbeatTick 里那条安全网**逐字相同**，
            // 放隔壁才能让"放弃序列"只有一处需要维护。
            _host.Escape += delegate
            {
                _log("# 逃逸键触发");
                // 放弃路径也必须通知设备侧清状态：接管期间若按着鼠标键/修饰键再逃逸，
                // 不补发 LEAVE 会让手机侧 buttonsDown 与按键槽位永久残留（leave 才会清）。
                // 设备侧处理 MSG_LEAVE 时会 buttonsDown=0 并 KeyState.releaseAll。
                _transport.Send(Protocol.EncodeLeave());
                _tracker.AbortTakeover();
                _supp.Release();
                _host.SetStatus("IDLE");
            };
        }

        /// <summary>设备侧进程句柄。所有权归 Watchers：重连会换进程，退出时要 Kill 它。</summary>
        public System.Diagnostics.Process DeviceProcess { get { return _devProc; } }

        /// <summary>接入配置（ReconnectSeconds 每轮重读、AllowKillAdb 决定要不要上第③级）。</summary>
        public void AttachConfig(Config cfg) { _cfg = cfg; }

        /// <summary>拉起设备侧进程（首次启动用）。</summary>
        public bool StartDevice()
        {
            _devProc = DeviceLauncher.Start();
            return _devProc != null;
        }

        public void Start()
        {
            _geoPoll = new System.Threading.Thread(delegate()
            {
                while (!_stop)
                {
                    System.Threading.Thread.Sleep(2000);
                    if (_stop) return;
                    if (!_transport.IsConnected) continue;   // 掉线时静默跳过，不刷日志
                    int w, h;
                    if (!DeviceLauncher.QueryDisplay(out w, out h)) continue;
                    Action<int, int> g = GeometryQueried;
                    if (g != null) g(w, h);
                }
            });
            _geoPoll.IsBackground = true;
            _geoPoll.Start();

            // 前台守卫：前台被抢走（UAC 安全桌面、锁屏、点击激活了别的窗口）时立即放弃抑制
            _guard = new Timer();
            _guard.Interval = 250;
            _guard.Tick += delegate { _supp.CheckForeground(); };
            _guard.Start();

            _heartbeat = new Timer();
            _heartbeat.Interval = 1000;
            _heartbeat.Tick += delegate { HeartbeatTick(); };
            _heartbeat.Start();

            // 重连监督（阶段三 #4）：后台线程，线程纪律见类头注释——绝不直接写日志/碰控件。
            // Start() 是 Program.cs 在 Application.Run 之前的最后一次调用，
            // 此刻首次建隧道+拉注入器已由 Program.cs 完成，监督线程不会抢跑。
            _reconnect = new System.Threading.Thread(ReconnectLoop);
            _reconnect.IsBackground = true;
            _reconnect.Start();
        }

        /// <summary>退出前调用（由 TrayUi 的 ApplicationExit 处理器调用）。
        /// 必须在 log.Close() 之前——否则后台线程会写进一个已关闭的 StreamWriter。
        /// 不 Join 线程：几何轮询是 IsBackground，最多 2 秒后自己看到 _stop 退出；
        /// 重连监督睡成 100ms 小段，已在飞的至多一轮重建会在 _stop 守卫处提前收手。
        /// 顺手 Kill 设备侧进程（所有权在本类，重连会换进程）。</summary>
        public void Stop()
        {
            _stop = true;
            if (_guard != null) _guard.Stop();
            if (_heartbeat != null) _heartbeat.Stop();
            try { if (_devProc != null && !_devProc.HasExited) _devProc.Kill(); }
            catch (System.Exception) { }
        }

        void HeartbeatTick()
        {
            // 先做安全网：处于 TAKEOVER 时，「没连接」与「PONG 超时」都要立刻解除抑制。
            // 原写法把 `if (!IsConnected) return;` 放在最前，会在掉线后让安全网整体短路——
            // 用户此时再推到边缘会重新进入 TAKEOVER 并夺取前台，而没有东西能救他
            //（Task 7 安全不变量 4 / Ruling 18b，第二轮跨任务扫描发现）。
            if (_tracker.Current == KvmState.Takeover)
            {
                double age = (DateTime.UtcNow - new DateTime(
                    System.Threading.Interlocked.Read(ref _lastPongTicks))).TotalSeconds;
                if (!_transport.IsConnected || age > 2.0)
                {
                    _log("# 心跳失联（" + age.ToString("F1") + "s, connected="
                         + _transport.IsConnected + "），强制解除抑制");
                    // 放弃路径要通知设备侧清 buttonsDown/按键槽位（Ruling 25）；连接已断时 Send 是安全 no-op
                    _transport.Send(Protocol.EncodeLeave());
                    _tracker.AbortTakeover();
                    _supp.Release();
                    _host.SetStatus("IDLE");
                }
            }

            if (!_transport.IsConnected) return;
            _pingSeq++;
            _transport.Send(Protocol.EncodePing(_pingSeq));
        }

        /// <summary>
        /// 断联自愈（阶段三 #4）。分级升级：
        ///   ① 重建隧道 + 重启注入器（每次未连接都做）
        ///   ② 连续 3 个周期仍不通 → adb kill-server + start-server（温和）
        ///   ③ 再连续 3 个周期仍不通 且 AllowKillAdb → taskkill 所有 adb + start-server（激进）
        /// "不通"的判据统一是：**该周期结束时 transport.IsConnected 仍为 false**。
        ///
        /// ⚠️ 覆盖边界（如实写在这里，别让后人以为它能修好一切）：
        /// 2026-09-23 现场实测到一次**设备级**掉线——adb devices 全空、Windows 侧 ADB Interface
        /// 状态 OK 却打不开、kill-server + start-server 已实测无效、且当时只有 1 个 adb 进程
        /// （故不是 ledger 记的"多 adb 抢设备"）。本级联能覆盖"隧道断/注入器死"，
        /// 不保证覆盖"adb 整个看不见设备"——那仍需人看一眼手机。
        /// </summary>
        void ReconnectLoop()
        {
            while (!_stop)
            {
                int secs = 5;
                if (_cfg != null && _cfg.ReconnectSeconds >= Config.ReconnectSecondsMin)
                    secs = _cfg.ReconnectSeconds;
                // 拆成 100ms 小睡，让 Stop() 能及时生效（最长等一个周期）
                for (int i = 0; i < secs * 10 && !_stop; i++)
                    System.Threading.Thread.Sleep(100);
                if (_stop) return;

                if (_transport.IsConnected)
                {
                    if (_failStreak != 0 || _level != 0)
                    {
                        _failStreak = 0; _level = 0;
                        Report("已连接");
                    }
                    continue;
                }

                _failStreak++;

                // ①：重建隧道 + 重启注入器。幂等——重启前先 Kill 旧的，
                // 否则设备侧会堆多个 app_process。
                TryRebuildLink();

                if (_transport.IsConnected)
                {
                    _failStreak = 0; _level = 0;
                    Report("已连接");
                    continue;
                }

                // Stop() 可能落在上面几步（含 TryRebuildLink 里的 adb 调用）执行期间：
                // 退出流程已在收尾，绝不再升级到 ②/③ 去 kill-server/强杀 adb。
                if (_stop) return;

                // ②：连续 3 个周期仍不通 → 温和恢复（只此一次）
                if (_failStreak >= 3 && _level < 1)
                {
                    _level = 1;
                    Report("adb server 重启中…");
                    DeviceLauncher.RestartAdbServer();
                    continue;
                }

                // ③：再 3 个周期仍不通 且 用户开了开关 → 激进恢复（只此一次）
                if (_failStreak >= 6 && _level < 2)
                {
                    _level = 2;
                    if (_cfg != null && _cfg.AllowKillAdb)
                    {
                        Report("强杀 adb 进程中…");
                        DeviceLauncher.KillAllAdb();
                    }
                    continue;
                }

                string why = DeviceLauncher.DeviceVisible() ? "隧道/注入器未就绪" : "adb 看不到设备";
                ReportWaiting(why, _failStreak);
            }
        }

        bool TryRebuildLink()
        {
            // 退出中绝不再建隧道/推 jar/起新进程——否则退出清理（rm jar + 拆 reverse）之后
            // 又留下新的反向隧道与注入器，"退出干净"就间歇性失效（本特征恰好在"掉线时退出"时触发）。
            if (_stop) return false;
            try
            {
                if (_devProc != null && !_devProc.HasExited) _devProc.Kill();
            }
            catch (System.Exception) { }
            _devProc = null;

            string jar = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pckvm.jar");
            if (!DeviceLauncher.EnsureTunnel()) return false;
            // EnsureTunnel 的 adb 调用可能等上数秒，期间 Stop() 可能已置位：见上面的守卫理由
            if (_stop) return false;
            // jar 每轮都重推（刻意：/data/local/tmp 被清或设备重启后能自愈）；推失败不致命，只这一轮可能起不来，下一轮还会再推
            if (!DeviceLauncher.PushJar(jar))
                LogFromWorker("# 重连：jar 推送失败（继续尝试拉起注入器）");
            // PushJar 同样可能等上数秒——最后再核一次，起进程是留在世上最久的东西
            if (_stop) return false;
            _devProc = DeviceLauncher.Start();
            return _devProc != null;
        }

        /// <summary>状态与日志上报的统一入口。
        /// **status** = 要显示在状态条上的文本；**key** = 拿去重用的【状态种类】。
        /// 两者刻意分开：等待消息里含每轮递增的重试次数，**那个次数绝不能拿去重**
        ///（否则 `key != _lastLoggedState` 永不成立，每轮都会写一行，掉线一小时约 720 行）。
        ///
        /// **状态条每次都设、绝不去重**：它是"现在是真值"，一旦被去重跳过，用户就会看到
        /// 停在"等待设备…"而实际已连上。日志才按状态种类去重。
        ///
        /// 为什么只留一把键（不再保留上一版那把"已显示文本"旧键）：两把键分属不同域时，一把的早返回会
        /// **吞掉另一把的重置** —— 那正是本缺陷的成因（短掉线恢复时 `Report("已连接")`
        /// 被旧键门掉，顺带没重置 `_lastLoggedState`，于是下一次掉线的等待阶段
        /// 一条日志都不打）。一把键、一个域，这个耦合就不存在了。</summary>
        void ReportState(string status, string key)
        {
            try
            {
                _host.BeginInvoke((MethodInvoker)delegate
                {
                    _host.SetStatus(status);          // 状态条：每次都设，绝不去重
                    if (key != _lastLoggedState)      // 日志：只在状态种类变化时写一行
                    {
                        _lastLoggedState = key;
                        _log("# " + status);
                    }
                });
            }
            catch (System.Exception) { }
        }

        /// <summary>一次性状态（"已连接"/"adb server 重启中…"/"强杀 adb 进程中…"）：
        /// 状态与去重键同为该文本。</summary>
        void Report(string s)
        {
            ReportState(s, s);
        }

        /// <summary>等待中的状态：状态条含重试次数（每轮都变），去重键只用 why。</summary>
        void ReportWaiting(string why, int attempts)
        {
            ReportState("等待设备…（已重试 " + attempts + " 次，" + why + "）", why);
        }

        /// <summary>后台线程写【事件】日志的通道（R9）：marshal 回 UI 线程再写。
        /// 事件类（如 jar 推送失败）每发生一次就该打一次、不去重；状态类上报走
        /// Report/ReportWaiting 的共用去重键 _lastLoggedState，不经这里。
        /// 绝不在后台线程直接调 _log——TrayUi 的 ApplicationExit 会 Close 掉 StreamWriter，
        /// 而 Stop() 只置标志不 Join 线程；对已关闭的 writer 写字是后台线程上的未处理异常
        /// = 整个进程被杀，不是干净退出。BeginInvoke 在窗体已销毁的极端时序下自身会抛，就地吞掉。</summary>
        void LogFromWorker(string s)
        {
            try
            {
                _host.BeginInvoke((MethodInvoker)delegate { _log(s); });
            }
            catch (System.Exception) { }
        }
    }
}
