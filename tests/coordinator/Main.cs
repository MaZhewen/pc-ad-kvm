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
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL " + error); return 1; }
        finally { coordinator.Stop(); }
    }
}
