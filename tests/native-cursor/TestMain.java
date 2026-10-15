import java.lang.reflect.*;
public class TestMain {
 static Class<?> backendClass,controlClass,cursorClass;
 static final class State implements InvocationHandler {
  final Object token=new Object();Object accepted=token;Object[] tokens={new Object(),token};
  int prepared,closed,iconCalls,lastDevice;boolean allowHide=true,allowRestore=true,exists=true,failPrepare,failIcon;
  public Object invoke(Object proxy,Method m,Object[] a)throws Throwable {
   if(m.getName().equals("prepare")){prepared++;if(failPrepare)throw new Exception("API unavailable");return null;}
   if(m.getName().equals("tokens"))return tokens;
   if(m.getName().equals("deviceExists"))return exists;
   if(m.getName().equals("close")){closed++;return null;}
   if(m.getName().equals("setIcon")){iconCalls++;lastDevice=(Integer)a[0];if(failIcon)throw new Exception("icon unavailable");return a[1]==accepted&&((Boolean)a[2]?allowHide:allowRestore);}
   return null;
  }
 }
 static Object create(State state)throws Exception {
  Object backend=Proxy.newProxyInstance(TestMain.class.getClassLoader(),new Class<?>[]{backendClass},state);
  Constructor<?> c=Class.forName("NativeCursor$Guard").getDeclaredConstructor(backendClass);c.setAccessible(true);return c.newInstance(backend);
 }
 static Object call(Object control,String method,Class<?>[]types,Object...args)throws Exception{return controlClass.getMethod(method,types).invoke(control,args);}
 static void prepare(Object c)throws Exception{call(c,"prepare",new Class<?>[0]);}
 static boolean hide(Object c,int id)throws Exception{return (Boolean)call(c,"hide",new Class<?>[]{int.class},id);}
 static void restore(Object c)throws Exception{call(c,"restore",new Class<?>[0]);}
 static void close(Object c)throws Exception{call(c,"close",new Class<?>[0]);}
 static void check(boolean ok,String message){if(!ok)throw new AssertionError(message);System.out.println("PASS "+message);}
 public static void main(String[]args)throws Exception {
  try{cursorClass=Class.forName("NativeCursor");backendClass=Class.forName("NativeCursor$Backend");controlClass=Class.forName("NativeCursor$Control");}
  catch(ClassNotFoundException missing){throw new AssertionError("owned native cursor hide/restore guard is missing",missing);}
  State s=new State();Object c=create(s);prepare(c);prepare(c);
  check(s.prepared==1,"prepare registers once without input-icon mutation");
  check(hide(c,41)&&s.lastDevice==41&&s.iconCalls==2,"hide succeeds only for accepted input token and owned device");
  int calls=s.iconCalls;s.allowHide=false;check(hide(c,41)&&s.iconCalls==calls,"repeated down retains already-hidden owned cursor without invalid token retry");
  restore(c);check(s.iconCalls==calls+1,"restore tries cached token first");
  calls=s.iconCalls;restore(c);restore(c);check(s.iconCalls==calls,"ordinary hover restore is a no-op after successful recovery");
  close(c);close(c);prepare(c);check(!hide(c,41)&&s.closed==1&&s.prepared==1,"closed guard cannot reacquire and unregisters once");
  s=new State();s.allowHide=false;c=create(s);check(!hide(c,41),"denied native hide returns false for overlay fallback");calls=s.iconCalls;restore(c);check(s.iconCalls==calls,"failed hide owns no icon to restore");close(c);
  s=new State();c=create(s);hide(c,41);s.allowRestore=false;restore(c);s.allowRestore=true;restore(c);
  calls=s.iconCalls;restore(c);check(s.iconCalls==calls,"temporary token mismatch retains restoration for a later explicit retry");close(c);
  s=new State();c=create(s);hide(c,41);s.accepted=new Object();s.tokens=new Object[]{s.accepted};restore(c);
  calls=s.iconCalls;restore(c);check(s.iconCalls==calls,"restore recovers through a new hovered input token");close(c);
  s=new State();c=create(s);hide(c,41);s.allowRestore=false;s.exists=false;restore(c);s.allowRestore=true;s.exists=true;
  check(hide(c,42)&&s.lastDevice==42,"removed old mouse clears pending state for a new geometry device");close(c);
  s=new State();s.failPrepare=true;c=create(s);check(!hide(c,41),"partial listener preparation failure is nonfatal");close(c);check(s.closed==1,"failed preparation cleans partial listener ownership");
  s=new State();c=create(s);hide(c,41);s.failIcon=true;restore(c);s.failIcon=false;restore(c);calls=s.iconCalls;restore(c);
  check(s.iconCalls==calls,"restore exceptions preserve ownership until a successful retry");close(c);
 }
}
