public class TouchTestMain {
 static final class Sink implements TouchPointer.Sink {
  int count, failAt=-1, failAt2=-1; int[] action=new int[12]; long[] down=new long[12], when=new long[12]; int[] x=new int[12], y=new int[12];
  long now=1000;
  public long uptime() { return now+=40; }
  public boolean inject(int a,long d,long t,int xx,int yy) {
   int i=count++; action[i]=a; down[i]=d; when[i]=t; x[i]=xx; y[i]=yy;
   return i!=failAt && i!=failAt2;
  }
 }
 static void eq(long a,long b) { if(a!=b) throw new AssertionError(a+" != "+b); }
 public static void main(String[] args) {
  Sink s=new Sink(); TouchPointer p=new TouchPointer(s);
  if(!p.down(120,350)||!p.up(120,350)||!p.down(120,350)||!p.up(120,350)) throw new AssertionError("tap failed");
  eq(s.count,4); eq(s.action[0],0); eq(s.action[1],1); eq(s.action[2],0); eq(s.action[3],1);
  eq(s.down[0],s.down[1]); eq(s.down[2],s.down[3]);
  if(s.down[0]==s.down[2]) throw new AssertionError("second tap must have new downTime");
  for(int i=0;i<4;i++) { eq(s.x[i],120); eq(s.y[i],350); if(i>0 && s.when[i]<=s.when[i-1]) throw new AssertionError("event times not ordered"); }
  if(!p.down(120,350)||!p.move(145,350)||!p.up(145,350)) throw new AssertionError("drag failed");
  eq(s.action[4],0); eq(s.action[5],2); eq(s.action[6],1); eq(s.down[4],s.down[5]); eq(s.down[5],s.down[6]);
  if(!p.down(120,350)) throw new AssertionError("down failed"); p.cancel(); eq(s.action[7],0); eq(s.action[8],3);
  Sink fail=new Sink(); fail.failAt=1; TouchPointer failed=new TouchPointer(fail);
  if(!failed.down(120,350)||failed.up(120,350)) throw new AssertionError("failure ignored");
  eq(fail.count,3); eq(fail.action[2],3);
  Sink failedDown=new Sink(); failedDown.failAt=0; TouchPointer uncertain=new TouchPointer(failedDown);
  if(uncertain.down(120,350)) throw new AssertionError("failed down accepted");
  eq(failedDown.count,2); eq(failedDown.action[1],3);
  if(uncertain.up(120,350)) throw new AssertionError("up after failed down accepted");
  Sink failedCancel=new Sink(); failedCancel.failAt=1; failedCancel.failAt2=2;
  TouchPointer retry=new TouchPointer(failedCancel);
  if(!retry.down(120,350)) throw new AssertionError("initial down failed");
  retry.cancel(); eq(failedCancel.count,3);
  if(!retry.down(120,350)) throw new AssertionError("cleanup retry blocked new gesture");
  eq(failedCancel.action[3],3); eq(failedCancel.action[4],0);
  System.out.println("PASS touch click, double-click, drag, cancel and failure handling");
 }
}