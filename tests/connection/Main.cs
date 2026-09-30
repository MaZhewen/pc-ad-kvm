using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using PcKvm;

class ConnectionTests
{
    static int checks;
    static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception(name);
        checks++;
        Console.WriteLine("PASS " + name);
    }

    static int Main()
    {
        try
        {
            List<AdbDevice> devices = AdbDevice.Parse("List of devices attached\n\nUSB-123 device usb:1-2 product:x model:Tablet transport_id:7\n192.168.1.5:39001 offline transport_id:8\nGUID._adb-tls-connect._tcp device model:Pad transport_id:9\nNOPE unauthorized\n");
            Check(devices.Count == 4, "devices header and blanks ignored");
            Check(devices[0].IsUsb && devices[0].IsOnline && devices[0].TransportId == 7, "USB metadata parsed");
            Check(!devices[1].IsOnline && !devices[1].IsUsb, "offline wireless not usable");
            Check(devices[2].Serial == "GUID._adb-tls-connect._tcp" && devices[2].IsOnline, "mDNS serial retained whole");
            Check(!devices[3].IsOnline, "unauthorized excluded");
            Check(ConnectionProfile.ChooseUsb(devices, "") != null && ConnectionProfile.ChooseUsb(devices, "").Serial == "USB-123", "only online USB auto selected");
            List<AdbDevice> twoUsb = AdbDevice.Parse("List of devices attached\nUSB-1 device usb:1-1\nUSB-2 device usb:1-2\n");
            Check(ConnectionProfile.ChooseUsb(twoUsb, "") == null, "multiple USB devices require selection");
            Check(ConnectionProfile.ChooseUsb(twoUsb, "USB-2").Serial == "USB-2", "saved USB identity selected exactly");
            Check(ConnectionProfile.ChooseUsb(twoUsb, "missing") == null, "missing saved target never falls back to another device");
            List<AdbDevice> twoWireless = AdbDevice.Parse("List of devices attached\n192.168.1.2:40000 device transport_id:1\n192.168.1.3:40001 device transport_id:2\n");
            Check(ConnectionProfile.ChooseWireless(twoWireless, "tablet-A", delegate(AdbDevice d) { return d.Serial == "192.168.1.3:40001" ? "tablet-A" : "tablet-B"; }).Serial == "192.168.1.3:40001", "wireless target selected by verified identity");
            Check(ConnectionProfile.ChooseWireless(twoWireless, "tablet-A", delegate(AdbDevice d) { return "tablet-A"; }) == null, "duplicate device identity requires selection");
            Check(ConnectionProfile.BackoffMilliseconds(5, 0, 0) == 5000 && ConnectionProfile.BackoffMilliseconds(5, 1, 0) == 10000 && ConnectionProfile.BackoffMilliseconds(5, 20, 20) == 60000, "serial exponential retry is capped");
            Check(ConnectionProfile.UsbRecoveryAction(2, 0, false) == 0
                && ConnectionProfile.UsbRecoveryAction(3, 0, false) == 1
                && ConnectionProfile.UsbRecoveryAction(6, 1, true) == 2
                && ConnectionProfile.UsbRecoveryAction(6, 1, false) == 0,
                "USB escalation follows existing guarded thresholds");
            HeartbeatLease lease = new HeartbeatLease(1000);
            lease.Connected(7, 0);
            lease.Sent(1);
            Check(!lease.Pong(6, 1, 100) && !lease.Pong(7, 2, 100), "old generation and unsent PONG ignored");
            Check(lease.Pong(7, 1, 100) && !lease.Pong(7, 1, 200), "valid PONG accepted once");
            Check(lease.Healthy(1900) && lease.Expired(2201), "idle session expires after valid PONG age");

            WirelessEndpoint endpoint;
            Check(WirelessEndpoint.TryParse("192.168.1.5:39123", out endpoint) && endpoint.Port == 39123, "IPv4 endpoint accepted");
            Check(!WirelessEndpoint.TryParse("192.168.1.5:39123 -s other", out endpoint), "arguments rejected in endpoint");
            Check(!WirelessEndpoint.TryParse("192.168.1.5:0", out endpoint), "invalid port rejected");
            Check(!WirelessEndpoint.TryParse("host.local:39123", out endpoint), "hostnames rejected");

            List<WirelessService> services = WirelessDiscovery.Parse("List of discovered mdns services\nadb-X._adb-tls-pairing._tcp. 192.168.1.5:40000\nadb-X._adb-tls-connect._tcp. 192.168.1.5:39123\n");
            Check(services.Count == 1 && services[0].Endpoint.ToString() == "192.168.1.5:39123", "pairing service not treated as connect service");
            List<WirelessService> columnServices = WirelessDiscovery.Parse("List of discovered mdns services\nadb-X _adb-tls-connect._tcp. 192.168.1.5:39124\n");
            Check(columnServices.Count == 1 && columnServices[0].Endpoint.Port == 39124, "separate mDNS service columns parsed");
            string folder = AppDomain.CurrentDomain.BaseDirectory;
            string capture = Path.Combine(folder, "capture.txt");
            Environment.SetEnvironmentVariable("PCKVM_FAKE_CAPTURE", capture);
            AdbClient adb = new AdbClient(Path.Combine(folder, "adb.exe"));
            AdbResult pair = adb.Execute(new string[] { "pair", "192.168.1.5:40000" }, null, 3000, "001234\n", CancellationToken.None);
            Check(pair.Success && File.ReadAllText(capture).Contains("stdin=001234"), "pair code sent on stdin with leading zero");
            AdbResult push = adb.Execute(new string[] { "push", "C:\\a b\\pckvm.jar", "/data/local/tmp/pckvm.jar" }, AdbTarget.ForSerial("GUID._adb-tls-connect._tcp"), 3000, null, CancellationToken.None);
            string recorded = File.ReadAllText(capture);
            Check(push.Success && recorded.Contains("arg0=-s") && recorded.Contains("arg1=GUID._adb-tls-connect._tcp") && recorded.Contains("arg3=C:\\a b\\pckvm.jar"), "target and spaced path passed as distinct arguments");
            adb.Execute(new string[] { "shell", "id" }, AdbTarget.ForTransport(7, "USB-123"), 3000, null, CancellationToken.None);
            Check(File.ReadAllText(capture).Contains("arg0=-t") && File.ReadAllText(capture).Contains("arg1=7"), "transport selector used");
            DeviceLauncher.UseClient(adb);
            DeviceLauncher.LocalPort = 27183;
            Check(DeviceLauncher.EnsureTunnel(AdbTarget.ForTransport(7, "USB-123"), CancellationToken.None), "targeted reverse succeeds");
            Check(File.ReadAllText(capture).Contains("arg0=-t") && File.ReadAllText(capture).Contains("arg2=reverse"), "reverse is explicitly targeted");
            Environment.SetEnvironmentVariable("PCKVM_FAKE_MODE", "existing-reverse");
            bool created;
            Check(DeviceLauncher.EnsureTunnel(AdbTarget.ForTransport(7, "USB-123"), CancellationToken.None, out created) && !created,
                "existing matching reverse is borrowed, not owned");
            Environment.SetEnvironmentVariable("PCKVM_FAKE_MODE", null);
            Environment.SetEnvironmentVariable("PCKVM_FAKE_MODE", "slow");
            AdbResult timeout = adb.Execute(new string[] { "devices", "-l" }, null, 150, null, CancellationToken.None);
            Check(timeout.TimedOut && !timeout.Success, "command timeout stops process");
            Environment.SetEnvironmentVariable("PCKVM_FAKE_MODE", "flood");
            AdbResult flood = adb.Execute(new string[] { "devices", "-l" }, null, 3000, null, CancellationToken.None);
            Check(flood.Success && flood.Stdout.Length <= 65536 && flood.Stderr.Length <= 65536, "both streams drained and bounded");
            Environment.SetEnvironmentVariable("PCKVM_FAKE_MODE", "slow");
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                cancel.CancelAfter(100);
                AdbResult canceled = adb.Execute(new string[] { "devices", "-l" }, null, 3000, null, cancel.Token);
                Check(canceled.Canceled && !canceled.Success, "cancellation stops command");
            }
            Console.WriteLine("TOTAL " + checks);
            return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL " + e); return 1; }
    }
}
