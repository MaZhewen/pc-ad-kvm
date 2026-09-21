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
    }
}
