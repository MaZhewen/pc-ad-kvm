package android.window;
import android.view.InputWindowHandle;
import android.util.Pair;
/** Compile-only signature; Android supplies this class at runtime. */
public abstract class WindowInfosListener {
 public static final class DisplayInfo { }
 public WindowInfosListener() { }
 public abstract void onWindowInfosChanged(InputWindowHandle[] windows, DisplayInfo[] displays);
 public Pair<InputWindowHandle[],DisplayInfo[]> register() { return null; }
 public void unregister() { }
}
