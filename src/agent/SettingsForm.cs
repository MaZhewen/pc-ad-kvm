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

        public double MouseSensitivity { get; private set; }
        public bool PhoneOnLeft { get; private set; }
        public bool AllowKillAdb { get; private set; }

        public SettingsForm(Config current)
        {
            Text = "PC-KVM 设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(360, 230);

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

            Button ok = new Button();
            ok.Text = "确定";
            ok.DialogResult = DialogResult.OK;
            ok.Location = new Point(180, 190);
            ok.Click += delegate { Capture(); };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(266, 190);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            OnSpeedChanged();   // 初始显示数值
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

        // 与 Control.Capture（bool 属性）撞名，非有意隐藏；按 csc 的提示加 new，
        // 满足"零警告"的构建红线（CS0108），语义不变。
        new void Capture()
        {
            MouseSensitivity = (double)_speed.Value / 100.0;
            PhoneOnLeft = _left.Checked;
            AllowKillAdb = _killAdb.Checked;
        }
    }
}
