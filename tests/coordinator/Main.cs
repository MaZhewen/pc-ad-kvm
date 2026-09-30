using System;
using System.IO;
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
            if (status == "等待指针与心跳就绪…") {
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
}
