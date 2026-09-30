using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using PcKvm;

class ConnectionFormTests
{
    static T Find<T>(Control parent, string name) where T : Control
    {
        Control[] matches = parent.Controls.Find(name, true);
        return matches.Length == 1 ? matches[0] as T : null;
    }

    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }

    static void PumpUntil(Func<bool> condition, int milliseconds)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < deadline && !condition())
        { Application.DoEvents(); Thread.Sleep(10); }
    }

    static string ReadTrace(string path)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(stream)) return reader.ReadToEnd();
            }
            catch (IOException) { Thread.Sleep(10); }
        }
        throw new IOException("fake ADB trace remained locked");
    }

    static Rectangle BoundsIn(Control ancestor, Control control)
    {
        Point topLeft = ancestor.PointToClient(control.Parent.PointToScreen(control.Location));
        return new Rectangle(topLeft, control.Size);
    }

    [STAThread]
    static int Main()
    {
        string folder = AppDomain.CurrentDomain.BaseDirectory;
        string tracePath = Path.Combine(folder, "trace.txt");
        Environment.SetEnvironmentVariable("PCKVM_COORD_TRACE", tracePath);
        Environment.SetEnvironmentVariable("PCKVM_COORD_MODE", "matching");
        Environment.SetEnvironmentVariable("PCKVM_COORD_ENDPOINT", "127.0.0.1:40001");
        Environment.SetEnvironmentVariable("PCKVM_COORD_SERVICE", "127.0.0.1:40001");
        File.WriteAllText(tracePath, "");
        Config config = new Config();
        config.ConnectionMode = "WirelessTls";
        config.WirelessDeviceSerial = "tablet-A";
        Transport transport = new Transport(0);
        ConnectionCoordinator connection = new ConnectionCoordinator(config,
            new AdbClient(Path.Combine(folder, "adb.exe")), transport,
            new PointerSession(delegate(byte[] frame) { }), delegate { }, delegate(string message) { });
        bool controlling = false;
        ConnectionForm form = new ConnectionForm(connection, config, delegate { return controlling; });
        try
        {
            form.ShowInTaskbar = false; form.Opacity = 0; form.Show();
            ComboBox mode = Find<ComboBox>(form, "ConnectionMode");
            ComboBox devices = Find<ComboBox>(form, "ConnectionDevices");
            TextBox endpoint = Find<TextBox>(form, "ConnectionEndpoint");
            TextBox pairIp = Find<TextBox>(form, "PairIp");
            TextBox pairPort = Find<TextBox>(form, "PairPort");
            TextBox pairCode = Find<TextBox>(form, "PairCode");
            Button pair = Find<Button>(form, "PairDevice");
            Check(mode != null && devices != null && endpoint != null && pairIp != null
                && pairPort != null && pairCode != null && pair != null,
                "manual connection and pairing fields are present");
            Check(form.Controls.Find("ScanPorts", true).Length == 0
                && form.Controls.Find("CancelScan", true).Length == 0
                && form.Controls.Find("FindPairPort", true).Length == 0,
                "no port-scan or port-discovery actions remain");
            Check(form.BackColor == Color.FromArgb(244, 247, 251)
                && form.Font.Name == "Microsoft YaHei UI"
                && form.Controls.Find("ConnectionCard", true).Length == 1
                && Find<Panel>(form, "ConnectionCard").BackColor == Color.White,
                "connection window matches Settings colors and cards");
            Size normalSize = form.ClientSize;
            form.ClientSize = new Size(560, 460);
            Application.DoEvents();
            Panel viewport = Find<Panel>(form, "ConnectionContentViewport");
            Check(viewport != null && viewport.AutoScroll
                && form.ClientRectangle.Contains(BoundsIn(form, Find<Button>(form, "ConnectDevice"))),
                "short screen keeps actions visible while cards scroll");
            form.ClientSize = normalSize;
            Application.DoEvents();
            string preview = Environment.GetEnvironmentVariable("PCKVM_CAPTURE_CONNECTION_UI");
            if (!String.IsNullOrEmpty(preview))
            {
                using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    bitmap.Save(preview);
                }
            }
            PumpUntil(delegate { return devices.Items.Count > 1; }, 3000);
            Check(devices.Items.Count > 1, "online device list remains available");
            ConnectionSnapshot ready = new ConnectionSnapshot { Ready = true, Desired = true,
                Device = "Test tablet", Mode = "WirelessTls", Status = "ready", Error = "" };
            form.RenderState(ready);
            Check(Find<Label>(form, "DeviceSummary").Text.Contains("当前控制电脑")
                && Find<Label>(form, "DeviceSummary").Text.Contains("Test tablet")
                && !Find<Button>(form, "ConnectDevice").Visible
                && Find<Button>(form, "DisconnectDevice").Visible
                && !Find<Panel>(form, "DeviceCard").Visible,
                "ready view identifies device and offers disconnect instead of setup");
            controlling = true;
            form.RenderState(ready);
            Check(Find<Label>(form, "DeviceSummary").Text.Contains("正在控制 Android")
                && Find<Button>(form, "ReturnToComputer").Visible,
                "takeover changes visible control target");
            bool returnRequested = false;
            form.ReturnToComputerRequested += delegate { returnRequested = true; };
            Find<Button>(form, "ReturnToComputer").PerformClick();
            Check(returnRequested, "return action is delivered to application controller");
            form.RenderState(ready);
            string readyPreview = Environment.GetEnvironmentVariable("PCKVM_CAPTURE_CONNECTION_UI");
            if (!String.IsNullOrEmpty(readyPreview))
                using (Bitmap bitmap = new Bitmap(form.Width, form.Height)) {
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    bitmap.Save(readyPreview + ".ready.png");
                }
            controlling = false;
            form.RenderState(new ConnectionSnapshot { Desired = true, Attempting = true,
                Device = "", Mode = "Usb", Status = "connecting", Error = "" });
            Check(!Find<Button>(form, "ConnectDevice").Enabled
                && Find<Button>(form, "DisconnectDevice").Visible,
                "connection in progress prevents duplicate submission and allows cancellation");
            form.RenderState(new ConnectionSnapshot { Desired = true,
                Device = "", Mode = "Usb", Status = "waiting", Error = "failure detail" });
            Check(Find<Button>(form, "RetryConnection").Visible
                && Find<Label>(form, "ErrorDetails").Text.Contains("failure detail")
                && Find<Panel>(form, "ErrorCard").Visible,
                "retry state retains failure reason");
            connection.Disconnect();
            form.RenderState();
            Check(Find<Button>(form, "ConnectDevice").Enabled
                && !Find<Button>(form, "DisconnectDevice").Visible,
                "manual disconnect restores connect action and stops recovery");
            Check(!ReadTrace(tracePath).Contains("mdns services"),
                "opening connection window does not search ports");
            mode.SelectedIndex = 0;
            Check(!Find<Panel>(form, "ConnectionCard").Visible
                && !Find<Panel>(form, "PairCard").Visible,
                "USB hides wireless setup instead of showing disabled forms");
            Check(!endpoint.Enabled && !pairIp.Enabled && !pairPort.Enabled && !pairCode.Enabled && !pair.Enabled,
                "USB mode disables wireless fields");
            mode.SelectedIndex = 1;
            Check(!Find<Panel>(form, "PairCard").Visible,
                "first pairing is collapsed until requested");
            Find<Button>(form, "ShowPairing").PerformClick();
            Check(endpoint.Enabled && pairIp.Enabled && pairPort.Enabled && pairCode.Enabled && pair.Enabled,
                "wireless mode enables manual fields");

            pairIp.Text = "127.0.0.1"; pairPort.Text = ""; pairCode.Text = "001234";
            pair.PerformClick();
            Application.DoEvents();
            Check(pairCode.Text == "001234", "local validation preserves pairing code");
            Check(!ReadTrace(tracePath).Contains("pair "), "blank pairing port never calls adb pair");
            pairPort.Text = "37123"; pairCode.Text = "001234";
            pair.PerformClick();
            PumpUntil(delegate { return ReadTrace(tracePath).Contains("stdin=001234"); }, 5000);
            string trace = ReadTrace(tracePath);
            if (!trace.Contains("pair 127.0.0.1:37123") || !trace.Contains("stdin=001234")
                || trace.Contains(" push ") || trace.Contains(" reverse "))
                throw new Exception("manual pairing failed; commands="
                    + trace.Replace("stdin=001234", "stdin=<redacted>").Replace("\n", " | "));
            Console.WriteLine("PASS manual pairing keeps the code and does not deploy KVM");
            PumpUntil(delegate { return endpoint.Text == "127.0.0.1"; }, 3000);
            Check(endpoint.Text == "127.0.0.1", "successful pairing prefills only the connection IP");
            int completedPairs = 0;
            connection.PairCompleted += delegate { Interlocked.Increment(ref completedPairs); };
            Environment.SetEnvironmentVariable("PCKVM_COORD_PAIR_DELAY_MS", "400");
            pairCode.Text = "001234";
            pair.PerformClick();
            endpoint.Text = "127.0.0.1:40001";
            PumpUntil(delegate { return Interlocked.CompareExchange(ref completedPairs, 0, 0) > 0; }, 4000);
            Application.DoEvents();
            Check(endpoint.Text == "127.0.0.1:40001",
                "pair completion preserves a manually entered connection port");
            Environment.SetEnvironmentVariable("PCKVM_COORD_PAIR_DELAY_MS", null);
            endpoint.Text = "127.0.0.1";
            Button connect = Find<Button>(form, "ConnectDevice");
            connect.PerformClick();
            Check(Find<Label>(form, "ConnectionStatus").Text.Contains("连接端口")
                && !ReadTrace(tracePath).Contains("connect 127.0.0.1"),
                "connection requires a manually entered port");
            endpoint.Text = "127.0.0.1:40001";
            Application.DoEvents();
            Check(endpoint.Text == "127.0.0.1:40001", "manual connection endpoint remains editable");
            Environment.SetEnvironmentVariable("PCKVM_COORD_MODE", "failedconnect");
            connection.Start();
            connect.PerformClick();
            PumpUntil(delegate { return ReadTrace(tracePath).Contains("connect 127.0.0.1:40001"); }, 5000);
            Check(ReadTrace(tracePath).Contains("connect 127.0.0.1:40001"),
                "manual connection submits exactly the entered endpoint");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL " + error); return 1; }
        finally { form.Close(); form.Dispose(); connection.Stop(); }
    }
}
