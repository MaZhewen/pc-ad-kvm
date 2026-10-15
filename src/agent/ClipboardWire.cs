using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace PcKvm
{
    public enum ClipboardKind { Text = 1, Image = 2, Files = 3 }
    public sealed class ClipboardItem
    {
        public string Name, Mime, Path;
        public long Length;
        public Func<Stream> Open;
    }
    public sealed class ClipboardSnapshot
    {
        public ClipboardKind Kind;
        public long Id;
        public List<ClipboardItem> Items = new List<ClipboardItem>();
        internal string CachedFingerprint;
        // File-backed snapshots must be fingerprinted by the background transfer worker.
        public string Fingerprint()
        {
            if (CachedFingerprint == null) ClipboardWire.Write(Stream.Null, this, null);
            return CachedFingerprint;
        }
    }
    public static class ClipboardWire
    {
        const int Chunk = 65536, MetadataLimit = 4096, TextLimit = 1048576;
        const long FileLimit = 1073741824L, TotalLimit = 2147483648L;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        static void Cancel(Func<bool> canceled)
        { if (canceled != null && canceled()) throw new OperationCanceledException("Clipboard transfer canceled"); }
        static void Number(Stream stream, long value, int bytes)
        { for (int n = 0; n < bytes; n++) stream.WriteByte((byte)((ulong)value >> (8 * n))); }
        static long Number(Stream stream, int bytes)
        {
            ulong value = 0;
            for (int n = 0; n < bytes; n++) { int b = stream.ReadByte(); if (b < 0) throw new EndOfStreamException("Truncated clipboard record"); value |= (ulong)b << (8 * n); }
            if (value > long.MaxValue) throw new InvalidDataException("Clipboard integer overflow");
            return (long)value;
        }
        static byte[] Encoded(string value)
        {
            if (value == null) throw new InvalidDataException("Missing metadata");
            try { byte[] bytes = Utf8.GetBytes(value); if (bytes.Length == 0 || bytes.Length > MetadataLimit) throw new InvalidDataException("Metadata length"); return bytes; }
            catch (EncoderFallbackException e) { throw new InvalidDataException("Invalid metadata UTF-8", e); }
        }
        static void String(Stream stream, string value)
        { byte[] bytes = Encoded(value); Number(stream, bytes.Length, 4); stream.Write(bytes, 0, bytes.Length); }
        static void Exact(Stream stream, byte[] buffer, int count)
        { int offset = 0; while (offset < count) { int n = stream.Read(buffer, offset, count - offset); if (n <= 0) throw new EndOfStreamException("Truncated clipboard content"); offset += n; } }
        static string String(Stream stream)
        {
            long length = Number(stream, 4); if (length < 1 || length > MetadataLimit) throw new InvalidDataException("Metadata length");
            byte[] bytes = new byte[(int)length]; Exact(stream, bytes, bytes.Length);
            try { return Utf8.GetString(bytes); } catch (DecoderFallbackException e) { throw new InvalidDataException("Invalid metadata UTF-8", e); }
        }
        static void Name(string name)
        {
            Encoded(name);
            if (name == "." || name == ".." || name.EndsWith(" ", StringComparison.Ordinal) || name.EndsWith(".", StringComparison.Ordinal)) throw new InvalidDataException("Unsafe clipboard name");
            foreach (char c in name) if (char.IsControl(c) || "<>:\"/\\|?*".IndexOf(c) >= 0) throw new InvalidDataException("Unsafe clipboard name");
            string stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" || stem == "CONIN$" || stem == "CONOUT$" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789\u00b9\u00b2\u00b3".IndexOf(stem[3]) >= 0)) throw new InvalidDataException("Reserved clipboard name");
        }
        static void Header(ClipboardKind kind, long id, int count)
        {
            if (id <= 0 || count < 1 || count > 128 || (kind != ClipboardKind.Text && kind != ClipboardKind.Image && kind != ClipboardKind.Files) || (kind != ClipboardKind.Files && count != 1)) throw new InvalidDataException("Invalid clipboard header");
        }
        static void Item(ClipboardKind kind, ClipboardItem item, ref long total)
        {
            if (item == null) throw new InvalidDataException("Missing item"); Name(item.Name); Encoded(item.Mime);
            if (item.Length < 0 || item.Length > FileLimit || (kind == ClipboardKind.Text && item.Length > TextLimit) || item.Length > TotalLimit - total) throw new InvalidDataException("Clipboard content limit");
            total += item.Length;
        }
        static byte[] Copy(Stream input, Stream output, long length, Func<bool> canceled)
        {
            byte[] buffer = new byte[Chunk];
            using (SHA256 hash = SHA256.Create())
            {
                while (length > 0) { Cancel(canceled); int n = input.Read(buffer, 0, (int)Math.Min(buffer.Length, length)); if (n <= 0) throw new EndOfStreamException("Clipboard source ended early"); output.Write(buffer, 0, n); hash.TransformBlock(buffer, 0, n, buffer, 0); length -= n; }
                Cancel(canceled); hash.TransformFinalBlock(new byte[0], 0, 0); return hash.Hash;
            }
        }
        static void FingerItem(Stream fingerprint, ClipboardItem item, byte[] digest)
        { String(fingerprint, item.Name); String(fingerprint, item.Mime); Number(fingerprint, item.Length, 8); fingerprint.Write(digest, 0, digest.Length); }
        static MemoryStream FingerStart(ClipboardSnapshot snapshot)
        { var stream = new MemoryStream(); stream.WriteByte((byte)snapshot.Kind); Number(stream, snapshot.Items.Count, 4); return stream; }
        static string FingerEnd(MemoryStream stream)
        { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant(); }
        public static ClipboardSnapshot Text(string text)
        {
            byte[] bytes;
            try { bytes = Utf8.GetBytes(text ?? ""); } catch (EncoderFallbackException e) { throw new InvalidDataException("Invalid clipboard text", e); }
            if (bytes.Length > TextLimit) throw new InvalidDataException("Clipboard text limit");
            var snapshot = new ClipboardSnapshot { Kind = ClipboardKind.Text, Id = DateTime.UtcNow.Ticks };
            snapshot.Items.Add(new ClipboardItem { Name = "text.txt", Mime = "text/plain", Length = bytes.Length, Open = delegate { return new MemoryStream(bytes, false); } });
            return snapshot;
        }
        public static string TextContent(ClipboardSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Kind != ClipboardKind.Text || snapshot.Items.Count != 1 || snapshot.Items[0].Length < 0 || snapshot.Items[0].Length > TextLimit) throw new InvalidDataException("Not a text snapshot");
            ClipboardItem item = snapshot.Items[0]; byte[] bytes = new byte[(int)item.Length];
            using (Stream input = item.Open()) { Exact(input, bytes, bytes.Length); if (input.ReadByte() != -1) throw new InvalidDataException("Text source changed"); }
            try { return Utf8.GetString(bytes); } catch (DecoderFallbackException e) { throw new InvalidDataException("Invalid text UTF-8", e); }
        }
        public static void Write(Stream output, ClipboardSnapshot snapshot, Func<bool> canceled)
        {
            if (snapshot == null || snapshot.Items == null) throw new InvalidDataException("Missing snapshot");
            Header(snapshot.Kind, snapshot.Id, snapshot.Items.Count);
            long total = 0;
            foreach (var item in snapshot.Items) { Item(snapshot.Kind, item, ref total); if (item.Open == null) throw new InvalidDataException("Missing content source"); }
            Cancel(canceled); snapshot.CachedFingerprint = null;
            output.WriteByte((byte)snapshot.Kind); Number(output, snapshot.Id, 8); Number(output, snapshot.Items.Count, 4);
            using (MemoryStream fingerprint = FingerStart(snapshot))
            {
                foreach (var item in snapshot.Items)
                {
                    Cancel(canceled); String(output, item.Name); String(output, item.Mime); Number(output, item.Length, 8);
                    byte[] digest; using (Stream input = item.Open()) { if (input == null) throw new IOException("Missing source stream"); digest = Copy(input, output, item.Length, canceled); if (input.ReadByte() != -1) throw new InvalidDataException("Clipboard source grew"); }
                    output.Write(digest, 0, digest.Length); FingerItem(fingerprint, item, digest);
                }
                Cancel(canceled); snapshot.CachedFingerprint = FingerEnd(fingerprint);
            }
        }
        public static ClipboardSnapshot Read(Stream input, string cacheRoot, Func<bool> canceled)
        {
            Cancel(canceled); int kind = input.ReadByte(); if (kind < 0) throw new EndOfStreamException("Clipboard connection closed");
            var snapshot = new ClipboardSnapshot { Kind = (ClipboardKind)kind, Id = Number(input, 8) };
            long count = Number(input, 4); if (count > 128) throw new InvalidDataException("Clipboard item limit"); Header(snapshot.Kind, snapshot.Id, (int)count);
            Directory.CreateDirectory(cacheRoot); string token = Guid.NewGuid().ToString("N");
            string partial = System.IO.Path.Combine(cacheRoot, token + ".partial"), complete = System.IO.Path.Combine(cacheRoot, token);
            bool owned = false;
            try
            {
                if (Directory.Exists(partial) || Directory.Exists(complete)) throw new IOException("Cache collision"); Directory.CreateDirectory(partial); owned = true;
                long total = 0;
                using (MemoryStream fingerprint = FingerStartForRead(snapshot, (int)count))
                {
                    for (int n = 0; n < count; n++)
                    {
                        Cancel(canceled); var item = new ClipboardItem { Name = String(input), Mime = String(input), Length = Number(input, 8) }; Item(snapshot.Kind, item, ref total);
                        string itemDir = System.IO.Path.Combine(partial, n.ToString(System.Globalization.CultureInfo.InvariantCulture)); Directory.CreateDirectory(itemDir);
                        string path = System.IO.Path.Combine(itemDir, item.Name); byte[] digest;
                        using (Stream file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) digest = Copy(input, file, item.Length, canceled);
                        byte[] expected = new byte[32]; Exact(input, expected, expected.Length); int diff = 0; for (int j = 0; j < 32; j++) diff |= expected[j] ^ digest[j]; if (diff != 0) throw new InvalidDataException("Clipboard digest mismatch");
                        item.Path = path; ClipboardItem captured = item; item.Open = delegate { return File.OpenRead(captured.Path); }; snapshot.Items.Add(item); FingerItem(fingerprint, item, digest);
                    }
                    if (snapshot.Kind == ClipboardKind.Text) TextContent(snapshot);
                    snapshot.CachedFingerprint = FingerEnd(fingerprint);
                }
                Cancel(canceled); for (int n = 0; n < snapshot.Items.Count; n++) snapshot.Items[n].Path = System.IO.Path.Combine(System.IO.Path.Combine(complete, n.ToString(System.Globalization.CultureInfo.InvariantCulture)), snapshot.Items[n].Name);
                Directory.Move(partial, complete); return snapshot;
            }
            catch { if (owned) { try { Directory.Delete(partial, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } } throw; }
        }
        static MemoryStream FingerStartForRead(ClipboardSnapshot snapshot, int count)
        { var stream = new MemoryStream(); stream.WriteByte((byte)snapshot.Kind); Number(stream, count, 4); return stream; }
    }
}
