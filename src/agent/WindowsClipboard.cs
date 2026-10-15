using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PcKvm
{
    // Clipboard/OLE and the native listener always belong to the dispatcher's STA.
    public sealed class WindowsClipboard : IDisposable
    {
        const long MaxImage = 64L * 1024 * 1024;
        const long MaxFile = 1024L * 1024 * 1024;
        const long MaxTotal = 2L * 1024 * 1024 * 1024;
        readonly Control _dispatcher;
        readonly int _thread;
        readonly string _cacheRoot;
        readonly Action _changed;
        readonly Listener _listener;
        readonly Dictionary<ClipboardSnapshot, string> _captures = new Dictionary<ClipboardSnapshot, string>();
        bool _disposed, _publishing;
        long _revision;
        uint _observed, _known;

        [DllImport("user32.dll", SetLastError = true)] static extern bool AddClipboardFormatListener(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)] static extern bool RemoveClipboardFormatListener(IntPtr window);
        [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();

        public WindowsClipboard(Control dispatcher, string cacheRoot, Action changed)
        {
            if (dispatcher == null) throw new ArgumentNullException("dispatcher");
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA || dispatcher.InvokeRequired)
                throw new InvalidOperationException("Clipboard requires its owning STA thread.");
            if (dispatcher.IsDisposed) throw new ObjectDisposedException("dispatcher");
            if (String.IsNullOrEmpty(cacheRoot)) throw new ArgumentNullException("cacheRoot");
            _thread = Thread.CurrentThread.ManagedThreadId;
            _dispatcher = dispatcher;
            _cacheRoot = Path.GetFullPath(cacheRoot);
            _changed = changed;
            _observed = Sequence;
            _listener = new Listener(this);
            _dispatcher.HandleCreated += OnHandleCreated;
            _dispatcher.HandleDestroyed += OnHandleDestroyed;
            _dispatcher.Disposed += OnDispatcherDisposed;
            try { _listener.Attach(_dispatcher.Handle); }
            catch { _dispatcher.HandleCreated -= OnHandleCreated; _dispatcher.HandleDestroyed -= OnHandleDestroyed; _dispatcher.Disposed -= OnDispatcherDisposed; throw; }
        }

        public uint Sequence { get { return GetClipboardSequenceNumber(); } }
        public long Revision { get { CheckThread(); return _revision; } }
        public bool IsKnownSequence(uint sequence) { CheckThread(); return sequence != 0 && sequence == _known; }

        void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _thread || Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException("Clipboard requires its owning STA thread.");
            if (_disposed) throw new ObjectDisposedException("WindowsClipboard");
        }

        void OnHandleCreated(object sender, EventArgs args) { if (!_disposed) _listener.Attach(_dispatcher.Handle); }
        void OnHandleDestroyed(object sender, EventArgs args) { _listener.Detach(); }
        void OnDispatcherDisposed(object sender, EventArgs args) { Dispose(); }
        void Updated()
        {
            if (_disposed) return;
            uint current = Sequence;
            if (_publishing || current == _observed) return;
            _observed = current;
            if (current == _known) return;
            _known = 0;
            _revision++;
            if (_changed != null) _changed();
        }

        public ClipboardSnapshot Capture()
        {
            CheckThread();
            uint sequence = Sequence;
            IDataObject data = Clipboard.GetDataObject();
            if (data == null) return null;
            ClipboardSnapshot result = null;
            try
            {
                // A drop's path strings must never fall through into text synchronization.
                if (data.GetDataPresent(DataFormats.FileDrop, false)) result = CaptureFiles(data);
                else if (data.GetDataPresent("PNG", false)) result = CapturePng(data.GetData("PNG", false));
                else if (data.GetDataPresent(DataFormats.Bitmap)) { using (Image image = data.GetData(DataFormats.Bitmap) as Image) result = CaptureBitmap(image); }
                else if (data.GetDataPresent(DataFormats.UnicodeText, false)) result = ClipboardWire.Text(data.GetData(DataFormats.UnicodeText, false) as string ?? "");
                else if (data.GetDataPresent(DataFormats.Text, false)) result = ClipboardWire.Text(data.GetData(DataFormats.Text, false) as string ?? "");
                if (sequence != Sequence) throw new ExternalException("Clipboard changed while being captured.");
                return result;
            }
            catch { if (result != null) Release(result); throw; }
        }

        ClipboardSnapshot CaptureFiles(IDataObject data)
        {
            string[] paths = data.GetData(DataFormats.FileDrop, false) as string[];
            if (paths == null || paths.Length == 0 || paths.Length > 128) throw new InvalidDataException("Invalid clipboard file count.");
            ClipboardSnapshot snapshot = new ClipboardSnapshot { Kind = ClipboardKind.Files, Id = DateTime.UtcNow.Ticks };
            long total = 0;
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists || (info.Attributes & FileAttributes.Directory) != 0) throw new InvalidDataException("Clipboard source is not a regular file.");
                if (info.Length > MaxFile || info.Length > MaxTotal - total) throw new InvalidDataException("Clipboard files exceed the transfer limit.");
                total += info.Length;
                string name = info.Name;
                int suffix = 2;
                while (!names.Add(name)) name = Path.GetFileNameWithoutExtension(info.Name) + " (" + (suffix++) + ")" + info.Extension;
                snapshot.Items.Add(FileItem(info.FullName, name, "application/octet-stream", info.Length, info.LastWriteTimeUtc));
            }
            return snapshot;
        }

        static ClipboardItem FileItem(string path, string name, string mime, long length, DateTime written)
        {
            return new ClipboardItem { Name = name, Mime = mime, Path = path, Length = length, Open = delegate
            {
                FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                try
                {
                    if (stream.Length != length || File.GetLastWriteTimeUtc(path) != written) throw new IOException("Clipboard source changed before transfer.");
                    return stream;
                }
                catch { stream.Dispose(); throw; }
            } };
        }

        string NewCaptureDirectory()
        {
            string path = Path.Combine(_cacheRoot, "capture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        ClipboardSnapshot CachedImage(string directory)
        {
            string path = Path.Combine(directory, "image.png");
            FileInfo info = new FileInfo(path);
            ClipboardSnapshot snapshot = new ClipboardSnapshot { Kind = ClipboardKind.Image, Id = DateTime.UtcNow.Ticks };
            snapshot.Items.Add(FileItem(path, "image.png", "image/png", info.Length, info.LastWriteTimeUtc));
            _captures.Add(snapshot, directory);
            return snapshot;
        }

        ClipboardSnapshot CapturePng(object value)
        {
            Stream input = value as Stream;
            byte[] bytes = value as byte[];
            if (input == null && bytes == null) throw new InvalidDataException("Clipboard PNG is not a stream.");
            string directory = NewCaptureDirectory();
            try
            {
                using (FileStream output = File.Create(Path.Combine(directory, "image.png")))
                {
                    if (bytes != null) { if (bytes.LongLength > MaxImage) throw new InvalidDataException("Clipboard image exceeds the transfer limit."); output.Write(bytes, 0, bytes.Length); }
                    else { if (input.CanSeek) input.Position = 0; CopyBounded(input, output, MaxImage, -1); }
                }
                return CachedImage(directory);
            }
            catch { DeleteCapture(directory); throw; }
        }

        ClipboardSnapshot CaptureBitmap(Image image)
        {
            if (image == null) throw new InvalidDataException("Clipboard bitmap is unavailable.");
            CheckPixels(image);
            string directory = NewCaptureDirectory();
            try
            {
                using (FileStream output = File.Create(Path.Combine(directory, "image.png")))
                using (BoundedWriteStream bounded = new BoundedWriteStream(output, MaxImage)) image.Save(bounded, ImageFormat.Png);
                return CachedImage(directory);
            }
            catch { DeleteCapture(directory); throw; }
        }

        static void CheckPixels(Image image)
        {
            if (image.Width <= 0 || image.Height <= 0 || (long)image.Width * image.Height > MaxImage / 4)
                throw new InvalidDataException("Clipboard bitmap dimensions exceed the transfer limit.");
        }

        public void Publish(ClipboardSnapshot snapshot)
        {
            CheckThread();
            if (snapshot == null || snapshot.Items == null) throw new ArgumentNullException("snapshot");
            DataObject data = new DataObject();
            MemoryStream png = null;
            Bitmap bitmap = null;
            try
            {
                if (snapshot.Kind == ClipboardKind.Text)
                    data.SetData(DataFormats.UnicodeText, false, ClipboardWire.TextContent(snapshot));
                else if (snapshot.Kind == ClipboardKind.Image)
                {
                    if (snapshot.Items.Count != 1) throw new InvalidDataException("Clipboard image requires one item.");
                    ClipboardItem item = snapshot.Items[0];
                    if (item.Length < 0 || item.Length > MaxImage || item.Open == null) throw new InvalidDataException("Clipboard image exceeds the transfer limit.");
                    png = new MemoryStream();
                    using (Stream input = item.Open()) CopyBounded(input, png, MaxImage, item.Length);
                    png.Position = 0;
                    bool encodedPng;
                    using (Image image = Image.FromStream(png, true, true)) { CheckPixels(image); encodedPng = image.RawFormat.Guid == ImageFormat.Png.Guid; bitmap = new Bitmap(image); }
                    if (!encodedPng)
                    {
                        png.SetLength(0);
                        using (BoundedWriteStream bounded = new BoundedWriteStream(png, MaxImage)) bitmap.Save(bounded, ImageFormat.Png);
                    }
                    png.Position = 0;
                    data.SetData("PNG", false, png);
                    data.SetData(DataFormats.Bitmap, true, bitmap);
                }
                else if (snapshot.Kind == ClipboardKind.Files)
                {
                    if (snapshot.Items.Count == 0 || snapshot.Items.Count > 128) throw new InvalidDataException("Invalid clipboard file count.");
                    StringCollection paths = new StringCollection();
                    long total = 0;
                    foreach (ClipboardItem item in snapshot.Items)
                    {
                        if (item.Length < 0 || item.Length > MaxFile || item.Length > MaxTotal - total) throw new InvalidDataException("Clipboard files exceed the transfer limit.");
                        total += item.Length;
                        if (String.IsNullOrEmpty(item.Path)) throw new InvalidDataException("Clipboard file requires a cached local path.");
                        FileInfo info = new FileInfo(item.Path);
                        if (!info.Exists || info.Length != item.Length) throw new IOException("Clipboard file is no longer available.");
                        // Transfer caches own their lifetime: Explorer may paste after this adapter exits.
                        paths.Add(info.FullName);
                    }
                    data.SetFileDropList(paths);
                }
                else throw new InvalidDataException("Unsupported clipboard kind.");
                _publishing = true;
                try
                {
                    Clipboard.SetDataObject(data, true, 0, 0);
                    _known = Sequence;
                    _observed = _known;
                }
                finally { _publishing = false; }
            }
            finally { if (bitmap != null) bitmap.Dispose(); if (png != null) png.Dispose(); }
        }

        static void CopyBounded(Stream input, Stream output, long limit, long expected)
        {
            byte[] buffer = new byte[65536];
            long total = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                if (read > limit - total || (expected >= 0 && read > expected - total)) throw new InvalidDataException("Clipboard payload exceeds its declared length.");
                output.Write(buffer, 0, read);
                total += read;
            }
            if (expected >= 0 && total != expected) throw new EndOfStreamException("Clipboard payload is shorter than declared.");
        }

        public void Release(ClipboardSnapshot snapshot)
        {
            CheckThread();
            string directory;
            if (snapshot != null && _captures.TryGetValue(snapshot, out directory)) { _captures.Remove(snapshot); DeleteCapture(directory); }
        }

        static void DeleteCapture(string directory)
        {
            try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            CheckThread();
            _disposed = true;
            _dispatcher.HandleCreated -= OnHandleCreated;
            _dispatcher.HandleDestroyed -= OnHandleDestroyed;
            _dispatcher.Disposed -= OnDispatcherDisposed;
            _listener.Detach();
            foreach (string directory in _captures.Values) DeleteCapture(directory);
            _captures.Clear();
        }

        sealed class Listener : NativeWindow
        {
            readonly WindowsClipboard _owner;
            public Listener(WindowsClipboard owner) { _owner = owner; }
            public void Attach(IntPtr handle)
            {
                Detach();
                AssignHandle(handle);
                if (!AddClipboardFormatListener(handle)) { int error = Marshal.GetLastWin32Error(); ReleaseHandle(); throw new Win32Exception(error); }
            }
            public void Detach() { if (Handle != IntPtr.Zero) { RemoveClipboardFormatListener(Handle); ReleaseHandle(); } }
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x031D) _owner.Updated();
                base.WndProc(ref message);
            }
        }

        sealed class BoundedWriteStream : Stream
        {
            readonly Stream _inner;
            readonly long _limit;
            public BoundedWriteStream(Stream inner, long limit) { _inner = inner; _limit = limit; }
            public override bool CanRead { get { return false; } }
            public override bool CanSeek { get { return _inner.CanSeek; } }
            public override bool CanWrite { get { return true; } }
            public override long Length { get { return _inner.Length; } }
            public override long Position { get { return _inner.Position; } set { if (value < 0 || value > _limit) throw new IOException("Image exceeds limit."); _inner.Position = value; } }
            public override void Flush() { _inner.Flush(); }
            public override int Read(byte[] b, int o, int c) { throw new NotSupportedException(); }
            public override long Seek(long o, SeekOrigin origin) { long target = origin == SeekOrigin.Begin ? o : origin == SeekOrigin.Current ? Position + o : Length + o; Position = target; return target; }
            public override void SetLength(long value) { if (value < 0 || value > _limit) throw new IOException("Image exceeds limit."); _inner.SetLength(value); }
            public override void Write(byte[] b, int o, int c) { if (c > _limit - Position) throw new IOException("Image exceeds limit."); _inner.Write(b, o, c); }
        }
    }
}