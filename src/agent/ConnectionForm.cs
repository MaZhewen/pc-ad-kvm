using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace PcKvm
{
    sealed class ConnectionForm : Form
    {
        sealed class DeviceChoice
        {
            public readonly string Serial;
            public readonly bool Wireless;
            public readonly string Label;
            public DeviceChoice(string serial, bool wireless, string label)
            { Serial = serial; Wireless = wireless; Label = label; }
            public override string ToString() { return Label; }
        }

        readonly ConnectionCoordinator _connection;
        readonly Config _config;
        readonly ComboBox _mode, _devices, _endpoint;
        readonly TextBox _pairIp, _pairCode;
        readonly ComboBox _pairPort;
        readonly Label _status, _hint, _adbInfo;
        readonly Button _scanButton, _cancelScanButton, _pairButton, _findPairPortButton;
        List<AdbDevice> _knownDevices = new List<AdbDevice>();
        bool _refreshing;
        CancellationTokenSource _scanCancel;
        int _scanEpoch;
        CancellationTokenSource _pairLookupCancel;
        int _pairLookupEpoch;
        string _lastPairIp = "";
        bool _pairJustCompleted;
        readonly int _scanTimeoutMs;

        public ConnectionForm(ConnectionCoordinator connection, Config config) : this(connection, config, 180000) { }

        internal ConnectionForm(ConnectionCoordinator connection, Config config, int scanTimeoutMs)
        {
            if (scanTimeoutMs < 1) throw new ArgumentOutOfRangeException("scanTimeoutMs");
            _scanTimeoutMs = scanTimeoutMs;
            _connection = connection; _config = config;
            Text = "PC-KVM · 连接设备";
            ClientSize = new Size(560, 420);
            MinimumSize = new Size(560, 430);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            AddLabel("连接方式", 20, 20, 90);
            _mode = new ComboBox { Name = "ConnectionMode", Left = 115, Top = 17, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            _mode.Items.Add("USB"); _mode.Items.Add("无线调试 (Android 11+)");
            _mode.SelectedIndex = config.ConnectionMode == "WirelessTls" ? 1 : 0;
            Controls.Add(_mode);

            Button refresh = Button("刷新设备", 315, 17, 105, delegate { RefreshDevices(); });
            Button chooseAdb = Button("选择 ADB", 430, 17, 90, delegate { BrowseAdb(); });
            AddLabel("在线设备", 20, 61, 90);
            _devices = new ComboBox { Name = "ConnectionDevices", Left = 115, Top = 57,
                Width = 405, DropDownStyle = ComboBoxStyle.DropDownList };
            Controls.Add(_devices);

            AddLabel("连接地址", 20, 104, 90);
            _endpoint = new ComboBox { Name = "ConnectionEndpoint", Left = 115, Top = 100,
                Width = 300, DropDownStyle = ComboBoxStyle.DropDown };
            _endpoint.Text = config.WirelessLastEndpoint;
            Controls.Add(_endpoint);
            _scanButton = Button("扫描端口", 425, 100, 95, delegate { ScanPorts(); });
            _scanButton.Name = "ScanPorts";
            _hint = AddLabel("可输入 IPv4 扫描；连接端口在无线调试主页面，配对端口不同。", 115, 129, 425);
            _hint.ForeColor = Color.DimGray;

            GroupBox pairing = new GroupBox { Text = "首次配对", Left = 20, Top = 162, Width = 520, Height = 95 };
            Controls.Add(pairing);
            pairing.Controls.Add(new Label { Text = "IP", Left = 12, Top = 28, Width = 25 });
            _pairIp = new TextBox { Name = "PairIp", Left = 42, Top = 24, Width = 158 };
            pairing.Controls.Add(_pairIp);
            pairing.Controls.Add(new Label { Text = "端口", Left = 209, Top = 28, Width = 38 });
            _pairPort = new ComboBox { Name = "PairPort", Left = 250, Top = 24, Width = 71, DropDownStyle = ComboBoxStyle.DropDown };
            pairing.Controls.Add(_pairPort);
            pairing.Controls.Add(new Label { Text = "六位码", Left = 329, Top = 28, Width = 54 });
            _pairCode = new TextBox { Name = "PairCode", Left = 388, Top = 24, Width = 82, MaxLength = 6, UseSystemPasswordChar = true };
            pairing.Controls.Add(_pairCode);
            _findPairPortButton = new Button { Name = "FindPairPort", Text = "查找配对端口", Left = 42, Top = 57, Width = 116 };
            _findPairPortButton.Click += delegate { FindPairPort(null); };
            pairing.Controls.Add(_findPairPortButton);
            _pairButton = new Button { Name = "PairDevice", Text = "配对", Left = 169, Top = 57, Width = 80 };
            _pairButton.Click += delegate { Pair(); };
            pairing.Controls.Add(_pairButton);
            Button cancelPair = new Button { Text = "取消配对", Left = 260, Top = 57, Width = 95 };
            cancelPair.Click += delegate { CancelPairLookup(); _connection.CancelPair(); };
            pairing.Controls.Add(cancelPair);
            _pairIp.TextChanged += delegate { CancelPairLookup(); _pairPort.Items.Clear(); };
            _pairPort.TextChanged += delegate { CancelPairLookup(); };

            Button connect = Button("连接", 20, 277, 90, delegate { Connect(); });
            Button retry = Button("重试", 120, 277, 90, delegate { _connection.Retry(); });
            Button disconnect = Button("断开", 220, 277, 90, delegate { _connection.Disconnect(); });
            Button forget = Button("忘记设备", 320, 277, 100, delegate { Forget(); });
            _cancelScanButton = Button("取消扫描", 430, 277, 90, delegate { CancelScan(); });
            _cancelScanButton.Name = "CancelScan";
            _cancelScanButton.Enabled = false;
            _status = AddLabel(connection.State, 20, 327, 520);
            _status.Name = "ConnectionStatus";
            _status.AutoSize = false; _status.Height = 45;
            _status.ForeColor = Color.DarkSlateBlue;
            _adbInfo = AddLabel("ADB: " + connection.AdbPath, 20, 380, 520);
            _adbInfo.ForeColor = Color.DimGray;
            _mode.SelectedIndexChanged += delegate { if (_mode.SelectedIndex != 1) { CancelScan(); CancelPairLookup(); } FilterDevices(); };
            _connection.StateChanged += OnStateChanged;
            _connection.PairCompleted += OnPairCompleted;
            FormClosed += delegate
            {
                CancelScan();
                CancelPairLookup();
                _connection.StateChanged -= OnStateChanged;
                _connection.PairCompleted -= OnPairCompleted;
            };
            Shown += delegate { RefreshDevices(); };
        }

        Label AddLabel(string text, int x, int y, int width)
        {
            Label label = new Label { Text = text, Left = x, Top = y, Width = width, AutoSize = false, Height = 25 };
            Controls.Add(label); return label;
        }

        Button Button(string text, int x, int y, int width, EventHandler click)
        {
            Button button = new Button { Text = text, Left = x, Top = y, Width = width, Height = 30 };
            button.Click += click; Controls.Add(button); return button;
        }

        void OnStateChanged(string value)
        {
            try
            {
                if (IsDisposed) return;
                BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) _status.Text = value; });
            }
            catch (Exception) { }
        }

        void OnPairCompleted(bool success, string message)
        {
            if (!success) return;
            string pairedIp = _lastPairIp;
            try { BeginInvoke((MethodInvoker)delegate
            {
                if (IsDisposed || _mode.SelectedIndex != 1) return;
                _devices.SelectedIndex = 0;
                _endpoint.Text = pairedIp;
                _pairJustCompleted = true;
                RefreshDevices();
            }); }
            catch (Exception) { }
        }

        void RefreshDevices()
        {
            if (_refreshing) return;
            _refreshing = true;
            _status.Text = "正在刷新设备与连接服务…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string version = _connection.AdbVersion();
                List<AdbDevice> devices = _connection.Devices();
                List<WirelessService> services = _connection.Services();
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _refreshing = false;
                        if (IsDisposed) return;
                        _knownDevices = devices;
                        FilterDevices();
                        string typed = _endpoint.Text;
                        _endpoint.Items.Clear();
                        foreach (WirelessService service in services)
                            if (!_endpoint.Items.Contains(service.Endpoint.ToString()))
                                _endpoint.Items.Add(service.Endpoint.ToString());
                        _endpoint.Text = typed;
                        if (_scanCancel == null)
                        {
                            _status.Text = _pairJustCompleted
                                ? "配对完成；请扫描或输入无线调试主页面的连接端口，然后点击“连接”"
                                : _connection.State + "（发现 " + services.Count + " 个连接服务）";
                            _pairJustCompleted = false;
                        }
                        _adbInfo.Text = "ADB: " + version + " · " + _connection.AdbPath;
                    });
                }
                catch (Exception) { _refreshing = false; }
            });
        }

        void FilterDevices()
        {
            string previous = (_devices.SelectedItem as DeviceChoice) == null ? "" : ((DeviceChoice)_devices.SelectedItem).Serial;
            bool wireless = _mode.SelectedIndex == 1;
            _devices.Items.Clear();
            _devices.Items.Add(new DeviceChoice("", wireless, "（自动 / 使用下方地址）"));
            foreach (AdbDevice device in _knownDevices)
            {
                if (!device.IsOnline || device.IsUsb == wireless) continue;
                _devices.Items.Add(new DeviceChoice(device.Serial, wireless,
                    device.Serial + (device.Model.Length > 0 ? "  ·  " + device.Model : "")));
            }
            _devices.SelectedIndex = 0;
            for (int i = 1; i < _devices.Items.Count; i++)
                if (((DeviceChoice)_devices.Items[i]).Serial == previous) _devices.SelectedIndex = i;
            _endpoint.Enabled = wireless;
            _scanButton.Enabled = wireless && _scanCancel == null;
            _pairIp.Enabled = wireless;
            _pairPort.Enabled = wireless;
            _pairCode.Enabled = wireless;
            _pairButton.Enabled = wireless;
            _findPairPortButton.Enabled = wireless && _pairLookupCancel == null;
        }

        void ScanPorts()
        {
            if (_mode.SelectedIndex != 1) return;
            IPAddress address;
            if (!WirelessPortScanner.TryParseAddress(_endpoint.Text.Trim(), out address))
            { _status.Text = "请输入要扫描的 IPv4 地址"; return; }
            CancelScan();
            CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.CancelAfter(_scanTimeoutMs);
            Stopwatch elapsed = Stopwatch.StartNew();
            _scanCancel = cancellation;
            int epoch = ++_scanEpoch;
            _scanButton.Enabled = false;
            _cancelScanButton.Enabled = true;
            _status.Text = "正在查找 " + address + " 的无线调试端口…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string found = null;
                string error = null;
                WirelessPortScanner.ScanResult result = null;
                try
                {
                    List<WirelessService> services = _connection.Services(cancellation.Token);
                    foreach (WirelessService service in services)
                    {
                        if (cancellation.IsCancellationRequested) break;
                        if (service.Endpoint.Address != address.ToString()) continue;
                        string candidate = service.Endpoint.ToString();
                        if (_connection.VerifyWirelessEndpoint(candidate, cancellation.Token))
                        { found = candidate; break; }
                    }
                    if (found == null && !cancellation.IsCancellationRequested)
                    {
                        object verificationGate = new object();
                        int verifiedPort = 0;
                        int remainingMs = Math.Max(1, _scanTimeoutMs - (int)elapsed.ElapsedMilliseconds);
                        result = WirelessPortScanner.Scan(address, WirelessPortScanner.OrderedPorts(),
                            delegate(int port, CancellationToken scanToken)
                            {
                                lock (verificationGate)
                                {
                                    if (verifiedPort != 0 || scanToken.IsCancellationRequested) return false;
                                    if (!_connection.VerifyWirelessEndpoint(address + ":" + port, scanToken)) return false;
                                    verifiedPort = port;
                                    return true;
                                }
                            },
                            delegate(int done, int total)
                            {
                                try { BeginInvoke((MethodInvoker)delegate
                                {
                                    if (!IsDisposed && epoch == _scanEpoch && Object.ReferenceEquals(_scanCancel, cancellation))
                                        _status.Text = "正在扫描 " + address + "：" + done + "/" + total;
                                }); }
                                catch (Exception) { }
                            }, cancellation.Token, remainingMs);
                        if (result.FoundPort > 0) found = address + ":" + result.FoundPort;
                    }
                }
                catch (Exception ex) { error = ex.Message; }
                try { BeginInvoke((MethodInvoker)delegate
                {
                    try
                    {
                        if (IsDisposed || epoch != _scanEpoch || !Object.ReferenceEquals(_scanCancel, cancellation)) return;
                        _scanCancel = null;
                        _scanButton.Enabled = _mode.SelectedIndex == 1;
                        _cancelScanButton.Enabled = false;
                        if (found != null)
                        {
                            _devices.SelectedIndex = 0; // The verified endpoint wins over an earlier list selection.
                            _endpoint.Text = found;
                            _status.Text = "已验证无线调试端口 " + found + "；点击“连接”继续";
                        }
                        else if (error != null) _status.Text = "扫描失败：" + error;
                        else _status.Text = cancellation.IsCancellationRequested || result != null && result.TimedOut
                            ? "扫描已达时间上限；可输入手机显示的连接端口"
                            : "未发现已验证的无线调试端口；请检查配对或手动输入连接端口";
                    }
                    finally { cancellation.Dispose(); }
                }); }
                catch (Exception) { cancellation.Dispose(); }
            });
        }

        void CancelScan()
        {
            CancellationTokenSource cancellation = _scanCancel;
            if (cancellation == null) return;
            _scanEpoch++;
            _scanCancel = null;
            cancellation.Cancel();
            _cancelScanButton.Enabled = false;
            _scanButton.Enabled = _mode.SelectedIndex == 1;
            _status.Text = "端口扫描已取消";
        }

        void Pair()
        {
            string code = _pairCode.Text;
            _pairCode.Clear();
            CancelPairLookup();
            if (!ValidPairCode(code)) { _status.Text = "配对码需要六位数字"; return; }
            if (_pairPort.Text.Trim().Length == 0) { FindPairPort(code); return; }
            PairWithPort(_pairIp.Text.Trim(), _pairPort.Text.Trim(), code);
        }

        static bool ValidPairCode(string code)
        {
            if (code == null || code.Length != 6) return false;
            foreach (char c in code) if (c < '0' || c > '9') return false;
            return true;
        }

        static bool TryParsePairIp(string text, out IPAddress address)
        {
            address = null;
            return text.IndexOf(':') < 0 && WirelessPortScanner.TryParseAddress(text, out address);
        }

        void PairWithPort(string ip, string port, string code)
        {
            IPAddress address;
            WirelessEndpoint endpoint;
            if (!TryParsePairIp(ip, out address))
            { _status.Text = "请输入有效的配对 IPv4 地址"; return; }
            if (!WirelessEndpoint.TryParse(address + ":" + port, out endpoint))
            { _status.Text = "请输入手机配对弹窗中的端口（1–65535）"; return; }
            try
            {
                _lastPairIp = address.ToString();
                _pairJustCompleted = false;
                _connection.Pair(endpoint.ToString(), code);
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }

        void FindPairPort(string code)
        {
            if (_mode.SelectedIndex != 1) return;
            IPAddress address;
            if (!TryParsePairIp(_pairIp.Text.Trim(), out address))
            { _status.Text = "请输入有效的配对 IPv4 地址"; return; }
            CancelPairLookup();
            CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.CancelAfter(5000);
            _pairLookupCancel = cancellation;
            int epoch = ++_pairLookupEpoch;
            string ip = address.ToString();
            _findPairPortButton.Enabled = false;
            _status.Text = "正在查找 " + ip + " 的配对端口…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<int> ports = new List<int>();
                string error = null;
                try
                {
                    foreach (WirelessService service in _connection.PairingServices(cancellation.Token))
                        if (!cancellation.IsCancellationRequested && service.Endpoint.Address == ip
                            && !ports.Contains(service.Endpoint.Port)) ports.Add(service.Endpoint.Port);
                }
                catch (Exception ex) { error = ex.Message; }
                try { BeginInvoke((MethodInvoker)delegate
                {
                    try
                    {
                        if (IsDisposed || epoch != _pairLookupEpoch || !Object.ReferenceEquals(_pairLookupCancel, cancellation)) return;
                        _pairLookupCancel = null;
                        _findPairPortButton.Enabled = _mode.SelectedIndex == 1;
                        if (_mode.SelectedIndex != 1 || _pairIp.Text.Trim() != ip) return;
                        _pairPort.Items.Clear();
                        foreach (int port in ports) _pairPort.Items.Add(port.ToString());
                        if (ports.Count == 1)
                        {
                            _pairPort.Text = ports[0].ToString();
                            if (code == null) _status.Text = "已找到配对端口；输入手机显示的六位码后点击“配对”";
                            else PairWithPort(ip, _pairPort.Text, code);
                        }
                        else if (ports.Count > 1)
                            _status.Text = "发现多个配对端口；请选择手机配对弹窗显示的端口";
                        else _status.Text = error != null ? "查找配对端口失败：" + error + "；请手动输入手机显示的端口"
                            : "未发现此 IP 的配对端口；请手动输入手机配对弹窗中的端口";
                    }
                    finally { cancellation.Dispose(); }
                }); }
                catch (Exception) { cancellation.Dispose(); }
            });
        }

        void CancelPairLookup()
        {
            CancellationTokenSource cancellation = _pairLookupCancel;
            if (cancellation == null) return;
            _pairLookupEpoch++;
            _pairLookupCancel = null;
            cancellation.Cancel();
            _findPairPortButton.Enabled = _mode.SelectedIndex == 1;
        }

        void BrowseAdb()
        {
            CancelScan();
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "Android Debug Bridge (adb.exe)|adb.exe|可执行文件 (*.exe)|*.exe";
                dialog.Title = "选择 Android platform-tools 中的 adb.exe";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string path = dialog.FileName;
                _status.Text = "正在检查 ADB…";
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string error = null;
                    try { _connection.SelectAdbPath(path); }
                    catch (Exception ex) { error = ex.Message; }
                    try { BeginInvoke((MethodInvoker)delegate
                    {
                        if (error != null) _status.Text = error;
                        else RefreshDevices();
                    }); }
                    catch (Exception) { }
                });
            }
        }

        void Connect()
        {
            CancelScan();
            try
            {
                DeviceChoice choice = _devices.SelectedItem as DeviceChoice;
                string serial = choice == null ? "" : choice.Serial;
                _connection.Connect(_mode.SelectedIndex == 1 ? "WirelessTls" : "Usb", serial,
                    _mode.SelectedIndex == 1 && serial.Length == 0 ? _endpoint.Text.Trim() : "");
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }

        void Forget()
        {
            if (MessageBox.Show(this, "清除本程序保存的设备记录？手机中的 ADB 配对授权仍需在手机上撤销。",
                "忘记设备", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                _connection.Forget();
        }
    }
}
