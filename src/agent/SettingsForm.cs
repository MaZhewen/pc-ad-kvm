using System;
using System.Drawing;
using System.Windows.Forms;

namespace PcKvm
{
    /// <summary>
    /// 设置对话框。托盘右键「设置…」打开。
    ///
    /// 为什么是对话框而不是托盘菜单项：鼠标速度必须**边调边感受**，
    /// 菜单项的「速度 +」「速度 −」做不到这一点。
    ///
    /// 滑块内部用整数 10–300 表示 0.10–3.00；拖动后吸附到 0.05 的整数倍（与 spec 一致）。
    /// </summary>
    public class SettingsForm : Form
    {
        const int MinHundredths = 10;    // 0.10
        const int MaxHundredths = 300;   // 3.00
        const int StepHundredths = 5;    // 0.05
        const string UiFontFamily = "Microsoft YaHei UI";

        readonly TrackBar _speed;
        readonly Label _speedValue;
        readonly RadioButton _left;
        readonly RadioButton _right;
        readonly CheckBox _killAdb;
        readonly CheckBox _enableEdgeSwitch;
        readonly TextBox _hotkeyInput;
        readonly Label _hotkeyError;
        readonly Func<HotkeyBinding, bool> _trySwitchHotkey;
        HotkeyBinding _selectedHotkey;

        public double MouseSensitivity { get; private set; }
        public bool PhoneOnLeft { get; private set; }
        public bool AllowKillAdb { get; private set; }
        public bool EnableEdgeSwitch { get; private set; }
        public HotkeyBinding SwitchHotkey { get; private set; }

        public SettingsForm(Config current, Func<HotkeyBinding, bool> trySwitchHotkey)
        {
            _trySwitchHotkey = trySwitchHotkey;
            _selectedHotkey = current.SwitchHotkey;
            Text = "PC-KVM 设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font(UiFontFamily, 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(244, 247, 251);
            ForeColor = Color.FromArgb(35, 48, 68);
            Screen screen = Screen.FromPoint(Cursor.Position);
            int clientHeight = Math.Min(720, screen.WorkingArea.Height - 48);
            if (clientHeight < 420) clientHeight = 420;
            ClientSize = new Size(460, clientHeight);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.Padding = new Padding(16);
            layout.BackColor = BackColor;
            layout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));

            Panel contentViewport = new Panel();
            contentViewport.Name = "SettingsContentViewport";
            contentViewport.Dock = DockStyle.Fill;
            contentViewport.AutoScroll = true;
            contentViewport.BackColor = BackColor;
            contentViewport.Margin = Padding.Empty;

            TableLayoutPanel sectionLayout = new TableLayoutPanel();
            sectionLayout.Dock = DockStyle.Top;
            sectionLayout.AutoSize = true;
            sectionLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            sectionLayout.BackColor = BackColor;
            sectionLayout.ColumnCount = 1;
            sectionLayout.RowCount = 4;
            sectionLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            sectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < sectionLayout.RowCount; i++)
                sectionLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label l1 = new Label();
            l1.Text = "手机上的鼠标速度";
            l1.AutoSize = true;
            l1.Margin = new Padding(3, 4, 3, 4);

            _speedValue = new Label();
            _speedValue.AutoSize = true;
            _speedValue.Anchor = AnchorStyles.Right;
            _speedValue.ForeColor = Color.FromArgb(37, 99, 235);
            _speedValue.Font = new Font(UiFontFamily, 9F, FontStyle.Bold, GraphicsUnit.Point);
            _speedValue.Margin = new Padding(3, 4, 3, 4);

            _speed = new TrackBar();
            _speed.Minimum = MinHundredths;
            _speed.Maximum = MaxHundredths;
            _speed.SmallChange = StepHundredths;
            _speed.LargeChange = StepHundredths * 4;
            _speed.TickFrequency = StepHundredths * 10;
            _speed.TickStyle = TickStyle.None;
            _speed.BackColor = Color.White;
            _speed.Dock = DockStyle.Fill;
            _speed.Margin = new Padding(2, 0, 2, 0);
            int init = (int)Math.Round(current.MouseSensitivity * 100);
            if (init < MinHundredths) init = MinHundredths;
            if (init > MaxHundredths) init = MaxHundredths;
            _speed.Value = init;
            _speed.ValueChanged += delegate { OnSpeedChanged(); };

            TableLayoutPanel speedHeader = new TableLayoutPanel();
            speedHeader.Dock = DockStyle.Fill;
            speedHeader.AutoSize = true;
            speedHeader.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            speedHeader.BackColor = Color.White;
            speedHeader.ColumnCount = 2;
            speedHeader.RowCount = 1;
            speedHeader.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            speedHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            speedHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            speedHeader.Controls.Add(l1, 0, 0);
            speedHeader.Controls.Add(_speedValue, 1, 0);

            TableLayoutPanel speedEndpoints = new TableLayoutPanel();
            speedEndpoints.Dock = DockStyle.Fill;
            speedEndpoints.AutoSize = true;
            speedEndpoints.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            speedEndpoints.BackColor = Color.White;
            speedEndpoints.ColumnCount = 2;
            speedEndpoints.RowCount = 1;
            speedEndpoints.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            speedEndpoints.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            speedEndpoints.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            Label slowLabel = new Label();
            slowLabel.Text = "慢";
            slowLabel.AutoSize = true;
            Label fastLabel = new Label();
            fastLabel.Text = "快";
            fastLabel.AutoSize = true;
            fastLabel.Anchor = AnchorStyles.Right;
            speedEndpoints.Controls.Add(slowLabel, 0, 0);
            speedEndpoints.Controls.Add(fastLabel, 1, 0);

            TableLayoutPanel speedLayout = CreateVerticalLayout(3);
            speedLayout.Controls.Add(speedHeader, 0, 0);
            speedLayout.Controls.Add(_speed, 0, 1);
            speedLayout.Controls.Add(speedEndpoints, 0, 2);
            sectionLayout.Controls.Add(CreateSection("鼠标速度", speedLayout), 0, 0);

            Label l2 = new Label();
            l2.Text = "手机在显示器的哪一侧";
            l2.AutoSize = true;
            l2.Margin = new Padding(3, 3, 3, 6);

            _left = new RadioButton();
            _left.Text = "左侧";
            _left.AutoSize = true;
            _left.Margin = new Padding(3, 2, 20, 2);

            _right = new RadioButton();
            _right.Text = "右侧";
            _right.AutoSize = true;
            _right.Margin = new Padding(3, 2, 3, 2);
            if (current.PhoneOnLeft) _left.Checked = true; else _right.Checked = true;

            FlowLayoutPanel positionRow = new FlowLayoutPanel();
            positionRow.Dock = DockStyle.Fill;
            positionRow.AutoSize = true;
            positionRow.BackColor = Color.White;
            positionRow.FlowDirection = FlowDirection.LeftToRight;
            positionRow.WrapContents = false;
            positionRow.Controls.Add(_left);
            positionRow.Controls.Add(_right);
            TableLayoutPanel positionLayout = CreateVerticalLayout(2);
            positionLayout.Controls.Add(l2, 0, 0);
            positionLayout.Controls.Add(positionRow, 0, 1);
            sectionLayout.Controls.Add(CreateSection("手机位置", positionLayout), 0, 1);

            _killAdb = new CheckBox();
            _killAdb.Text = "断联时允许强杀 adb 进程（最后一招）";
            _killAdb.AutoSize = true;
            _killAdb.Checked = current.AllowKillAdb;
            _killAdb.Margin = new Padding(3, 2, 3, 3);

            Label l3 = new Label();
            l3.Text = "会打断本机其它正在用 adb 的工具（如 InputShare）。默认关闭。";
            l3.ForeColor = Color.FromArgb(104, 119, 139);
            l3.AutoSize = true;
            l3.MaximumSize = new Size(340, 0);
            l3.Margin = new Padding(24, 0, 3, 3);

            _enableEdgeSwitch = new CheckBox();
            _enableEdgeSwitch.Text = "启用双次贴边切换";
            _enableEdgeSwitch.AutoSize = true;
            _enableEdgeSwitch.Checked = current.EnableEdgeSwitch;
            _enableEdgeSwitch.Margin = new Padding(3, 2, 3, 2);

            Label edgeSwitchHint = new Label();
            edgeSwitchHint.Text = "关闭后贴边切换停用，快捷键仍可用于切换。";
            edgeSwitchHint.ForeColor = Color.FromArgb(104, 119, 139);
            edgeSwitchHint.AutoSize = true;
            edgeSwitchHint.MaximumSize = new Size(340, 0);
            edgeSwitchHint.Margin = new Padding(24, 0, 3, 6);

            Label hotkeyLabel = new Label();
            hotkeyLabel.Text = "切换快捷键（点击输入框后按组合键）";
            hotkeyLabel.AutoSize = true;
            hotkeyLabel.Margin = new Padding(3, 2, 3, 3);

            _hotkeyInput = new TextBox();
            _hotkeyInput.ReadOnly = true;
            _hotkeyInput.BorderStyle = BorderStyle.FixedSingle;
            _hotkeyInput.BackColor = Color.White;
            _hotkeyInput.Dock = DockStyle.Fill;
            _hotkeyInput.Text = _selectedHotkey.ToString();
            _hotkeyInput.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                CaptureShortcut(e.KeyData);
                e.SuppressKeyPress = true;
            };
            _hotkeyInput.PreviewKeyDown += delegate(object sender, PreviewKeyDownEventArgs e)
            {
                e.IsInputKey = true;
            };
            _hotkeyInput.Margin = new Padding(3, 2, 3, 5);

            Label hotkeyHint = new Label();
            hotkeyHint.Text = "需包含 Ctrl 或 Alt；Esc 保留为紧急退出。";
            hotkeyHint.ForeColor = Color.FromArgb(104, 119, 139);
            hotkeyHint.AutoSize = true;
            hotkeyHint.MaximumSize = new Size(340, 0);
            hotkeyHint.Margin = new Padding(3, 0, 3, 3);

            _hotkeyError = new Label();
            _hotkeyError.ForeColor = Color.Firebrick;
            _hotkeyError.AutoSize = true;
            _hotkeyError.MaximumSize = new Size(340, 0);
            _hotkeyError.Margin = new Padding(3, 0, 3, 3);

            TableLayoutPanel switchLayout = CreateVerticalLayout(6);
            switchLayout.Controls.Add(_enableEdgeSwitch, 0, 0);
            switchLayout.Controls.Add(edgeSwitchHint, 0, 1);
            switchLayout.Controls.Add(hotkeyLabel, 0, 2);
            switchLayout.Controls.Add(_hotkeyInput, 0, 3);
            switchLayout.Controls.Add(hotkeyHint, 0, 4);
            switchLayout.Controls.Add(_hotkeyError, 0, 5);
            sectionLayout.Controls.Add(CreateSection("切换方式", switchLayout), 0, 2);

            TableLayoutPanel recoveryLayout = CreateVerticalLayout(2);
            recoveryLayout.Controls.Add(_killAdb, 0, 0);
            recoveryLayout.Controls.Add(l3, 0, 1);
            sectionLayout.Controls.Add(CreateSection("断联恢复", recoveryLayout), 0, 3);

            Button ok = new Button();
            ok.Text = "确定";
            ok.FlatStyle = FlatStyle.Flat;
            ok.FlatAppearance.BorderSize = 0;
            ok.FlatAppearance.MouseOverBackColor = Color.FromArgb(29, 78, 216);
            ok.FlatAppearance.MouseDownBackColor = Color.FromArgb(30, 64, 175);
            ok.BackColor = Color.FromArgb(37, 99, 235);
            ok.ForeColor = Color.White;
            ok.UseVisualStyleBackColor = false;
            ok.Size = new Size(88, 32);
            ok.Click += delegate { if (ReadBack()) DialogResult = DialogResult.OK; };
            ok.Margin = new Padding(3, 3, 0, 3);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.FlatAppearance.BorderSize = 1;
            cancel.FlatAppearance.BorderColor = Color.FromArgb(210, 219, 229);
            cancel.FlatAppearance.MouseOverBackColor = Color.FromArgb(246, 248, 251);
            cancel.BackColor = Color.White;
            cancel.ForeColor = ForeColor;
            cancel.UseVisualStyleBackColor = false;
            cancel.Size = new Size(88, 32);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Margin = new Padding(3, 3, 8, 3);

            FlowLayoutPanel buttonRow = new FlowLayoutPanel();
            buttonRow.Dock = DockStyle.Fill;
            buttonRow.FlowDirection = FlowDirection.RightToLeft;
            buttonRow.WrapContents = false;
            buttonRow.Margin = Padding.Empty;
            buttonRow.Padding = new Padding(0, 5, 0, 0);
            buttonRow.Controls.Add(ok);
            buttonRow.Controls.Add(cancel);
            contentViewport.Controls.Add(sectionLayout);
            layout.Controls.Add(contentViewport, 0, 0);
            layout.Controls.Add(buttonRow, 0, 1);

            Controls.Add(layout);

            AcceptButton = ok;
            CancelButton = cancel;

            OnSpeedChanged();   // 初始显示数值
        }

        static TableLayoutPanel CreateVerticalLayout(int rows)
        {
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.AutoSize = true;
            layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            layout.BackColor = Color.White;
            layout.ColumnCount = 1;
            layout.RowCount = rows;
            layout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < rows; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return layout;
        }

        static SettingsCard CreateSection(string title, TableLayoutPanel content)
        {
            SettingsCard section = new SettingsCard(title);
            section.Dock = DockStyle.Fill;
            section.AutoSize = true;
            section.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            section.Padding = new Padding(14, 12, 14, 12);
            section.Margin = new Padding(0, 0, 0, 10);

            TableLayoutPanel cardLayout = new TableLayoutPanel();
            cardLayout.Dock = DockStyle.Fill;
            cardLayout.AutoSize = true;
            cardLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            cardLayout.BackColor = Color.White;
            cardLayout.ColumnCount = 1;
            cardLayout.RowCount = 2;
            cardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            cardLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label heading = new Label();
            heading.Text = title;
            heading.AutoSize = true;
            heading.ForeColor = Color.FromArgb(35, 48, 68);
            heading.Font = new Font(UiFontFamily, 9F, FontStyle.Bold, GraphicsUnit.Point);
            heading.Margin = new Padding(0, 0, 0, 8);
            content.Margin = Padding.Empty;
            cardLayout.Controls.Add(heading, 0, 0);
            cardLayout.Controls.Add(content, 0, 1);
            section.Controls.Add(cardLayout);
            return section;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_hotkeyInput != null && _hotkeyInput.Focused
                && (keyData & (Keys.Control | Keys.Alt)) != 0)
            {
                CaptureShortcut(keyData);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void CaptureShortcut(Keys keyData)
        {
            HotkeyBinding binding;
            if (HotkeyBinding.TryFromKeyData(keyData, out binding))
            {
                _selectedHotkey = binding;
                _hotkeyInput.Text = binding.ToString();
                _hotkeyError.Text = "";
            }
            else
            {
                Keys key = keyData & Keys.KeyCode;
                if (key != Keys.ControlKey && key != Keys.ShiftKey && key != Keys.Menu)
                    _hotkeyError.Text = "需包含 Ctrl 或 Alt，且不能使用 Esc。";
            }
        }

        void OnSpeedChanged()
        {
            int v = _speed.Value;
            // 吸附到 0.05 的整数倍（spec 的步进要求）。重赋值会再触发一次 ValueChanged，
            // 那一轮 v 已等于吸附值、直接走下面的显示逻辑，不会无限递归。
            int snapped = ((v + (StepHundredths / 2)) / StepHundredths) * StepHundredths;
            if (snapped < MinHundredths) snapped = MinHundredths;
            if (snapped > MaxHundredths) snapped = MaxHundredths;
            if (snapped != v) { _speed.Value = snapped; return; }
            _speedValue.Text = ((double)v / 100.0).ToString("F2");
        }

        bool ReadBack()
        {
            if (_trySwitchHotkey != null && !_trySwitchHotkey(_selectedHotkey))
            {
                _hotkeyError.Text = "快捷键已被占用，请换一个组合。";
                return false;
            }
            _hotkeyError.Text = "";
            MouseSensitivity = (double)_speed.Value / 100.0;
            PhoneOnLeft = _left.Checked;
            AllowKillAdb = _killAdb.Checked;
            EnableEdgeSwitch = _enableEdgeSwitch.Checked;
            SwitchHotkey = _selectedHotkey;
            return true;
        }
    }

    sealed class SettingsCard : Panel
    {
        public SettingsCard(string title)
        {
            Name = title;
            BackColor = Color.White;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer, true);
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle bounds = ClientRectangle;
            bounds.Width -= 1;
            bounds.Height -= 1;
            using (Pen border = new Pen(Color.FromArgb(223, 229, 237)))
                e.Graphics.DrawRectangle(border, bounds);
        }
    }
}
