import java.lang.reflect.Method;
import java.io.ByteArrayOutputStream;
import java.io.PrintStream;
import android.view.TestState;
public class TestMain {
 static Class<?> cursor,view;
 static Object create() throws Exception{return cursor.getMethod("create").invoke(null);}
 static Object call(Object target,String name,Class<?>[] types,Object...args) throws Exception{return view.getMethod(name,types).invoke(target,args);}
 static boolean show(Object v,int x,int y)throws Exception{return (Boolean)call(v,"show",new Class<?>[]{int.class,int.class},x,y);}
 static void move(Object v,int x,int y)throws Exception{call(v,"move",new Class<?>[]{int.class,int.class},x,y);}
 static void hide(Object v)throws Exception{call(v,"hide",new Class<?>[0]);}
 static void close(Object v)throws Exception{call(v,"close",new Class<?>[0]);}
 static long count(String call){long n=0;for(String c:TestState.calls)if(c.equals(call))n++;return n;}
 static void check(boolean ok,String message){if(!ok)throw new AssertionError(message);System.out.println("PASS "+message);}
 public static void main(String[] args)throws Exception {
  try{cursor=Class.forName("DragCursor");view=Class.forName("DragCursor$View");}
  catch(ClassNotFoundException missing){throw new AssertionError("independent graphics-only drag cursor is missing",missing);}
  TestState.reset();Object v=create();move(v,1,2);hide(v);
  check(TestState.calls.isEmpty(),"creation and inactive move/hide acquire no Android resources");
  check(show(v,120,340),"first show initializes a noninteractive compositor cursor");
  check(count("parent:null")==1&&count("stack:0")==1&&count("hidden:true")==1&&count("opaque:false")==1,
   "root layer targets default display stack with alpha and starts hidden");
  check(count("canvas.post")==1&&count("canvas.arrow")==2&&count("hotspot:0.0,0.0")==1&&count("paint:-1:FILL:0.0")==1&&count("paint:-16777216:STROKE:2.0")==1,
   "arrow buffer is drawn once with black-white contrast and exact hotspot");
  move(v,150,360);hide(v);check(show(v,190,400),"later drags reuse the existing buffer");
  check(count("build")==1&&count("canvas.post")==1&&count("position:150.0,360.0")==1&&count("hide")==1,
   "move changes only position and hide preserves reusable resource");
  close(v);close(v);check(!show(v,5,6),"closed cursor never creates a layer again");
  check(count("remove")==1&&count("surface.release")==1&&count("layer.release")==1&&count("transaction.close")==1,
   "close removes layer and releases all graphics resources exactly once");
  for(String fail:new String[]{"surface","canvas.lock","canvas.arrow","trusted:true","show","apply"}){
   TestState.reset();TestState.fail=fail;v=create();
   ByteArrayOutputStream captured=new ByteArrayOutputStream();PrintStream original=System.out;
   try{System.setOut(new PrintStream(captured,true,"UTF-8"));checkUnavailable(v);}
   finally{System.setOut(original);}
   check(count("layer.release")==1,"partial "+fail+" failure releases acquired layer");
   check(count("surface.release")== (fail.equals("surface")?0:1)
      &&count("transaction.close")== (fail.equals("surface")?0:1),
      "partial failure releases every acquired native graphics reference");
   check(captured.toString("UTF-8").contains("DRAG_CURSOR unavailable")&&!captured.toString("UTF-8").contains("120"),
    "unavailable graphics report avoids input coordinates");
   close(v);
  }
  TestState.reset();v=create();show(v,1,2);TestState.fail="position:3.0,4.0";move(v,3,4);hide(v);close(v);
  check(count("surface.release")==1&&count("layer.release")==1,"move failure disables graphics while completing cleanup");
  TestState.reset();v=create();show(v,1,2);TestState.fail="surface.release";close(v);
  check(count("layer.release")==1&&count("transaction.close")==1,
   "Surface release failure cannot skip layer and transaction cleanup");
  TestState.fail=null;close(v);
  check(count("surface.release")==2&&count("layer.release")==1&&count("transaction.close")==1,
   "failed native release can retry without repeating successful cleanup");
  TestState.reset();v=create();show(v,1,2);TestState.fail="remove";close(v);
  check(count("surface.release")==1&&count("layer.release")==1&&count("transaction.close")==1,
   "remove failure cannot skip independent graphics resource releases");
  TestState.reset();TestState.systemIcon=true;v=create();
  check(show(v,120,340),"system icon supports compositor drag feedback");
  check(count("canvas.system-bitmap:60,60")==1 && count("canvas.arrow")==0 && count("size:60,60")==1,
   "drag copies the system cursor bitmap at native pixel dimensions");
  check(count("position:109.0,332.0")==1,"system cursor hotspot stays at the actual pointer position");
  move(v,150,360);check(count("position:139.0,352.0")==1,"drag movement preserves the native hotspot offset");
  hide(v);show(v,190,400);check(count("canvas.system-bitmap:60,60")==1,"later clicks reuse the loaded system icon");close(v);
  TestState.reset();TestState.systemIcon=true;TestState.transparentIcon=true;v=create();show(v,120,340);
  check(count("canvas.system-bitmap:60,60")==0 && count("canvas.arrow")==2 && count("size:40,48")==1,
   "transparent vendor system icon falls back to a visible cursor");close(v);
  TestState.reset();TestState.systemIcon=true;TestState.transparentIcon=true;TestState.nativeResource=true;
  v=create();show(v,120,340);
  check(count("canvas.system-bitmap:60,60")==1 && count("canvas.arrow")==0 && count("position:109.0,332.0")==1,
   "transparent vendor placeholder uses visible native arrow resource with the system hotspot");close(v);
 }
 static void checkUnavailable(Object v)throws Exception {
  if(show(v,120,340))throw new AssertionError("graphics failure must return false");
  if(show(v,120,340))throw new AssertionError("disabled cursor must stay unavailable");
  move(v,1,2);hide(v);
 }
}
