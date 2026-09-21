using System.Diagnostics;
using System.IO;

namespace PcKvm
{
    /// <summary>通过 adb 建立反向隧道并拉起设备侧进程。零安装：不推送 APK，只 push 一个 jar。</summary>
    public static class DeviceLauncher
    {
        public const int Port = 27183;
        const string RemoteJar = "/data/local/tmp/pckvm.jar";

        public static bool Prepare(string localJarPath)
        {
            if (!File.Exists(localJarPath)) return false;
            if (RunAdb("reverse tcp:" + Port + " tcp:" + Port) != 0) return false;
            if (RunAdb("push \"" + localJarPath + "\" " + RemoteJar) != 0) return false;
            return true;
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
            Process p = Process.Start(psi);
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
            w = 0; h = 0;
            string outp;
            if (RunAdbCapture("shell \"wm size; dumpsys window displays 2>/dev/null"
                              + " | grep -o mRotation=[A-Z0-9_]* | head -1\"", out outp) != 0)
                return false;
            return ParseDisplaySize(outp, out w, out h);
        }

        /// <summary>纯函数：把 adb 输出解析成逻辑尺寸。旋转 90/270 时宽高互换。</summary>
        public static bool ParseDisplaySize(string adbOutput, out int w, out int h)
        {
            w = 0; h = 0;
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
