using System;
using System.Collections.Generic;
using System.Drawing;
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
        readonly TextBox _pairEndpoint, _pairCode;
        readonly Label _status, _hint, _adbInfo;
        List<AdbDevice> _knownDevices = new List<AdbDevice>();
        bool _refreshing;

        public ConnectionForm(ConnectionCoordinator connection, Config config)
        {
            _connection = connection; _config = config;
            Text = "PC-KVM · 连接设备";
            ClientSize = new Size(560, 420);
            MinimumSize = new Size(560, 430);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            AddLabel("连接方式", 20, 20, 90);
            _mode = new ComboBox { Left = 115, Top = 17, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            _mode.Items.Add("USB"); _mode.Items.Add("无线调试 (Android 11+)");
            _mode.SelectedIndex = config.ConnectionMode == "WirelessTls" ? 1 : 0;
            Controls.Add(_mode);

            Button refresh = Button("刷新设备", 315, 17, 105, delegate { RefreshDevices(); });
            Button chooseAdb = Button("选择 ADB", 430, 17, 90, delegate { BrowseAdb(); });
            AddLabel("在线设备", 20, 61, 90);
            _devices = new ComboBox { Left = 115, Top = 57, Width = 405, DropDownStyle = ComboBoxStyle.DropDownList };
            Controls.Add(_devices);

            AddLabel("连接地址", 20, 104, 90);
            _endpoint = new ComboBox { Left = 115, Top = 100, Width = 405, DropDownStyle = ComboBoxStyle.DropDown };
            _endpoint.Text = config.WirelessLastEndpoint;
            Controls.Add(_endpoint);
            _hint = AddLabel("连接端口在手机“无线调试”主页面；配对弹窗端口与它不同。", 115, 129, 425);
            _hint.ForeColor = Color.DimGray;

            GroupBox pairing = new GroupBox { Text = "首次配对", Left = 20, Top = 162, Width = 520, Height = 95 };
            Controls.Add(pairing);
            pairing.Controls.Add(new Label { Text = "配对地址", Left = 12, Top = 28, Width = 75 });
            _pairEndpoint = new TextBox { Left = 90, Top = 24, Width = 180 };
            pairing.Controls.Add(_pairEndpoint);
            pairing.Controls.Add(new Label { Text = "六位码", Left = 280, Top = 28, Width = 55 });
            _pairCode = new TextBox { Left = 340, Top = 24, Width = 90, MaxLength = 6, UseSystemPasswordChar = true };
            pairing.Controls.Add(_pairCode);
            Button pair = new Button { Text = "配对", Left = 90, Top = 57, Width = 80 };
            pair.Click += delegate { Pair(); };
            pairing.Controls.Add(pair);
            Button cancelPair = new Button { Text = "取消配对", Left = 180, Top = 57, Width = 95 };
            cancelPair.Click += delegate { _connection.CancelPair(); };
            pairing.Controls.Add(cancelPair);

            Button connect = Button("连接", 20, 277, 90, delegate { Connect(); });
            Button retry = Button("重试", 120, 277, 90, delegate { _connection.Retry(); });
            Button disconnect = Button("断开", 220, 277, 90, delegate { _connection.Disconnect(); });
            Button forget = Button("忘记设备", 320, 277, 100, delegate { Forget(); });
            _status = AddLabel(connection.State, 20, 327, 520);
            _status.AutoSize = false; _status.Height = 45;
            _status.ForeColor = Color.DarkSlateBlue;
            _adbInfo = AddLabel("ADB: " + connection.AdbPath, 20, 380, 520);
            _adbInfo.ForeColor = Color.DimGray;
            _mode.SelectedIndexChanged += delegate { FilterDevices(); };
            _connection.StateChanged += OnStateChanged;
            _connection.PairCompleted += OnPairCompleted;
            FormClosed += delegate
            {
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
            try { BeginInvoke((MethodInvoker)delegate { RefreshDevices(); }); }
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
                        _status.Text = _connection.State + "（发现 " + services.Count + " 个连接服务）";
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
            _pairEndpoint.Enabled = wireless;
            _pairCode.Enabled = wireless;
        }

        void Pair()
        {
            string code = _pairCode.Text;
            _pairCode.Clear();
            try { _connection.Pair(_pairEndpoint.Text.Trim(), code); }
            catch (Exception ex) { _status.Text = ex.Message; }
        }

        void BrowseAdb()
        {
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
