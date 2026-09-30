using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace PcKvm
{
    /** The sole owner of deploy/reverse/injector/retry for one selected device. */
    sealed class ConnectionCoordinator
    {
        readonly Config _config;
        AdbClient _adb;
        readonly Transport _transport;
        readonly PointerSession _pointer;
        readonly Action _releaseInput;
        readonly Action<string> _log;
        readonly object _gate = new object();
        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        Thread _worker;
        CancellationTokenSource _attemptCancel = new CancellationTokenSource();
        CancellationTokenSource _pairCancel;
        Func<bool> _heartbeatHealthy;
        volatile bool _stopping;
        volatile bool _ready;
        bool _desired;
        string _mode, _selectedSerial = "", _endpoint = "";
        bool _manual;
        int _requestVersion, _failures;
        int _usbRecoveryLevel;
        int _sessionRequestVersion = -1;
        long _generation;
        AdbTarget _pendingTarget;
        ConnectionProfile _session;
        Process _injector;
        bool _ownsReverse;
        bool _pushedJar;
        string _state = "未连接";

        public event Action<string> StateChanged;
        public event Action<bool, string> PairCompleted;

        public ConnectionCoordinator(Config config, AdbClient adb, Transport transport, PointerSession pointer,
                                     Action releaseInput, Action<string> log)
        {
            _config = config; _adb = adb; _transport = transport; _pointer = pointer;
            _releaseInput = releaseInput; _log = log;
            _mode = config.ConnectionMode;
            _desired = config.ConnectionConfigError.Length == 0;
            DeviceLauncher.UseClient(adb);
        }

        public bool IsReady { get { return _ready; } }
        public long Generation { get { return Interlocked.Read(ref _generation); } }
        public AdbTarget CurrentTarget { get { lock (_gate) return _pendingTarget; } }
        public string State { get { lock (_gate) return _state; } }
        public string AdbPath { get { lock (_gate) return _adb.Path; } }

        public string AdbVersion()
        {
            AdbResult result = _adb.Execute(new string[] { "version" }, null, 5000, null, CancellationToken.None);
            if (!result.Success) return "ADB 不可用：" + ErrorSummary(result);
            string[] lines = result.Stdout.Replace("\r", "").Split('\n');
            return lines.Length > 0 ? lines[0] : "ADB 已启动";
        }

        public void SelectAdbPath(string path)
        {
            if (!File.Exists(path)) throw new ArgumentException("找不到 adb.exe");
            lock (_gate) if (_ready) throw new InvalidOperationException("请先断开当前连接再更换 ADB");
            AdbClient replacement = new AdbClient(path);
            AdbResult version = replacement.Execute(new string[] { "version" }, null, 5000, null, CancellationToken.None);
            if (!version.Success) throw new IOException("ADB 不可用：" + ErrorSummary(version));
            lock (_gate)
            {
                if (_stopping) return;
                if (_ready) throw new InvalidOperationException("请先断开当前连接再更换 ADB");
                _attemptCancel.Cancel(); _attemptCancel.Dispose(); _attemptCancel = new CancellationTokenSource();
                _adb = replacement;
                _config.AdbPath = path;
                _config.Save();
                _requestVersion++;
                DeviceLauncher.UseClient(replacement);
            }
            Publish("已选择 ADB：" + version.Stdout.Split('\n')[0].Trim());
            _wake.Set();
        }

        public void SetHealthProbe(Func<bool> probe) { _heartbeatHealthy = probe; }

        public void Start()
        {
            _worker = new Thread(Run);
            _worker.IsBackground = true;
            _worker.Name = "pckvm-connection";
            _worker.Start();
            _wake.Set();
            if (_config.ConnectionConfigError.Length > 0) Publish("连接配置错误：" + _config.ConnectionConfigError);
        }

        public void Connect(string mode, string selectedSerial, string endpoint)
        {
            if (mode != "Usb" && mode != "WirelessTls") throw new ArgumentException("Invalid connection mode");
            if (!String.IsNullOrEmpty(endpoint))
            {
                WirelessEndpoint parsed;
                if (!WirelessEndpoint.TryParse(endpoint, out parsed)) throw new ArgumentException("连接地址需要 IPv4:端口");
            }
            if (!String.IsNullOrEmpty(selectedSerial)) AdbTarget.ForSerial(selectedSerial);
            if (mode == "WirelessTls" && String.IsNullOrEmpty(selectedSerial)
                && String.IsNullOrEmpty(endpoint) && _config.WirelessDeviceSerial.Length == 0)
                throw new ArgumentException("请选择在线无线设备或输入连接地址");
            lock (_gate)
            {
                if (_stopping) return;
                _mode = mode; _selectedSerial = selectedSerial ?? ""; _endpoint = endpoint ?? "";
                _manual = true; _desired = true; _requestVersion++; _failures = 0; _usbRecoveryLevel = 0;
                _attemptCancel.Cancel(); _attemptCancel.Dispose(); _attemptCancel = new CancellationTokenSource();
            }
            _wake.Set();
        }

        public void Retry()
        {
            lock (_gate)
            {
                if (_stopping) return;
                _desired = true; _requestVersion++; _failures = 0; _usbRecoveryLevel = 0;
                _attemptCancel.Cancel(); _attemptCancel.Dispose(); _attemptCancel = new CancellationTokenSource();
            }
            _wake.Set();
        }

        public void Disconnect()
        {
            lock (_gate)
            {
                if (_stopping) return;
                _desired = false; _requestVersion++;
                _attemptCancel.Cancel(); _attemptCancel.Dispose(); _attemptCancel = new CancellationTokenSource();
            }
            _ready = false;
            _releaseInput();
            _transport.CloseSession();
            Publish("已断开（手动）");
            _wake.Set();
        }

        public void Forget()
        {
            Disconnect();
            _config.UsbSerial = ""; _config.WirelessDeviceSerial = "";
            _config.WirelessDeviceGuid = ""; _config.WirelessServiceName = "";
            _config.WirelessLastEndpoint = ""; _config.ConnectionMode = "Usb";
            _config.Save();
            Publish("本程序的设备记录已清除；系统 ADB 授权需在手机撤销");
        }

        public void LinkLost()
        {
            if (_stopping || !_transport.IsConnected) return;
            _ready = false;
            _releaseInput();
            _transport.CloseSession();
            Publish("心跳失联，正在恢复连接");
            _wake.Set();
        }

        public List<AdbDevice> Devices()
        {
            AdbResult result = _adb.Execute(new string[] { "devices", "-l" }, null, 5000, null, CancellationToken.None);
            return result.Success ? AdbDevice.Parse(result.Stdout) : new List<AdbDevice>();
        }

        public List<WirelessService> Services()
        {
            return Services(CancellationToken.None);
        }

        public List<WirelessService> Services(CancellationToken cancel)
        {
            AdbResult result = _adb.Execute(new string[] { "mdns", "services" }, null, 5000, null, cancel);
            return result.Success ? WirelessDiscovery.Parse(result.Stdout) : new List<WirelessService>();
        }

        public List<WirelessService> PairingServices(CancellationToken cancel)
        {
            AdbResult result = _adb.Execute(new string[] { "mdns", "services" }, null, 5000, null, cancel);
            return result.Success ? WirelessDiscovery.ParsePairing(result.Stdout) : new List<WirelessService>();
        }

        /** Verify a scan candidate without deploying KVM or changing the saved profile. */
        public bool VerifyWirelessEndpoint(string endpoint, CancellationToken cancel)
        {
            WirelessEndpoint parsed;
            if (!WirelessEndpoint.TryParse(endpoint, out parsed)) return false;
            if (_stopping || cancel.IsCancellationRequested) return false;
            AdbClient client;
            lock (_gate) client = _adb;
            // ADB transports are shared across processes. A scan cannot prove ownership of a
            // newly listed endpoint, so it must never disconnect one after a failed probe.
            AdbResult connected = client.Execute(new string[] { "connect", endpoint }, null, 5000, null, cancel);
            if (!connected.Success || cancel.IsCancellationRequested) return false;
            AdbResult listing = client.Execute(new string[] { "devices", "-l" }, null, 5000, null, cancel);
            if (!listing.Success) return false;
            foreach (AdbDevice device in AdbDevice.Parse(listing.Stdout))
            {
                if (!device.IsOnline || device.IsUsb || device.Serial != endpoint) continue;
                AdbResult identity = client.Execute(new string[] { "shell", "getprop ro.serialno" },
                    device.Target, 5000, null, cancel);
                if (!identity.Success || cancel.IsCancellationRequested) return false;
                string serial = identity.Stdout.Trim();
                if (serial.Length == 0 || String.Equals(serial, "unknown", StringComparison.OrdinalIgnoreCase))
                    return false;
                if (_config.WirelessDeviceSerial.Length > 0 && serial != _config.WirelessDeviceSerial)
                    return false;
                return true;
            }
            return false;
        }

        public void Pair(string pairEndpoint, string code)
        {
            WirelessEndpoint parsed;
            if (!WirelessEndpoint.TryParse(pairEndpoint, out parsed)) throw new ArgumentException("配对地址需要 IPv4:端口");
            if (code == null || code.Length != 6) throw new ArgumentException("配对码需要六位数字");
            foreach (char c in code) if (c < '0' || c > '9') throw new ArgumentException("配对码需要六位数字");
            CancellationTokenSource cancellation = new CancellationTokenSource();
            lock (_gate)
            {
                if (_stopping) return;
                if (_pairCancel != null) _pairCancel.Cancel();
                _pairCancel = cancellation;
            }
            Publish("正在配对…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                AdbResult result = _adb.Execute(new string[] { "pair", parsed.ToString() }, null, 30000,
                    code + "\n", cancellation.Token);
                bool current;
                lock (_gate) current = !_stopping && Object.ReferenceEquals(_pairCancel, cancellation);
                if (!current) return;
                string message = result.Success ? "配对命令完成，请选择连接服务或输入手机主页面的连接端口"
                    : result.Canceled ? "配对已取消" : "配对失败：" + ErrorSummary(result);
                Publish(message);
                Action<bool, string> done = PairCompleted;
                if (done != null) done(result.Success, message);
                lock (_gate) if (Object.ReferenceEquals(_pairCancel, cancellation)) _pairCancel = null;
                cancellation.Dispose();
            });
        }

        public void CancelPair()
        {
            lock (_gate) if (_pairCancel != null) _pairCancel.Cancel();
        }

        public void Stop()
        {
            lock (_gate)
            {
                if (_stopping) return;
                _stopping = true; _desired = false; _requestVersion++;
                _attemptCancel.Cancel();
                if (_pairCancel != null) _pairCancel.Cancel();
            }
            _ready = false;
            _releaseInput();
            _transport.CloseSession();
            _wake.Set();
            if (_worker != null && _worker != Thread.CurrentThread) _worker.Join(10000);
        }

        void Run()
        {
            while (!_stopping)
            {
                string mode, serial, endpoint;
                bool desired, manual;
                int version;
                CancellationToken token;
                lock (_gate)
                {
                    desired = _desired; mode = _mode; serial = _selectedSerial;
                    endpoint = _endpoint; manual = _manual; version = _requestVersion;
                    token = _attemptCancel.Token;
                }
                if (!desired)
                {
                    Deactivate();
                    _wake.WaitOne(1000);
                    continue;
                }
                if (_ready && version == _sessionRequestVersion && _transport.IsConnected)
                {
                    _wake.WaitOne(250);
                    continue;
                }
                try
                {
                    Resolved resolved = Resolve(mode, serial, endpoint, manual, token);
                    if (resolved == null)
                    {
                        _failures++;
                        MaybeRecoverUsb(mode);
                        int waitForTarget = ConnectionProfile.BackoffMilliseconds(_config.ReconnectSeconds, _failures - 1, new Random().Next(21));
                        _wake.WaitOne(waitForTarget);
                        continue;
                    }
                    if (token.IsCancellationRequested || _stopping || version != _requestVersion) continue;
                    Deactivate();
                    if (token.IsCancellationRequested || _stopping) continue;
                    if (Deploy(mode, resolved, token))
                    {
                        if (_stopping || token.IsCancellationRequested || version != _requestVersion)
                        {
                            Deactivate();
                            continue;
                        }
                        _failures = 0; _usbRecoveryLevel = 0;
                        SaveVerified(mode, resolved);
                        lock (_gate)
                        {
                            _sessionRequestVersion = version;
                            _selectedSerial = ""; _endpoint = ""; _manual = false;
                        }
                        Publish((mode == "Usb" ? "USB" : "无线") + "：已就绪");
                        continue;
                    }
                    Deactivate();
                }
                catch (Exception ex) { Publish("连接失败：" + ex.Message); Deactivate(); }
                if (_stopping || token.IsCancellationRequested) continue;
                _failures++;
                MaybeRecoverUsb(mode);
                int wait = ConnectionProfile.BackoffMilliseconds(_config.ReconnectSeconds, _failures - 1, new Random().Next(21));
                Publish("等待重试（" + (wait / 1000) + " 秒）");
                _wake.WaitOne(wait);
            }
            Deactivate();
        }

        void MaybeRecoverUsb(string mode)
        {
            if (mode != "Usb" || _session != null || _stopping) return;
            int action = ConnectionProfile.UsbRecoveryAction(_failures, _usbRecoveryLevel, _config.AllowKillAdb);
            if (action == 1)
            {
                _usbRecoveryLevel = 1;
                Publish("USB：正在重启 ADB server…");
                DeviceLauncher.RestartAdbServer();
            }
            else if (action == 2)
            {
                _usbRecoveryLevel = 2;
                Publish("USB：正在执行允许的 ADB 强制恢复…");
                DeviceLauncher.KillAllAdb();
            }
            else if (_failures >= 6) _usbRecoveryLevel = 2;
        }

        sealed class Resolved
        {
            public AdbDevice Device;
            public string Identity;
            public string Endpoint;
        }

        Resolved Resolve(string mode, string serial, string endpoint, bool manual, CancellationToken cancel)
        {
            Publish("查找设备…");
            List<AdbDevice> devices = Enumerate(cancel);
            if (mode == "Usb")
            {
                AdbDevice usb = ConnectionProfile.ChooseUsb(devices,
                    serial.Length > 0 ? serial : _config.UsbSerial);
                if (usb == null) { Publish("需要选择在线 USB 设备"); return null; }
                return new Resolved { Device = usb, Identity = DeviceLauncher.GetIdentity(usb.Target, cancel), Endpoint = "" };
            }

            if (serial.Length > 0)
            {
                foreach (AdbDevice d in devices)
                    if (d.IsOnline && !d.IsUsb && d.Serial == serial)
                        return CheckIdentity(d, endpoint, manual, cancel);
                Publish("所选无线设备未在线"); return null;
            }
            if (!manual && _config.WirelessDeviceSerial.Length == 0)
            {
                Publish("需要选择无线设备或输入连接端口");
                return null;
            }
            if (endpoint.Length == 0 && _config.WirelessDeviceSerial.Length > 0)
            {
                bool ambiguous;
                AdbDevice saved = ConnectionProfile.ChooseWireless(devices, _config.WirelessDeviceSerial,
                    delegate(AdbDevice d) { return DeviceLauncher.GetIdentity(d.Target, cancel); }, out ambiguous);
                if (ambiguous) { Publish("多个无线设备报告同一身份，需要手动选择"); return null; }
                if (saved != null)
                    return new Resolved { Device = saved, Identity = _config.WirelessDeviceSerial, Endpoint = endpoint };
            }
            List<string> candidates = new List<string>();
            if (endpoint.Length > 0) candidates.Add(endpoint);
            else
            {
                AdbResult discovery = _adb.Execute(new string[] { "mdns", "services" }, null, 5000, null, cancel);
                if (discovery.Success)
                    foreach (WirelessService service in WirelessDiscovery.Parse(discovery.Stdout))
                        if (!candidates.Contains(service.Endpoint.ToString())) candidates.Add(service.Endpoint.ToString());
                if (_config.WirelessLastEndpoint.Length > 0 && !candidates.Contains(_config.WirelessLastEndpoint))
                    candidates.Add(_config.WirelessLastEndpoint);
            }
            foreach (string candidate in candidates)
            {
                if (cancel.IsCancellationRequested) return null;
                AdbResult connection = _adb.Execute(new string[] { "connect", candidate }, null, 10000, null, cancel);
                if (!connection.Success) continue;
                List<AdbDevice> after = Enumerate(cancel);
                if (!manual && _config.WirelessDeviceSerial.Length > 0)
                {
                    bool ambiguous;
                    AdbDevice matching = ConnectionProfile.ChooseWireless(after, _config.WirelessDeviceSerial,
                        delegate(AdbDevice d) { return DeviceLauncher.GetIdentity(d.Target, cancel); }, out ambiguous);
                    if (ambiguous) { Publish("多个无线设备报告同一身份，需要手动选择"); return null; }
                    if (matching != null)
                        return new Resolved { Device = matching, Identity = _config.WirelessDeviceSerial, Endpoint = candidate };
                    continue;
                }
                int onlineWireless = 0;
                foreach (AdbDevice listed in after) if (listed.IsOnline && !listed.IsUsb) onlineWireless++;
                foreach (AdbDevice d in after)
                {
                    if (!d.IsOnline || d.IsUsb) continue;
                    if (_config.WirelessDeviceSerial.Length == 0 && d.Serial != candidate && onlineWireless != 1) continue;
                    Resolved checkedDevice = CheckIdentity(d, candidate, manual, cancel);
                    if (checkedDevice != null) return checkedDevice;
                }
            }
            Publish(_config.WirelessDeviceSerial.Length == 0 ? "需要选择无线设备或输入连接端口"
                : "已配对设备未在线；检查无线调试或更新连接端口");
            return null;
        }

        Resolved CheckIdentity(AdbDevice device, string endpoint, bool manual, CancellationToken cancel)
        {
            string identity = DeviceLauncher.GetIdentity(device.Target, cancel);
            if (identity.Length == 0 && !manual) { Publish("设备身份不可读取，需要手动选择"); return null; }
            if (_config.WirelessDeviceSerial.Length > 0 && identity != _config.WirelessDeviceSerial)
            {
                Publish("设备身份变化；如需改绑，请先点“忘记设备”再选择"); return null;
            }
            return new Resolved { Device = device, Identity = identity, Endpoint = endpoint };
        }

        List<AdbDevice> Enumerate(CancellationToken cancel)
        {
            AdbResult result = _adb.Execute(new string[] { "devices", "-l" }, null, 5000, null, cancel);
            if (!result.Success) throw new IOException("ADB 设备枚举失败：" + ErrorSummary(result));
            return AdbDevice.Parse(result.Stdout);
        }

        bool Deploy(string mode, Resolved resolved, CancellationToken cancel)
        {
            long generation = Interlocked.Increment(ref _generation);
            byte[] token = new byte[32];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(token);
            StringBuilder hex = new StringBuilder(64);
            foreach (byte value in token) hex.Append(value.ToString("x2"));
            lock (_gate) _pendingTarget = resolved.Device.Target;
            _transport.ConfigureSession(token, generation);
            DeviceLauncher.LocalPort = _transport.ListeningPort;
            Publish("建立反向隧道…");
            bool createdReverse;
            if (!DeviceLauncher.EnsureTunnel(resolved.Device.Target, cancel, out createdReverse)) { Publish("反向隧道失败或端口已被占用"); return false; }
            _ownsReverse = createdReverse;
            if (_stopping || cancel.IsCancellationRequested) return false;
            Publish("部署注入器…");
            string jar = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pckvm.jar");
            if (!DeviceLauncher.PushJar(jar, resolved.Device.Target, cancel)) { Publish("注入器推送失败"); return false; }
            _pushedJar = true;
            if (_stopping || cancel.IsCancellationRequested) return false;
            _injector = DeviceLauncher.Start(resolved.Device.Target, hex.ToString());
            if (_injector == null) { Publish("注入器启动失败"); return false; }
            Publish("等待指针与心跳就绪…");
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 20000 && !_stopping && !cancel.IsCancellationRequested)
            {
                if (_transport.IsConnected && _pointer.Ready && (_heartbeatHealthy == null || _heartbeatHealthy()))
                {
                    _session = new ConnectionProfile(generation, mode, resolved.Device.Target, resolved.Identity, resolved.Endpoint);
                    _ready = true;
                    return true;
                }
                if (_injector.HasExited) break;
                Thread.Sleep(100);
            }
            Publish("会话握手、几何、READY 或 PONG 超时");
            return false;
        }

        void SaveVerified(string mode, Resolved resolved)
        {
            _config.ConnectionMode = mode;
            if (mode == "Usb") _config.UsbSerial = resolved.Device.Serial;
            else if (resolved.Identity.Length > 0)
            {
                _config.WirelessDeviceSerial = resolved.Identity;
                if (resolved.Endpoint.Length > 0) _config.WirelessLastEndpoint = resolved.Endpoint;
            }
            _config.ConnectionConfigError = "";
            _config.Save();
        }

        void Deactivate()
        {
            if (_pendingTarget == null && _injector == null && !_ownsReverse && !_pushedJar && !_transport.IsConnected)
                return;
            _ready = false;
            _releaseInput();
            _transport.CloseSession();
            AdbTarget target;
            lock (_gate) { target = _pendingTarget; _pendingTarget = null; }
            Process process = _injector; _injector = null;
            bool owned = _ownsReverse; _ownsReverse = false;
            bool pushed = _pushedJar; _pushedJar = false;
            if (process != null || owned || pushed) DeviceLauncher.Cleanup(target, process, owned, pushed);
            _session = null;
            _pointer.Reset();
        }

        void Publish(string status)
        {
            lock (_gate) { if (_state == status) return; _state = status; }
            if (!_stopping && _log != null) _log("# 连接: " + status);
            Action<string> changed = StateChanged;
            if (changed != null) changed(status);
        }

        static string ErrorSummary(AdbResult result)
        {
            if (result.Canceled) return "已取消";
            if (result.TimedOut) return "超时";
            string text = result.Stderr.Trim().Length > 0 ? result.Stderr.Trim() : result.Stdout.Trim();
            if (text.Length > 200) text = text.Substring(0, 200);
            return text.Length > 0 ? text : result.Error.Length > 0 ? result.Error : "退出码 " + result.ExitCode;
        }
    }
}
