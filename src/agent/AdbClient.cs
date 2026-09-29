using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace PcKvm
{
    public sealed class AdbResult
    {
        public int ExitCode;
        public string Stdout = "";
        public string Stderr = "";
        public long ElapsedMilliseconds;
        public bool TimedOut;
        public bool Canceled;
        public string Error = "";
        public bool Success { get { return !TimedOut && !Canceled && Error.Length == 0 && ExitCode == 0; } }
    }

    public sealed class AdbClient
    {
        const int CaptureLimit = 65536;
        public readonly string Path;
        public AdbClient(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("ADB path required", "path");
            Path = path;
        }

        public static string ResolvePath()
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string directory in path.Split(';'))
            {
                if (String.IsNullOrWhiteSpace(directory)) continue;
                try
                {
                    string candidate = System.IO.Path.Combine(directory.Trim('"'), "adb.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch (Exception) { }
            }
            return "adb";
        }

        public ProcessStartInfo StartInfo(string[] args, AdbTarget target, bool redirectInput)
        {
            if (args == null || args.Length == 0) throw new ArgumentException("ADB command required", "args");
            List<string> values = new List<string>();
            if (target != null)
            {
                if (target.TransportId > 0)
                {
                    values.Add("-t");
                    values.Add(target.TransportId.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    values.Add("-s"); values.Add(target.Serial);
                }
            }
            values.AddRange(args);
            StringBuilder command = new StringBuilder();
            foreach (string value in values)
            {
                if (value == null || value.IndexOf('\0') >= 0) throw new ArgumentException("Invalid adb argument");
                if (command.Length != 0) command.Append(' ');
                command.Append(Quote(value));
            }
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = Path;
            info.Arguments = command.ToString();
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardInput = redirectInput;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            return info;
        }

        // Windows CommandLineToArgvW-compatible quoting, including trailing backslashes.
        public static string Quote(string value)
        {
            if (value.Length > 0 && value.IndexOfAny(new char[] { ' ', '\t', '\n', '\v', '"' }) < 0) return value;
            StringBuilder b = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { b.Append('\\', slashes * 2 + 1); b.Append('"'); slashes = 0; continue; }
                b.Append('\\', slashes); slashes = 0; b.Append(c);
            }
            b.Append('\\', slashes * 2);
            b.Append('"');
            return b.ToString();
        }

        public AdbResult Execute(string[] args, AdbTarget target, int timeoutMs, string stdin, CancellationToken cancel)
        {
            if (timeoutMs < 1) throw new ArgumentOutOfRangeException("timeoutMs");
            AdbResult result = new AdbResult();
            result.ExitCode = -1;
            Stopwatch watch = Stopwatch.StartNew();
            Process process = null;
            StringBuilder stdout = new StringBuilder(), stderr = new StringBuilder();
            object outGate = new object(), errGate = new object();
            using (ManualResetEvent outDone = new ManualResetEvent(false))
            using (ManualResetEvent errDone = new ManualResetEvent(false))
            try
            {
                if (cancel.IsCancellationRequested) { result.Canceled = true; return result; }
                process = new Process();
                process.StartInfo = StartInfo(args, target, stdin != null);
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) { try { outDone.Set(); } catch (ObjectDisposedException) { } }
                    else lock (outGate) AppendBounded(stdout, e.Data);
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) { try { errDone.Set(); } catch (ObjectDisposedException) { } }
                    else lock (errGate) AppendBounded(stderr, e.Data);
                };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (stdin != null)
                {
                    process.StandardInput.Write(stdin);
                    process.StandardInput.Close();
                }
                while (!process.WaitForExit(50))
                {
                    if (cancel.IsCancellationRequested) { result.Canceled = true; return result; }
                    if (watch.ElapsedMilliseconds >= timeoutMs) { result.TimedOut = true; return result; }
                }
                while (!outDone.WaitOne(0) || !errDone.WaitOne(0))
                {
                    if (cancel.IsCancellationRequested) { result.Canceled = true; return result; }
                    if (watch.ElapsedMilliseconds >= timeoutMs) { result.TimedOut = true; return result; }
                    Thread.Sleep(20);
                }
                if (cancel.IsCancellationRequested) result.Canceled = true;
                else result.ExitCode = process.ExitCode;
            }
            catch (Exception e) { result.Error = e.Message; }
            finally
            {
                if (process != null)
                {
                    try { if (!process.HasExited) process.Kill(); } catch (Exception) { }
                    process.Dispose();
                }
                lock (outGate) result.Stdout = stdout.ToString();
                lock (errGate) result.Stderr = stderr.ToString();
                result.ElapsedMilliseconds = watch.ElapsedMilliseconds;
            }
            return result;
        }

        static void AppendBounded(StringBuilder b, string line)
        {
            int remaining = CaptureLimit - b.Length;
            if (remaining <= 0) return;
            if (line.Length >= remaining) b.Append(line, 0, remaining);
            else { b.Append(line); b.Append('\n'); }
        }

        public Process StartLongRunning(string[] args, AdbTarget target)
        {
            Process process = new Process();
            process.StartInfo = StartInfo(args, target, false);
            process.OutputDataReceived += delegate { };
            process.ErrorDataReceived += delegate { };
            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                return process;
            }
            catch (Exception) { process.Dispose(); return null; }
        }
    }
}
