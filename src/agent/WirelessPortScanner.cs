using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace PcKvm
{
    /** TCP candidate discovery for one explicit IPv4 address. No ADB or UI work happens here. */
    public static class WirelessPortScanner
    {
        const int WorkerLimit = 128;
        const int PortTimeoutMs = 300;
        const int ScanTimeoutMs = 180000;

        public sealed class ScanResult
        {
            public int FoundPort;
            public int ScannedPorts;
            public bool Canceled;
            public bool TimedOut;
        }

        public static bool TryParseAddress(string text, out IPAddress address)
        {
            address = null;
            if (String.IsNullOrEmpty(text) || text != text.Trim()) return false;
            WirelessEndpoint endpoint;
            if (!WirelessEndpoint.TryParse(text.IndexOf(':') >= 0 ? text : text + ":1", out endpoint))
                return false;
            if (!IPAddress.TryParse(endpoint.Address, out address)
                || address.AddressFamily != AddressFamily.InterNetwork
                || address.ToString() != endpoint.Address)
            { address = null; return false; }
            return true;
        }

        public static int[] OrderedPorts()
        {
            int[] ports = new int[65535];
            int i = 0;
            for (int port = 32768; port <= 60999; port++) ports[i++] = port;
            for (int port = 1; port < 32768; port++) ports[i++] = port;
            for (int port = 61000; port <= 65535; port++) ports[i++] = port;
            return ports;
        }

        public static ScanResult Scan(IPAddress address, int[] ports, Func<int, CancellationToken, bool> onOpen,
                                      Action<int, int> onProgress, CancellationToken cancel)
        {
            return Scan(address, ports, onOpen, onProgress, cancel, ScanTimeoutMs);
        }

        internal static ScanResult Scan(IPAddress address, int[] ports, Func<int, CancellationToken, bool> onOpen,
                                        Action<int, int> onProgress, CancellationToken cancel, int maxDurationMs)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork)
                throw new ArgumentException("IPv4 address required", "address");
            if (ports == null || onOpen == null) throw new ArgumentNullException(ports == null ? "ports" : "onOpen");
            if (maxDurationMs < 1) throw new ArgumentOutOfRangeException("maxDurationMs");
            foreach (int port in ports) if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException("ports");
            ScanResult result = new ScanResult();
            if (cancel.IsCancellationRequested) { result.Canceled = true; return result; }
            if (ports.Length == 0) return result;

            int next = -1, scanned = 0, found = 0, stopped = 0;
            Exception callbackError = null;
            object callbackGate = new object();
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                deadline.CancelAfter(maxDurationMs);
                CancellationToken scanToken = deadline.Token;
                int count = Math.Min(WorkerLimit, ports.Length);
                Thread[] workers = new Thread[count];
                for (int i = 0; i < workers.Length; i++)
                {
                    workers[i] = new Thread(new ThreadStart(delegate
                    {
                        while (!scanToken.IsCancellationRequested && Interlocked.CompareExchange(ref stopped, 0, 0) == 0
                               && Interlocked.CompareExchange(ref found, 0, 0) == 0)
                        {
                            int index = Interlocked.Increment(ref next);
                            if (index >= ports.Length) return;
                            int port = ports[index];
                            bool open = Probe(address, port);
                            int done = Interlocked.Increment(ref scanned);
                            if (open && !scanToken.IsCancellationRequested && Interlocked.CompareExchange(ref found, 0, 0) == 0)
                            {
                                try
                                {
                                    if (onOpen(port, scanToken)) Interlocked.CompareExchange(ref found, port, 0);
                                }
                                catch (Exception error)
                                {
                                    lock (callbackGate) if (callbackError == null) callbackError = error;
                                    Interlocked.Exchange(ref stopped, 1);
                                }
                            }
                            if (onProgress != null && done % 512 == 0)
                            {
                                try { onProgress(done, ports.Length); }
                                catch (Exception error)
                                {
                                    lock (callbackGate) if (callbackError == null) callbackError = error;
                                    Interlocked.Exchange(ref stopped, 1);
                                }
                            }
                        }
                    }));
                    workers[i].IsBackground = true;
                    workers[i].Name = "pckvm-port-scan";
                    workers[i].Start();
                }
                foreach (Thread worker in workers) worker.Join();
                if (callbackError != null) throw new InvalidOperationException("Port scan callback failed", callbackError);
                result.FoundPort = found;
                result.ScannedPorts = scanned;
                result.Canceled = cancel.IsCancellationRequested;
                result.TimedOut = found == 0 && !result.Canceled && deadline.IsCancellationRequested;
            }
            if (onProgress != null) onProgress(scanned, ports.Length);
            return result;
        }

        static bool Probe(IPAddress address, int port)
        {
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                WaitHandle wait = null;
                try
                {
                    IAsyncResult pending = socket.BeginConnect(address, port, null, null);
                    wait = pending.AsyncWaitHandle;
                    if (!wait.WaitOne(PortTimeoutMs)) return false;
                    socket.EndConnect(pending);
                    return socket.Connected;
                }
                catch (SocketException) { return false; }
                catch (ObjectDisposedException) { return false; }
                finally { if (wait != null) wait.Close(); }
            }
        }
    }
}
