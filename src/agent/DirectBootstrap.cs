using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PcKvm
{
    // Secrets travel in a private file over the already authorized ADB transport.
    public sealed class DirectBootstrap
    {
        const string Directory = "/data/local/tmp/";
        static readonly SemaphoreSlim DeploymentGate = new SemaphoreSlim(1, 1);
        internal delegate AdbResult AdbCommand(string[] args, AdbTarget target, int timeout, string stdin, CancellationToken cancel);
        readonly AdbCommand _execute;
        readonly AdbTarget _target;
        readonly string _hardwareSerial;
        public readonly string InstanceId = Guid.NewGuid().ToString("N");

        public DirectBootstrap(AdbClient adb, AdbTarget target, string hardwareSerial)
            : this(adb.Execute, target, hardwareSerial) { }

        internal DirectBootstrap(AdbCommand execute, AdbTarget target, string hardwareSerial)
        {
            if (execute == null) throw new ArgumentNullException("execute");
            if (target == null) throw new ArgumentNullException("target");
            _execute = execute;
            _target = target;
            _hardwareSerial = AdbTarget.ForSerial(hardwareSerial).Serial;
        }
        public static string ParseWifiAddress(string routes)
        {
            foreach (string line in (routes ?? "").Split('\n'))
            {
                if (!Regex.IsMatch(line, @"\bdev\s+wlan\d+\b")) continue;
                Match match = Regex.Match(line, @"\bsrc\s+(\d+\.\d+\.\d+\.\d+)\b");
                IPAddress address;
                if (match.Success && TryAddress(match.Groups[1].Value, out address)) return address.ToString();
            }
            return "";
        }
        static bool TryAddress(string value, out IPAddress address)
        {
            address = null;
            if (!Regex.IsMatch(value ?? "", @"^\d{1,3}(\.\d{1,3}){3}$") || !IPAddress.TryParse(value, out address)
                || address.AddressFamily != AddressFamily.InterNetwork) return false;
            byte[] bytes = address.GetAddressBytes();
            return bytes[0] > 0 && bytes[0] < 224 && bytes[0] != 127 && bytes[3] != 255;
        }
        public IPAddress LocalAddress(CancellationToken cancel)
        {
            VerifyIdentity(cancel);
            AdbResult result = _execute(new[] { "shell", "ip -4 route" }, _target, 5000, null, cancel);
            Require(result, "无法读取设备 Wi-Fi 地址");
            string remote = ParseWifiAddress(result.Stdout);
            if (remote.Length == 0) throw new IOException("设备没有可用的 Wi-Fi IPv4 地址，请先连接局域网");
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Connect(new IPEndPoint(IPAddress.Parse(remote), 9));
                IPAddress local = ((IPEndPoint)socket.LocalEndPoint).Address;
                if (IPAddress.IsLoopback(local) || local.Equals(IPAddress.Any)) throw new IOException("没有通往设备 Wi-Fi 的本机网络接口");
                return local;
            }
        }
        static void Hex(string value, int length)
        {
            if (value == null || value.Length != length || !Regex.IsMatch(value, "\\A[0-9a-fA-F]+\\z"))
                throw new ArgumentException("Invalid direct connection credential");
        }
        public static string FormatConfig(string host, int port, string pin, string token, string id)
        {
            IPAddress address;
            if (!TryAddress(host, out address) || port <= 0 || port > 65535) throw new ArgumentException("Invalid direct endpoint");
            Hex(pin, 64); Hex(token, 64); Hex(id, 32);
            return "1\n" + address + "\n" + port + "\n" + pin + "\n" + token + "\n" + id + "\n";
        }
        public static string StartCommand(string id)
        {
            string path = InstancePath(id);
            return "umask 077; (CLASSPATH=" + path + "/injector.jar nohup setsid app_process / Injector --direct "
                + path + "/config </dev/null >" + path + "/log 2>&1 & )";
        }
        public static string StopCommand(string id)
        {
            return "umask 077; touch " + InstancePath(id) + "/stop";
        }

        public void Start(string jar, IPAddress host, int port, string pin, string token, CancellationToken cancel, Action ownerPublished = null)
        {
            string config = FormatConfig(host.ToString(), port, pin, token, InstanceId);
            string configHash = Digest(new MemoryStream(Encoding.ASCII.GetBytes(config), false));
            string jarHash = Digest(File.OpenRead(jar));
            string path = InstancePath(InstanceId);
            DeploymentGate.Wait(cancel);
            bool prepared = false, publishing = false;
            try
            {
                VerifyIdentity(cancel);
                AdbResult debugging = Shell("settings get global adb_enabled", cancel);
                Require(debugging, "无法核对 USB 调试开关");
                if (debugging.Stdout.Trim() != "1") throw new IOException("请保持 Android 的 USB 调试开关开启；独立 Wi-Fi 启动后可以拔线");
                AdbResult directory = Shell("umask 077; mkdir " + path, cancel);
                prepared = directory.Success || directory.Canceled || directory.TimedOut;
                Require(directory, "创建设备私有目录失败");
                Require(_execute(new[] { "push", jar, path + "/injector.jar" }, _target, 15000, null, cancel), "部署设备端程序失败");
                Require(_execute(new[] { "shell", "-T", "umask 077; cat > " + path + "/config" },
                    _target, 5000, config, cancel), "传输启动配置失败");
                Require(Shell("chmod 600 " + path + "/injector.jar " + path + "/config", cancel), "设置部署文件权限失败");
                VerifyHash(path + "/injector.jar", jarHash, cancel);
                VerifyHash(path + "/config", configHash, cancel);
                cancel.ThrowIfCancellationRequested();
                publishing = true;
                Require(Shell(OwnerCommand("publish"), cancel), "发布设备连接实例失败");
                cancel.ThrowIfCancellationRequested();
                if (ownerPublished != null) ownerPublished();
                Require(Shell(StartCommand(InstanceId), cancel), "启动独立网络进程失败");
                cancel.ThrowIfCancellationRequested();
            }
            catch
            {
                if (prepared) Rollback(path, publishing);
                throw;
            }
            finally { DeploymentGate.Release(); }
        }

        static string InstancePath(string id)
        {
            Hex(id, 32);
            return Directory + "pckvm-direct-" + id;
        }

        string OwnerCommand(string operation)
        {
            return "umask 077; CLASSPATH=" + InstancePath(InstanceId)
                + "/injector.jar app_process / DirectOwner " + operation + " " + InstanceId;
        }

        AdbResult Shell(string command, CancellationToken cancel, int timeout = 5000)
        {
            return _execute(new[] { "shell", command }, _target, timeout, null, cancel);
        }

        void VerifyIdentity(CancellationToken cancel, int timeout = 5000)
        {
            AdbResult result = _execute(new[] { "shell", "getprop", "ro.serialno" }, _target, timeout, null, cancel);
            Require(result, "无法核对设备身份");
            if (!String.Equals(result.Stdout.Trim(), _hardwareSerial, StringComparison.Ordinal))
                throw new IOException("所选 ADB 目标与绑定设备不匹配");
        }

        static string Digest(Stream data)
        {
            using (data)
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
        }

        void VerifyHash(string path, string expected, CancellationToken cancel)
        {
            AdbResult result = Shell("sha256sum " + path, cancel);
            Require(result, "无法校验设备部署文件");
            if (!Regex.IsMatch(result.Stdout, "\\A" + expected + @"\s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new IOException("设备部署文件校验失败");
        }

        void Rollback(string path, bool publishing)
        {
            try
            {
                VerifyIdentity(CancellationToken.None, 2000);
                if (publishing)
                {
                    if (!Shell(OwnerCommand("stop"), CancellationToken.None, 1500).Success)
                        Shell(StopCommand(InstanceId), CancellationToken.None, 1500);
                    Shell(OwnerCommand("cleanup"), CancellationToken.None, 2000);
                }
                else
                    Shell("rm -f " + path + "/config " + path + "/injector.jar && rmdir " + path, CancellationToken.None, 2000);
            }
            catch (IOException) { }
        }
        static void Require(AdbResult result, string stage)
        {
            if (result.Canceled) throw new OperationCanceledException();
            if (!result.Success) throw new IOException(stage + (result.TimedOut ? "（超时）" : ""));
        }
        public string Diagnostics()
        {
            try
            {
                VerifyIdentity(CancellationToken.None);
                AdbResult result = Shell("tail -c 2048 " + InstancePath(InstanceId) + "/log", CancellationToken.None);
                return result.Success ? result.Stdout.Trim() : "设备端日志暂不可读";
            }
            catch (IOException) { return "设备端日志暂不可读"; }
        }
        public void Stop()
        {
            Rollback(InstancePath(InstanceId), true);
        }
    }
}
