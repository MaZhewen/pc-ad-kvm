using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PcKvm
{
    /// <summary>
    /// 托盘图标、设置入口与退出清理（阶段三 P0 抽取的第二块，见计划任务 1 的 Ruling 33）。
    /// 与 Watchers 分家的理由：Watchers 管"后台存活性检查"（含 adb 进程 churn），
    /// 这里管"界面与生命周期"，属两个不同的风险域；合成一个文件会让 Watchers 变成杂物间。
    /// 本类不参与任何输入或状态机逻辑。
    /// </summary>
    // 注：非 public——MessageHost 是 internal，public 构造器会触发 CS0051；
    // 本类只在同一程序集内被 Program.cs 消费。
    class TrayUi
    {
        readonly MessageHost _host;
        readonly Suppressor _supp;
        readonly Watchers _watchers;
        readonly Transport _transport;
        readonly StreamWriter _log;
        readonly Process _devProc;
        readonly Config _cfg;

        public NotifyIcon Tray { get; private set; }

        /// <summary>设置对话框点了确定、且配置已更新并写盘之后抛出。
        /// 应用行为（改速度、换跨越边）由 Program.cs 订阅处理——它才持有 scaler/tracker/supp。
        /// 本类只做界面与持久化，不碰运行时状态。</summary>
        public event Action<Config> SettingsApplied;

        public TrayUi(MessageHost host, Suppressor supp, Watchers watchers,
                      Transport transport, StreamWriter log, Process devProc, Config cfg)
        {
            _host = host;
            _supp = supp;
            _watchers = watchers;
            _transport = transport;
            _log = log;
            _devProc = devProc;
            _cfg = cfg;
        }

        /// <summary>建托盘、装菜单、订阅生命周期事件。必须在 Application.Run 之前调用。</summary>
        public void Install()
        {
            Tray = new NotifyIcon();
            Tray.Icon = SystemIcons.Application;   // 任务 6 换成 exe 自带的那份
            Tray.Text = "PC-KVM";
            Tray.Visible = true;

            MenuItem settings = new MenuItem("设置…");
            settings.Click += delegate
            {
                using (SettingsForm f = new SettingsForm(_cfg))
                {
                    if (f.ShowDialog(_host) != DialogResult.OK) return;
                    _cfg.MouseSensitivity = f.MouseSensitivity;
                    _cfg.AllowKillAdb = f.AllowKillAdb;
                    _cfg.PhoneOnLeft = f.PhoneOnLeft;
                    if (!_cfg.Save())
                        _log.WriteLine("# 配置写盘失败（设置本次仍生效，只是下次启动会丢）");
                }
                Action<Config> h = SettingsApplied;
                if (h != null) h(_cfg);
            };
            MenuItem quit = new MenuItem("退出");
            quit.Click += delegate { Application.Exit(); };
            Tray.ContextMenu = new ContextMenu(new MenuItem[] { settings, quit });

            _host.FormClosing += delegate { _supp.Release(); };
            Application.ApplicationExit += delegate
            {
                // 顺序要紧：先停后台监督（否则它会与 _log.Close() 抢），再释放抑制、收设备。
                _watchers.Stop();
                _supp.Release();
                _log.WriteLine("# 退出");
                Tray.Visible = false;
                _transport.Stop();
                DeviceLauncher.Cleanup(_devProc);
                _log.Close();
            };
        }
    }
}
