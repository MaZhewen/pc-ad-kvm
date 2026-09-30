using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using PcKvm;

class PortScanTests
{
    static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }

    static int Main()
    {
        try
        {
            IPAddress address;
            Check(WirelessPortScanner.TryParseAddress("10.222.240.129", out address)
                && address.ToString() == "10.222.240.129", "bare IPv4 accepted");
            Check(WirelessPortScanner.TryParseAddress("10.222.240.129:39123", out address)
                && address.ToString() == "10.222.240.129", "port suffix can be scanned again");
            Check(!WirelessPortScanner.TryParseAddress("10.222.240", out address)
                && !WirelessPortScanner.TryParseAddress("example.org", out address)
                && !WirelessPortScanner.TryParseAddress("10.222.240.129 -s other", out address)
                && !WirelessPortScanner.TryParseAddress("010.222.240.129", out address),
                "only strict IPv4 text accepted");

            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int openPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            TcpListener closed = new TcpListener(IPAddress.Loopback, 0);
            closed.Start();
            int closedPort = ((IPEndPoint)closed.LocalEndpoint).Port;
            closed.Stop();
            try
            {
                List<int> candidates = new List<int>();
                WirelessPortScanner.ScanResult result = WirelessPortScanner.Scan(IPAddress.Loopback,
                    new int[] { closedPort, openPort }, delegate(int port, CancellationToken token)
                    {
                        lock (candidates) candidates.Add(port);
                        return port == openPort;
                    }, null, CancellationToken.None);
                Check(result.FoundPort == openPort && result.ScannedPorts == 2,
                    "local listener found without treating closed port as open");
                Check(candidates.Count == 1 && candidates[0] == openPort,
                    "candidate callback receives only the open port");
            }
            finally { listener.Stop(); }

            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                cancel.Cancel();
                WirelessPortScanner.ScanResult result = WirelessPortScanner.Scan(IPAddress.Loopback,
                    new int[] { 1, 2, 3 }, delegate(int port, CancellationToken token) { return false; }, null, cancel.Token);
                Check(result.Canceled && result.ScannedPorts == 0, "pre-canceled scan opens no sockets");
            }
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                int[] ports = WirelessPortScanner.OrderedPorts();
                Check(ports.Length == 65535 && ports[0] == 32768 && ports[ports.Length - 1] == 65535,
                    "full valid range is present and dynamic ports have priority");
                Stopwatch watch = Stopwatch.StartNew();
                Thread trigger = new Thread(new ThreadStart(delegate { Thread.Sleep(50); cancel.Cancel(); }));
                trigger.Start();
                WirelessPortScanner.ScanResult result = WirelessPortScanner.Scan(IPAddress.Parse("192.0.2.1"),
                    ports, delegate(int port, CancellationToken token) { return false; }, null, cancel.Token);
                trigger.Join();
                Check(result.Canceled && result.ScannedPorts < ports.Length && watch.ElapsedMilliseconds < 3000,
                    "in-flight cancellation stops bounded workers promptly");
            }
            TcpListener timedListener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                timedListener.Start();
                int port = ((IPEndPoint)timedListener.LocalEndpoint).Port;
                Stopwatch watch = Stopwatch.StartNew();
                WirelessPortScanner.ScanResult result = WirelessPortScanner.Scan(IPAddress.Loopback,
                    new int[] { port }, delegate(int candidate, CancellationToken token)
                    {
                        while (!token.IsCancellationRequested) Thread.Sleep(10);
                        return false;
                    }, null, CancellationToken.None, 100);
                Check(result.TimedOut && watch.ElapsedMilliseconds < 1000,
                    "overall deadline cancels an in-flight candidate verification");
            }
            finally { timedListener.Stop(); }
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL " + error); return 1; }
    }
}
