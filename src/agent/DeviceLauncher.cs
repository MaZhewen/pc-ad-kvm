using System.Diagnostics;
using System.IO;

namespace PcKvm
{
    /// <summary>通过 adb 建立反向隧道并拉起设备侧进程。零安装：不推送 APK，只 push 一个 jar。</summary>
    public static class DeviceLauncher
    {
        public const int Port = 27183;
        // Shared by initial setup and reconnects; the Android endpoint stays fixed.
        public static int LocalPort = Port;
        public static string TunnelArguments
        {
            get { return "reverse tcp:" + Port + " tcp:" + LocalPort; }
        }
        const string RemoteJar = "/data/local/tmp/pckvm.jar";

        /// <summary>只建反向隧道。重连时用这个——不重推 jar（设备侧那份还在）。</summary>
        public static bool EnsureTunnel()
        {
            return RunAdb(TunnelArguments) == 0;
        }

        /// <summary>推 jar。首次启动、或设备侧被清过之后才需要。</summary>
        public static bool PushJar(string localJarPath)
        {
            if (!File.Exists(localJarPath)) return false;
            return RunAdb("push \"" + localJarPath + "\" " + RemoteJar) == 0;
        }

        /// <summary>首次启动：建隧道 + 推 jar。行为与拆分前完全一致。</summary>
        public static bool Prepare(string localJarPath)
        {
            if (!EnsureTunnel()) return false;
            return PushJar(localJarPath);
        }

        /// <summary>拉起设备侧进程。返回的 Process 由调用方负责 Kill——它是 PC 端唯一能关掉它的手段。</summary>
        public static Process Start()
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "adb";
            psi.Arguments = "shell \"CLASSPATH=" + RemoteJar + " app_process / Injector\"";
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            // 不抛：重连监督在后台线程调这里（TOCTOU 窗口后 adb 可能拉不起来），
            // 未处理异常会杀掉整个进程。失败返回 null，与 RunAdb/RunAdbCapture 的错误风格一致。
            Process p;
            try { p = Process.Start(psi); }
            catch (System.Exception)
            {
                return null;
            }
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            return p;
        }

        public static void Cleanup(Process p)
        {
            if (p != null)
            {
                try { if (!p.HasExited) p.Kill(); } catch (System.Exception) { }
            }
            RunAdb("shell rm -f " + RemoteJar);
            RunAdb("reverse --remove tcp:" + Port);
        }

        /// <summary>纯函数：`adb devices` 的输出里是否真有可用设备。
        /// **关键：必须跳过 "List of devices attached" 那行表头** —— 它含有 "devices" 字样，
        /// 任何 `Contains("device")` 之类的粗判都会把空列表误判成"有设备"，而本任务整条分级升级
        /// 都建立在这个判据上。（此风险由离线用例钉住。）</summary>
        public static bool ParseDeviceVisible(string adbOutput)
        {
            if (adbOutput == null) return false;
            string[] lines = adbOutput.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string s = lines[i].Trim();
                if (s.Length == 0) continue;
                // 形如 "04053891899C1540\tdevice"；"unauthorized"/"offline" 也必须算【不可用】
                if (s.EndsWith("\tdevice") || s.EndsWith(" device")) return true;
            }
            return false;
        }

        /// <summary>adb 现在看得见设备吗？判据是 `adb devices` 里出现一行以 "device" 结尾的条目。
        /// 解析已拆成纯函数 ParseDeviceVisible（可离线测试，D1–D5），这里只剩薄薄一层 adb 调用。</summary>
        public static bool DeviceVisible()
        {
            string outp;
            if (RunAdbCapture("devices", out outp) != 0) return false;
            return ParseDeviceVisible(outp);
        }

        /// <summary>温和恢复：重启 adb server。不碰别的进程，代价最小。</summary>
        public static bool RestartAdbServer()
        {
            RunAdb("kill-server");
            return RunAdb("start-server") == 0;
        }

        /// <summary>
        /// 激进恢复：强杀**所有** adb 进程再拉起 server。
        /// ⚠️ 本机同时跑着 3 个 adb（两个 platform-tools + InputShare 自带的），
        /// 强杀会打断其它正在用 adb 的工具。故由 Config.AllowKillAdb 把关、默认关闭。
        /// 实测依据：ledger 记过"kill-server 单独不够，出现过第二个 adb 进程仍占着设备"。
        /// </summary>
        public static bool KillAllAdb()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "taskkill";
                psi.Arguments = "/F /IM adb.exe";
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                Process p = Process.Start(psi);
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(5000);
            }
            catch (System.Exception)
            {
                return false;
            }
            return RunAdb("start-server") == 0;
        }

        static int RunAdb(string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "adb";
            psi.Arguments = args;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            try
            {
                Process p = Process.Start(psi);
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(5000);
                return p.ExitCode;
            }
            catch (System.Exception)
            {
                return -1;
            }
        }

        /// <summary>跑 adb 并把 stdout 取回。返回退出码；失败返回 -1。</summary>
        static int RunAdbCapture(string args, out string stdout)
        {
            stdout = "";
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "adb";
            psi.Arguments = args;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            try
            {
                Process p = Process.Start(psi);
                stdout = p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(5000);
                return p.ExitCode;
            }
            catch (System.Exception)
            {
                return -1;
            }
        }

        /// <summary>读取手机当前逻辑屏幕尺寸（已按旋转换算）。失败返回 false，且不改动 out。</summary>
        public static bool QueryDisplay(out int w, out int h)
        {
            int rotation;
            return QueryDisplay(out w, out h, out rotation);
        }

        /// <summary>读取逻辑尺寸与当前显示旋转（0/90/180/270）。</summary>
        public static bool QueryDisplay(out int w, out int h, out int rotation)
        {
            w = 0; h = 0; rotation = -1;
            string outp;
            if (RunAdbCapture("shell \"wm size; dumpsys window displays 2>/dev/null"
                              + " | grep -o mRotation=[A-Z0-9_]* | head -1\"", out outp) != 0)
                return false;
            return ParseDisplaySize(outp, out w, out h, out rotation);
        }

        /// <summary>纯函数：把 adb 输出解析成逻辑尺寸。旋转 90/270 时宽高互换。</summary>
        public static bool ParseDisplaySize(string adbOutput, out int w, out int h)
        {
            int rotation;
            return ParseDisplaySize(adbOutput, out w, out h, out rotation);
        }

        public static bool ParseDisplaySize(string adbOutput, out int w, out int h, out int rotation)
        {
            w = 0; h = 0; rotation = -1;
            if (adbOutput == null) return false;

            int ow = 0, oh = 0;      // Override size 优先
            int pw = 0, ph = 0;      // Physical size 兜底
            int rot = -1;

            string[] lines = adbOutput.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string s = lines[i].Trim();
                if (s.StartsWith("Override size:"))
                    ParseWxH(s.Substring(14), ref ow, ref oh);
                else if (s.StartsWith("Physical size:"))
                    ParseWxH(s.Substring(14), ref pw, ref ph);
                else if (s.StartsWith("mRotation="))
                    rot = ParseRotation(s.Substring(10));
            }

            int baseW = ow > 0 ? ow : pw;
            int baseH = ow > 0 ? oh : ph;
            if (baseW <= 0 || baseH <= 0 || rot < 0) return false;
            if (rot == 90 || rot == 270) { w = baseH; h = baseW; }
            else { w = baseW; h = baseH; }
            rotation = rot;
            return true;
        }

        static void ParseWxH(string s, ref int w, ref int h)
        {
            int i = s.IndexOf('x');
            if (i <= 0) return;
            int a, b;
            if (!int.TryParse(s.Substring(0, i).Trim(), out a)) return;
            if (!int.TryParse(s.Substring(i + 1).Trim(), out b)) return;
            if (a <= 0 || b <= 0) return;
            w = a; h = b;
        }

        static int ParseRotation(string s)
        {
            if (s == "ROTATION_0") return 0;
            if (s == "ROTATION_90") return 90;
            if (s == "ROTATION_180") return 180;
            if (s == "ROTATION_270") return 270;
            return -1;
        }
    }
}
