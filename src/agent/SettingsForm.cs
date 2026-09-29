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
            ClientSize = new Size(360, 382);

            Label l1 = new Label();
            l1.Text = "手机上的鼠标速度";
            l1.Location = new Point(16, 16);
            l1.AutoSize = true;
            Controls.Add(l1);

            _speedValue = new Label();
            _speedValue.Location = new Point(280, 16);
            _speedValue.AutoSize = true;
            Controls.Add(_speedValue);

            _speed = new TrackBar();
            _speed.Minimum = MinHundredths;
            _speed.Maximum = MaxHundredths;
            _speed.SmallChange = StepHundredths;
            _speed.LargeChange = StepHundredths * 4;
            _speed.TickFrequency = StepHundredths * 10;
            _speed.Location = new Point(12, 40);
            _speed.Width = 330;
            int init = (int)Math.Round(current.MouseSensitivity * 100);
            if (init < MinHundredths) init = MinHundredths;
            if (init > MaxHundredths) init = MaxHundredths;
            _speed.Value = init;
            _speed.ValueChanged += delegate { OnSpeedChanged(); };
            Controls.Add(_speed);

            Label l2 = new Label();
            l2.Text = "手机在显示器的";
            l2.Location = new Point(16, 105);
            l2.AutoSize = true;
            Controls.Add(l2);

            _left = new RadioButton();
            _left.Text = "左侧";
            _left.Location = new Point(140, 103);
            _left.AutoSize = true;
            Controls.Add(_left);

            _right = new RadioButton();
            _right.Text = "右侧";
            _right.Location = new Point(220, 103);
            _right.AutoSize = true;
            Controls.Add(_right);
            if (current.PhoneOnLeft) _left.Checked = true; else _right.Checked = true;

            _killAdb = new CheckBox();
            _killAdb.Text = "断联时允许强杀 adb 进程（最后一招）";
            _killAdb.Location = new Point(16, 135);
            _killAdb.AutoSize = true;
            _killAdb.Checked = current.AllowKillAdb;
            Controls.Add(_killAdb);

            Label l3 = new Label();
            l3.Text = "会打断本机其它正在用 adb 的工具（如 InputShare）。默认关闭。";
            l3.ForeColor = SystemColors.GrayText;
            l3.Location = new Point(34, 158);
            l3.AutoSize = true;
            Controls.Add(l3);

            _enableEdgeSwitch = new CheckBox();
            _enableEdgeSwitch.Text = "启用双次贴边切换";
            _enableEdgeSwitch.Location = new Point(16, 190);
            _enableEdgeSwitch.AutoSize = true;
            _enableEdgeSwitch.Checked = current.EnableEdgeSwitch;
            Controls.Add(_enableEdgeSwitch);

            Label hotkeyLabel = new Label();
            hotkeyLabel.Text = "切换快捷键（点击输入框后按组合键）";
            hotkeyLabel.Location = new Point(16, 222);
            hotkeyLabel.AutoSize = true;
            Controls.Add(hotkeyLabel);

            _hotkeyInput = new TextBox();
            _hotkeyInput.ReadOnly = true;
            _hotkeyInput.Location = new Point(16, 250);
            _hotkeyInput.Width = 220;
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
            Controls.Add(_hotkeyInput);

            Label hotkeyHint = new Label();
            hotkeyHint.Text = "需包含 Ctrl 或 Alt；Esc 保留为紧急退出。";
            hotkeyHint.ForeColor = SystemColors.GrayText;
            hotkeyHint.Location = new Point(16, 280);
            hotkeyHint.AutoSize = true;
            Controls.Add(hotkeyHint);

            _hotkeyError = new Label();
            _hotkeyError.ForeColor = Color.Firebrick;
            _hotkeyError.Location = new Point(16, 306);
            _hotkeyError.Size = new Size(330, 30);
            Controls.Add(_hotkeyError);

            Button ok = new Button();
            ok.Text = "确定";
            ok.Location = new Point(180, 342);
            ok.Click += delegate { if (ReadBack()) DialogResult = DialogResult.OK; };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(266, 342);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            OnSpeedChanged();   // 初始显示数值
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
}
