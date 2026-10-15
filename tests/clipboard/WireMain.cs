using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using PcKvm;
class WireMain {
 static int checks;
 static void Check(bool yes,string label) { if(!yes) throw new Exception(label); checks++; }
 static byte[] Encode(ClipboardSnapshot s) { using(var m=new MemoryStream()) { ClipboardWire.Write(m,s,null); return m.ToArray(); } }
 static ClipboardSnapshot Decode(byte[] b,string root) { return ClipboardWire.Read(new MemoryStream(b),root,null); }
 static void Bad(byte[] b,string root,string label) { int before=Directory.GetDirectories(root).Length; bool bad=false; try { Decode(b,root); } catch(IOException) { bad=true; } catch(InvalidDataException) { bad=true; } Check(bad,label); Check(Directory.GetDirectories(root).Length==before,label+" cleanup"); }
 static byte[] Raw(int kind,int count,string name,string mime,long length,byte[] content) { using(var m=new MemoryStream()) using(var w=new BinaryWriter(m)) { w.Write((byte)kind);w.Write((long)5);w.Write(count); if(count==1) { byte[] n=Encoding.UTF8.GetBytes(name),t=Encoding.UTF8.GetBytes(mime);w.Write(n.Length);w.Write(n);w.Write(t.Length);w.Write(t);w.Write(length);w.Write(content);using(var sha=SHA256.Create())w.Write(sha.ComputeHash(content)); } return m.ToArray(); } }
 static ClipboardSnapshot Files() { var s=new ClipboardSnapshot();s.Kind=ClipboardKind.Files;s.Id=123;s.Items.Add(new ClipboardItem {Name="marked-文件.bin",Mime="application/octet-stream",Length=150003,Open=delegate { byte[] b=new byte[150003]; for(int i=0;i<b.Length;i++)b[i]=(byte)(i*31);return new MemoryStream(b); }}); return s; }
 static void Self(string root) {
  var s=ClipboardWire.Text("中文\nhello 😀"); s.Id=17; var b=Encode(s); var r=Decode(b,root); Check(ClipboardWire.TextContent(r)=="中文\nhello 😀","unicode text"); Check(r.Id==17,"id");Check(s.Fingerprint()==r.Fingerprint(),"text fingerprint");
  var f=Files();var fb=Encode(f);var fr=Decode(fb,root); Check(fr.Items[0].Length==150003,"file length");Check(File.Exists(fr.Items[0].Path),"committed path");Check(!fr.Items[0].Path.Contains(".partial"),"atomic completion");Check(f.Fingerprint()==fr.Fingerprint(),"file fingerprint");
  var duplicates=Files();duplicates.Items.Add(Files().Items[0]);var dr=Decode(Encode(duplicates),root);Check(dr.Items[0].Name==dr.Items[1].Name && dr.Items[0].Path!=dr.Items[1].Path,"duplicate basenames isolated");
  var im=Files();im.Kind=ClipboardKind.Image;im.Items[0].Name="image.png";im.Items[0].Mime="image/png";Check(Decode(Encode(im),root).Kind==ClipboardKind.Image,"image");
  var corrupt=(byte[])fb.Clone();corrupt[corrupt.Length-1]^=1;Bad(corrupt,root,"digest mismatch");var trunc=new byte[fb.Length-2];Array.Copy(fb,trunc,trunc.Length);Bad(trunc,root,"truncated");
  foreach(string n in new[]{"../evil","a/b","a\\b","C:evil","CON","nul.txt","LPT1.txt","a.","a ","..","a\u0001","a\u0085"}) Bad(Raw(3,1,n,"x",0,new byte[0]),root,"unsafe "+n);
  Bad(Raw(9,0,"","",0,new byte[0]),root,"kind");Bad(Raw(3,129,"","",0,new byte[0]),root,"count");Bad(Raw(1,0,"","",0,new byte[0]),root,"text count");Bad(Raw(3,1,"ok","x",1073741825L,new byte[0]),root,"file limit");Bad(Raw(1,1,"text.txt","text/plain",1048577,new byte[0]),root,"text limit");
  var invalid=Raw(3,1,"a","x",0,new byte[0]);invalid[17]=255;Bad(invalid,root,"invalid UTF8");var meta=Raw(3,1,"a","x",0,new byte[0]);Array.Copy(BitConverter.GetBytes(4097),0,meta,13,4);Bad(meta,root,"metadata limit");
  int calls=0;bool canceled=false;int before=Directory.GetDirectories(root).Length;try{ClipboardWire.Read(new MemoryStream(fb),root,delegate{return ++calls>=3;});}catch(OperationCanceledException){canceled=true;}Check(canceled,"read cancel");Check(Directory.GetDirectories(root).Length==before,"cancel cleanup");
  calls=0;canceled=false;try{ClipboardWire.Write(new MemoryStream(),Files(),delegate{return ++calls>=3;});}catch(OperationCanceledException){canceled=true;}Check(canceled,"write cancel");
  var shortSource=Files();shortSource.Items[0].Open=delegate{return new MemoryStream(new byte[2]);};bool failed=false;try{Encode(shortSource);}catch(IOException){failed=true;}Check(failed,"source shrunk");
    var invalidText=ClipboardWire.Text("x"); invalidText.Items[0].Open=delegate{return new MemoryStream(new byte[]{255});};Bad(Encode(invalidText),root,"invalid text UTF8");
  byte[] overflow=Raw(3,1,"ok","x",0,new byte[0]);overflow[8]=128;Bad(overflow,root,"u64 overflow");
  var excessive=Files();excessive.Items[0].Length=1073741824L;excessive.Items.Add(new ClipboardItem{Name="b",Mime="x",Length=1073741824L,Open=delegate{throw new Exception("source opened before validation");}});excessive.Items.Add(new ClipboardItem{Name="c",Mime="x",Length=1,Open=delegate{throw new Exception("source opened before validation");}});bool limited=false;try{Encode(excessive);}catch(InvalidDataException){limited=true;}Check(limited,"total limit before opening sources");
  bool bounded=true;var streamed=Files();streamed.Items[0].Open=delegate{return new BoundedSource(150003,delegate(int request){if(request>65536)bounded=false;});};var streamedBytes=Encode(streamed);Check(bounded,"outbound bounded chunks");bounded=true;var guarded=new BoundedSource(streamedBytes,delegate(int request){if(request>65536)bounded=false;});ClipboardWire.Read(guarded,root,null);Check(bounded,"inbound bounded chunks");
  var cached=Decode(fb,root);File.Delete(cached.Items[0].Path);Check(cached.Fingerprint()==f.Fingerprint(),"received fingerprint cached");
  bool badString=false;try{ClipboardWire.Text("\ud800");}catch(InvalidDataException){badString=true;}Check(badString,"invalid local surrogate");
  Console.WriteLine("C# wire: "+checks+" assertions passed");
 }
 class BoundedSource:MemoryStream { readonly Action<int> request;public BoundedSource(int length,Action<int> r):base(new byte[length]){request=r;}public BoundedSource(byte[] b,Action<int> r):base(b){request=r;}public override int Read(byte[] b,int o,int n){request(n);return base.Read(b,o,n);} }
 static void Main(string[] args) { try { Run(args); } catch(Exception e) { Console.WriteLine("FAIL: "+e.GetType().Name+": "+e.Message+" at "+e.StackTrace);Environment.Exit(1); } } static void Run(string[] args) { string root=Path.Combine(args[1],"csharp-cache");Directory.CreateDirectory(root);if(args[0]=="self")Self(root);else if(args[0]=="write"){ File.WriteAllBytes(Path.Combine(args[1],"cs-text.bin"),Encode(ClipboardWire.Text("中文\nhello 😀")));File.WriteAllBytes(Path.Combine(args[1],"cs-files.bin"),Encode(Files())); }else if(args[0]=="verify"){Check(ClipboardWire.TextContent(Decode(File.ReadAllBytes(Path.Combine(args[1],"java-text.bin")),root))=="中文\nhello 😀","Java text");var f=Decode(File.ReadAllBytes(Path.Combine(args[1],"java-files.bin")),root);Check(f.Items[0].Name=="marked-文件.bin","Java name");Check(f.Fingerprint()==Files().Fingerprint(),"Java fingerprint");Console.WriteLine("Java -> C# fixtures passed");} }
}
