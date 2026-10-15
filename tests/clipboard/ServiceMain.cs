using System;
using System.IO;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Threading;
using PcKvm;

class ServiceMain
{
    static int checks;
    static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        checks++;
    }
    static void Until(Func<bool> condition, string name)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(4);
        while (!condition() && DateTime.UtcNow < end) Thread.Sleep(10);
        Assert(condition(), name);
    }

    sealed class Backend : IClipboardBackend
    {
        public event Action Changed;
        public uint Number = 1;
        public ClipboardSnapshot Current = ClipboardWire.Text("before connection");
        public int Publishes, Owner, FailCaptures;
        public bool Disposed, FailPublish;
        void Check()
        {
            if (Thread.CurrentThread.ManagedThreadId != Owner) throw new Exception("wrong adapter thread");
        }
        public uint Sequence { get { Check(); return Number; } }
        public long Revision { get { Check(); return Number; } }
        public bool IsKnownSequence(uint sequence) { Check(); return false; }
        public ClipboardSnapshot Capture()
        {
            Check();
            if (FailCaptures-- > 0) throw new System.Runtime.InteropServices.ExternalException("busy");
            return Current;
        }
        public void Publish(ClipboardSnapshot snapshot)
        {
            Check();
            if (FailPublish) throw new InvalidOperationException("test-only sensitive data");
            Current = snapshot; Publishes++; Number++;
        }
        public void Release(ClipboardSnapshot snapshot) { Check(); }
        public void Dispose() { Check(); Disposed = true; }
        public void Copy(string text) { CopySnapshot(ClipboardWire.Text(text)); }
        public void CopySnapshot(ClipboardSnapshot snapshot)
        {
            Check(); Current = snapshot; Number++;
            if (Changed != null) Changed();
        }
    }

    sealed class Fixture : IDisposable
    {
        readonly Queue<Action> queue = new Queue<Action>();
        readonly AutoResetEvent signal = new AutoResetEvent(false);
        readonly Thread thread;
        bool done;
        public readonly Backend Backend = new Backend();
        public readonly string Root = Path.Combine(Path.GetTempPath(), "pckvm-service-" + Guid.NewGuid().ToString("N"));
        public readonly byte[] Token = new byte[32];
        public readonly ClipboardService Service;
        public Fixture()
        {
            thread = new Thread(delegate()
            {
                Backend.Owner = Thread.CurrentThread.ManagedThreadId;
                while (true)
                {
                    Action action = null;
                    lock (queue)
                    {
                        if (queue.Count > 0) action = queue.Dequeue();
                        else if (done) return;
                    }
                    if (action != null) action();
                    else signal.WaitOne();
                }
            });
            thread.IsBackground = true; thread.Start();
            Service = new ClipboardService(Backend, Post, Root, delegate { });
            for (int i = 0; i < 32; i++) Token[i] = (byte)(i + 1);
            Service.Start(); Service.Configure(Token, 10);
        }
        public void Post(Action action) { lock (queue) queue.Enqueue(action); signal.Set(); }
        public void Do(Action action)
        {
            using (var wait = new ManualResetEvent(false))
            {
                Post(delegate { action(); wait.Set(); });
                Assert(wait.WaitOne(4000), "dispatcher completes");
            }
        }
        public TcpClient Connect(byte[] token, bool baseline)
        {
            var client = new TcpClient("127.0.0.1", Service.ListeningPort);
            client.ReceiveTimeout = 3000;
            NetworkStream stream = client.GetStream();
            byte[] prefix = { 80, 75, 67, 66, 1 };
            stream.Write(prefix, 0, prefix.Length); stream.Write(token, 0, token.Length);
            if (baseline)
            {
                Assert(stream.ReadByte() == 1, "authentication ack");
                Assert(stream.ReadByte() == 0, "server baseline"); stream.WriteByte(0);
            }
            return client;
        }
        public void Dispose()
        {
            Service.Dispose();
            Until(delegate { return Backend.Disposed; }, "backend disposed");
            lock (queue) done = true;
            signal.Set(); thread.Join(1000); signal.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }

    sealed class BlockingRead : Stream
    {
        readonly MemoryStream data;
        readonly ManualResetEvent entered, release;
        bool waited;
        public BlockingRead(byte[] bytes, ManualResetEvent entered, ManualResetEvent release)
        { data = new MemoryStream(bytes); this.entered = entered; this.release = release; }
        public override bool CanRead { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override bool CanSeek { get { return false; } }
        public override long Length { get { return data.Length; } }
        public override long Position { get { return data.Position; } set { throw new NotSupportedException(); } }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!waited)
            {
                waited = true; entered.Set();
                if (!release.WaitOne(4000)) throw new IOException("blocked source timeout");
            }
            return data.Read(buffer, offset, count);
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long length) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }

    static byte[] Frame(ClipboardSnapshot snapshot)
    {
        using (var stream = new MemoryStream())
        { stream.WriteByte(1); ClipboardWire.Write(stream, snapshot, null); return stream.ToArray(); }
    }
    static ClipboardSnapshot Files(long id, string content)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var snapshot = new ClipboardSnapshot { Kind = ClipboardKind.Files, Id = id };
        for (int n = 0; n < 2; n++)
            snapshot.Items.Add(new ClipboardItem { Name = "shared.bin", Mime = "application/octet-stream", Length = bytes.Length,
                Open = delegate { return new MemoryStream(bytes, false); } });
        return snapshot;
    }
    static ClipboardSnapshot CachedFiles(string root, long id, string content)
    {
        ClipboardSnapshot snapshot = Files(id, content); snapshot.Items.RemoveAt(1);
        string directory = Path.Combine(Path.Combine(root, Guid.NewGuid().ToString("N")), "0");
        Directory.CreateDirectory(directory); snapshot.Items[0].Path = Path.Combine(directory, "shared.bin");
        File.WriteAllText(snapshot.Items[0].Path, content); return snapshot;
    }
    static byte Ack(NetworkStream stream, long expected)
    {
        Assert(stream.ReadByte() == 3, "ack marker");
        long id = 0;
        for (int n = 0; n < 8; n++)
        {
            int value = stream.ReadByte(); Assert(value >= 0, "ack id byte"); id |= (long)value << (8 * n);
        }
        Assert(id == expected, "ack id"); return (byte)stream.ReadByte();
    }
    static bool Closed(TcpClient client)
    {
        try { return client.GetStream().ReadByte() == -1; }
        catch (IOException) { return true; }
        catch (ObjectDisposedException) { return true; }
    }
    static void Main()
    {
        try { Run(); }
        catch (Exception error) { Console.WriteLine("ERROR " + error.GetType().Name + " " + error.Message); Environment.ExitCode = 1; }
    }
    static void Run()
    {
        Assert(ClipboardPublicationGate.CanPublish(1, 1, 2, 2, 3, 4), "gate accepts current");
        Assert(!ClipboardPublicationGate.CanPublish(1, 2, 2, 2, 3, 4), "gate old generation");
        Assert(!ClipboardPublicationGate.CanPublish(1, 1, 2, 3, 3, 4), "gate native copy");
        Assert(!ClipboardPublicationGate.CanPublish(1, 1, 2, 2, 4, 4), "gate replay");
        using (var fixture = new Fixture())
        {
            using (var client = fixture.Connect(new byte[32], false)) Assert(Closed(client), "wrong token rejected");
            fixture.Service.RequestSync();
            using (var client = fixture.Connect(fixture.Token, true))
            {
                NetworkStream stream = client.GetStream();
                Assert(stream.ReadByte() == 1, "preconnect frame");
                ClipboardSnapshot received = ClipboardWire.Read(stream, fixture.Root, null);
                Assert(ClipboardWire.TextContent(received) == "before connection", "preconnect text");
                fixture.Do(delegate { Assert(fixture.Backend.Publishes == 0, "baseline never publishes"); });
                ClipboardSnapshot remote = ClipboardWire.Text("remote one"); remote.Id = 100;
                byte[] frame = Frame(remote); stream.Write(frame, 0, frame.Length);
                Until(delegate { return fixture.Backend.Publishes == 1; }, "incoming publishes");
                Assert(Ack(stream, 100) == 0, "published ack");
                stream.Write(frame, 0, frame.Length); Thread.Sleep(60);
                Assert(fixture.Backend.Publishes == 1, "same id ignored");
                Assert(Ack(stream, 100) == 1, "replay rejected ack");
                fixture.Service.CloseSession(9);
                Assert(fixture.Service.Status != "未连接", "stale close ignored");
                remote = ClipboardWire.Text("remote slow"); remote.Id = 101; frame = Frame(remote);
                stream.Write(frame, 0, 10); Thread.Sleep(80);
                fixture.Do(delegate { fixture.Backend.Copy("new local copy"); });
                stream.Write(frame, 10, frame.Length - 10); Thread.Sleep(100);
                Assert(fixture.Backend.Publishes == 1, "native copy wins");
                fixture.Service.SetEnabled(false);
                while (stream.DataAvailable) stream.ReadByte();
                Assert(Closed(client), "disable closes socket");
            }
            try { using (var client = fixture.Connect(fixture.Token, false)) Assert(Closed(client), "disabled rejects"); }
            catch (IOException) { Assert(true, "disabled rejects during hello"); }
            fixture.Service.SetEnabled(true);
            using (var client = fixture.Connect(fixture.Token, true))
            {
                byte[] frame = Frame(ClipboardWire.Text("incomplete"));
                client.GetStream().Write(frame, 0, frame.Length - 1);
                fixture.Service.Configure(fixture.Token, 11); Assert(Closed(client), "generation closes partial");
            }
            Thread.Sleep(100);
            Assert(fixture.Backend.Publishes == 1, "old partial never publishes");
            Assert(!Directory.Exists(fixture.Root) || Directory.GetDirectories(fixture.Root, "*.partial").Length == 0, "partial cleaned");
            using (var client = fixture.Connect(fixture.Token, true))
            { fixture.Service.CloseSession(11); Assert(Closed(client), "close idle receive"); }
        }
        using (var fixture = new Fixture())
        using (var client = fixture.Connect(fixture.Token, true))
        {
            fixture.Do(delegate { fixture.Backend.FailPublish = true; });
            ClipboardSnapshot snapshot = ClipboardWire.Text("failed publication"); snapshot.Id = 201;
            byte[] frame = Frame(snapshot); client.GetStream().Write(frame, 0, frame.Length);
            Assert(Ack(client.GetStream(), 201) == 2, "publish failure ack");
            Assert(fixture.Backend.Publishes == 0, "publish failure preserves clipboard");
        }
        using (var fixture = new Fixture())
        using (var client = fixture.Connect(fixture.Token, true))
        {
            ClipboardSnapshot snapshot = ClipboardWire.Text("stale callback"); snapshot.Id = 301;
            byte[] frame = Frame(snapshot); NetworkStream stream = client.GetStream();
            stream.Write(frame, 0, 10); Thread.Sleep(80);
            using (var blocked = new ManualResetEvent(false))
            using (var release = new ManualResetEvent(false))
            {
                fixture.Post(delegate { blocked.Set(); release.WaitOne(4000); });
                Assert(blocked.WaitOne(4000), "dispatcher paused");
                stream.Write(frame, 10, frame.Length - 10); Thread.Sleep(80);
                fixture.Service.Configure(fixture.Token, 11); release.Set();
                fixture.Do(delegate { }); Assert(fixture.Backend.Publishes == 0, "queued old connection publication rejected");
            }
        }
        using (var fixture = new Fixture())
        using (var client = fixture.Connect(fixture.Token, true))
        {
            fixture.Do(delegate { fixture.Backend.FailCaptures = 2; }); fixture.Service.RequestSync();
            NetworkStream stream = client.GetStream(); Assert(stream.ReadByte() == 1, "busy capture retried");
            ClipboardSnapshot snapshot = ClipboardWire.Read(stream, fixture.Root, null);
            Assert(ClipboardWire.TextContent(snapshot) == "before connection", "retry preserves text");
        }
        using (var fixture = new Fixture())
        using (var entered = new ManualResetEvent(false))
        using (var release = new ManualResetEvent(false))
        {
            using (var client = fixture.Connect(fixture.Token, true))
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes("old outgoing"); int opens = 0;
                var snapshot = new ClipboardSnapshot { Kind = ClipboardKind.Text };
                snapshot.Items.Add(new ClipboardItem { Name = "text.txt", Mime = "text/plain", Length = bytes.Length, Open = delegate
                {
                    if (Interlocked.Increment(ref opens) == 1) return new MemoryStream(bytes);
                    return new BlockingRead(bytes, entered, release);
                } });
                fixture.Do(delegate { fixture.Backend.CopySnapshot(snapshot); });
                Assert(entered.WaitOne(4000), "outgoing frame in progress");
                Assert(client.GetStream().ReadByte() == 1, "old frame started");
                fixture.Do(delegate { fixture.Backend.Copy("latest outgoing"); }); release.Set();
                int read; do { read = client.GetStream().ReadByte(); } while (read >= 0);
                Assert(read < 0, "superseded frame socket closes");
            }
            using (var client = fixture.Connect(fixture.Token, true))
            {
                NetworkStream stream = client.GetStream(); Assert(stream.ReadByte() == 1, "latest forced after reconnect");
                ClipboardSnapshot snapshot = ClipboardWire.Read(stream, fixture.Root, null);
                Assert(ClipboardWire.TextContent(snapshot) == "latest outgoing", "supersession sends latest only");
            }
        }
        using (var fixture = new Fixture())
        using (var client = fixture.Connect(fixture.Token, true))
        {
            NetworkStream stream = client.GetStream();
            byte[] frame = Frame(Files(401, "retained published bytes"));
            stream.Write(frame, 0, frame.Length);
            Assert(Ack(stream, 401) == 0, "file publication acknowledged");
            string published = fixture.Backend.Current.Items[0].Path;
            Assert(File.Exists(published), "published file exists");
            string unrelated = Path.Combine(fixture.Root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(unrelated); File.WriteAllText(Path.Combine(unrelated, "keep.txt"), "owned by another operation");
            fixture.Do(delegate { fixture.Backend.FailPublish = true; });
            frame = Frame(Files(402, "failed file bytes")); stream.Write(frame, 0, frame.Length);
            Assert(Ack(stream, 402) == 2, "failed files acknowledged");
            Until(delegate { return Directory.GetDirectories(fixture.Root).Length == 2; }, "failed received GUID directory removed");
            Assert(File.ReadAllText(published) == "retained published bytes", "failed cleanup preserves published file");
            Assert(File.Exists(Path.Combine(unrelated, "keep.txt")), "failed cleanup preserves unrelated directory");
            frame = Frame(Files(401, "replayed file bytes")); stream.Write(frame, 0, frame.Length);
            Assert(Ack(stream, 401) == 1, "superseded files acknowledged");
            Until(delegate { return Directory.GetDirectories(fixture.Root).Length == 2; }, "superseded received GUID directory removed");
            Assert(File.ReadAllText(published) == "retained published bytes", "superseded cleanup preserves published file");
            fixture.Service.Dispose();
            Until(delegate { return fixture.Backend.Disposed; }, "dispose completes with published cache");
            Assert(File.Exists(published), "published cache survives service exit");
        }
        using (var fixture = new Fixture())
        using (var client = fixture.Connect(fixture.Token, true))
        {
            NetworkStream stream = client.GetStream();
            ClipboardSnapshot text = ClipboardWire.Text("materialized text"); text.Id = 501;
            byte[] frame = Frame(text); stream.Write(frame, 0, frame.Length);
            Assert(Ack(stream, 501) == 0, "successful text acknowledged");
            Until(delegate { return Directory.GetDirectories(fixture.Root).Length == 0; }, "successful text cache removed");
            ClipboardSnapshot image = Files(502, "fake materialized PNG"); image.Kind = ClipboardKind.Image; image.Items.RemoveAt(1);
            frame = Frame(image); stream.Write(frame, 0, frame.Length);
            Assert(Ack(stream, 502) == 0, "successful image acknowledged");
            Until(delegate { return Directory.GetDirectories(fixture.Root).Length == 0; }, "successful image cache removed");
        }
        string retentionRoot = Path.Combine(Path.GetTempPath(), "pckvm-retention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(retentionRoot);
        try
        {
            DateTime now = DateTime.UtcNow;
            var retention = new ClipboardCacheRetention(retentionRoot, delegate { return now; }, 100);
            ClipboardSnapshot live = CachedFiles(retentionRoot, 601, "live");
            ClipboardSnapshot retired = CachedFiles(retentionRoot, 602, "retired");
            retention.Record(live); retention.Complete(live, true);
            retention.Record(retired); retention.Complete(retired, false);
            retention.Observe(live); retention.Sweep();
            now = now.AddHours(25); retention.Sweep();
            Assert(File.Exists(live.Items[0].Path), "age sweep protects current filedrop");
            Assert(!File.Exists(retired.Items[0].Path), "age sweep removes retired receipt");
            string foreign = Path.Combine(retentionRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(foreign); File.WriteAllText(Path.Combine(foreign, "foreign.txt"), "untouched");
            Directory.SetLastWriteTimeUtc(foreign, now.AddDays(-10)); retention.Sweep();
            Assert(File.Exists(Path.Combine(foreign, "foreign.txt")), "sweep ignores unrelated GUID directory");
            retention.Observe(null); retention.Sweep();
            Assert(File.Exists(live.Items[0].Path), "replaced filedrop receives retention interval");
            now = now.AddHours(25);
            var restarted = new ClipboardCacheRetention(retentionRoot, delegate { return now; }, 100);
            restarted.Observe(null); restarted.Sweep();
            Assert(!File.Exists(live.Items[0].Path), "startup scavenges old unreferenced owned receipt");
            ClipboardSnapshot older = CachedFiles(retentionRoot, 603, "older");
            ClipboardSnapshot newer = CachedFiles(retentionRoot, 604, "newer");
            older.Items[0].Length = 80; newer.Items[0].Length = 80;
            restarted.Record(older); restarted.Complete(older, false); now = now.AddMinutes(1);
            restarted.Record(newer); restarted.Complete(newer, false); restarted.Sweep();
            Assert(!File.Exists(older.Items[0].Path), "retired byte cap removes oldest receipt");
            Assert(File.Exists(newer.Items[0].Path), "retired byte cap retains newest receipt");
        }
        finally { Directory.Delete(retentionRoot, true); }
        Console.WriteLine("PASS clipboard service " + checks);
    }
}
