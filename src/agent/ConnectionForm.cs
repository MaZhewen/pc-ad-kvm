using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace PcKvm
{
    sealed class ConnectionForm : Form
    {
        sealed class DeviceChoice
        {
            public readonly string Serial;
            public readonly string Label;
            public DeviceChoice(string serial, string label) { Serial = serial; Label = label; }
            public override string ToString() { return Label; }
        }

        static readonly Color Canvas = Color.FromArgb(244, 247, 251);
        static readonly Color Ink = Color.FromArgb(35, 48, 68);
        static readonly Color Muted = Color.FromArgb(104, 119, 139);
        static readonly Color Blue = Color.FromArgb(37, 99, 235);
        readonly ConnectionCoordinator _connection;
        readonly ComboBox _mode, _devices;
        readonly TextBox _endpoint, _pairIp, _pairPort, _pairCode;
        readonly Button _pairButton;
        readonly Label _status, _adbInfo;
        readonly Label _summary, _instructions, _errorDetails;
        readonly Config _config;
        readonly Func<bool> _isControlling, _switchingAvailable;
        readonly System.Windows.Forms.Timer _statusTimer;
        bool _showPairing;
        List<AdbDevice> _knownDevices = new List<AdbDevice>();
        bool _refreshing;
        bool _pairJustCompleted;
        string _lastPairIp = "";

        public event EventHandler OpenSettingsRequested;
        public event EventHandler ReturnToComputerRequested;

        public ConnectionForm(ConnectionCoordinator connection, Config config,
            Func<bool> isControlling = null, Func<bool> switchingAvailable = null)
        {
            _connection = connection;
            _config = config;
            _isControlling = isControlling ?? delegate { return false; };
            _switchingAvailable = switchingAvailable ?? delegate { return true; };
            Text = "PC-KVM · 设备与连接";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Canvas;
            ForeColor = Ink;
            Screen screen = Screen.FromPoint(Cursor.Position);
            int clientHeight = Math.Max(460, Math.Min(610, screen.WorkingArea.Height - 48));
            ClientSize = new Size(560, clientHeight);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.Padding = new Padding(16);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            root.BackColor = Canvas;

            Panel viewport = new Panel();
            viewport.Name = "ConnectionContentViewport";
            viewport.Dock = DockStyle.Fill;
            viewport.AutoScroll = true;
            viewport.Margin = Padding.Empty;
            viewport.BackColor = Canvas;
            TableLayoutPanel sections = new TableLayoutPanel();
            sections.Dock = DockStyle.Top;
            sections.AutoSize = true;
            sections.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            sections.ColumnCount = 1;
            sections.RowCount = 5;
            sections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 5; i++) sections.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sections.BackColor = Canvas;
            TableLayoutPanel errors = CreateGrid(1, 1);
            errors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _errorDetails = Hint("");
            _errorDetails.Name = "ErrorDetails";
            _errorDetails.ForeColor = Color.Firebrick;
            _errorDetails.MaximumSize = new Size(460, 0);
            errors.Controls.Add(_errorDetails, 0, 0);
            sections.Controls.Add(Card("连接提示", "ErrorCard", errors), 0, 0);

            TableLayoutPanel deviceLayout = CreateGrid(4, 2);
            deviceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76F));
            deviceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            deviceLayout.Controls.Add(FieldLabel("连接方式"), 0, 0);
            _mode = new ComboBox { Name = "ConnectionMode", DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 260, Margin = new Padding(0, 1, 0, 7) };
            _mode.Items.Add("USB");
            _mode.Items.Add("无线调试 (Android 11+)");
            _mode.SelectedIndex = config.ConnectionMode == "WirelessTls" ? 1 : 0;
            deviceLayout.Controls.Add(_mode, 1, 0);
            deviceLayout.Controls.Add(FieldLabel("在线设备"), 0, 1);
            _devices = new ComboBox { Name = "ConnectionDevices", Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 1, 0, 7) };
            deviceLayout.Controls.Add(_devices, 1, 1);
            FlowLayoutPanel deviceActions = Actions();
            deviceActions.Controls.Add(StyledButton("刷新设备", "RefreshDevices", false,
                delegate { RefreshDevices(); }));
            deviceActions.Controls.Add(StyledButton("选择 ADB", "ChooseAdb", false,
                delegate { BrowseAdb(); }));
            deviceLayout.Controls.Add(deviceActions, 1, 2);
            _adbInfo = FieldLabel("ADB: " + connection.AdbPath);
            _adbInfo.AutoSize = false;
            _adbInfo.Height = 22;
            _adbInfo.ForeColor = Muted;
            _adbInfo.AutoEllipsis = true;
            _adbInfo.Dock = DockStyle.Fill;
            deviceLayout.Controls.Add(_adbInfo, 0, 3);
            deviceLayout.SetColumnSpan(_adbInfo, 2);
            sections.Controls.Add(Card("设备", "DeviceCard", deviceLayout), 0, 1);

            TableLayoutPanel connectionLayout = CreateGrid(3, 2);
            connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76F));
            connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            connectionLayout.Controls.Add(FieldLabel("连接地址"), 0, 0);
            _endpoint = new TextBox { Name = "ConnectionEndpoint", Dock = DockStyle.Fill,
                Text = config.WirelessLastEndpoint, Margin = new Padding(0, 0, 0, 7) };
            connectionLayout.Controls.Add(_endpoint, 1, 0);
            Label endpointHint = Hint("手动输入无线调试主页面的 IP:连接端口；与配对端口不同。");
            connectionLayout.Controls.Add(endpointHint, 0, 1);
            connectionLayout.SetColumnSpan(endpointHint, 2);
            Button showPairing = StyledButton("添加新设备 / 首次配对", "ShowPairing", false,
                delegate { _showPairing = !_showPairing; RenderState(); });
            showPairing.AutoSize = true;
            connectionLayout.Controls.Add(showPairing, 0, 2);
            connectionLayout.SetColumnSpan(showPairing, 2);
            sections.Controls.Add(Card("无线连接", "ConnectionCard", connectionLayout), 0, 2);

            TableLayoutPanel pairingLayout = CreateGrid(4, 3);
            pairingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));
            pairingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23F));
            pairingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            pairingLayout.Controls.Add(FieldLabel("配对 IP"), 0, 0);
            pairingLayout.Controls.Add(FieldLabel("配对端口"), 1, 0);
            pairingLayout.Controls.Add(FieldLabel("六位码"), 2, 0);
            _pairIp = new TextBox { Name = "PairIp", Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 10, 5) };
            _pairPort = new TextBox { Name = "PairPort", Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 10, 5) };
            _pairCode = new TextBox { Name = "PairCode", Dock = DockStyle.Fill,
                MaxLength = 6, UseSystemPasswordChar = true, Margin = new Padding(0, 0, 0, 5) };
            pairingLayout.Controls.Add(_pairIp, 0, 1);
            pairingLayout.Controls.Add(_pairPort, 1, 1);
            pairingLayout.Controls.Add(_pairCode, 2, 1);
            Label pairHint = Hint("按手机“使用配对码配对设备”弹窗填写，端口必须手动输入。");
            pairingLayout.Controls.Add(pairHint, 0, 2);
            pairingLayout.SetColumnSpan(pairHint, 3);
            FlowLayoutPanel pairingActions = Actions();
            _pairButton = StyledButton("配对", "PairDevice", true, delegate { Pair(); });
            pairingActions.Controls.Add(_pairButton);
            pairingActions.Controls.Add(StyledButton("取消配对", "CancelPair", false,
                delegate { _connection.CancelPair(); }));
            pairingLayout.Controls.Add(pairingActions, 0, 3);
            pairingLayout.SetColumnSpan(pairingActions, 3);
            sections.Controls.Add(Card("首次配对", "PairCard", pairingLayout), 0, 3);
            TableLayoutPanel usage = CreateGrid(2, 1);
            usage.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            Label usageText = Hint("鼠标和键盘已可共享。按上方快捷键切换控制对象。\n\n双次贴边：向连接侧外推一次，回到屏内，再次向外推。\n\n断线后会自动返回电脑；重连成功后需要主动切换回 Android。\n\n关闭此窗口仍会保持连接，可从托盘重新打开。");
            usageText.MaximumSize = new Size(460, 0);
            usage.Controls.Add(usageText, 0, 0);
            sections.Controls.Add(Card("使用说明", "UsageCard", usage), 0, 4);

            _status = new Label { Name = "ConnectionStatus", Dock = DockStyle.Fill,
                Text = "关闭窗口后仍在托盘运行；退出请使用托盘菜单。", ForeColor = Muted, AutoEllipsis = false,
                TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 4, 0, 0) };
            FlowLayoutPanel footer = Actions();
            footer.Dock = DockStyle.Fill;
            footer.FlowDirection = FlowDirection.RightToLeft;
            footer.Controls.Add(StyledButton("返回电脑", "ReturnToComputer", true, delegate {
                EventHandler requested = ReturnToComputerRequested;
                if (requested != null) requested(this, EventArgs.Empty);
                RenderState();
            }));
            footer.Controls.Add(StyledButton("连接", "ConnectDevice", true, delegate { Connect(); }));
            footer.Controls.Add(StyledButton("重试", "RetryConnection", false,
                delegate { _status.Text = ""; _connection.Retry(); RenderState(); }));
            footer.Controls.Add(StyledButton("断开", "DisconnectDevice", false,
                delegate { _connection.Disconnect(); _status.Text = "已断开，自动重连已停止。"; RenderState(); }));
            Button more = StyledButton("更多…", "MoreActions", false, delegate { });
            ContextMenu menu = new ContextMenu();
            menu.MenuItems.Add("忘记设备…", delegate { Forget(); RenderState(); });
            more.Click += delegate { menu.Show(more, new Point(0, more.Height)); };
            more.Disposed += delegate { menu.Dispose(); };
            footer.Controls.Add(more);
            footer.Controls.Add(StyledButton("设置…", "OpenSettings", false, delegate
            {
                EventHandler requested = OpenSettingsRequested;
                if (requested != null) requested(this, EventArgs.Empty);
            }));

            viewport.Controls.Add(sections);
            TableLayoutPanel summaryLayout = CreateGrid(2, 1);
            summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _summary = new Label { Name = "DeviceSummary", Dock = DockStyle.Fill, AutoSize = true,
                Font = new Font(Font.FontFamily, 11F, FontStyle.Bold), Margin = new Padding(8) };
            _instructions = new Label { Name = "ControlInstructions", Dock = DockStyle.Fill,
                AutoSize = true, Margin = new Padding(8, 0, 8, 8), ForeColor = Muted };
            summaryLayout.Controls.Add(_summary, 0, 0);
            summaryLayout.Controls.Add(_instructions, 0, 1);
            root.Controls.Add(summaryLayout, 0, 0);
            root.Controls.Add(viewport, 0, 1);
            root.Controls.Add(_status, 0, 2);
            root.Controls.Add(footer, 0, 3);
            Controls.Add(root);

            _mode.SelectedIndexChanged += delegate { FilterDevices(); };
            _connection.StateChanged += OnStateChanged;
            _connection.PairCompleted += OnPairCompleted;
            FormClosed += delegate
            {
                _statusTimer.Stop();
                _statusTimer.Dispose();
                _connection.StateChanged -= OnStateChanged;
                _connection.PairCompleted -= OnPairCompleted;
            };
            Shown += delegate { RefreshDevices(); };
            _statusTimer = new System.Windows.Forms.Timer { Interval = 250 };
            _statusTimer.Tick += delegate { RenderState(); };
            _statusTimer.Start();
            FilterDevices();
        }

        Control Named(string name) { return Controls.Find(name, true)[0]; }

        internal void RenderState()
        {
            RenderState(_connection.Snapshot());
        }

        internal void RenderState(ConnectionSnapshot state)
        {
            bool controlling = state.Ready && _isControlling();
            bool wireless = _mode.SelectedIndex == 1;
            string deviceName = state.Device;
            foreach (AdbDevice device in _knownDevices)
                if (device.Serial == state.Device && device.Model.Length > 0)
                    deviceName = device.Model + " · " + device.Serial;
            _summary.Text = state.Ready
                ? (controlling ? "正在控制 Android" : "已连接 · 当前控制电脑")
                    + "\n" + (state.Mode == "Usb" ? "USB" : "无线") + " · " + deviceName
                : state.Desired ? (state.Error.Length > 0 ? "连接未完成 · 正在重试" : "正在连接设备") : "未连接设备";
            _summary.ForeColor = state.Ready ? Color.SeaGreen : Ink;
            _instructions.Text = state.Ready
                ? (!_switchingAvailable() ? "切换快捷键暂不可用；请关闭设置或检查快捷键注册。 " : "按 " + _config.SwitchHotkey + " 切换。 ")
                    + (_config.EnableEdgeSwitch ? "向" + (_config.PhoneOnLeft ? "左" : "右") + "侧双次贴边可进入 Android。 " : "贴边切换已关闭。 ")
                    + "Ctrl+Alt+Esc 紧急返回电脑。"
                : state.Error.Length > 0 ? "请根据下方提示修正连接信息，或重试连接。"
                    : state.Desired ? state.Status : "选择 USB 或无线设备开始连接。";
            _errorDetails.Text = state.Error;
            Named("ErrorCard").Visible = !state.Ready && state.Error.Length > 0;
            Named("DeviceCard").Visible = !state.Ready;
            Named("ReturnToComputer").Visible = controlling;
            Named("UsageCard").Visible = state.Ready;
            Named("ConnectionCard").Visible = wireless && !state.Ready;
            Named("PairCard").Visible = wireless && !state.Ready && _showPairing;
            Named("ConnectDevice").Visible = !state.Ready;
            Named("ConnectDevice").Enabled = !state.Pairing && !state.Attempting;
            Named("RetryConnection").Visible = !state.Ready && state.Error.Length > 0;
            Named("DisconnectDevice").Visible = state.Ready || state.Desired;
            Named("DisconnectDevice").Text = state.Ready ? "断开连接" : "停止连接";
            _pairButton.Enabled = wireless && !state.Pairing && !state.Ready;
            Named("CancelPair").Enabled = state.Pairing;
            Named("ChooseAdb").Enabled = !state.Ready && !state.Pairing;
        }

        static TableLayoutPanel CreateGrid(int rows, int columns)
        {
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.AutoSize = true;
            layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            layout.ColumnCount = columns;
            layout.RowCount = rows;
            layout.BackColor = Color.White;
            for (int i = 0; i < rows; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return layout;
        }

        static SettingsCard Card(string title, string name, TableLayoutPanel content)
        {
            SettingsCard card = new SettingsCard(title);
            card.Name = name;
            card.Dock = DockStyle.Fill;
            card.AutoSize = true;
            card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            card.Padding = new Padding(14, 12, 14, 12);
            card.Margin = new Padding(0, 0, 0, 10);
            TableLayoutPanel inner = CreateGrid(2, 1);
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            Label heading = new Label { Text = title, AutoSize = true,
                ForeColor = Ink, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 8) };
            content.Margin = Padding.Empty;
            inner.Controls.Add(heading, 0, 0);
            inner.Controls.Add(content, 0, 1);
            card.Controls.Add(inner);
            return card;
        }

        static Label FieldLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(0, 3, 5, 5) };
        }

        static Label Hint(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Muted,
                Margin = new Padding(0, 2, 0, 6) };
        }

        static FlowLayoutPanel Actions()
        {
            return new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill,
                WrapContents = false, Margin = Padding.Empty };
        }

        static Button StyledButton(string text, string name, bool primary, EventHandler click)
        {
            Button button = new Button { Name = name, Text = text, Size = new Size(text.Length > 4 ? 94 : 78, 32),
                FlatStyle = FlatStyle.Flat, BackColor = primary ? Blue : Color.White,
                ForeColor = primary ? Color.White : Ink, UseVisualStyleBackColor = false,
                Margin = new Padding(0, 2, 8, 2) };
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(210, 219, 229);
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(29, 78, 216)
                : Color.FromArgb(246, 248, 251);
            button.Click += click;
            return button;
        }

        void OnStateChanged(string value)
        {
            try { BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) RenderState(); }); }
            catch (Exception) { }
        }

        void OnPairCompleted(bool success, string message)
        {
            string pairedIp = _lastPairIp;
            try { BeginInvoke((MethodInvoker)delegate
            {
                if (IsDisposed) return;
                _status.Text = message;
                if (!success) return;
                _devices.SelectedIndex = 0;
                string currentAddress = _endpoint.Text.Trim();
                if (currentAddress.Length == 0 || currentAddress == pairedIp)
                    _endpoint.Text = pairedIp;
                _pairJustCompleted = true;
                _showPairing = false;
                RefreshDevices();
            }); }
            catch (Exception) { }
        }

        void RefreshDevices()
        {
            if (_refreshing) return;
            _refreshing = true;
            _status.Text = "正在刷新设备…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string version = _connection.AdbVersion();
                List<AdbDevice> devices = _connection.Devices();
                try { BeginInvoke((MethodInvoker)delegate
                {
                    _refreshing = false;
                    if (IsDisposed) return;
                    _knownDevices = devices;
                    FilterDevices();
                    _status.Text = _pairJustCompleted
                        ? "配对完成；请在连接地址中补上手机主页面显示的连接端口。"
                        : devices.Count == 0 ? "未检测到设备。USB 请检查连接线与调试授权；无线请填写连接地址。" : "设备列表已更新。";
                    _pairJustCompleted = false;
                    _adbInfo.Text = "ADB: " + version + " · " + _connection.AdbPath;
                }); }
                catch (Exception) { _refreshing = false; }
            });
        }

        void FilterDevices()
        {
            string previous = (_devices.SelectedItem as DeviceChoice) == null ? ""
                : ((DeviceChoice)_devices.SelectedItem).Serial;
            bool wireless = _mode.SelectedIndex == 1;
            _devices.Items.Clear();
            _devices.Items.Add(new DeviceChoice("", wireless ? "使用下方连接地址" : "自动选择 USB 设备"));
            foreach (AdbDevice device in _knownDevices)
            {
                if (device.IsUsb == wireless) continue;
                _devices.Items.Add(new DeviceChoice(device.Serial,
                    device.Serial + (device.Model.Length > 0 ? "  ·  " + device.Model : "")
                    + (device.IsOnline ? "" : device.State == "unauthorized" ? " · 请在手机允许 USB 调试" : " · 设备离线")));
            }
            _devices.SelectedIndex = 0;
            for (int i = 1; i < _devices.Items.Count; i++)
                if (((DeviceChoice)_devices.Items[i]).Serial == previous) _devices.SelectedIndex = i;
            _endpoint.Enabled = wireless;
            _pairIp.Enabled = wireless;
            _pairPort.Enabled = wireless;
            _pairCode.Enabled = wireless;
            _pairButton.Enabled = wireless;
            RenderState();
        }

        static bool TryParsePairIp(string text, out IPAddress address)
        {
            address = null;
            WirelessEndpoint endpoint;
            if (String.IsNullOrEmpty(text) || text.IndexOf(':') >= 0
                || !WirelessEndpoint.TryParse(text + ":1", out endpoint)
                || !IPAddress.TryParse(endpoint.Address, out address)
                || address.ToString() != endpoint.Address)
            { address = null; return false; }
            return true;
        }

        void Pair()
        {
            string code = _pairCode.Text;
            if (code == null || code.Length != 6)
            { _status.Text = "配对码需要六位数字"; return; }
            foreach (char c in code) if (c < '0' || c > '9')
            { _status.Text = "配对码需要六位数字"; return; }
            IPAddress address;
            if (!TryParsePairIp(_pairIp.Text.Trim(), out address))
            { _status.Text = "请输入有效的配对 IPv4 地址"; return; }
            WirelessEndpoint endpoint;
            if (!WirelessEndpoint.TryParse(address + ":" + _pairPort.Text.Trim(), out endpoint))
            { _status.Text = "请输入手机配对弹窗中的端口（1–65535）"; return; }
            try
            {
                _pairCode.Clear();
                _lastPairIp = address.ToString();
                _pairJustCompleted = false;
                _connection.Pair(endpoint.ToString(), code);
                _status.Text = "正在配对…";
                RenderState();
            }
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
                        if (IsDisposed) return;
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
                bool wireless = _mode.SelectedIndex == 1;
                foreach (AdbDevice device in _knownDevices)
                    if (device.Serial == serial && !device.IsOnline)
                    {
                        _status.Text = device.State == "unauthorized"
                            ? "请在手机上允许 USB 调试，然后刷新设备。" : "设备离线，请检查连接后刷新设备。";
                        return;
                    }
                string endpoint = "";
                if (wireless && serial.Length == 0)
                {
                    WirelessEndpoint parsed;
                    if (!WirelessEndpoint.TryParse(_endpoint.Text.Trim(), out parsed))
                    { _status.Text = "请输入手机无线调试主页面的 IP:连接端口"; return; }
                    endpoint = parsed.ToString();
                }
                _connection.Connect(wireless ? "WirelessTls" : "Usb", serial, endpoint);
                _status.Text = "连接请求已提交。";
                RenderState();
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
