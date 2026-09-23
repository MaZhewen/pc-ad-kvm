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
    ///    一律经 Report()（任务 8 落地）/BeginInvoke 回到 UI 线程。
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

        long _lastPongTicks = DateTime.UtcNow.Ticks;
        uint _pingSeq;
        volatile bool _stop;            // 退出时置位，让后台线程自己收工
        System.Threading.Thread _geoPoll;
        Timer _guard;
        Timer _heartbeat;

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
                if (type == Protocol.MsgPong) _lastPongTicks = DateTime.UtcNow.Ticks;
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
        }

        /// <summary>退出前调用（由 TrayUi 的 ApplicationExit 处理器调用）。
        /// 必须在 log.Close() 之前——否则后台线程会写进一个已关闭的 StreamWriter。
        /// 不 Join 线程：几何轮询是 IsBackground，最多 2 秒后自己看到 _stop 退出，
        /// 而进程此刻已经在收尾，不值得为它多等。</summary>
        public void Stop()
        {
            _stop = true;
            if (_guard != null) _guard.Stop();
            if (_heartbeat != null) _heartbeat.Stop();
        }

        void HeartbeatTick()
        {
            // 先做安全网：处于 TAKEOVER 时，「没连接」与「PONG 超时」都要立刻解除抑制。
            // 原写法把 `if (!IsConnected) return;` 放在最前，会在掉线后让安全网整体短路——
            // 用户此时再推到边缘会重新进入 TAKEOVER 并夺取前台，而没有东西能救他
            //（Task 7 安全不变量 4 / Ruling 18b，第二轮跨任务扫描发现）。
            if (_tracker.Current == KvmState.Takeover)
            {
                double age = (DateTime.UtcNow - new DateTime(_lastPongTicks)).TotalSeconds;
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
    }
}
