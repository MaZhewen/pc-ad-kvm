/** Uses the production descriptor/report encoder; safe hover-only by default. */
public class ProductionProbe {
 public static void main(String[] a) throws Exception {
  Thread watchdog=new Thread(new Runnable(){public void run(){try{Thread.sleep(22000);}catch(Exception e){} Runtime.getRuntime().halt(0);}}); watchdog.setDaemon(true);watchdog.start();
  UhidDevice mouse=new UhidDevice("PC-KVM Absolute Pointer",AbsolutePointer.DESCRIPTOR);
  Thread.sleep(2000);
  int x=Integer.parseInt(a[0]),y=Integer.parseInt(a[1]);
  mouse.sendReport(AbsolutePointer.report(0,x,y,3200,2136,0,true));
  System.out.println("TARGET "+x+","+y);
  Thread.sleep(1000);
  if(a.length>2 && a[2].equals("click")) {
   mouse.sendReport(AbsolutePointer.report(1,x,y,3200,2136,0,true)); Thread.sleep(80);
   mouse.sendReport(AbsolutePointer.report(0,x,y,3200,2136,0,true));
  }
  Thread.sleep(15000);
  mouse.sendReport(AbsolutePointer.report(0,x,y,3200,2136,0,false));
  Runtime.getRuntime().halt(0);
 }
}
