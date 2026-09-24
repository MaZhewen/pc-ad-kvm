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
            return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL: " + e.Message); return 1; }
        finally { transport.Stop(); occupied.Stop(); received.Close(); }
    }
}
