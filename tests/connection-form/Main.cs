using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using PcKvm;

class ConnectionFormTests
{
    [STAThread]
    static int Main()
    {
        string folder = AppDomain.CurrentDomain.BaseDirectory;
        Environment.SetEnvironmentVariable("PCKVM_COORD_TRACE", Path.Combine(folder, "trace.txt"));
        Environment.SetEnvironmentVariable("PCKVM_COORD_MODE", "matching");
        Environment.SetEnvironmentVariable("PCKVM_COORD_ENDPOINT", "127.0.0.1:40001");
        Environment.SetEnvironmentVariable("PCKVM_COORD_SERVICE", "127.0.0.1:40001");
        Config config = new Config();
        config.ConnectionMode = "WirelessTls";
        config.WirelessDeviceSerial = "tablet-A";
        Transport transport = new Transport(0);
        ConnectionCoordinator connection = new ConnectionCoordinator(config,
            new AdbClient(Path.Combine(folder, "adb.exe")), transport,
            new PointerSession(delegate(byte[] frame) { }), delegate { }, delegate(string message) { });
        ConnectionForm form = new ConnectionForm(connection, config);
        try
        {
            form.ShowInTaskbar = false;
            form.Opacity = 0;
            form.Show();
            ComboBox endpoint = form.Controls.Find("ConnectionEndpoint", true)[0] as ComboBox;
            ComboBox devices = form.Controls.Find("ConnectionDevices", true)[0] as ComboBox;
            Button scan = form.Controls.Find("ScanPorts", true)[0] as Button;
            Button cancel = form.Controls.Find("CancelScan", true)[0] as Button;
            if (endpoint == null || devices == null || scan == null || cancel == null) throw new Exception("scan controls unavailable");
            Console.WriteLine("PASS scan and cancel controls are present");
            ComboBox mode = form.Controls.Find("ConnectionMode", true)[0] as ComboBox;
            TextBox pairIp = form.Controls.Find("PairIp", true)[0] as TextBox;
            ComboBox pairPort = form.Controls.Find("PairPort", true)[0] as ComboBox;
            TextBox pairCode = form.Controls.Find("PairCode", true)[0] as TextBox;
            Button pair = form.Controls.Find("PairDevice", true)[0] as Button;
            Button findPairPort = form.Controls.Find("FindPairPort", true)[0] as Button;
            if (mode == null || pairIp == null || pairPort == null || pairCode == null || pair == null || findPairPort == null)
                throw new Exception("first-pairing controls unavailable");
            DateTime settled = DateTime.UtcNow.AddSeconds(1);
            while (DateTime.UtcNow < settled) { Application.DoEvents(); Thread.Sleep(10); }
            if (devices.Items.Count < 2) throw new Exception("fake wireless device was not listed");
            mode.SelectedIndex = 0;
            if (pairIp.Enabled || pairPort.Enabled || pairCode.Enabled || pair.Enabled || findPairPort.Enabled)
                throw new Exception("USB mode left wireless pairing enabled");
            mode.SelectedIndex = 1;
            if (!pairIp.Enabled || !pairPort.Enabled || !pairCode.Enabled || !pair.Enabled || !findPairPort.Enabled)
                throw new Exception("wireless pairing was not enabled");
            Console.WriteLine("PASS USB and wireless mode switch controls pairing");
            string tracePath = Path.Combine(folder, "trace.txt");
            File.WriteAllText(tracePath, "");
            pairIp.Text = "127.0.0.1"; pairPort.Text = "37123"; pairCode.Text = "001234";
            pair.PerformClick();
            DateTime pairDeadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < pairDeadline && !File.ReadAllText(tracePath).Contains("stdin=001234"))
            { Application.DoEvents(); Thread.Sleep(10); }
            string pairTrace = File.ReadAllText(tracePath);
            if (!pairTrace.Contains("pair 127.0.0.1:37123") || !pairTrace.Contains("stdin=001234") || pairCode.Text.Length != 0)
                throw new Exception("manual pairing did not use separate fields and preserve code: " + pairTrace);
            if (pairTrace.Contains(" push ") || pairTrace.Contains(" reverse ")) throw new Exception("pairing deployed KVM");
            Console.WriteLine("PASS manual IP, port, and leading-zero code pair without deployment");
            Environment.SetEnvironmentVariable("PCKVM_COORD_PAIR_SERVICE", "127.0.0.1:38383");
            pairPort.Text = "";
            findPairPort.PerformClick();
            DateTime findDeadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < findDeadline && pairPort.Text != "38383")
            { Application.DoEvents(); Thread.Sleep(10); }
            if (pairPort.Text != "38383") throw new Exception("matching pairing service did not fill port");
            Console.WriteLine("PASS matching mDNS pairing port filled");
            Environment.SetEnvironmentVariable("PCKVM_COORD_PAIR_SERVICE", "127.0.0.1:38383;127.0.0.1:39393");
            pairPort.Text = "";
            findPairPort.PerformClick();
            DateTime multipleDeadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < multipleDeadline && findPairPort.Enabled == false)
            { Application.DoEvents(); Thread.Sleep(10); }
            if (pairPort.Text.Length != 0 || pairPort.Items.Count != 2)
                throw new Exception("multiple pairing ports were silently selected");
            Console.WriteLine("PASS multiple matching pairing ports require selection");
            Environment.SetEnvironmentVariable("PCKVM_COORD_PAIR_SERVICE", "192.168.9.9:38383");
            pairPort.Text = "";
            findPairPort.PerformClick();
            DateTime absentDeadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < absentDeadline && findPairPort.Enabled == false)
            { Application.DoEvents(); Thread.Sleep(10); }
            if (pairPort.Text.Length != 0) throw new Exception("other IP pairing port was selected");
            Console.WriteLine("PASS no matching service leaves manual port available");
            Environment.SetEnvironmentVariable("PCKVM_COORD_PAIR_SERVICE", "127.0.0.1:45454");
            File.WriteAllText(tracePath, "");
            pairPort.Text = ""; pairCode.Text = "002345";
            pair.PerformClick();
            DateTime automaticDeadline = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < automaticDeadline && !File.ReadAllText(tracePath).Contains("stdin=002345"))
            { Application.DoEvents(); Thread.Sleep(10); }
            string automaticTrace = File.ReadAllText(tracePath);
            if (!automaticTrace.Contains("pair 127.0.0.1:45454") || !automaticTrace.Contains("stdin=002345"))
                throw new Exception("blank port did not discover and pair: " + automaticTrace);
            while (DateTime.UtcNow < automaticDeadline && endpoint.Text != "127.0.0.1")
            { Application.DoEvents(); Thread.Sleep(10); }
            if (endpoint.Text != "127.0.0.1") throw new Exception("successful pairing did not prefill connect IP");
            Label pairingStatus = form.Controls.Find("ConnectionStatus", true)[0] as Label;
            while (DateTime.UtcNow < automaticDeadline && !pairingStatus.Text.Contains("配对完成"))
            { Application.DoEvents(); Thread.Sleep(10); }
            Console.WriteLine("PASS blank pairing port is discovered and successful pair prefills connect IP");
            pairPort.Text = "";
            Environment.SetEnvironmentVariable("PCKVM_COORD_MODE", "slowmdns");
            findPairPort.PerformClick();
            pairIp.Text = "127.0.0.2";
            mode.SelectedIndex = 0;
            DateTime staleDeadline = DateTime.UtcNow.AddMilliseconds(500);
            while (DateTime.UtcNow < staleDeadline) { Application.DoEvents(); Thread.Sleep(10); }
            if (pairPort.Text.Length != 0 || pairPort.Enabled) throw new Exception("stale lookup applied after IP and mode change");
            mode.SelectedIndex = 1;
            pairIp.Text = "127.0.0.1";
            Environment.SetEnvironmentVariable("PCKVM_COORD_MODE", "matching");
            Console.WriteLine("PASS changed IP and USB switch cancel pairing-port lookup");
            pairIp.Text = "127.0.0.1:38383";
            pairPort.Text = "";
            findPairPort.PerformClick();
            if (!pairingStatus.Text.Contains("IPv4") || !findPairPort.Enabled)
                throw new Exception("pairing IP accepted an IP:port value");
            Console.WriteLine("PASS pairing IP field rejects an embedded port");
            pairIp.Text = "127.0.0.1";
            File.WriteAllText(tracePath, "");
            Environment.SetEnvironmentVariable("PCKVM_COORD_MDNS_DELAY_MS", "500");
            Environment.SetEnvironmentVariable("PCKVM_COORD_PAIR_SERVICE", "127.0.0.1:45454");
            findPairPort.PerformClick();
            pairPort.Text = "46464";
            pairCode.Text = "003456";
            pair.PerformClick();
            DateTime manualDeadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < manualDeadline && !File.ReadAllText(tracePath).Contains("stdin=003456"))
            { Application.DoEvents(); Thread.Sleep(10); }
            DateTime callbackDeadline = DateTime.UtcNow.AddMilliseconds(800);
            while (DateTime.UtcNow < callbackDeadline) { Application.DoEvents(); Thread.Sleep(10); }
            string manualTrace = File.ReadAllText(tracePath);
            if (pairPort.Text != "46464" || !manualTrace.Contains("pair 127.0.0.1:46464")
                || !manualTrace.Contains("stdin=003456") || manualTrace.Contains("pair 127.0.0.1:45454"))
                throw new Exception("old lookup superseded a manual pairing: " + manualTrace);
            Environment.SetEnvironmentVariable("PCKVM_COORD_MDNS_DELAY_MS", null);
            Console.WriteLine("PASS manual port entry cancels stale lookup and code");
            Environment.SetEnvironmentVariable("PCKVM_COORD_PAIR_SERVICE", null);
            devices.SelectedIndex = 1;
            endpoint.Text = "127.0.0.1";
            scan.PerformClick();
            DateTime deadline = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < deadline && endpoint.Text != "127.0.0.1:40001")
            { Application.DoEvents(); Thread.Sleep(10); }
            if (endpoint.Text != "127.0.0.1:40001")
                throw new Exception("verified mDNS endpoint was not filled: " + endpoint.Text);
            if (devices.SelectedIndex != 0)
                throw new Exception("old selected device would override scanned endpoint");
            string trace = File.ReadAllText(Path.Combine(folder, "trace.txt"));
            if (trace.Contains(" push ") || trace.Contains(" reverse ") || trace.Contains("app_process"))
                throw new Exception("scan deployed KVM: " + trace);
            Console.WriteLine("PASS verified endpoint fills address without KVM deployment");
            endpoint.Text = "127.0.0.1";
            scan.PerformClick();
            Thread.Sleep(500); // let the worker finish while its UI callback remains queued
            cancel.PerformClick();
            Application.DoEvents();
            if (endpoint.Text != "127.0.0.1") throw new Exception("canceled result replaced user input");
            Console.WriteLine("PASS canceled queued result cannot overwrite input");

            ConnectionForm slow = new ConnectionForm(connection, config, 150);
            try
            {
                slow.ShowInTaskbar = false; slow.Opacity = 0; slow.Show();
                DateTime warmup = DateTime.UtcNow.AddSeconds(1);
                while (DateTime.UtcNow < warmup) { Application.DoEvents(); Thread.Sleep(10); }
                Environment.SetEnvironmentVariable("PCKVM_COORD_MODE", "slowmdns");
                ComboBox slowEndpoint = slow.Controls.Find("ConnectionEndpoint", true)[0] as ComboBox;
                Button slowScan = slow.Controls.Find("ScanPorts", true)[0] as Button;
                Label slowStatus = slow.Controls.Find("ConnectionStatus", true)[0] as Label;
                slowEndpoint.Text = "127.0.0.1";
                slowScan.PerformClick();
                DateTime timeout = DateTime.UtcNow.AddSeconds(2);
                while (DateTime.UtcNow < timeout && !slowStatus.Text.Contains("时间上限"))
                { Application.DoEvents(); Thread.Sleep(10); }
                if (!slowStatus.Text.Contains("时间上限"))
                    throw new Exception("slow mDNS exceeded scan deadline: " + slowStatus.Text);
                Console.WriteLine("PASS one deadline covers mDNS before TCP scanning");
            }
            finally { slow.Close(); slow.Dispose(); }
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL " + error); return 1; }
        finally { form.Close(); form.Dispose(); connection.Stop(); }
    }
}
