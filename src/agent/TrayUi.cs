using System;
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
        readonly ConnectionCoordinator _connection;
        readonly Transport _transport;
        readonly TextWriter _log;
        readonly Config _cfg;
        readonly Func<HotkeyBinding, bool> _trySwitchHotkey;
        SettingsForm _settingsDialog;
        ConnectionForm _connectionDialog;

        public NotifyIcon Tray { get; private set; }

        /// <summary>设置对话框点了确定、且配置已更新并写盘之后抛出。
        /// 应用行为（改速度、换跨越边）由 Program.cs 订阅处理——它才持有 scaler/tracker/supp。
        /// 本类只做界面与持久化，不碰运行时状态。</summary>
        public event Action<Config> SettingsApplied;

        public TrayUi(MessageHost host, Suppressor supp, Watchers watchers, ConnectionCoordinator connection,
                      Transport transport, TextWriter log, Config cfg,
                      Func<HotkeyBinding, bool> trySwitchHotkey)
        {
            _host = host;
            _supp = supp;
            _watchers = watchers;
            _connection = connection;
            _transport = transport;
            _log = log;
            _cfg = cfg;
            _trySwitchHotkey = trySwitchHotkey;
        }

        /// <summary>建托盘、装菜单、订阅生命周期事件。必须在 Application.Run 之前调用。</summary>
        public void Install()
        {
            Tray = new NotifyIcon();
            // 用 exe 自己嵌入的图标（/win32icon 那份），不再是 SystemIcons.Application
            // ——那个 Windows 通用图标是用户看到的"丑"的主要来源。取不到则回退，绝不抛。
            try
            {
                Tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception)
            {
                Tray.Icon = null;
            }
            if (Tray.Icon == null) Tray.Icon = SystemIcons.Application;
            Tray.Text = "PC-KVM";
            Tray.Visible = true;

            MenuItem settings = new MenuItem("设置…");
            settings.Click += delegate { OpenSettings(); };
            MenuItem connect = new MenuItem("连接设备…");
            connect.Click += delegate { OpenConnection(); };
            Tray.DoubleClick += delegate { OpenSettings(); };

            MenuItem quit = new MenuItem("退出");
            quit.Click += delegate { Application.Exit(); };
            Tray.ContextMenu = new ContextMenu(new MenuItem[] { connect, settings, quit });

            _host.FormClosing += delegate { _supp.Release(); };
            Application.ApplicationExit += delegate
            {
                // 顺序要紧：先停后台监督（否则它会与 _log.Close() 抢），再释放抑制、收设备。
                _watchers.Stop();
                _connection.Stop();
                _supp.Release();
                _log.WriteLine("# 退出");
                Tray.Visible = false;
                _transport.Stop();
                _log.Close();
            };
        }

        public void OpenConnection()
        {
            ConnectionForm existing = _connectionDialog;
            if (existing != null && !existing.IsDisposed)
            {
                if (existing.WindowState == FormWindowState.Minimized)
                    existing.WindowState = FormWindowState.Normal;
                existing.BringToFront(); existing.Activate(); return;
            }
            _connectionDialog = new ConnectionForm(_connection, _cfg);
            _connectionDialog.OpenSettingsRequested += delegate { OpenSettings(); };
            _connectionDialog.FormClosed += delegate { _connectionDialog = null; };
            _connectionDialog.Show();
            _connectionDialog.BringToFront();
        }
        /// <summary>统一处理托盘菜单、托盘双击、启动与第二次启动发来的设置请求。</summary>
        public void OpenSettings()
        {
            SettingsForm existing = _settingsDialog;
            if (existing != null && !existing.IsDisposed)
            {
                if (existing.WindowState == FormWindowState.Minimized)
                    existing.WindowState = FormWindowState.Normal;
                existing.BringToFront();
                existing.Activate();
                return;
            }

            _host.SwitchingEnabled = false;
            SettingsForm form = null;
            try
            {
                form = new SettingsForm(_cfg, _trySwitchHotkey);
                _settingsDialog = form;
                form.FormClosed += delegate
                {
                    if (Object.ReferenceEquals(_settingsDialog, form))
                        _settingsDialog = null;
                    _host.SwitchingEnabled = true;
                    if (form.DialogResult != DialogResult.OK) return;
                    bool connectAfterApply = form.ConnectAfterApply;

                    _cfg.MouseSensitivity = form.MouseSensitivity;
                    _cfg.AllowKillAdb = form.AllowKillAdb;
                    _cfg.PhoneOnLeft = form.PhoneOnLeft;
                    _cfg.EnableEdgeSwitch = form.EnableEdgeSwitch;
                    _cfg.SwitchHotkey = form.SwitchHotkey;
                    if (!_cfg.Save())
                        _log.WriteLine("# 配置写盘失败（设置本次仍生效，只是下次启动会丢）");

                    Action<Config> h = SettingsApplied;
                    if (h != null) h(_cfg);
                    if (connectAfterApply) OpenConnection();
                };
                // A modal dialog disables its owner (the capture window). Windows then
                // redirects a pinned mouse click back to Settings and ends takeover.
                form.Show();
                form.BringToFront();
            }
            catch
            {
                if (Object.ReferenceEquals(_settingsDialog, form))
                    _settingsDialog = null;
                _host.SwitchingEnabled = true;
                if (form != null) form.Dispose();
                throw;
            }
        }
    }
}
