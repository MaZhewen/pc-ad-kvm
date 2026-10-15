import java.lang.reflect.Method;

/** Injects a real touchscreen stream for left-button gestures. */
public final class TouchPointer implements AbsoluteSession.TouchAction {
 interface Sink {
  long uptime();
  boolean inject(int action,long downTime,long eventTime,int x,int y);
 }
 private Sink sink;
 private boolean held;
 private long downTime,lastTime;
 private int x,y;
 TouchPointer() { }
 TouchPointer(Sink sink) { this.sink=sink; }
 private Sink sink() {
  if(sink!=null) return sink;
  try { sink=new AndroidSink(); return sink; }
  catch(Exception e) { System.out.println("Touch setup failed: "+e+" cause="+e.getCause()); return null; }
 }
 private long nextTime() { return lastTime=Math.max(sink.uptime(),lastTime+1); }
 public boolean down(int xx,int yy) {
  if(held) { cancel(); if(held) return false; }
  if(sink()==null) return false;
  x=xx; y=yy; downTime=nextTime();
  held=true;
  boolean ok=sink.inject(0,downTime,downTime,x,y);
  if(!ok) cancel(); // A timed-out DOWN may still be delivered later.
  return ok;
 }
 public boolean move(int xx,int yy) {
  if(!held) return false;
  if(xx==x && yy==y) return true;
  x=xx; y=yy;
  boolean ok=sink.inject(2,downTime,nextTime(),x,y);
  if(!ok) cancel();
  return ok;
 }
 public boolean up(int xx,int yy) {
  if(!held) return false;
  x=xx; y=yy;
  boolean ok=sink.inject(1,downTime,nextTime(),x,y);
  if(ok) held=false;
  else cancel();
  return ok;
 }
 public void cancel() {
  if(!held) return;
  if(sink.inject(3,downTime,nextTime(),x,y)
      || sink.inject(3,downTime,nextTime(),x,y)) held=false;
 }
 static final class AndroidSink implements Sink {
  final Method obtain,setSource,setDisplayId,recycle,inject,uptime;
  final Object manager;
  AndroidSink() throws Exception {
   Class<?> motion=Class.forName("android.view.MotionEvent");
   Class<?> event=Class.forName("android.view.InputEvent");
   Class<?> input=Class.forName("android.hardware.input.InputManagerGlobal");
   manager=input.getMethod("getInstance").invoke(null);
   if(manager==null) throw new IllegalStateException("InputManagerGlobal unavailable");
   inject=input.getMethod("injectInputEvent",event,int.class);
   obtain=motion.getMethod("obtain",long.class,long.class,int.class,float.class,float.class,int.class);
   setSource=motion.getMethod("setSource",int.class);
   setDisplayId=motion.getMethod("setDisplayId",int.class);
   recycle=motion.getMethod("recycle");
   uptime=Class.forName("android.os.SystemClock").getMethod("uptimeMillis");
  }
  public long uptime() {
   try { return (Long)uptime.invoke(null); }
   catch(Exception e) { throw new IllegalStateException(e); }
  }
  public boolean inject(int action,long downTime,long eventTime,int x,int y) {
   Object event=null;
   try {
    event=obtain.invoke(null,downTime,eventTime,action,(float)x,(float)y,0);
    setSource.invoke(event,0x1002); // SOURCE_TOUCHSCREEN
    setDisplayId.invoke(event,0); // default physical display
    boolean ok=(Boolean)inject.invoke(manager,event,1); // wait for dispatch result
    if(!ok) System.out.println("Touch event rejected: action="+action);
    return ok;
   } catch(Exception e) {
    System.out.println("Touch event failed: action="+action+" error="+e+" cause="+e.getCause());
    return false;
   } finally {
    if(event!=null) try { recycle.invoke(event); } catch(Exception ignored) { }
   }
  }
 }
}
