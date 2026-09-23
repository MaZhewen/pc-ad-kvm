using System;
using System.Drawing;
using System.Windows.Forms;

namespace PcKvm
{
    /// <summary>
    /// 主窗体：Raw Input 的消息宿主、托盘载体，兼作状态指示窗。
    /// 必须是可见的真实窗口——隐藏窗口无法持有前台，抑制器夺取前台依赖它。
    /// </summary>
    class MessageHost : Form
    {
        Label _status;

        public MessageHost()
        {
            Text = "PC-KVM";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Location = new System.Drawing.Point(0, 0);
            Size = new System.Drawing.Size(220, 28);
            Opacity = 0.75;
            BackColor = System.Drawing.Color.DarkSlateBlue;

            _status = new Label();
            _status.Dock = DockStyle.Fill;
            _status.ForeColor = System.Drawing.Color.White;
            _status.Font = new System.Drawing.Font("Consolas", 9f);
            _status.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            _status.Text = "IDLE";
            Controls.Add(_status);
        }

        public void SetStatus(string s) { _status.Text = s; }

        /// <summary>紧急逃逸键 Ctrl+Alt+Esc。抑制生效时本窗口持有前台，此键一定能收到。</summary>
        public event Action Escape;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Alt | Keys.Escape))
            {
                Action h = Escape;
                if (h != null) h();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Alt | Keys.Escape))
            {
                Action h = Escape;
                if (h != null) h();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }
    }
}
