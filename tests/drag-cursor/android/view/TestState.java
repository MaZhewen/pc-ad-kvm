package android.view;
import java.util.ArrayList;
import java.util.List;
public final class TestState {
 public static final List<String> calls=new ArrayList<String>();
 public static String fail;
 public static SurfaceControl layer;
 public static boolean systemIcon, transparentIcon, nativeResource;
 public static void reset(){calls.clear();fail=null;layer=null;systemIcon=false;transparentIcon=false;nativeResource=false;}
 public static void call(String value){calls.add(value);if(value.equals(fail))throw new IllegalStateException("boundary failure");}
}
