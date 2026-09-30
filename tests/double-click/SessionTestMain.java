import java.io.ByteArrayOutputStream;
import java.lang.reflect.Constructor;
public class SessionTestMain {
 static final class Touch implements AbsoluteSession.TouchAction {
  StringBuilder calls=new StringBuilder(); boolean result=true;
  public boolean down(int x,int y) { calls.append('D'); return result; }
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
  Touch touch=new Touch(); AbsoluteSession s=new AbsoluteSession(touch);
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
  button(s,1,1);
  byte[] move=new byte[12]; move[0]=7; move[8]=120; move[10]=(byte)210;
  s.handle(0x0b,move,new ByteArrayOutputStream());
  eq(touch.calls.toString(),"DUDUDCDM");
  button(s,2,1); eq(touch.calls.toString(),"DUDUDCDMC"); eq(s.buttons,2);
  button(s,1,0); eq(touch.calls.toString(),"DUDUDCDMC");
  button(s,2,0);
  touch.result=false; button(s,1,1); button(s,1,0); eq(s.buttons,0);
  System.out.println("PASS all left clicks use touch and session release cancels touch");
 }
}