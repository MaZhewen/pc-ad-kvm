public class TestMain {
 static void eq(int a,int b) { if(a!=b) throw new AssertionError(a+" != "+b); }
 public static void main(String[] args) {
  eq(AbsolutePointer.axis(800,3200),8192);
  eq(AbsolutePointer.axis(3199,3200),32758);
  eq(AbsolutePointer.axis(-1,3200),0);
  for(int size:new int[]{2136,3200,3840}) for(int p=0;p<size;p++) {
   double actual=AbsolutePointer.axis(p,size)*size/32768.0;
   if(Math.abs(actual-p)>0.07) throw new AssertionError("pixel drift "+actual+" vs "+p);
  }
  byte[] report=AbsolutePointer.report(3,800,1600,3200,2136,-1,true);
  eq(report.length,8); eq(report[0],1); eq(report[1],3);
  eq((report[2]&255)|((report[3]&255)<<8),8192);
  eq((report[4]&255)|((report[5]&255)<<8),16384);
  eq(report[6],-1); eq(report[7],1);
  int[] raw90=AbsolutePointer.rawPoint(800,800,3200,2136,90);
  eq(raw90[0],8192); eq(raw90[1],24576);
  int[] raw270=AbsolutePointer.rawPoint(800,800,3200,2136,270);
  eq(raw270[0],24576); eq(raw270[1],8192);
  byte[] rotated=AbsolutePointer.report(0,800,800,3200,2136,90,0,true);
  eq((rotated[2]&255)|((rotated[3]&255)<<8),8192);
  eq((rotated[4]&255)|((rotated[5]&255)<<8),24576);
  byte[] cookedTarget=AbsolutePointer.displayReport(0,800,800,3200,2136,90,0,true);
  eq((cookedTarget[2]&255)|((cookedTarget[3]&255)<<8),800);
  eq((cookedTarget[4]&255)|((cookedTarget[5]&255)<<8),2399);
  eq(AbsolutePointer.axisMax(3200,2136,90,0),2135);
  eq(AbsolutePointer.axisMax(3200,2136,90,1),3199);
  byte[] horizontal=AbsolutePointer.displayReport(0,1600,800,3200,2136,90,0,true);
  eq((horizontal[2]&255)|((horizontal[3]&255)<<8),800);
  eq((horizontal[4]&255)|((horizontal[5]&255)<<8),1599);
  eq(AbsolutePointer.report(0,0,0,2136,3200,0,false)[7],0);
  System.out.println("PASS absolute mapping: all pixels, report layout, buttons, wheel, hover/leave");
 }
}
