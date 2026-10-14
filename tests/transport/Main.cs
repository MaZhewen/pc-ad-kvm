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
        var ordered = new FrameQueue(4);
        ordered.Add(Protocol.EncodePointer(1, 1, 10, 10), 100);
        ordered.Add(Protocol.EncodePointer(1, 2, 20, 20), 120);
        ordered.Add(Protocol.EncodeButton(1, 1), 130);
        ordered.Add(Protocol.EncodePointer(1, 3, 30, 30), 140);
        long queuedAt;
        byte[] first = ordered.Take(out queuedAt);
        Check(Protocol.GetU32(first, 5) == 2 && queuedAt == 120,
            "coalesced pointer keeps newest enqueue time");
        Check(ordered.Take(out queuedAt)[0] == Protocol.MsgButton && queuedAt == 130,
            "button remains an ordering barrier");
        Check(Protocol.GetU32(ordered.Take(out queuedAt), 5) == 3 && queuedAt == 140,
            "pointer after button keeps its position");
        InputLatencyStats stats = new InputLatencyStats(1000);
        stats.RecordWriting(Protocol.EncodePing(7), 95, 2, 100);
        stats.RecordWriteCompleted(100, 170);
        stats.RecordReceived(Protocol.MsgPong, Payload(Protocol.EncodePong(6)), 130);
        stats.RecordReceived(Protocol.MsgPong, Payload(Protocol.EncodePong(7)), 245);
        LatencySummary firstSummary = stats.TakeSummary();
        Check(firstSummary.Ping.Count == 1 && firstSummary.Ping.MaxMs == 145,
            "matching PONG measures transport round trip");
        Check(firstSummary.PingQueue.Count == 1 && firstSummary.PingQueue.MaxMs == 5
            && firstSummary.Write.Count == 1 && firstSummary.Write.MaxMs == 70,
            "heartbeat queue wait and socket write stall are distinct from round trip");
        Check(firstSummary.Queue.Count == 0, "heartbeat is excluded from input queue timing");
        stats.RecordWriting(Protocol.EncodePointer(2, 10, 100, 200), 195, 3, 200);
        stats.RecordReceived(Protocol.MsgPointerAck, Payload(Protocol.EncodePointer(1, 10, 100, 200)), 250);
        stats.RecordReceived(Protocol.MsgPointerAck, Payload(Protocol.EncodePointer(2, 10, 100, 200)), 280);
        LatencySummary secondSummary = stats.TakeSummary();
        Check(secondSummary.Pointer.Count == 1 && secondSummary.Pointer.MaxMs == 80,
            "pointer ACK measures send to UHID submission and ignores old epoch");
        Check(secondSummary.Queue.Count == 1 && secondSummary.Queue.MaxMs == 5 && secondSummary.MaxPending == 3,
            "input queue delay and backlog are measured separately");
        Check(stats.TakeSummary().Pointer.Count == 0, "summary resets window samples");
        stats.RecordWriting(Protocol.EncodePointer(3, 1, 1, 1), 300, 0, 300);
        Check(stats.TakeAndResetSummary().Queue.Count == 1,
            "disconnect summary retains the final input sample");
        stats.RecordReceived(Protocol.MsgPointerAck, Payload(Protocol.EncodePointer(3, 1, 1, 1)), 400);
        Check(stats.TakeSummary().Pointer.Count == 0, "session reset rejects stale acknowledgements");
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
            var writing = new ManualResetEvent(false);
            transport.FrameWriting += delegate(byte[] frame, long queued, int pending, long now)
            {
                if (frame[0] == Protocol.MsgPing && now >= queued && pending >= 0) writing.Set();
            };
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
                transport.Send(Protocol.EncodePing(42));
                Check(writing.WaitOne(2000), "writer exposes actual frame dispatch timing");
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
    static byte[] Payload(byte[] frame)
    {
        byte[] payload = new byte[frame.Length - 1];
        Array.Copy(frame, 1, payload, 0, payload.Length);
        return payload;
    }
}
