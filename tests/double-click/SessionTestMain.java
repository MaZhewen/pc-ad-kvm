import java.io.ByteArrayOutputStream;
import java.lang.reflect.Constructor;
public class SessionTestMain {
 static final class NativeIcon implements NativeCursor.Control {
  boolean result=true,hidden; int device=-1,restored,closed;
  public void prepare() { }
  public boolean hide(int id) { device=id; hidden=result; return result; }
  public void restore() { hidden=false; restored++; }
  public void close() { hidden=false; closed++; }
 }
 static final class Cursor implements DragCursor.View {
  int x,y,moved,closed; boolean visible,available=true,failMove;
  public boolean show(int xx,int yy) { x=xx; y=yy; visible=available; return available; }
  public boolean move(int xx,int yy) { x=xx; y=yy; moved++; if(failMove) visible=false; return visible; }
  public void hide() { visible=false; }
  public void close() { visible=false; closed++; }
 }
 static final class Touch implements AbsoluteSession.TouchAction {
  StringBuilder calls=new StringBuilder(); boolean result=true; NativeIcon icon;
  public boolean down(int x,int y) {
   if(icon!=null && icon.result && !icon.hidden) throw new AssertionError("touch began before native cursor was hidden");
   calls.append('D'); return result;
  }
  public boolean move(int x,int y) { calls.append('M'); return result; }
  public boolean up(int x,int y) { calls.append('U'); return result; }
  public void cancel() { calls.append('C'); }
 }
 static void eq(int a,int b) { if(a!=b) throw new AssertionError(a+" != "+b); }
 static void eq(String a,String b) { if(!a.equals(b)) throw new AssertionError(a+" != "+b); }
 static void button(AbsoluteSession s,int id,int down) throws Exception {
  s.handle(3,new byte[]{(byte)id,(byte)down},new ByteArrayOutputStream());
 }
 public static void main(String[] args) throws Exception {
  Touch touch=new Touch(); Cursor cursor=new Cursor(); NativeIcon icon=new NativeIcon();
  touch.icon=icon;
  AbsoluteSession s=new AbsoluteSession(touch,cursor,icon); s.mouseDeviceId=41;
  s.active=true; s.x=100; s.y=200;
  button(s,1,1); eq(s.buttons,0); button(s,1,0); eq(s.buttons,0);
  button(s,1,1); eq(s.buttons,0); button(s,1,0); eq(touch.calls.toString(),"DUDU"); eq(s.buttons,0);
  button(s,2,1); eq(s.buttons,2); button(s,2,0); eq(s.buttons,0);
  button(s,1,1); s.release(); eq(touch.calls.toString(),"DUDUDC");
  s.active=true; button(s,1,0); eq(touch.calls.toString(),"DUDUDC");
  // A held left button follows pointer updates as touch MOVE, then cleans up on right click.
  Constructor<AbsoluteTransform> ctor=AbsoluteTransform.class.getDeclaredConstructor(double[].class,double[].class);
  ctor.setAccessible(true);
  double[] identity={1,0,0,0,1,0};
  s.transform=ctor.newInstance(identity,identity); s.epoch=7;
  s.mouse=new UhidDevice("test",new int[0]);
  int beforePress=s.mouse.reports;
  button(s,1,1);
  eq(s.mouse.reports,beforePress);
  if(!icon.hidden || icon.device!=41) throw new AssertionError("owned native icon was not hidden before touch");
  System.out.println("PASS touch down does not send a mouse leave report in place of hiding its icon");
  byte[] move=new byte[12]; move[0]=7; move[8]=120; move[10]=(byte)210;
  int beforeDrag=s.mouse.reports;
  s.handle(0x0b,move,new ByteArrayOutputStream());
  eq(touch.calls.toString(),"DUDUDCDM");
  eq(s.mouse.reports,beforeDrag);
  if(!cursor.visible) throw new AssertionError("drag feedback was not shown");
  eq(cursor.x,120); eq(cursor.y,210); eq(cursor.moved,1);
  System.out.println("PASS touch drag never emits mouse hover that can cancel the gesture");
  button(s,2,1); eq(touch.calls.toString(),"DUDUDCDMC"); eq(s.buttons,2);
  if(icon.hidden) throw new AssertionError("cancel failed to restore native icon");
  if(cursor.visible) throw new AssertionError("cancel kept drag feedback visible");
  button(s,1,0); eq(touch.calls.toString(),"DUDUDCDMC");
  button(s,2,0);
  touch.result=false; button(s,1,1); button(s,1,0); eq(s.buttons,0);
  s.close(); eq(cursor.closed,1); eq(icon.closed,1);
  Touch mixedTouch=new Touch(); Cursor mixedCursor=new Cursor();
  AbsoluteSession mixed=new AbsoluteSession(mixedTouch,mixedCursor,new NativeIcon()); mixed.mouseDeviceId=42;
  mixed.active=true; mixed.x=100; mixed.y=200; mixed.transform=ctor.newInstance(identity,identity); mixed.epoch=7;
  mixed.mouse=new UhidDevice("mixed",new int[0]);
  button(mixed,2,1); button(mixed,1,1); button(mixed,2,0);
  eq(mixedTouch.calls.toString(),"DC");
  if(mixed.leftDown || mixedCursor.visible) throw new AssertionError("other-button release left touch feedback active");
  eq(mixed.buttons,0); mixed.close();
  NativeIcon deniedIcon=new NativeIcon(); deniedIcon.result=false;
  Cursor fallbackCursor=new Cursor(); Touch fallbackTouch=new Touch();
  AbsoluteSession fallback=new AbsoluteSession(fallbackTouch,fallbackCursor,deniedIcon);
  fallback.active=true; fallback.mouseDeviceId=43;
  button(fallback,1,1);
  if(fallbackCursor.visible) throw new AssertionError("failed native hide showed a second cursor");
  eq(fallbackTouch.calls.toString(),"D");
  button(fallback,1,0); eq(fallbackTouch.calls.toString(),"DU"); fallback.close();
  System.out.println("PASS failed native hide retains touch with no duplicate cursor");
  Cursor unavailableCursor=new Cursor(); unavailableCursor.available=false;
  NativeIcon recoverIcon=new NativeIcon(); Touch recoverTouch=new Touch();
  AbsoluteSession recovery=new AbsoluteSession(recoverTouch,unavailableCursor,recoverIcon);
  recovery.active=true; recovery.mouseDeviceId=44; button(recovery,1,1);
  if(recoverIcon.hidden) throw new AssertionError("layer failure kept native cursor hidden");
  eq(recoverTouch.calls.toString(),"D"); recovery.release(); recovery.close();
  System.out.println("PASS layer failure restores native icon and release cleans up touch");
  NativeIcon upIcon=new NativeIcon(); Cursor upCursor=new Cursor(); Touch upTouch=new Touch();
  AbsoluteSession released=new AbsoluteSession(upTouch,upCursor,upIcon);
  released.active=true; released.mouseDeviceId=45; released.x=100; released.y=200;
  released.transform=ctor.newInstance(identity,identity); released.epoch=7;
  released.mouse=new UhidDevice("up",new int[0]);
  button(released,1,1); released.handle(0x0b,move,new ByteArrayOutputStream());
  button(released,1,0); eq(upTouch.calls.toString(),"DMU");
  if(upCursor.visible || upIcon.hidden) throw new AssertionError("up did not restore one native cursor");
  eq(Injector.u16(released.mouse.lastReport,2),120); eq(Injector.u16(released.mouse.lastReport,4),210);
  released.close();
  System.out.println("PASS drag up restores native mouse at newest coordinates");
  NativeIcon failedMoveIcon=new NativeIcon(); Cursor failedMoveCursor=new Cursor(); Touch failedMoveTouch=new Touch();
  AbsoluteSession failedMove=new AbsoluteSession(failedMoveTouch,failedMoveCursor,failedMoveIcon);
  failedMove.active=true; failedMove.mouseDeviceId=46; failedMove.x=100; failedMove.y=200;
  failedMove.transform=ctor.newInstance(identity,identity); failedMove.epoch=7;
  failedMove.mouse=new UhidDevice("failed move",new int[0]);
  button(failedMove,1,1); failedMoveCursor.failMove=true;
  failedMove.handle(0x0b,move,new ByteArrayOutputStream());
  if(failedMoveIcon.hidden) throw new AssertionError("failed compositor movement kept the native cursor hidden");
  eq(failedMoveTouch.calls.toString(),"DM"); eq(failedMove.mouse.reports,0);
  button(failedMove,1,0); eq(failedMoveTouch.calls.toString(),"DMU"); failedMove.close();
  System.out.println("PASS compositor movement failure restores native icon without interrupting touch or injecting hover");
  System.out.println("PASS other-button release cancels touch before restoring native mouse");
  System.out.println("PASS all left clicks use touch and session release cancels touch");
 }
}
