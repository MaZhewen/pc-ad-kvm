using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using PcKvm;
class ClipboardPublicationTests
{
    sealed class MemoryClipboard : IWindowsClipboardAccess
    {
        public uint Number = 1;
        public string Current = "original local";
        public int Writes; public IDataObject Data;
        public uint Sequence { get { return Number; } }
        public IDataObject GetDataObject() { if (Data != null) return Data; var data = new DataObject(); data.SetText(Current); return data; }
        public void SetDataObject(IDataObject data)
        {
            Writes++; Number++;
            Current = data.GetDataPresent("PNG", false) ? "remote image" : (string)data.GetData(DataFormats.UnicodeText, false);
        }
        public void Copy(string text) { Current = text; Number++; }
    }
    sealed class UnavailableText : IDataObject
    {
        public object GetData(string f,bool a){return null;} public object GetData(string f){return null;} public object GetData(Type f){return null;}
        public bool GetDataPresent(string f,bool a){return f==DataFormats.UnicodeText;} public bool GetDataPresent(string f){return GetDataPresent(f,false);} public bool GetDataPresent(Type f){return false;}
        public string[] GetFormats(bool a){return new string[]{DataFormats.UnicodeText};} public string[] GetFormats(){return GetFormats(false);}
        public void SetData(string f,bool a,object v){throw new NotSupportedException();} public void SetData(string f,object v){throw new NotSupportedException();} public void SetData(Type f,object v){throw new NotSupportedException();} public void SetData(object v){throw new NotSupportedException();}
    }
    static int checks;
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
    static ClipboardSnapshot Image(byte[] bytes, Action opening)
    {
        return new ClipboardSnapshot { Kind = ClipboardKind.Image, Id = 1, Items = new List<ClipboardItem> {
            new ClipboardItem { Name = "image.png", Mime = "image/png", Length = bytes.Length, Open = delegate { if (opening != null) opening(); return new MemoryStream(bytes, false); } }
        } };
    }
    [STAThread] static int Main()
    {
        string cache = Path.Combine(Path.GetTempPath(), "pckvm-publication-" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[] png;
            using (var output = new MemoryStream()) { using (var image = new Bitmap(8, 8)) image.Save(output, ImageFormat.Png); png = output.ToArray(); }
            using (var host = new Form())
            {
                var memory = new MemoryClipboard();
                using (var adapter = new WindowsClipboard(host, cache, delegate { }, memory))
                {
                    uint expected = adapter.Sequence;
                    ClipboardSnapshot remote = Image(png, delegate { memory.Copy("new local copy during image preparation"); });
                    Check(!adapter.TryPublish(remote, expected, delegate { return true; }), "new native copy cancels prepared remote image");
                    Check(memory.Current == "new local copy during image preparation" && memory.Writes == 0, "new native copy is preserved without any remote write");
                    Check(!adapter.IsKnownSequence(adapter.Sequence), "rejected image is never marked as own publication");
                    Check(!adapter.TryPublish(Image(png, null), expected, delegate { return true; }), "sequence sampled before entering backend is respected");
                    Check(memory.Writes == 0, "stale expected sequence causes no write");
                    bool allowed = true;
                    remote = Image(png, delegate { allowed = false; });
                    Check(!adapter.TryPublish(remote, adapter.Sequence, delegate { return allowed; }), "canceled session cannot publish after preparation");
                    Check(memory.Writes == 0, "canceled image causes no write");
                    Check(adapter.TryPublish(Image(png, null), adapter.Sequence, delegate { return true; }), "current image publishes normally");
                    Check(memory.Current == "remote image" && memory.Writes == 1, "current image commits once");
                    Check(adapter.IsKnownSequence(adapter.Sequence), "successful publication has echo protection");
                    expected = adapter.Sequence; memory.Copy("fresh local text");
                    Check(!adapter.TryPublish(ClipboardWire.Text("old remote text"), expected, delegate { return true; }), "text also respects final sequence guard");
                    Check(memory.Current == "fresh local text" && memory.Writes == 1, "stale text cannot overwrite local copy");
                    memory.Data = new UnavailableText(); bool unavailable = false;
                    try { adapter.Capture(); } catch (System.Runtime.InteropServices.ExternalException) { unavailable = true; }
                    Check(unavailable, "advertised but unavailable text must be retried, never converted to empty text");
                    Check(memory.Current == "fresh local text", "unavailable capture leaves current clipboard intact");
                }
            }
            Console.WriteLine("PASS clipboard publication " + checks + " assertions; native clipboard untouched"); return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL " + error.Message); return 1; }
    }
}