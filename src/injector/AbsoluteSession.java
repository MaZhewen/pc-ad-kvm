import java.io.OutputStream;
/** Versioned mouse session; socket thread owns all mutable state. */
public final class AbsoluteSession {
 interface TouchAction { boolean down(int x,int y); boolean move(int x,int y); boolean up(int x,int y); void cancel(); }
 UhidDevice mouse;
 int epoch,width,height,rotation,x,y,buttons;
 boolean active;
 Object inputDevice;
 int mouseDeviceId=-1;
 boolean leftDown;
 final TouchAction touchAction;
 final DragCursor.View dragCursor;
 final NativeCursor.Control nativeCursor;
 AbsoluteTransform transform;
 long nextCaptureAt;
 final Class<?> input;
 public AbsoluteSession() throws Exception {
  this(new TouchPointer());
 }
 AbsoluteSession(TouchAction touchAction) throws Exception {
  this(touchAction,DragCursor.create());
 }
 AbsoluteSession(TouchAction touchAction,DragCursor.View dragCursor) throws Exception {
  this(touchAction,dragCursor,NativeCursor.create());
 }
 AbsoluteSession(TouchAction touchAction,DragCursor.View dragCursor,NativeCursor.Control nativeCursor) throws Exception {
  this.touchAction=touchAction;
  this.dragCursor=dragCursor;
  this.nativeCursor=nativeCursor;
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
   mouseDeviceId=(Integer)input.getMethod("getId").invoke(inputDevice);
   nativeCursor.prepare();
   // Android creates PointerChoreographer's viewport only after the first
   // in-range report. Leave the device out of range until takeover; this
   // is registration setup, not the native icon hiding used for touch drag.
   mouse.sendReport(AbsolutePointer.rawReport(0,0,0,0,true));
   mouse.sendReport(AbsolutePointer.rawReport(0,0,0,0,false));
   nextCaptureAt=0;
   tryReady(out);
   return true;
  }
  if(type==0x08) { if(mouse!=null && transform==null) tryReady(out); return false; }
  if(type==0x0b) {
   if(epoch==0||transform==null||Injector.i32(p,0)!=epoch) return true;
   int nx=Injector.u16(p,8), ny=Injector.u16(p,10);
   boolean moved=nx!=x || ny!=y;
   x=nx; y=ny;
   if(active && leftDown && moved) touchAction.move(x,y);
   active=true;
   if(leftDown) { if(!dragCursor.move(x,y)) nativeCursor.restore(); }
   else { send(0); nativeCursor.restore(); }
   // Acknowledge position processing, not display presentation. PC also requires
   // sustained outward movement, and never trusts an acknowledgement from an old epoch.
   byte[] ack=new byte[13]; ack[0]=0x0e; System.arraycopy(p,0,ack,1,12);
   out.write(ack); out.flush(); return true;
  }
  if(type==0x03) {
   if(active) {
    int btn=p[0]&255, bit=Injector.btnToBit(btn);
    boolean down=p[1]!=0;
    if(btn==1) {
     if(down) {
      if(leftDown) touchAction.cancel();
      leftDown=true;
      // InRange=false does not hide Android's mouse sprite. Hide its icon
      // without injecting hover, and never add another visible cursor if that fails.
      dragCursor.hide();
      if(nativeCursor.hide(mouseDeviceId) && !dragCursor.show(x,y)) nativeCursor.restore();
      if(!touchAction.down(x,y)) {
       System.out.println("Touch down failed; left click suppressed");
       leftDown=false; dragCursor.hide(); send(0); nativeCursor.restore();
      }
     } else if(leftDown) {
      if(!touchAction.up(x,y)) System.out.println("Touch up failed; left click suppressed");
      leftDown=false;
      dragCursor.hide();
      send(0);
      nativeCursor.restore();
     }
     return true;
    }
    if(leftDown) { touchAction.cancel(); leftDown=false; dragCursor.hide(); }
    if(down) buttons|=bit; else buttons&=~bit;
    send(0);
    nativeCursor.restore();
   }
   return true;
  }
  if(type==0x04) { if(leftDown) { touchAction.cancel(); leftDown=false; dragCursor.hide(); } if(active) { send(Injector.clamp127(Injector.i16(p,2))); nativeCursor.restore(); } return true; }
  if(type==0x06) { release(); return false; } // keyboard release remains in Injector
  if(type==0x01||type==0x02||type==0x0a) return true; // legacy relative path is never used
  return false;
 }
 void send(int wheel) throws Exception {
  sendVisible(wheel,active);
 }
 void sendVisible(int wheel,boolean visible) throws Exception {
  if(mouse==null || transform==null) return;
  int[] raw=transform.raw(x,y);
  mouse.sendReport(AbsolutePointer.rawReport(buttons,raw[0],raw[1],wheel,visible));
 }
 void release() throws Exception { if(leftDown) touchAction.cancel(); leftDown=false; dragCursor.hide(); nativeCursor.restore(); buttons=0; active=false; if(width>0) send(0); }
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
 public void close() {
  try { release(); } catch(Exception e) { }
  try { dragCursor.close(); } catch(Exception ignored) { }
  try { nativeCursor.close(); } catch(Exception ignored) { }
  if(mouse!=null) mouse.close(); mouse=null;
 }
}
