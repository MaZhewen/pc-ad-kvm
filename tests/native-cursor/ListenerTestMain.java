import android.window.WindowInfosListener;
import android.view.InputWindowHandle;
public class ListenerTestMain {
 static void check(boolean ok,String message) { if(!ok) throw new AssertionError(message); System.out.println("PASS "+message); }
 public static void main(String[] args) {
  WindowInfosListener.initial=new InputWindowHandle[]{new InputWindowHandle()};
  WindowInfosListener.callback=new InputWindowHandle[0];
  NativeWindowListener listener=new NativeWindowListener(); listener.start();
  check(listener.snapshot().length==0,"empty fresh callback wins over stale initial snapshot");
  InputWindowHandle[] latest={new InputWindowHandle(),new InputWindowHandle()};
  listener.onWindowInfosChanged(latest,new WindowInfosListener.DisplayInfo[0]);
  check(listener.snapshot()==latest,"later callback publishes current window snapshot");
  listener.close(); listener.close();
  listener.onWindowInfosChanged(latest,new WindowInfosListener.DisplayInfo[0]);
  check(listener.snapshot().length==0 && WindowInfosListener.unregistrations==1,"late callback cannot repopulate a closed listener");
  WindowInfosListener.callback=null;
  NativeWindowListener cached=new NativeWindowListener(); cached.start();
  check(cached.snapshot()==WindowInfosListener.initial,"cached initial snapshot is used when no callback arrived"); cached.close();
 }
}
