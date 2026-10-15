using System;
using System.IO;
using System.Collections.Generic;
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
    static int Main(string[] args)
    {
        try { CheckDispatchScheduling(); CheckWriterOrdering(); CheckWriterShutdown(); }
        catch (Exception e) { Console.WriteLine("FAIL: " + e.Message); return 1; }
        if (args.Length > 0 && args[0] == "--dispatch-only") return 0;
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
        InputLatencyStats burstStats = new InputLatencyStats(1000);
        burstStats.RecordWriting(Protocol.EncodePointer(4, 1, 10, 10), 100, 0, 100);
        burstStats.RecordWriting(Protocol.EncodePointer(4, 2, 20, 20), 110, 0, 110);
        burstStats.RecordReceived(Protocol.MsgPointerAck, Payload(Protocol.EncodePointer(4, 2, 20, 20)), 180);
        LatencySummary burstSummary = burstStats.TakeSummary();
        Check(burstSummary.Pointer.Count == 1 && burstSummary.Pointer.MaxMs == 70,
            "coalesced newer pointer ACK retains its measured input latency");
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
    static int DispatchWait(FrameQueue queue, long lastPointer, long now)
    {
        return queue.GetDispatchWaitMilliseconds(lastPointer, now, 1000);
    }
    static void CheckDispatchScheduling()
    {
        var queue = new FrameQueue(8);
        queue.Add(Protocol.EncodePointer(1, 1, 10, 10), 100);
        Check(DispatchWait(queue, 100, 101) == 3, "isolated pointer preserves remaining four-millisecond pacing");
        Check(DispatchWait(queue, 100, 104) == 0, "ordinary pointer dispatches when its interval expires");
        Check(Protocol.GetU32(queue.Take(), 5) == 1, "first ordinary pointer stays ordered");
        queue.Add(Protocol.EncodePointer(1, 2, 20, 20), 102);
        Check(DispatchWait(queue, 101, 102) == 3, "second ordinary pointer is still paced");
        queue.Clear();
        byte[][] barriers = {
            Protocol.EncodeButton(1, 1), Protocol.EncodeButton(1, 0),
            Protocol.EncodeKey(30, 1, 0), Protocol.EncodeKey(30, 0, 0),
            Protocol.EncodeScroll(0, 1), Protocol.EncodeGeometry(2, 100, 200, 0),
            Protocol.EncodeLeave(), Protocol.EncodePing(7)
        };
        foreach (byte[] barrier in barriers)
        {
            queue.Add(Protocol.EncodePointer(1, 2, 20, 20), 101);
            queue.Add(barrier, 102);
            Check(DispatchWait(queue, 100, 101) == 0,
                "pointer before barrier " + barrier[0] + " bypasses remaining interval");
            long queuedAt;
            Check(Protocol.GetU32(queue.Take(out queuedAt), 5) == 2 && queuedAt == 101,
                "pointer before barrier " + barrier[0] + " remains first with its enqueue time");
            Check(DispatchWait(queue, 101, 101) == 0,
                "barrier " + barrier[0] + " never inherits pointer pacing");
            Check(Object.ReferenceEquals(queue.Take(out queuedAt), barrier) && queuedAt == 102,
                "barrier " + barrier[0] + " stays next with its enqueue time");
        }
        Check(DispatchWait(queue, 100, 101) == 0, "empty queue does not request a dispatch wait");
    }
    sealed class RecordingStream : Stream
    {
        readonly MemoryStream _bytes = new MemoryStream();
        readonly object _gate = new object();
        readonly int _expectedWrites;
        int _writes;
        bool _closed;
        public readonly ManualResetEvent FirstWrite = new ManualResetEvent(false);
        public readonly ManualResetEvent ReleaseFirstWrite = new ManualResetEvent(false);
        public readonly ManualResetEvent Complete = new ManualResetEvent(false);
        public RecordingStream(int expectedWrites) { _expectedWrites = expectedWrites; }
        public byte[] Bytes { get { lock (_gate) { return _bytes.ToArray(); } } }
        public bool Closed { get { lock (_gate) { return _closed; } } }
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_writes == 0)
            {
                FirstWrite.Set();
                if (!ReleaseFirstWrite.WaitOne(5000)) throw new TimeoutException("recording stream gate timed out");
            }
            lock (_gate)
            {
                if (_closed) throw new ObjectDisposedException("RecordingStream");
                _bytes.Write(buffer, offset, count);
                if (++_writes == _expectedWrites) Complete.Set();
            }
        }
        protected override void Dispose(bool disposing)
        {
            lock (_gate) { _closed = true; }
            ReleaseFirstWrite.Set();
            base.Dispose(disposing);
        }
        public override bool CanRead { get { return false; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return true; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
    }
    static void CheckWriterOrdering()
    {
        byte[][] frames = { Protocol.EncodePing(99), Protocol.EncodePointer(1, 2, 20, 20),
            Protocol.EncodeButton(1, 1), Protocol.EncodeKey(30, 0, 0),
            Protocol.EncodePointer(1, 3, 30, 30), Protocol.EncodeScroll(0, 1),
            Protocol.EncodeGeometry(2, 100, 200, 0), Protocol.EncodeLeave() };
        var stream = new RecordingStream(frames.Length);
        var dispatched = new List<byte[]>();
        bool validDiagnostics = true;
        int completedWrites = 0;
        var writer = new FrameWriter(stream,
            delegate(byte[] frame, long queued, int pending, long now) {
                dispatched.Add(frame);
                validDiagnostics &= queued > 0 && now >= queued && pending >= 0;
                if (dispatched.Count == 1) throw new Exception("diagnostic callback failure");
            },
            delegate(long started, long finished) {
                validDiagnostics &= finished >= started;
                completedWrites++;
                throw new Exception("diagnostic callback failure");
            });
        try
        {
            writer.Send(frames[0]);
            Check(stream.FirstWrite.WaitOne(2000), "recording stream holds the initial write");
            writer.Send(Protocol.EncodePointer(1, 1, 10, 10));
            for (int i = 1; i < frames.Length; i++) writer.Send(frames[i]);
            stream.ReleaseFirstWrite.Set();
            Check(stream.Complete.WaitOne(2000), "actual writer drains queued input barriers");
        }
        finally { writer.Stop(); }
        var expected = new MemoryStream();
        foreach (byte[] frame in frames) expected.Write(frame, 0, frame.Length);
        Check(Convert.ToBase64String(stream.Bytes) == Convert.ToBase64String(expected.ToArray()),
            "actual writer bytes preserve newest pointer, button, key, wheel, geometry and leave order");
        Check(validDiagnostics && dispatched.Count == frames.Length && completedWrites == frames.Length,
            "actual writer retains enqueue and completion callbacks despite diagnostic failures");
    }
    static void CheckWriterShutdown()
    {
        var stream = new RecordingStream(2);
        var writer = new FrameWriter(stream);
        writer.Send(Protocol.EncodePing(1));
        try
        {
            Check(stream.FirstWrite.WaitOne(2000), "shutdown test holds an active write");
            writer.Send(Protocol.EncodePointer(1, 1, 10, 10));
        }
        finally { writer.Stop(); }
        writer.Send(Protocol.EncodeButton(1, 0));
        Check(stream.Closed && stream.Bytes.Length == 0,
            "stop closes the stream and discards pending or later frames");
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
