import java.io.OutputStream;
/** Versioned mouse session; socket thread owns all mutable state. */
public final class AbsoluteSession {
 UhidDevice mouse;
 int epoch,width,height,rotation,x,y,buttons;
 boolean active;
 Object inputDevice;
 AbsoluteTransform transform;
 long nextCaptureAt;
 final Class<?> input;
 public AbsoluteSession() throws Exception {
  input=Class.forName("android.view.InputDevice");
 }
 public boolean handle(int type,byte[] p,OutputStream out) throws Exception {
  if(type==0x0c) {
   release(); if(mouse!=null) { mouse.close(); mouse=null; inputDevice=null; transform=null; }
   epoch=Injector.i32(p,0); width=Injector.u16(p,4); height=Injector.u16(p,6); rotation=p[8]&255;
   if(width<=0||height<=0) throw new IllegalArgumentException("Invalid geometry");
   // Equal natural axes leave enough raw range for either landscape direction.
   // The actual display mapping is measured after Android registers the device.
   mouse=new UhidDevice("PC-KVM Absolute Pointer",AbsolutePointer.DESCRIPTOR,0x5680);
   inputDevice=waitForInputDevice();
   // Android creates PointerChoreographer's viewport only after the first
   // visible report. Hide it immediately; no buttons or movement are sent.
   mouse.sendReport(AbsolutePointer.rawReport(0,0,0,0,true));
   mouse.sendReport(AbsolutePointer.rawReport(0,0,0,0,false));
   nextCaptureAt=0;
   tryReady(out);
   return true;
  }
  if(type==0x08) { if(mouse!=null && transform==null) tryReady(out); return false; }
  if(type==0x0b) {
   if(epoch==0||transform==null||Injector.i32(p,0)!=epoch) return true;
   x=Injector.u16(p,8); y=Injector.u16(p,10);
   active=true; send(0);
   // Acknowledge UHID submission, not display presentation. PC also requires
   // sustained outward movement, and never trusts an acknowledgement from an old epoch.
   out.write(0x0e); out.write(p); out.flush(); return true;
  }
  if(type==0x03) {
   if(active) { int bit=Injector.btnToBit(p[0]&255); if(p[1]!=0) buttons|=bit; else buttons&=~bit; send(0); }
   return true;
  }
  if(type==0x04) { if(active) send(Injector.clamp127(Injector.i16(p,2))); return true; }
  if(type==0x06) { release(); return false; } // keyboard release remains in Injector
  if(type==0x01||type==0x02||type==0x0a) return true; // legacy relative path is never used
  return false;
 }
 void send(int wheel) throws Exception {
  if(mouse==null || transform==null) return;
  int[] raw=transform.raw(x,y);
  mouse.sendReport(AbsolutePointer.rawReport(buttons,raw[0],raw[1],wheel,active));
 }
 void release() throws Exception { buttons=0; active=false; if(width>0) send(0); }
 void tryReady(OutputStream out) throws Exception {
  long now=System.currentTimeMillis();
  if(now<nextCaptureAt) return;
  nextCaptureAt=now+2000;
  try {
   AbsoluteTransform candidate=AbsoluteTransform.capture("PC-KVM Absolute Pointer");
   candidate.validate(width,height);
   transform=candidate;
   out.write(new byte[]{0x0d,(byte)epoch,(byte)(epoch>>>8),(byte)(epoch>>>16),(byte)(epoch>>>24)});
   out.flush();
  } catch(IllegalStateException e) {
   // While the display sleeps, PointerChoreographer has no viewport. Keep the
   // socket and PING/PONG alive; retry after the next wake instead of disconnecting.
   if(e.getMessage()==null || !e.getMessage().startsWith("Absolute mouse display transform unavailable")) throw e;
  }
 }
 Object waitForInputDevice() throws Exception {
  for(int attempt=0;attempt<500;attempt++) {
   int[] ids=(int[])input.getMethod("getDeviceIds").invoke(null);
   for(int id:ids) {
    Object d=input.getMethod("getDevice",int.class).invoke(null,id);
    if(d==null || !"PC-KVM Absolute Pointer".equals(input.getMethod("getName").invoke(d))) continue;
    int sources=(Integer)input.getMethod("getSources").invoke(d);
    if((sources&0x2002)!=0x2002) continue;
    Object xr=d.getClass().getMethod("getMotionRange",int.class,int.class).invoke(d,0,0x2002);
    Object yr=d.getClass().getMethod("getMotionRange",int.class,int.class).invoke(d,1,0x2002);
    if(xr==null||yr==null) continue;
    float xmax=(Float)xr.getClass().getMethod("getMax").invoke(xr);
    float ymax=(Float)yr.getClass().getMethod("getMax").invoke(yr);
    float xmin=(Float)xr.getClass().getMethod("getMin").invoke(xr);
    float ymin=(Float)yr.getClass().getMethod("getMin").invoke(yr);
    if(xmax>0 && ymax>0 && Math.abs(xmin)<2 && Math.abs(ymin)<2) return d;
   }
   Thread.sleep(20);
  }
  throw new IllegalStateException("Absolute mouse registration timed out");
 }
 public void close() { try { release(); } catch(Exception e) { } if(mouse!=null) mouse.close(); mouse=null; }
}
