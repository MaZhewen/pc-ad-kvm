using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using PcKvm;

internal static class ProbeHost
{
    static volatile bool stopping;

    static int Main(string[] args)
    {
        int durationSeconds = int.Parse(args[1]);
        byte[] token = new byte[32];
        using (var random = RandomNumberGenerator.Create()) random.GetBytes(token);
        var backend = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            backend.Start();
            int backendPort = ((IPEndPoint)backend.LocalEndpoint).Port;
            using (var gateway = new DirectNetworkGateway(IPAddress.Parse(args[0]), backendPort, null, args[2], token))
            {
                gateway.Start();
                // Only the runner reads this private stdout line and forwards it over ADB stdin.
                Console.WriteLine("{0} {1} {2}", gateway.Port, gateway.CertificateSha256, BitConverter.ToString(token).Replace("-", ""));
                Console.Out.Flush();
                new Thread(delegate()
                {
                    try { Console.ReadLine(); }
                    catch (IOException) { }
                    finally { stopping = true; }
                }) { IsBackground = true }.Start();
                var elapsed = Stopwatch.StartNew();
                int connections = 0;
                int replies = 0;
                while (!stopping && elapsed.Elapsed.TotalSeconds < durationSeconds)
                {
                    if (!backend.Pending())
                    {
                        Thread.Sleep(50);
                        continue;
                    }
                    using (TcpClient client = backend.AcceptTcpClient())
                    {
                        Console.WriteLine("{0:o} connect {1}", DateTimeOffset.Now, ++connections);
                        client.ReceiveTimeout = 2500;
                        NetworkStream stream = client.GetStream();
                        var reader = new BinaryReader(stream);
                        try
                        {
                            while (!stopping && elapsed.Elapsed.TotalSeconds < durationSeconds)
                            {
                                byte[] message = reader.ReadBytes(8);
                                if (message.Length != 8) break;
                                stream.Write(message, 0, message.Length);
                                Console.WriteLine("{0:o} echo connection={1} pid={2} sequence={3}",
                                    DateTimeOffset.Now, connections, IPAddress.NetworkToHostOrder(BitConverter.ToInt32(message, 0)),
                                    IPAddress.NetworkToHostOrder(BitConverter.ToInt32(message, 4)));
                                replies++;
                            }
                        }
                        catch (IOException) { }
                        Console.WriteLine("{0:o} disconnect {1}", DateTimeOffset.Now, connections);
                    }
                }
                return replies > 0 ? 0 : 1;
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally { backend.Stop(); }
    }
}
