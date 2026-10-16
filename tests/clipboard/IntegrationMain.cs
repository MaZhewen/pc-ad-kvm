using System;
using PcKvm;
class ClipboardIntegrationTests {
 static void Main() {
  byte[] enable=Protocol.EncodeClipboardControl(1), refresh=Protocol.EncodeClipboardControl(2);
  if(enable.Length!=2 || enable[0]!=0x0f || enable[1]!=1 || refresh[1]!=2 || Protocol.PayloadLength(0x0f)!=1) throw new Exception("clipboard control framing");
  if(DeviceLauncher.ClipboardRemotePort!=27184 || DeviceLauncher.ClipboardLocalPort!=0) throw new Exception("clipboard tunnel must be opt-in");
  if(!Config.Parse("",null).EnableClipboard || Config.Parse("EnableClipboard=false",null).EnableClipboard) throw new Exception("clipboard configuration toggle");
  Console.WriteLine("PASS clipboard control independent of input frames and optional reverse");
 }
}
