import android.window.WindowInfosListener;
import android.view.InputWindowHandle;
import android.util.Pair;

/** Raw input-channel tokens; no input window or input channel is created. */
public final class NativeWindowListener extends WindowInfosListener {
    private volatile Object[] windows = new Object[0];
    private boolean registered, receivedCallback, closed;
    public synchronized void onWindowInfosChanged(InputWindowHandle[] handles, DisplayInfo[] displays) {
        if (closed) return;
        receivedCallback = true;
        windows = handles == null ? new Object[0] : handles;
    }
    public void start() {
        synchronized (this) {
            if (registered || closed) return;
            registered = true;
        }
        // Do not hold the publication lock across native registration callbacks.
        Pair<InputWindowHandle[], DisplayInfo[]> initial = register();
        synchronized (this) {
            if (!closed && !receivedCallback && initial != null && initial.first != null)
                windows = initial.first;
        }
    }
    public Object[] snapshot() { return windows; }
    public void close() {
        synchronized (this) {
            closed = true;
            windows = new Object[0];
            if (!registered) return;
        }
        unregister();
        synchronized (this) { registered = false; }
    }
}
