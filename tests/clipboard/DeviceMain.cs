using System;using System.IO;using System.Collections.Generic;using System.Diagnostics;using System.Threading;using System.Drawing;using System.Drawing.Imaging;using System.Collections.Specialized;using System.Security.Cryptography;using System.Text;using System.Windows.Forms;using PcKvm;
class ClipboardDeviceTests{
 static readonly object gate=new object();static List<string> lines=new List<string>();static ClipboardService service;static Process device;
 static readonly string PcText="PC-KVM clipboard PC 测试\nsecond line", AndroidText="PC-KVM clipboard Android 测试\nsecond line";
 static string Hash(string path){using(var h=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
 static void Wait(Func<bool> predicate,string name,int ms){Stopwatch w=Stopwatch.StartNew();while(w.ElapsedMilliseconds<ms){Application.DoEvents();try{if(predicate()){Console.WriteLine("PASS "+name);return;}}catch(System.Runtime.InteropServices.ExternalException){}Thread.Sleep(10);}throw new Exception(name+" timed out; clipboard="+service.Status);}
 static bool Has(string text){lock(gate)return lines.Contains(text);}
 static void Put(Action write){Stopwatch w=Stopwatch.StartNew();while(true){try{write();return;}catch(System.Runtime.InteropServices.ExternalException){if(w.ElapsedMilliseconds>3000)throw;Application.DoEvents();Thread.Sleep(25);}}}
 static void Command(string text){device.StandardInput.WriteLine(text);device.StandardInput.Flush();string key=text.Split(' ')[0];Wait(delegate{lock(gate)foreach(string line in lines)if(line.StartsWith("DEVICE OK "+key)||line.StartsWith("DEVICE FAIL "+key))return true;return false;},"device "+key,12000);lock(gate)foreach(string line in lines)if(line.StartsWith("DEVICE FAIL "+key))throw new Exception(line);}
 static void Adb(AdbClient a,AdbTarget t,params string[] args){var r=a.Execute(args,t,15000,null,CancellationToken.None);if(!r.Success)throw new Exception("ADB test setup failed");}
 [STAThread]static int Main(string[] args){
  string root=args[0];Directory.CreateDirectory(root);var adb=new AdbClient(args[3]);var target=AdbTarget.ForTransport(Int32.Parse(args[1]),args[2]);bool owned=false;
  using(Form host=new Form())using(Form savedHost=new Form())using(WindowsClipboard savedClipboard=new WindowsClipboard(savedHost,Path.Combine(root,"saved"),delegate{})){
   IntPtr handle=host.Handle;ClipboardSnapshot saved=savedClipboard.Capture();
   try{
    service=new ClipboardService(host,Path.Combine(root,"received"),delegate(string s){Console.WriteLine(s);});service.Start();
    byte[] token=new byte[32];using(var random=RandomNumberGenerator.Create())random.GetBytes(token);service.Configure(token,1);
    DeviceLauncher.UseClient(adb);DeviceLauncher.ClipboardLocalPort=service.ListeningPort;
    if(!DeviceLauncher.EnsureClipboardTunnel(target,CancellationToken.None,out owned))throw new Exception("Test clipboard port in use");
    string hex=BitConverter.ToString(token).Replace("-","").ToLowerInvariant();
    device=new Process{StartInfo=adb.StartInfo(new string[]{"shell","CLASSPATH=/data/local/tmp/pckvm-clipboard-device.jar app_process / ClipboardDeviceProbe "+hex},target,true)};
    device.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data==null)return;lock(gate)lines.Add(e.Data);if(e.Data.StartsWith("DEVICE ")||e.Data.StartsWith("CLIPBOARD "))Console.WriteLine(e.Data);};
    device.ErrorDataReceived+=delegate{};device.Start();device.BeginOutputReadLine();device.BeginErrorReadLine();
    Wait(delegate{return Has("DEVICE READY")&&service.Status=="剪贴板已就绪";},"authenticated real clipboard baseline",15000);
    long oldAck=service.LastAcknowledgedId;Put(delegate{Clipboard.SetText(PcText);});Wait(delegate{return service.LastAcknowledgedId!=oldAck;},"Windows to Android text ACK",15000);Command("EXPECT_TEXT");
    Command("SET_TEXT");Wait(delegate{return Clipboard.ContainsText()&&Clipboard.GetText()==AndroidText;},"Android to Windows Unicode text",15000);
    string imagePc=Path.Combine(root,"pc-test.png"),imageAndroid=Path.Combine(root,"android-test.png");
    using(Bitmap b=new Bitmap(8,8)){using(Graphics g=Graphics.FromImage(b))g.Clear(Color.Blue);b.Save(imagePc,ImageFormat.Png);}
    using(Bitmap b=new Bitmap(9,9)){using(Graphics g=Graphics.FromImage(b))g.Clear(Color.Green);b.Save(imageAndroid,ImageFormat.Png);}
    Adb(adb,target,"push",imageAndroid,"/data/local/tmp/pckvm-clipboard-test-image.png");
    oldAck=service.LastAcknowledgedId;using(MemoryStream png=new MemoryStream(File.ReadAllBytes(imagePc))){DataObject d=new DataObject();d.SetData("PNG",false,png);Put(delegate{Clipboard.SetDataObject(d,true,0,0);});}
    Wait(delegate{return service.LastAcknowledgedId!=oldAck;},"Windows to Android image ACK",15000);Command("EXPECT_IMAGE "+Hash(imagePc));
    Command("SET_IMAGE");Wait(delegate{if(!Clipboard.ContainsImage())return false;using(Image i=Clipboard.GetImage())return i.Width==9&&i.Height==9;},"Android to Windows image",15000);
    string pcFile=Path.Combine(root,"pc-test.txt"),androidFile=Path.Combine(root,"android-test.txt");File.WriteAllText(pcFile,"PC-KVM PC test file 中文",new UTF8Encoding(false));File.WriteAllText(androidFile,"PC-KVM Android test file 中文",new UTF8Encoding(false));
    Adb(adb,target,"push",androidFile,"/data/local/tmp/pckvm-clipboard-test-file.txt");
    oldAck=service.LastAcknowledgedId;Put(delegate{Clipboard.SetFileDropList(new StringCollection{pcFile});});Wait(delegate{return service.LastAcknowledgedId!=oldAck;},"Windows to Android file ACK",15000);Command("EXPECT_FILE "+Hash(pcFile));
    Command("SET_FILE");Wait(delegate{if(!Clipboard.ContainsFileDropList())return false;StringCollection paths=Clipboard.GetFileDropList();return paths.Count==1&&File.Exists(paths[0])&&Hash(paths[0])==Hash(androidFile);},"Android to Windows file bytes",15000);
    Console.WriteLine("PASS real device text/image/file both directions; no messages sent");return 0;
   }catch(Exception e){Console.WriteLine("FAIL "+e.ToString());return 1;}
   finally{
    if(service!=null)service.SetEnabled(false);
    if(device!=null){try{device.StandardInput.WriteLine("QUIT");device.StandardInput.Flush();Wait(delegate{return Has("DEVICE RESTORED");},"Android original clipboard restored",8000);}catch{}try{if(!device.HasExited)device.Kill();}catch{}device.Dispose();}
    if(service!=null){service.Dispose();Application.DoEvents();}
    if(owned)DeviceLauncher.CleanupClipboardTunnel(target);
    Put(delegate{if(saved!=null)savedClipboard.Publish(saved);else Clipboard.Clear();});
   }
  }
 }
}
