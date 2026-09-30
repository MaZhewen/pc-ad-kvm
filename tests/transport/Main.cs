using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Reflection;
using System.Runtime.InteropServices;
using PcKvm;
class TransportTest
{
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetHandleInformation(IntPtr handle, out uint flags);
    static bool ListenerNotInherited(Transport transport) {
        var field=typeof(Transport).GetField("_listener",BindingFlags.Instance|BindingFlags.NonPublic);
        var listener=(TcpListener)field.GetValue(transport);
        uint flags;
        return GetHandleInformation(listener.Server.Handle,out flags) && (flags&1)==0;
    }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
    static int Main()
    {
        var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();
        int port = ((IPEndPoint)occupied.LocalEndpoint).Port;
        var transport = new Transport(port);
        var received = new ManualResetEvent(false);
        transport.MessageReceived += delegate(byte type, byte[] payload) { if (type == Protocol.MsgPing) received.Set(); };
        try
        {
            Check(transport.Start(), "occupied-port startup");
            Check(ListenerNotInherited(transport), "listener handle is not inherited by adb server");
            Check(transport.ListeningPort > 0 && transport.ListeningPort != port, "allocated alternate port");
            DeviceLauncher.LocalPort = transport.ListeningPort;
            Check(DeviceLauncher.TunnelArguments == "reverse tcp:27183 tcp:" + transport.ListeningPort, "initial/reconnect tunnel targets actual port");
            using (var client = new TcpClient())
            {
                client.Connect(IPAddress.Loopback, transport.ListeningPort);
                byte[] frame = Protocol.EncodePing(123);
                client.GetStream().Write(frame, 0, frame.Length);
                Check(received.WaitOne(2000), "fallback connection receives protocol frame");
            }
            transport.Stop();
            occupied.Stop();
            transport = new Transport(port);
            Check(transport.Start() && transport.ListeningPort == port, "free preferred port retained");
            transport.Stop();
            transport = new Transport(27183);
            Check(transport.Start(), "startup against real local port 27183");
            Console.WriteLine("Actual listening port: " + transport.ListeningPort);
            transport.Stop();

            transport = new Transport(0);
            byte[] token = new byte[32];
            for (int i = 0; i < token.Length; i++) token[i] = (byte)(i + 1);
            transport.ConfigureSession(token, 42);
            int connected = 0, disconnected = 0;
            transport.Connected += delegate { Interlocked.Increment(ref connected); };
            transport.Disconnected += delegate { Interlocked.Increment(ref disconnected); };
            Check(transport.Start(), "handshake listener starts");
            using (TcpClient wrong = new TcpClient())
            {
                wrong.Connect(IPAddress.Loopback, transport.ListeningPort);
                byte[] bad = Preamble(token);
                bad[5] ^= 0x7f;
                wrong.GetStream().Write(bad, 0, bad.Length);
            }
            Thread.Sleep(100);
            Check(connected == 0, "wrong token rejected before Connected");
            using (TcpClient old = new TcpClient())
            {
                old.Connect(IPAddress.Loopback, transport.ListeningPort);
                byte[] legacy = Protocol.EncodePing(1);
                old.GetStream().Write(legacy, 0, legacy.Length);
                Thread.Sleep(2200);
                Check(connected == 0, "legacy injector without preamble times out");
            }
            using (TcpClient good = new TcpClient())
            {
                good.Connect(IPAddress.Loopback, transport.ListeningPort);
                byte[] preamble = Preamble(token);
                good.GetStream().Write(preamble, 0, 7);
                good.GetStream().Write(preamble, 7, preamble.Length - 7);
                good.ReceiveTimeout = 2000;
                Check(good.GetStream().ReadByte() == 1, "fragmented preamble receives acknowledgement");
                Check(SpinWait.SpinUntil(delegate { return connected == 1; }, 2000), "valid handshake publishes Connected");
                transport.CloseSession();
                Check(SpinWait.SpinUntil(delegate { return disconnected == 1; }, 2000), "closed session publishes one disconnect");
            }
            using (TcpClient next = new TcpClient())
            {
                next.Connect(IPAddress.Loopback, transport.ListeningPort);
                byte[] preamble = Preamble(token);
                next.GetStream().Write(preamble, 0, preamble.Length);
                next.ReceiveTimeout = 2000;
                Check(next.GetStream().ReadByte() == 1, "listener accepts next verified session");
            }
            return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL: " + e.Message); return 1; }
        finally { transport.Stop(); occupied.Stop(); received.Close(); }
    }
    static byte[] Preamble(byte[] token)
    {
        byte[] value = new byte[37];
        value[0] = (byte)'P'; value[1] = (byte)'K'; value[2] = (byte)'V'; value[3] = (byte)'M'; value[4] = 1;
        Array.Copy(token, 0, value, 5, 32);
        return value;
    }
}
