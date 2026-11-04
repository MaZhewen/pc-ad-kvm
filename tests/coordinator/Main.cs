using System;
using System.IO;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Threading;
using PcKvm;

class CoordinatorTests
{
    static int Main()
    {
        string trace = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "trace.txt");
        Environment.SetEnvironmentVariable("PCKVM_COORD_TRACE", trace);
        File.WriteAllText(trace, "");
        Config config = new Config();
        config.ConnectionMode = "WirelessTls";
        config.WirelessDeviceSerial = "tablet-A";
        config.ReconnectSeconds = 60;
        Transport transport = new Transport(0);
        ConnectionCoordinator coordinator = new ConnectionCoordinator(config,
            new AdbClient(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb.exe")), transport,
            new PointerSession(delegate(byte[] frame) { }), delegate { }, delegate(string status) { });
        try
        {
            coordinator.Start();
            coordinator.Connect("WirelessTls", "192.168.1.3:40001", "");
            DateTime deadline = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < deadline && !coordinator.State.Contains("设备身份变化"))
                Thread.Sleep(20);
            if (!coordinator.State.Contains("设备身份变化"))
                throw new Exception("identity mismatch was not reported: " + coordinator.State);
            string commands = File.ReadAllText(trace);
            if (!commands.Contains("getprop ro.serialno")) throw new Exception("identity was not queried");
            if (commands.Contains(" push ") || commands.Contains(" reverse ") || commands.Contains("app_process"))
                throw new Exception("wrong device received deployment: " + commands);
            Console.WriteLine("PASS saved wireless identity blocks manual connection before deployment");
            ConnectionSnapshot snapshot = coordinator.Snapshot();
            if (snapshot.Ready || !snapshot.Desired || !snapshot.Error.Contains("设备身份变化"))
                throw new Exception("snapshot lost failure reason or reported false readiness");
            deadline = DateTime.UtcNow.AddSeconds(1);
            while (DateTime.UtcNow < deadline && coordinator.Snapshot().Attempting) Thread.Sleep(10);
            if (coordinator.Snapshot().Attempting) throw new Exception("retry wait blocks a new connection");
            coordinator.Disconnect();
            snapshot = coordinator.Snapshot();
            if (snapshot.Desired || snapshot.Ready || snapshot.Error.Length != 0)
                throw new Exception("manual disconnect did not reset visible state");
            Console.WriteLine("PASS snapshot preserves failure and permits correction during retry wait");
            VerifyUiFailures();
            VerifyDirectRuntime(trace);
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL " + error); return 1; }
        finally { coordinator.Stop(); }
    }

    static ConnectionCoordinator Create(Config config)
    {
        return new ConnectionCoordinator(config,
            new AdbClient(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb.exe")),
            new Transport(0), new PointerSession(delegate(byte[] frame) { }),
            delegate { }, delegate(string status) { });
    }

    static void VerifyUiFailures()
    {
        ConnectionCoordinator invalid = Create(new Config { ConnectionConfigError = "invalid saved endpoint" });
        try {
            invalid.Start();
            if (!invalid.Snapshot().Error.Contains("invalid saved endpoint"))
                throw new Exception("configuration error hidden from home");
            Console.WriteLine("PASS home exposes invalid saved configuration");
        } finally { invalid.Stop(); }

        Environment.SetEnvironmentVariable("PCKVM_COORD_MODE", "failedconnect");
        ConnectionCoordinator offline = Create(new Config { ConnectionMode = "WirelessTls",
            WirelessDeviceSerial = "tablet-A", WirelessLastEndpoint = "192.168.1.3:40001", ReconnectSeconds = 60 });
        try {
            offline.Start();
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline && offline.Snapshot().Error.Length == 0) Thread.Sleep(20);
            if (!offline.Snapshot().Error.Contains("未在线"))
                throw new Exception("offline wireless failure has no retryable error");
            Console.WriteLine("PASS offline wireless failure remains visible for retry");
        } finally { offline.Stop(); }

        Environment.SetEnvironmentVariable("PCKVM_COORD_MODE", "matching");
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pckvm.jar"), "fake jar");
        ConnectionCoordinator canceled = Create(new Config { ConnectionMode = "WirelessTls",
            WirelessDeviceSerial = "tablet-A", ReconnectSeconds = 60 });
        bool stoppedAtHandshake = false;
        canceled.StateChanged += delegate(string status) {
            if (status == "独立 Wi-Fi：准备启动…") {
                stoppedAtHandshake = true;
                canceled.Disconnect();
            }
        };
        try {
            canceled.Start();
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline && (!stoppedAtHandshake || canceled.Snapshot().Attempting)) Thread.Sleep(20);
            if (!stoppedAtHandshake) throw new Exception("test did not reach handshake cancellation");
            if (canceled.Snapshot().Error.Length != 0 || canceled.Snapshot().Desired)
                throw new Exception("manual cancellation became a persistent timeout error");
            Console.WriteLine("PASS stopping during handshake does not report false timeout");
        } finally { canceled.Stop(); }
    }

    static byte[] Read(Stream stream, int size)
    {
        byte[] bytes = new byte[size];
        int offset = 0;
        while (offset < size)
        {
            int count = stream.Read(bytes, offset, size - offset);
            if (count == 0) throw new EndOfStreamException();
            offset += count;
        }
        return bytes;
    }

    static void Wait(Func<bool> condition, string message)
    {
        if (!SpinWait.SpinUntil(condition, 6000)) throw new Exception(message);
    }

    static string ReadTrace(string path)
    {
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(file)) return reader.ReadToEnd();
    }

    static byte[] Hex(string value)
    {
        byte[] bytes = new byte[value.Length / 2];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(value.Substring(i * 2, 2), 16);
        return bytes;
    }

    static SslStream ConnectDirect(string[] config)
    {
        var client = new TcpClient(config[1], Int32.Parse(config[2]));
        var stream = new SslStream(client.GetStream(), false, delegate(object sender, System.Security.Cryptography.X509Certificates.X509Certificate certificate,
            System.Security.Cryptography.X509Certificates.X509Chain chain, SslPolicyErrors errors)
        {
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(certificate.GetRawCertData())).Replace("-", "")
                    .Equals(config[3], StringComparison.OrdinalIgnoreCase);
        });
        try
        {
            stream.AuthenticateAsClient("PcKvm session", null, SslProtocols.Tls12, false);
            stream.ReadTimeout = 5000;
            byte[] header = new byte[70];
            Buffer.BlockCopy(new byte[] { 68, 75, 86, 77, 2, 1 }, 0, header, 0, 6);
            Buffer.BlockCopy(Hex(config[5]), 0, header, 6, 16);
            Buffer.BlockCopy(Hex(config[4]), 0, header, 22, 32);
            stream.Write(header, 0, header.Length);
            if (Read(stream, 17)[0] != 0) throw new Exception("Direct TLS authentication failed");
            byte[] preamble = new byte[37];
            Buffer.BlockCopy(new byte[] { 80, 75, 86, 77, 1 }, 0, preamble, 0, 5);
            Buffer.BlockCopy(Hex(config[4]), 0, preamble, 5, 32);
            stream.Write(preamble, 0, preamble.Length);
            if (stream.ReadByte() != 1) throw new Exception("Direct local transport authentication failed");
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    static void Write(Stream stream, byte[] bytes) { stream.Write(bytes, 0, bytes.Length); }

    static void VerifyDirectRuntime(string trace)
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string remoteConfig = Path.Combine(root, "direct-config");
        Environment.SetEnvironmentVariable("PCKVM_COORD_MODE", "direct");
        File.WriteAllText(trace, "");
        var transport = new Transport(0);
        if (!transport.Start()) throw transport.StartError;
        var pointer = new PointerSession(transport.Send);
        int releases = 0, healthy = 0;
        var config = new Config { ConnectionMode = "WirelessTls", WirelessDeviceSerial = "tablet-A" };
        var coordinator = new ConnectionCoordinator(config, new AdbClient(Path.Combine(root, "adb.exe")), transport, pointer,
            delegate { Interlocked.Increment(ref releases); }, delegate { });
        coordinator.SetHealthProbe(delegate { return Interlocked.CompareExchange(ref healthy, 0, 0) == 1; });
        transport.SessionMessageReceived += delegate(long generation, byte type, byte[] payload)
        {
            pointer.Receive(generation, type, payload);
            if (type == Protocol.MsgDeviceGeometry)
            {
                coordinator.DirectGeometryPreparing(generation);
                pointer.Configure(100, 200, 0);
            }
            if (type == Protocol.MsgPong) Interlocked.Exchange(ref healthy, 1);
            coordinator.TryConfirmDirectReady(generation);
        };
        try
        {
            coordinator.Start();
            Wait(delegate { return File.Exists(remoteConfig) && ReadTrace(trace).Contains("nohup setsid"); }, "Direct deployment did not complete");
            string[] settings = File.ReadAllLines(remoteConfig);
            string initialTrace = ReadTrace(trace);
            if (initialTrace.Contains(" reverse ")) throw new Exception("Direct bootstrap invoked adb reverse");
            long oldGeneration;
            using (SslStream first = ConnectDirect(settings))
            {
                oldGeneration = coordinator.Generation;
                if (coordinator.IsReady) throw new Exception("TLS alone marked the direct session ready");
                Write(first, new byte[] { 0x10, 100, 0, 200, 0, 0 });
                byte[] geometry = Read(first, 10);
                Write(first, new byte[] { 0x0d, geometry[1], geometry[2], geometry[3], geometry[4] });
                if (coordinator.IsReady) throw new Exception("Pointer readiness bypassed heartbeat");
                Write(first, Protocol.EncodePong(1));
                if (Read(first, 5)[0] != Protocol.MsgSessionReady) throw new Exception("Direct READY not written over TLS");
                Wait(delegate { return coordinator.IsReady; }, "Direct session never reached ready");
            }
            Wait(delegate { return !coordinator.IsReady && coordinator.State.Contains("等待重新连接"); }, "EOF did not revoke readiness");
            Environment.SetEnvironmentVariable("PCKVM_COORD_MODE", "nonadb");
            Interlocked.Exchange(ref healthy, 0);
            using (SslStream second = ConnectDirect(settings))
            {
                if (coordinator.Generation <= oldGeneration || coordinator.IsReady) throw new Exception("Direct reconnect reused old state");
                if (ReadTrace(trace) != initialTrace) throw new Exception("Direct reconnect called ADB");
                coordinator.Disconnect();
                if (second.ReadByte() != Protocol.MsgStopDaemon) throw new Exception("Direct disconnect did not send STOP");
            }
            if (releases == 0) throw new Exception("Direct disconnect did not release PC input");
            Console.WriteLine("PASS direct TLS readiness, generation rollover, ADB-free reconnect and STOP");
        }
        finally { coordinator.Stop(); transport.Stop(); }
    }
}
