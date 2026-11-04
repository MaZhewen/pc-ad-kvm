using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using PcKvm;

class MainClass
{
    static int inputConnections, clipboardConnections;
    const string InstanceId = "0123456789abcdef1032547698badcfe";

    static string KeyName(DirectTlsCertificate certificate)
    {
        using (var key = (RSACng)certificate.Certificate.GetRSAPrivateKey()) return key.Key.KeyName;
    }

    static void TestCertificateRecovery()
    {
        var info = new ProcessStartInfo(typeof(MainClass).Assembly.Location, "--hold-test-key");
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        using (Process holder = Process.Start(info))
        {
            string crashedKey = null;
            try
            {
                var line = holder.StandardOutput.ReadLineAsync();
                if (!line.Wait(5000) || string.IsNullOrEmpty(line.Result)) throw new Exception("Test key was not created");
                crashedKey = line.Result;
                using (var live = new DirectTlsCertificate())
                {
                    if (KeyName(live) == crashedKey || !CngKey.Exists(crashedKey))
                        throw new Exception("One gateway reclaimed another live gateway's key");
                }
                holder.Kill();
                if (!holder.WaitForExit(5000) || !CngKey.Exists(crashedKey))
                    throw new Exception("Abrupt exit did not leave a recovery fixture");
                using (var recovery = new DirectTlsCertificate()) { }
                if (CngKey.Exists(crashedKey)) throw new Exception("Next gateway did not reclaim the stale key");
            }
            finally
            {
                if (!holder.HasExited)
                {
                    holder.StandardInput.Close();
                    holder.WaitForExit(5000);
                }
                if (crashedKey != null && CngKey.Exists(crashedKey))
                {
                    // Only a fresh lease may reclaim the failed test's stale slot.
                    using (var recovery = new DirectTlsCertificate()) { }
                }
            }
        }
        Console.WriteLine("PASS certificate cleanup, live key isolation and crash recovery");
    }

    static void Echo(TcpListener listener, bool input)
    {
        new Thread(delegate()
        {
            try
            {
                while (true)
                {
                    TcpClient client = listener.AcceptTcpClient();
                    if (input) Interlocked.Increment(ref inputConnections);
                    else Interlocked.Increment(ref clipboardConnections);
                    new Thread(delegate()
                    {
                        using (client)
                        {
                            try
                            {
                                NetworkStream stream = client.GetStream();
                                byte[] bytes = new byte[8192];
                                int count;
                                while ((count = stream.Read(bytes, 0, bytes.Length)) > 0)
                                    stream.Write(bytes, 0, count);
                            }
                            catch (IOException) { }
                        }
                    }) { IsBackground = true }.Start();
                }
            }
            catch (SocketException) { }
        }) { IsBackground = true }.Start();
    }

    static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--hold-test-key")
        {
            using (var certificate = new DirectTlsCertificate())
            {
                Console.WriteLine(KeyName(certificate));
                Console.Out.Flush();
                Console.ReadLine();
            }
            return 0;
        }
        AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs eventArgs)
        {
            Exception failure = eventArgs.ExceptionObject as Exception;
            if (failure != null) Console.Error.WriteLine("UNHANDLED " + failure.GetType().FullName + ": " + failure.Message + " at " + failure.StackTrace);
        };
        TcpListener input = new TcpListener(IPAddress.Loopback, 0);
        TcpListener clipboard = new TcpListener(IPAddress.Loopback, 0);
        DirectNetworkGateway gateway = null;
        DirectNetworkGateway inputOnly = null;
        try
        {
            if (BitConverter.ToString(Protocol.EncodeSessionReady(0x78563412)) != "12-12-34-56-78"
                || Protocol.PayloadLength(0x12) != 4)
                throw new Exception("SESSION_READY is not a little-endian geometry epoch");
            byte[] token = new byte[32];
            for (int i = 0; i < token.Length; i++) token[i] = (byte)i;
            input.Start(); clipboard.Start();
            Echo(input, true); Echo(clipboard, false);
            int inputPort = ((IPEndPoint)input.LocalEndpoint).Port;
            int clipboardPort = ((IPEndPoint)clipboard.LocalEndpoint).Port;
            gateway = new DirectNetworkGateway(IPAddress.Loopback, inputPort, clipboardPort, InstanceId, token);
            inputOnly = new DirectNetworkGateway(IPAddress.Loopback, inputPort, null, InstanceId, token);
            Console.WriteLine("CertificateRequest created the session certificate");
            gateway.Start();
            inputOnly.Start();
            ProcessStartInfo info = new ProcessStartInfo(args[0], "-cp \"" + args[1] + "\" TestMain "
                + gateway.Port + " " + gateway.CertificateSha256 + " " + inputOnly.Port + " " + inputOnly.CertificateSha256);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardInput = true;
            using (Process java = Process.Start(info))
            {
                string line;
                while ((line = java.StandardOutput.ReadLine()) != null)
                {
                    if (line == "DISPOSE")
                    {
                        Stopwatch timer = Stopwatch.StartNew();
                        gateway.Dispose();
                        if (timer.ElapsedMilliseconds > 1500) throw new Exception("Dispose exceeded 1500 ms");
                        java.StandardInput.WriteLine("DISPOSED"); java.StandardInput.Flush();
                    }
                    else if (line == "ENABLE_OPTIONAL" || line == "DISABLE_OPTIONAL")
                    {
                        inputOnly.SetClipboardPort(line == "ENABLE_OPTIONAL" ? (int?)clipboardPort : null);
                        java.StandardInput.WriteLine("CAPABILITY_CHANGED");
                        java.StandardInput.Flush();
                    }
                    else Console.WriteLine(line);
                }
                java.WaitForExit();
                if (java.ExitCode != 0) throw new Exception("Java TLS assertions failed");
            }
            if (inputConnections != 3 || clipboardConnections != 3)
                throw new Exception("Unauthorized or duplicate client reached the backend: " + inputConnections + "/" + clipboardConnections);
            Console.WriteLine("PASS backend isolation and prompt gateway disposal");
            for (int attempt = 0; attempt < 8; attempt++)
            {
                using (var raced = new DirectNetworkGateway(IPAddress.Loopback, inputPort, clipboardPort, InstanceId, token))
                {
                    raced.Start();
                    int racePort = raced.Port;
                    Thread connector = new Thread(delegate()
                    {
                        for (int i = 0; i < 30; i++)
                        {
                            try { using (TcpClient client = new TcpClient()) client.Connect(IPAddress.Loopback, racePort); }
                            catch (SocketException) { break; }
                        }
                    });
                    connector.Start();
                    Thread.Sleep(2);
                    raced.Dispose();
                    connector.Join();
                }
            }
            Thread.Sleep(100);
            Console.WriteLine("PASS accepting and disposing race");
            TestCertificateRecovery();
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (gateway != null) gateway.Dispose();
            if (inputOnly != null) inputOnly.Dispose();
            input.Stop();
            clipboard.Stop();
        }
    }
}
