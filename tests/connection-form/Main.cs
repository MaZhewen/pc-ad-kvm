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
            DateTime settled = DateTime.UtcNow.AddSeconds(1);
            while (DateTime.UtcNow < settled) { Application.DoEvents(); Thread.Sleep(10); }
            if (devices.Items.Count < 2) throw new Exception("fake wireless device was not listed");
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
