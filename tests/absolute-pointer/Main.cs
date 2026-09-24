using System;
using PcKvm;
class PointerTests {
 static void Check(bool v,string s) { if(!v) throw new Exception(s); Console.WriteLine("PASS "+s); }
 static int Main() { try {
  var q=new FrameQueue(4); q.Add(new byte[]{11,1}); q.Add(new byte[]{11,2});
  Check(q.Count==1 && q.Take()[1]==2,"moves coalesce to latest target");
  q.Add(new byte[]{11,3}); q.Add(new byte[]{3,1}); q.Add(new byte[]{11,4});
  Check(q.Take()[1]==3 && q.Take()[0]==3 && q.Take()[1]==4,"click preserves preceding and following position order");
  for(int i=0;i<4;i++) Check(q.Add(new byte[]{5}),"bounded queue accepts capacity");
  Check(!q.Add(new byte[]{5}),"overflow explicitly fails instead of dropping keys");
  var p=new PointerSession(delegate(byte[] b){}); p.Configure(3200,2136,90);
  Check(!p.Ready,"not ready before geometry ack");
  p.Receive(13,new byte[]{1,0,0,0}); Check(p.Ready,"matching geometry ack");
  p.Move(3199,100); byte[] ack=Protocol.EncodePointer(1,1,3199,100);
  byte[] payload=new byte[12]; Array.Copy(ack,1,payload,0,12); p.Receive(14,payload);
  Check(p.Confirmed(3199,100),"confirmed target accepted");
  p.Move(3000,100); Check(!p.Confirmed(3000,100),"pending target not confirmed");
  p.Configure(2136,3200,270); p.Receive(14,payload); Check(!p.Ready && !p.Confirmed(3199,100),"old geometry ack rejected");
  int rx,ry;
  RotationMap.Map(1000,500,3200,2136,90,2136,3200,0,out rx,out ry);
  Check(rx==500 && ry==2199,"landscape to portrait keeps physical cursor location");
  RotationMap.Map(rx,ry,2136,3200,0,3200,2136,90,out rx,out ry);
  Check(rx==1000 && ry==500,"portrait to landscape reverses mapping");
  RotationMap.Map(100,200,2136,3200,0,2136,3200,180,out rx,out ry);
  Check(rx==2035 && ry==2999,"half turn maps both axes");
  Console.WriteLine("ALL PASS"); return 0;
 } catch(Exception e) { Console.WriteLine("FAIL "+e.Message); return 1; } }
}
