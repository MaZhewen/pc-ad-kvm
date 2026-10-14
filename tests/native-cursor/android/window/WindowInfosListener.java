package android.window;
import android.view.InputWindowHandle;
import android.util.Pair;
/** Test platform boundary: deliver a callback while registration returns cached data. */
public abstract class WindowInfosListener {
 public static final class DisplayInfo { }
 public static InputWindowHandle[] initial, callback;
 public static int registrations, unregistrations;
 public abstract void onWindowInfosChanged(InputWindowHandle[] windows, DisplayInfo[] displays);
 public Pair<InputWindowHandle[],DisplayInfo[]> register() {
  registrations++;
  if(callback!=null) onWindowInfosChanged(callback,new DisplayInfo[0]);
  Pair<InputWindowHandle[],DisplayInfo[]> result=new Pair<InputWindowHandle[],DisplayInfo[]>();
  result.first=initial; result.second=new DisplayInfo[0]; return result;
 }
 public void unregister() { unregistrations++; }
}
