using System;
using System.Threading;
using System.Windows.Forms;

namespace PcKvm
{
    /** UI-thread safety timers and target-scoped geometry polling. Connection ownership is elsewhere. */
    class Watchers
    {
        readonly Transport _transport;
        readonly EdgeTracker _tracker;
        readonly Suppressor _suppressor;
        readonly MessageHost _host;
        readonly Action<string> _log;
        readonly HeartbeatLease _lease = new HeartbeatLease(1000);
        readonly object _pingGate = new object();
        uint _sequence;
        long _heartbeatGeneration;
        volatile bool _stopping;
        ConnectionCoordinator _connection;
        Thread _geometryThread;
        System.Windows.Forms.Timer _guard, _heartbeat;

        public event Action<long, int, int, int> GeometryQueried;
        public bool HeartbeatHealthy { get { return _lease.Healthy(HeartbeatLease.ElapsedMilliseconds); } }

        public Watchers(Transport transport, EdgeTracker tracker, Suppressor suppressor,
                        MessageHost host, Action<string> log)
        {
            _transport = transport; _tracker = tracker; _suppressor = suppressor;
            _host = host; _log = log;
            _transport.SessionConnected += delegate(long generation)
            {
                lock (_pingGate) { _heartbeatGeneration = generation; _sequence = 0; _lease.Connected(generation, HeartbeatLease.ElapsedMilliseconds); }
            };
            _transport.SessionDisconnected += delegate(long generation) { _lease.Disconnected(generation); };
            _transport.SessionMessageReceived += delegate(long generation, byte type, byte[] payload)
            {
                ConnectionCoordinator connection = _connection;
                bool direct = connection != null && connection.IsDirect && generation == connection.Generation;
                if (type == Protocol.MsgPong && payload.Length == 4)
                {
                    long now = HeartbeatLease.ElapsedMilliseconds;
                    if (direct && _lease.Expired(now)) { connection.LinkLost(); return; }
                    if (_lease.Pong(generation, Protocol.GetU32(payload, 0), now) && direct)
                        connection.TryConfirmDirectReady(generation);
                }
                if (!direct) return;
                if (type == Protocol.MsgPointerReady) connection.TryConfirmDirectReady(generation);
                if (type == Protocol.MsgDeviceGeometry)
                {
                    int width, height, rotation;
                    if (Protocol.TryDecodeDeviceGeometry(payload, out width, out height, out rotation))
                    {
                        Action<long, int, int, int> handler = GeometryQueried;
                        if (handler != null) handler(generation, width, height, rotation);
                    }
                }
            };
            _host.Escape += delegate
            {
                _transport.SendControl(Protocol.EncodeLeave());
                _tracker.AbortTakeover();
                _suppressor.Release();
                _host.SetStatus("IDLE");
                _log("# 逃逸键触发");
            };
        }

        public void AttachConnection(ConnectionCoordinator connection) { _connection = connection; }

        public void Start()
        {
            _geometryThread = new Thread(GeometryLoop);
            _geometryThread.IsBackground = true;
            _geometryThread.Name = "pckvm-geometry";
            _geometryThread.Start();
            _guard = new System.Windows.Forms.Timer();
            _guard.Interval = 250;
            _guard.Tick += delegate { _suppressor.CheckForeground(); };
            _guard.Start();
            _heartbeat = new System.Windows.Forms.Timer();
            _heartbeat.Interval = 1000;
            _heartbeat.Tick += delegate { HeartbeatTick(); };
            _heartbeat.Start();
        }

        void GeometryLoop()
        {
            while (!_stopping)
            {
                Thread.Sleep(2000);
                if (_stopping) return;
                ConnectionCoordinator connection = _connection;
                if (connection == null || connection.IsDirect || !_transport.IsConnected) continue;
                long generation = connection.Generation;
                AdbTarget target = connection.CurrentTarget;
                if (target == null) continue;
                int width, height, rotation;
                if (!DeviceLauncher.QueryDisplay(target, out width, out height, out rotation, CancellationToken.None)) continue;
                if (_stopping || connection.IsDirect || !_transport.IsConnected || generation != connection.Generation) continue;
                Action<long, int, int, int> handler = GeometryQueried;
                if (handler != null) handler(generation, width, height, rotation);
            }
        }

        void HeartbeatTick()
        {
            ConnectionCoordinator connection = _connection;
            int interval = connection != null && connection.IsDirect ? 500 : 1000;
            if (_heartbeat.Interval != interval) _heartbeat.Interval = interval;
            if (!_transport.IsConnected)
            {
                if (_tracker.Current == KvmState.Takeover)
                {
                    _tracker.AbortTakeover(); _suppressor.Release(); _host.SetStatus("IDLE");
                }
                return;
            }
            if (_lease.Expired(HeartbeatLease.ElapsedMilliseconds))
            {
                if (connection != null) connection.LinkLost();
                else
                {
                    _tracker.AbortTakeover(); _suppressor.Release(); _transport.CloseSession();
                }
                return;
            }
            uint sequence;
            long generation;
            lock (_pingGate) { sequence = ++_sequence; generation = _heartbeatGeneration; _lease.Sent(sequence); }
            _transport.SendForGeneration(generation, Protocol.EncodePing(sequence));
        }

        public void Stop()
        {
            _stopping = true;
            if (_guard != null) _guard.Stop();
            if (_heartbeat != null) _heartbeat.Stop();
        }
    }
}
