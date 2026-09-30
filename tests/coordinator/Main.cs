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
            if (!coordinator.State.Contains("设备身份变化")) throw new Exception("identity mismatch was not reported: " + coordinator.State);
            string commands = File.ReadAllText(trace);
            if (!commands.Contains("getprop ro.serialno")) throw new Exception("identity was not queried");
            if (commands.Contains(" push ") || commands.Contains(" reverse ") || commands.Contains("app_process"))
                throw new Exception("wrong device received deployment: " + commands);
            Console.WriteLine("PASS saved wireless identity blocks a manual selection before deployment");
            coordinator.Stop();
            File.WriteAllText(trace, "");
            ConnectionCoordinator probe = new ConnectionCoordinator(config,
                new AdbClient(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb.exe")), transport,
                new PointerSession(delegate(byte[] frame) { }), delegate { }, delegate(string status) { });
            Environment.SetEnvironmentVariable("PCKVM_COORD_MODE", "nonadb");
            if (probe.VerifyWirelessEndpoint("192.168.1.3:40001", CancellationToken.None))
                throw new Exception("non-ADB TCP service accepted");
            Console.WriteLine("PASS open TCP port without online ADB endpoint is rejected");
            File.WriteAllText(trace, "");
            Environment.SetEnvironmentVariable("PCKVM_COORD_MODE", "failedconnect");
            if (probe.VerifyWirelessEndpoint("192.168.1.3:40001", CancellationToken.None))
                throw new Exception("failed ADB connect accepted");
            if (File.ReadAllText(trace).Contains("disconnect 192.168.1.3:40001"))
                throw new Exception("failed scan disconnected shared ADB endpoint");
            Console.WriteLine("PASS failed ADB connect does not disconnect shared endpoint");
            File.WriteAllText(trace, "");
            Environment.SetEnvironmentVariable("PCKVM_COORD_MODE", "existingoffline");
            if (probe.VerifyWirelessEndpoint("192.168.1.3:40001", CancellationToken.None))
                throw new Exception("offline ADB endpoint accepted");
            if (File.ReadAllText(trace).Contains("disconnect 192.168.1.3:40001"))
                throw new Exception("pre-existing offline endpoint was removed");
            Console.WriteLine("PASS pre-existing offline endpoint remains untouched");
            Environment.SetEnvironmentVariable("PCKVM_COORD_MODE", "unauthorized");
            if (probe.VerifyWirelessEndpoint("192.168.1.3:40001", CancellationToken.None))
                throw new Exception("unauthorized ADB endpoint accepted");
            Console.WriteLine("PASS unauthorized endpoint is rejected");
            Environment.SetEnvironmentVariable("PCKVM_COORD_MODE", "mismatch");
            if (probe.VerifyWirelessEndpoint("192.168.1.3:40001", CancellationToken.None))
                throw new Exception("wrong device identity accepted");
            Console.WriteLine("PASS saved identity must match");
            Environment.SetEnvironmentVariable("PCKVM_COORD_MODE", "matching");
            if (probe.VerifyWirelessEndpoint("192.168.1.3:40002", CancellationToken.None))
                throw new Exception("different online endpoint accepted");
            Console.WriteLine("PASS exact endpoint must appear in devices list");
            if (!probe.VerifyWirelessEndpoint("192.168.1.3:40001", CancellationToken.None))
                throw new Exception("matching endpoint rejected");
            commands = File.ReadAllText(trace);
            if (commands.Contains(" push ") || commands.Contains(" reverse ") || commands.Contains("app_process"))
                throw new Exception("scan deployed KVM: " + commands);
            Console.WriteLine("PASS matching endpoint is verified without KVM deployment");
            config.WirelessDeviceSerial = "";
            Environment.SetEnvironmentVariable("PCKVM_COORD_MODE", "unknownidentity");
            if (probe.VerifyWirelessEndpoint("192.168.1.3:40001", CancellationToken.None))
                throw new Exception("unknown identity was auto-verified");
            Console.WriteLine("PASS scan requires readable identity for first binding");
            Environment.SetEnvironmentVariable("PCKVM_COORD_MODE", "matching");
            if (!probe.VerifyWirelessEndpoint("192.168.1.3:40001", CancellationToken.None))
                throw new Exception("first binding with readable identity was rejected");
            Console.WriteLine("PASS first binding accepts readable identity");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL " + error); return 1; }
        finally { coordinator.Stop(); }
    }
}
