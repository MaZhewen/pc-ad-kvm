using System;
using System.IO;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

namespace PcKvm
{
    internal interface IClipboardBackend : IDisposable
    {
        event Action Changed;
        ClipboardSnapshot Capture();
        void Publish(ClipboardSnapshot snapshot);
        void Release(ClipboardSnapshot snapshot);
        long Revision { get; }
        uint Sequence { get; }
        bool IsKnownSequence(uint sequence);
    }

    internal static class ClipboardPublicationGate
    {
        internal static bool CanPublish(long epoch, long currentEpoch, uint initialSequence,
            uint currentSequence, long previousId, long incomingId)
        { return epoch == currentEpoch && initialSequence == currentSequence && incomingId > previousId; }
    }

    // All directory scans and deletes run on workers. STA observations only replace path sets.
    internal sealed class ClipboardCacheRetention
    {
        const string Marker = ".pckvm-received";
        const string Signature = "PKCB-CACHE-1";
        readonly string _root;
        readonly Func<DateTime> _clock;
        readonly long _limit;
        readonly object _state = new object(), _worker = new object();
        readonly HashSet<string> _pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> _protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> _previous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool _uncertain = true;
        long _revision;
        int _requested, _running;
        sealed class Receipt { public string Directory, MarkerPath; public long Bytes; public DateTime Retired; }
        internal ClipboardCacheRetention(string root, Func<DateTime> clock, long limit)
        { _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); _clock = clock; _limit = limit; }
        string DirectoryFor(string path)
        {
            if (String.IsNullOrEmpty(path)) return null;
            string full = Path.GetFullPath(path), prefix = _root + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            string relative = full.Substring(prefix.Length); int slash = relative.IndexOf(Path.DirectorySeparatorChar);
            if (slash < 0) return null;
            string name = relative.Substring(0, slash); Guid parsed;
            return Guid.TryParseExact(name, "N", out parsed) ? Path.Combine(_root, name) : null;
        }
        internal void Suspend()
        { lock (_state) { _uncertain = true; _revision++; } }
        internal void Observe(ClipboardSnapshot snapshot)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (snapshot != null && snapshot.Kind == ClipboardKind.Files)
                foreach (ClipboardItem item in snapshot.Items) { string directory = DirectoryFor(item.Path); if (directory != null) paths.Add(directory); }
            lock (_state) { _protected = paths; _uncertain = false; _revision++; }
        }
        internal void Record(ClipboardSnapshot snapshot)
        {
            if (snapshot.Kind != ClipboardKind.Files || snapshot.Items.Count == 0) return;
            string directory = DirectoryFor(snapshot.Items[0].Path); if (directory == null) return;
            long bytes = 0; foreach (ClipboardItem item in snapshot.Items) bytes += item.Length;
            lock (_state) { _pending.Add(directory); _revision++; }
            string marker = Path.Combine(directory, Marker);
            File.WriteAllText(marker, Signature + "\n" + bytes.ToString(System.Globalization.CultureInfo.InvariantCulture), new System.Text.UTF8Encoding(false));
            File.SetLastWriteTimeUtc(marker, _clock());
        }
        internal void Complete(ClipboardSnapshot snapshot, bool published)
        {
            if (published) Observe(snapshot);
            if (snapshot.Kind != ClipboardKind.Files || snapshot.Items.Count == 0) return;
            string directory = DirectoryFor(snapshot.Items[0].Path);
            lock (_state) { if (directory != null) _pending.Remove(directory); _revision++; }
        }
        internal void RequestSweep()
        {
            Interlocked.Exchange(ref _requested, 1);
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { while (Interlocked.Exchange(ref _requested, 0) != 0) Sweep(); }
                finally { Interlocked.Exchange(ref _running, 0); if (Interlocked.CompareExchange(ref _requested, 0, 0) != 0) RequestSweep(); }
            });
        }
        internal void Sweep()
        {
            lock (_worker)
            {
                try { SweepCore(); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        internal void DeleteOwnedDirectory(string directory)
        {
            // A scanner must not hold a receipt marker open while removal is attempted.
            lock (_worker)
            {
                try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        void SweepCore()
        {
            HashSet<string> protect, pending; long revision;
            lock (_state)
            {
                if (_uncertain) return;
                protect = new HashSet<string>(_protected, StringComparer.OrdinalIgnoreCase);
                pending = new HashSet<string>(_pending, StringComparer.OrdinalIgnoreCase); revision = _revision;
            }
            if (!Directory.Exists(_root)) return;
            DateTime now = _clock();
            foreach (string previous in _previous)
                if (!protect.Contains(previous) && File.Exists(Path.Combine(previous, Marker))) File.SetLastWriteTimeUtc(Path.Combine(previous, Marker), now);
            _previous = protect;
            var receipts = new List<Receipt>(); long total = 0;
            foreach (string directory in Directory.GetDirectories(_root))
            {
                Guid parsed;
                if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out parsed) || protect.Contains(directory) || pending.Contains(directory) ||
                    (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                string marker = Path.Combine(directory, Marker);
                if (!File.Exists(marker) || new FileInfo(marker).Length > 128) continue;
                string[] lines = File.ReadAllLines(marker); long bytes;
                if (lines.Length != 2 || lines[0] != Signature || !Int64.TryParse(lines[1], out bytes) || bytes < 0 || bytes > 2147483648L) continue;
                receipts.Add(new Receipt { Directory = directory, MarkerPath = marker, Bytes = bytes, Retired = File.GetLastWriteTimeUtc(marker) });
                total += bytes;
            }
            receipts.Sort(delegate(Receipt left, Receipt right) { return left.Retired.CompareTo(right.Retired); });
            foreach (Receipt receipt in receipts)
            {
                if (now - receipt.Retired < TimeSpan.FromHours(24) && total <= _limit) continue;
                lock (_state) { if (_uncertain || revision != _revision || _protected.Contains(receipt.Directory) || _pending.Contains(receipt.Directory)) return; }
                try { Directory.Delete(receipt.Directory, true); total -= receipt.Bytes; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
    // The input channel owns the token and generation. This listener only binds loopback.
    public sealed class ClipboardService : IDisposable
    {
        readonly object _gate = new object();
        readonly IClipboardBackend _backend;
        readonly Action<Action> _dispatch;
        readonly string _cacheRoot;
        readonly Action<string> _log;
        readonly ClipboardCacheRetention _retention;
        System.Threading.Timer _cacheTimer;
        TcpListener _listener;
        TcpClient _candidate;
        Connection _active;
        byte[] _token;
        long _generation, _epoch, _nextId = DateTime.UtcNow.Ticks, _changes;
        long _lastAcknowledgedId;
        bool _enabled = true, _disposed, _forceRequested;
        string _status = "未连接";
        int _port;

        sealed class WindowsBackend : IClipboardBackend
        {
            readonly WindowsClipboard _clipboard;
            public event Action Changed;
            public WindowsBackend(Control dispatcher, string root)
            { _clipboard = new WindowsClipboard(dispatcher, root, delegate { if (Changed != null) Changed(); }); }
            public ClipboardSnapshot Capture() { return _clipboard.Capture(); }
            public void Publish(ClipboardSnapshot snapshot) { _clipboard.Publish(snapshot); }
            public void Release(ClipboardSnapshot snapshot) { _clipboard.Release(snapshot); }
            public uint Sequence { get { return _clipboard.Sequence; } }
            public long Revision { get { return _clipboard.Revision; } }
            public bool IsKnownSequence(uint sequence) { return _clipboard.IsKnownSequence(sequence); }
            public void Dispose() { _clipboard.Dispose(); }
        }
        sealed class Pending
        {
            public ClipboardSnapshot Snapshot;
            public bool Force;
            public long Change;
        }
        sealed class Acknowledgment { public long Id; public byte Status; }
        sealed class Connection
        {
            public readonly TcpClient Client;
            public readonly NetworkStream Stream;
            public readonly long Epoch;
            public readonly AutoResetEvent Wake = new AutoResetEvent(false);
            public volatile bool Closed;
            public bool BaselineCaptured, LocalReady, PeerReady, Writing, Processing, CaptureRequested;
            public ClipboardSnapshot Baseline;
            public Pending Pending;
            public string LastHash;
            public long LastReceivedId, AwaitingId;
            public readonly Queue<Acknowledgment> Acknowledgments = new Queue<Acknowledgment>();
            public Connection(TcpClient client, long epoch) { Client = client; Stream = client.GetStream(); Epoch = epoch; }
        }

        public ClipboardService(Control dispatcher, string cacheRoot, Action<string> log)
            : this(new WindowsBackend(dispatcher, cacheRoot), delegate(Action action)
                { if (!dispatcher.IsDisposed) dispatcher.BeginInvoke(action); }, cacheRoot, log) { }

        internal ClipboardService(IClipboardBackend backend, Action<Action> dispatch, string cacheRoot, Action<string> log)
        {
            if (backend == null || dispatch == null) throw new ArgumentNullException("backend");
            _backend = backend; _dispatch = dispatch; _cacheRoot = Path.GetFullPath(cacheRoot); _log = log;
            _retention = new ClipboardCacheRetention(_cacheRoot, delegate { return DateTime.UtcNow; }, 2147483648L);
            _backend.Changed += NativeChanged;
        }
        public int ListeningPort { get { lock (_gate) return _port; } }
        public long LastAcknowledgedId { get { lock (_gate) return _lastAcknowledgedId; } }
        public bool Enabled { get { lock (_gate) return _enabled; } }
        public string Status { get { lock (_gate) return _status; } }

        public int Start()
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException("ClipboardService");
                if (_listener != null) return _port;
                _listener = new TcpListener(IPAddress.Loopback, 0); _listener.Start(2);
                _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                StartWorker(AcceptLoop); Post(CaptureRetentionOnly);
                _cacheTimer = new System.Threading.Timer(delegate { _retention.RequestSweep(); }, null, 900000, 900000); return _port;
            }
        }
        public void Configure(byte[] token, long generation)
        {
            if (token == null || token.Length != 32) throw new ArgumentException("Clipboard session token must have 32 bytes.", "token");
            lock (_gate)
            {
                if (_disposed) return;
                InvalidateLocked(); _token = (byte[])token.Clone(); _generation = generation;
                _lastAcknowledgedId = 0;
                _forceRequested = false; _status = _enabled ? "等待剪贴板连接" : "已关闭";
            }
        }
        public void CloseSession() { lock (_gate) CloseSessionLocked(); }
        public void CloseSession(long generation) { lock (_gate) { if (_generation == generation) CloseSessionLocked(); } }
        void CloseSessionLocked()
        { InvalidateLocked(); _token = null; _forceRequested = false; _status = _enabled ? "未连接" : "已关闭"; }
        public void SetEnabled(bool enabled)
        {
            lock (_gate)
            {
                if (_disposed || _enabled == enabled) return;
                _enabled = enabled; InvalidateLocked(); _forceRequested = false;
                _status = !enabled ? "已关闭" : _token == null ? "未连接" : "等待剪贴板连接";
            }
        }
        public void RequestSync()
        {
            lock (_gate)
            {
                if (_disposed || !_enabled) return;
                _forceRequested = true;
                if (_active != null) _active.Wake.Set();
            }
        }
        void InvalidateLocked()
        {
            _epoch++;
            if (_candidate != null) { _candidate.Close(); _candidate = null; }
            if (_active != null) { CloseConnectionLocked(_active); _active = null; }
        }
        void CloseConnectionLocked(Connection connection)
        { if (!connection.Closed) { connection.Closed = true; connection.Client.Close(); connection.Wake.Set(); } }
        bool CurrentLocked(Connection connection)
        { return !_disposed && _enabled && connection != null && !connection.Closed && _active == connection && _epoch == connection.Epoch; }
        bool Canceled(Connection connection) { lock (_gate) return !CurrentLocked(connection); }
        static void StartWorker(ThreadStart action) { Thread thread = new Thread(action); thread.IsBackground = true; thread.Start(); }
        void Post(Action action)
        {
            try { _dispatch(action); }
            catch (InvalidOperationException) { }
        }
        void SafeLog(string stage) { if (_log != null) { try { _log("Clipboard: " + stage); } catch { } } }

        void AcceptLoop()
        {
            while (true)
            {
                TcpClient client = null;
                try
                {
                    TcpListener listener; lock (_gate) { if (_disposed) return; listener = _listener; }
                    client = listener.AcceptTcpClient();
                    byte[] expected; long epoch;
                    lock (_gate)
                    {
                        if (_disposed || !_enabled || _token == null || _active != null) { client.Close(); continue; }
                        _candidate = client; expected = (byte[])_token.Clone(); epoch = _epoch;
                    }
                    client.NoDelay = true; client.ReceiveTimeout = 2000; client.SendTimeout = 5000;
                    NetworkStream stream = client.GetStream(); byte[] hello = new byte[37];
                    DateTime deadline = DateTime.UtcNow.AddSeconds(2); int offset = 0;
                    while (offset < hello.Length) { int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds; if (remaining <= 0) throw new IOException("Authentication timeout"); client.ReceiveTimeout = remaining; int n = stream.Read(hello, offset, hello.Length - offset); if (n <= 0) throw new EndOfStreamException(); offset += n; }
                    int mismatch = (hello[0] ^ 80) | (hello[1] ^ 75) | (hello[2] ^ 67) | (hello[3] ^ 66) | (hello[4] ^ 1);
                    for (int i = 0; i < 32; i++) mismatch |= hello[i + 5] ^ expected[i];
                    Connection connection;
                    lock (_gate)
                    {
                        if (mismatch != 0 || _disposed || !_enabled || epoch != _epoch || _active != null || _candidate != client) { client.Close(); if (_candidate == client) _candidate = null; continue; }
                        _candidate = null; connection = new Connection(client, epoch); _active = connection;
                        _status = "正在准备剪贴板";
                    }
                    stream.WriteByte(1); client.ReceiveTimeout = 0;
                    Connection accepted = connection;
                    Post(delegate { CaptureBaseline(accepted); });
                    StartWorker(delegate { SendLoop(accepted); });
                    StartWorker(delegate { ReceiveLoop(accepted); });
                    client = null;
                }
                catch (Exception error)
                {
                    if (!(error is IOException) && !(error is SocketException) && !(error is ObjectDisposedException)) SafeLog("connection failed");
                    lock (_gate) { if (_candidate == client) _candidate = null; if (_active != null && _active.Client == client) { CloseConnectionLocked(_active); _active = null; } if (_disposed) return; }
                    if (client != null) client.Close();
                }
            }
        }
        void CaptureBaseline(Connection connection)
        {
            lock (_gate) { if (!CurrentLocked(connection)) return; }
            ClipboardSnapshot snapshot = null;
            try { snapshot = _backend.Capture(); _retention.Observe(snapshot); _retention.RequestSweep(); }
            catch { SafeLog("capture unavailable"); }
            lock (_gate)
            {
                if (!CurrentLocked(connection)) { Release(snapshot); return; }
                connection.Baseline = snapshot; connection.BaselineCaptured = true; connection.Wake.Set();
            }
        }
        void CaptureRetentionOnly()
        {
            lock (_gate) { if (_disposed) return; }
            ClipboardSnapshot snapshot = null;
            try { snapshot = _backend.Capture(); _retention.Observe(snapshot); _retention.RequestSweep(); }
            catch { return; }
            finally { Release(snapshot); }
        }
        void NativeChanged()
        {
            _retention.Suspend();
            Connection connection;
            lock (_gate)
            {
                _changes++; connection = _active;
                if (CurrentLocked(connection) && connection.Writing)
                {
                    _forceRequested = true; CloseConnectionLocked(connection); _active = null;
                    _status = "正在重新连接剪贴板"; connection = null;
                }
                else if (!CurrentLocked(connection)) connection = null;
            }
            if (connection == null) CaptureRetentionOnly();
            else CaptureOutgoing(connection, false);
        }
        void CaptureOutgoing(Connection connection, bool force)
        {
            long change; lock (_gate) change = _changes;
            CaptureOutgoing(connection, force, 0, change);
        }
        void CaptureOutgoing(Connection connection, bool force, int attempt, long expectedChange)
        {
            lock (_gate) { if (!CurrentLocked(connection) || expectedChange != _changes) return; connection.CaptureRequested = false; }
            ClipboardSnapshot snapshot = null;
            try { snapshot = _backend.Capture(); _retention.Observe(snapshot); _retention.RequestSweep(); }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                if (attempt < 3)
                {
                    lock (_gate) { if (!CurrentLocked(connection)) return; connection.CaptureRequested = true; }
                    StartWorker(delegate { Thread.Sleep(30 * (attempt + 1)); Post(delegate { CaptureOutgoing(connection, force, attempt + 1, expectedChange); }); });
                    return;
                }
                SafeLog("capture busy");
            }
            catch { SafeLog("capture unavailable"); }
            Pending old = null;
            lock (_gate)
            {
                if (snapshot == null) { if (CurrentLocked(connection)) { _status = "剪贴板暂不可用"; connection.Wake.Set(); } return; }
                if (!CurrentLocked(connection)) { Release(snapshot); return; }
                snapshot.Id = ++_nextId;
                old = connection.Pending;
                connection.Pending = new Pending { Snapshot = snapshot, Force = force || (old != null && old.Force), Change = _changes };
                connection.Wake.Set();
            }
            if (old != null) Release(old.Snapshot);
        }
        void Release(ClipboardSnapshot snapshot)
        {
            if (snapshot == null) return;
            // Adapter owns captured-image lifetime and thread affinity.
            Post(delegate { lock (_gate) { if (_disposed) return; } try { _backend.Release(snapshot); } catch { } });
        }
        void SendLoop(Connection connection)
        {
            ClipboardSnapshot baseline = null;
            Pending pending = null;
            try
            {
                while (true)
                {
                    bool baselineWork = false, capture = false; Acknowledgment acknowledgment = null;
                    lock (_gate)
                    {
                        if (!CurrentLocked(connection)) return;
                        if (connection.Acknowledgments.Count > 0) acknowledgment = connection.Acknowledgments.Dequeue();
                        else if (!connection.LocalReady && connection.BaselineCaptured)
                        { baseline = connection.Baseline; connection.Baseline = null; baselineWork = true; }
                        else if (connection.LocalReady && connection.PeerReady)
                        {
                            if (_forceRequested && !connection.CaptureRequested)
                            { _forceRequested = false; connection.CaptureRequested = true; capture = true; }
                            else if (connection.Pending != null) { pending = connection.Pending; connection.Pending = null; connection.Processing = true; }
                        }
                    }
                    if (acknowledgment != null) { connection.Stream.WriteByte(3); WriteId(connection.Stream, acknowledgment.Id); connection.Stream.WriteByte(acknowledgment.Status); continue; }
                    if (capture) { Post(delegate { CaptureOutgoing(connection, true); }); continue; }
                    if (baselineWork)
                    {
                        // An observed local baseline has never been mirrored to the peer.
                        lock (_gate) { if (!CurrentLocked(connection)) return; connection.LastHash = null; }
                        connection.Stream.WriteByte(0);
                        lock (_gate) { if (!CurrentLocked(connection)) return; connection.LocalReady = true; _status = "剪贴板已就绪"; }
                        Release(baseline); baseline = null; continue;
                    }
                    if (pending != null)
                    {
                        string hash = pending.Snapshot.Fingerprint(); bool send;
                        lock (_gate)
                        {
                            if (!CurrentLocked(connection)) return;
                            send = pending.Change == _changes && (pending.Force || hash != connection.LastHash);
                            if (send) { connection.Writing = true; connection.AwaitingId = pending.Snapshot.Id; _status = "正在同步剪贴板"; }
                        }
                        if (send)
                        {
                            connection.Stream.WriteByte(1);
                            ClipboardWire.Write(connection.Stream, pending.Snapshot, delegate { return Canceled(connection); });
                            connection.Stream.Flush();
                            lock (_gate) { if (CurrentLocked(connection)) { connection.LastHash = hash; connection.Writing = false; } }
                        }
                        Release(pending.Snapshot); pending = null; lock (_gate) connection.Processing = false; continue;
                    }
                    connection.Wake.WaitOne();
                }
            }
            catch { EndConnection(connection, "transfer failed"); }
            finally
            {
                Release(baseline); if (pending != null) Release(pending.Snapshot);
                lock (_gate)
                {
                    if (connection.Pending != null) { Release(connection.Pending.Snapshot); connection.Pending = null; }
                    if (connection.Baseline != null) { Release(connection.Baseline); connection.Baseline = null; }
                    connection.Wake.Dispose();
                }
            }
        }
        uint SampleSequence(Connection connection)
        {
            uint sequence = 0; Exception failure = null;
            using (ManualResetEvent done = new ManualResetEvent(false))
            {
                // The callback may arrive after timeout, so do not dispose its completion event early.
                ManualResetEvent completed = done;
                Post(delegate
                {
                    try { lock (_gate) { if (!CurrentLocked(connection)) throw new OperationCanceledException(); } sequence = _backend.Sequence; }
                    catch (Exception error) { failure = error; }
                    finally { try { completed.Set(); } catch (ObjectDisposedException) { } }
                });
                if (!done.WaitOne(5000) || failure != null || Canceled(connection)) throw new OperationCanceledException();
            }
            return sequence;
        }
        void ReceiveLoop(Connection connection)
        {
            try
            {
                while (!Canceled(connection))
                {
                    connection.Client.ReceiveTimeout = 0;
                    int marker = connection.Stream.ReadByte();
                    if (marker < 0) throw new EndOfStreamException();
                    if (marker == 0)
                    {
                        lock (_gate) { if (!CurrentLocked(connection)) return; connection.PeerReady = true; connection.Wake.Set(); }
                    }
                                        else if (marker == 2)
                    {
                        lock (_gate) { if (CurrentLocked(connection)) { _forceRequested = true; connection.Wake.Set(); } }
                    }
                    else if (marker == 3)
                    {
                        connection.Client.ReceiveTimeout = 30000;
                        long id = ReadId(connection.Stream); int status = connection.Stream.ReadByte();
                        if (status < 0 || status > 2) throw new InvalidDataException("Invalid clipboard acknowledgment");
                        lock (_gate)
                        {
                            if (CurrentLocked(connection) && connection.AwaitingId == id)
                            {
                                connection.AwaitingId = 0;
                                if (status == 0) _lastAcknowledgedId = id;
                                _status = status == 0 ? "已同步到系统剪贴板" : status == 1 ? "剪贴板未更新" : "剪贴板更新失败";
                            }
                        }
                    }
                    else if (marker == 1)
                    {
                        uint sequence = SampleSequence(connection);
                        connection.Client.ReceiveTimeout = 30000;
                        ClipboardSnapshot snapshot = ClipboardWire.Read(connection.Stream, _cacheRoot, delegate { return Canceled(connection); });
                        try { _retention.Record(snapshot); } catch { DeleteReceived(snapshot); throw; }
                        ClipboardSnapshot received = snapshot;
                        using (ManualResetEvent done = new ManualResetEvent(false))
                        {
                            ManualResetEvent completed = done;
                            Post(delegate { try { PublishIncoming(connection, received, sequence); } finally { try { completed.Set(); } catch (ObjectDisposedException) { } } });
                            if (!done.WaitOne(5000)) throw new IOException("Clipboard publication timeout");
                        }
                    }
                    else throw new InvalidDataException("Unknown clipboard marker");
                }
            }
            catch { EndConnection(connection, "connection closed"); }
        }
        static void WriteId(Stream stream, long id)
        { for (int n = 0; n < 8; n++) stream.WriteByte((byte)((ulong)id >> (8 * n))); }
        static long ReadId(Stream stream)
        {
            ulong id = 0;
            for (int n = 0; n < 8; n++) { int b = stream.ReadByte(); if (b < 0) throw new EndOfStreamException(); id |= (ulong)b << (8 * n); }
            if (id == 0 || id > long.MaxValue) throw new InvalidDataException("Invalid clipboard identifier");
            return (long)id;
        }
        void PublishIncoming(Connection connection, ClipboardSnapshot snapshot, uint sequence)
        {
            bool published = false; byte status = 1;
            try
            {
                lock (_gate)
                {
                    if (!CurrentLocked(connection)) return;
                    long previousId = connection.LastReceivedId;
                    if (snapshot.Id > previousId) connection.LastReceivedId = snapshot.Id;
                    if (!connection.LocalReady || !connection.PeerReady ||
                        !ClipboardPublicationGate.CanPublish(connection.Epoch, _epoch, sequence, _backend.Sequence, previousId, snapshot.Id)) return;
                    string hash = snapshot.Fingerprint();
                    if (hash == connection.LastHash) { status = 0; return; }
                    _backend.Publish(snapshot); published = true; status = 0; connection.LastHash = hash;
                    _status = "已同步到系统剪贴板";
                }
            }
            catch { status = 2; SafeLog("publish unavailable"); lock (_gate) { if (CurrentLocked(connection)) _status = "剪贴板暂不可用"; } }
            finally
            {
                lock (_gate)
                {
                    if (CurrentLocked(connection))
                    {
                        if (connection.Acknowledgments.Count >= 128) { CloseConnectionLocked(connection); _active = null; }
                        else { connection.Acknowledgments.Enqueue(new Acknowledgment { Id = snapshot.Id, Status = status }); connection.Wake.Set(); }
                    }
                }
                _retention.Complete(snapshot, published); _retention.RequestSweep();
                if (!published || snapshot.Kind != ClipboardKind.Files) StartWorker(delegate { DeleteReceived(snapshot); });
            }
        }
        void DeleteReceived(ClipboardSnapshot snapshot)
        {
            // Read() commits each message as cacheRoot/GUID/item-index/original-name.
            // Only an entire validated message directory belongs to this snapshot.
            if (snapshot.Items.Count == 0 || String.IsNullOrEmpty(snapshot.Items[0].Path)) return;
            string itemDirectory = Path.GetDirectoryName(Path.GetFullPath(snapshot.Items[0].Path));
            string directory = Path.GetDirectoryName(itemDirectory);
            Guid parsed;
            string parent = Path.GetDirectoryName(directory);
            if (parent == null || !String.Equals(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                _cacheRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(Path.GetFileName(directory), "N", out parsed)) return;
            for (int n = 0; n < snapshot.Items.Count; n++)
            {
                ClipboardItem item = snapshot.Items[n];
                if (String.IsNullOrEmpty(item.Path) || String.IsNullOrEmpty(item.Name)) return;
                string expected = Path.Combine(Path.Combine(directory, n.ToString(System.Globalization.CultureInfo.InvariantCulture)), item.Name);
                if (!String.Equals(Path.GetFullPath(item.Path), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase)) return;
            }
            _retention.DeleteOwnedDirectory(directory);
        }
        void EndConnection(Connection connection, string stage)
        {
            lock (_gate)
            {
                if (_active != connection) return;
                if (connection.Writing || connection.Processing || connection.AwaitingId != 0 || connection.Pending != null || connection.CaptureRequested) _forceRequested = true;
                CloseConnectionLocked(connection); _active = null;
                if (!_disposed && _enabled && _token != null) _status = "等待剪贴板连接";
            }
            SafeLog(stage);
        }
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true; InvalidateLocked(); _token = null;
                if (_cacheTimer != null) { _cacheTimer.Dispose(); _cacheTimer = null; }
                if (_listener != null) { _listener.Stop(); _listener = null; }
                _status = "未连接";
            }
            Post(delegate { _backend.Changed -= NativeChanged; _backend.Dispose(); });
        }
    }
}
