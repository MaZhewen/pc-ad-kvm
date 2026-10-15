import java.lang.reflect.Method;
import java.util.ArrayList;

/** Scoped native icon suppression; requires an actually hovered input-channel token. */
public final class NativeCursor {
    public interface Control {
        void prepare();
        boolean hide(int ownedDeviceId);
        void restore();
        void close();
    }
    interface Backend {
        void prepare() throws Exception;
        Object[] tokens() throws Exception;
        boolean setIcon(int deviceId, Object token, boolean hidden) throws Exception;
        boolean deviceExists(int deviceId) throws Exception;
        void close() throws Exception;
    }
    private NativeCursor() { }
    public static Control create() { return new Guard(new AndroidBackend()); }

    static final class Guard implements Control {
        final Backend backend;
        boolean prepared, unavailable, closed, cleanupNeeded;
        boolean acceptedReported, deniedReported, pendingReported;
        int hiddenDevice = -1;
        Object hiddenToken;
        Guard(Backend backend) { this.backend = backend; }
        public synchronized void prepare() {
            if (prepared || unavailable || closed) return;
            cleanupNeeded = true;
            try { backend.prepare(); prepared = true; }
            catch (Exception error) { unavailable = true; denied(error); cleanup(); }
            catch (LinkageError error) { unavailable = true; denied(error); cleanup(); }
        }
        public synchronized boolean hide(int ownedDeviceId) {
            if (closed || unavailable || ownedDeviceId < 0) return false;
            prepare();
            if (!prepared) return false;
            if (hiddenDevice == ownedDeviceId) return true;
            if (hiddenDevice >= 0) { restore(); if (hiddenDevice >= 0) return false; }
            try {
                for (Object token : backend.tokens()) {
                    if (backend.setIcon(ownedDeviceId, token, true)) {
                        hiddenDevice = ownedDeviceId; hiddenToken = token;
                        if (!acceptedReported) {
                            acceptedReported = true;
                            System.out.println("DRAG_CURSOR native-hide accepted");
                        }
                        return true;
                    }
                }
                denied(null);
            } catch (Exception error) { denied(error); }
              catch (LinkageError error) { denied(error); }
            return false;
        }
        public synchronized void restore() {
            if (hiddenDevice < 0) return;
            try {
                if (backend.setIcon(hiddenDevice, hiddenToken, false)) { restored(); return; }
                for (Object token : backend.tokens()) {
                    if (token != hiddenToken && backend.setIcon(hiddenDevice, token, false)) { restored(); return; }
                }
                // A removed geometry device no longer owns an icon needing restoration.
                if (!backend.deviceExists(hiddenDevice)) { restored(); return; }
                pending(null);
            } catch (Exception error) { pending(error); }
              catch (LinkageError error) { pending(error); }
        }
        void restored() { hiddenDevice = -1; hiddenToken = null; pendingReported = false; }
        public synchronized void close() { closed = true; restore(); cleanup(); }
        void cleanup() {
            if (!cleanupNeeded) return;
            try { backend.close(); cleanupNeeded = false; }
            catch (Exception error) { report("native-cleanup failed", error); }
            catch (LinkageError error) { report("native-cleanup failed", error); }
        }
        void denied(Throwable error) {
            if (deniedReported) return;
            deniedReported = true;
            report("native-hide unavailable", error);
        }
        void pending(Throwable error) {
            if (pendingReported) return;
            pendingReported = true;
            report("native-restore pending", error);
        }
        void report(String state, Throwable error) {
            if (error != null) while (error.getCause() != null && error.getCause() != error) error = error.getCause();
            System.out.println("DRAG_CURSOR " + state + ": "
                + (error == null ? "no hovered input token" : error.getClass().getSimpleName()));
        }
    }

    static final class AndroidBackend implements Backend {
        Object manager, nullIcon, defaultIcon, inputService;
        Method setPointerIcon, getInputDevice;
        NativeWindowListener listener;
        public void prepare() throws Exception {
            Class<?> looper = Class.forName("android.os.Looper");
            if (looper.getMethod("getMainLooper").invoke(null) == null)
                looper.getMethod("prepareMainLooper").invoke(null);
            Class<?> threadClass = Class.forName("android.app.ActivityThread");
            Object thread = threadClass.getMethod("currentActivityThread").invoke(null);
            if (thread == null) thread = threadClass.getMethod("systemMain").invoke(null);
            Object context = threadClass.getMethod("getSystemContext").invoke(thread);
            Class<?> global = Class.forName("android.hardware.input.InputManagerGlobal");
            Class<?> icon = Class.forName("android.view.PointerIcon");
            Class<?> binder = Class.forName("android.os.IBinder");
            manager = global.getMethod("getInstance").invoke(null);
            setPointerIcon = global.getMethod("setPointerIcon", icon, int.class, int.class, int.class, binder);
            Method systemIcon = icon.getMethod("getSystemIcon", Class.forName("android.content.Context"), int.class);
            nullIcon = systemIcon.invoke(null, context, icon.getField("TYPE_NULL").getInt(null));
            defaultIcon = systemIcon.invoke(null, context, icon.getField("TYPE_DEFAULT").getInt(null));
            Object inputBinder = Class.forName("android.os.ServiceManager")
                .getMethod("getService", String.class).invoke(null, "input");
            inputService = Class.forName("android.hardware.input.IInputManager$Stub")
                .getMethod("asInterface", binder).invoke(null, inputBinder);
            getInputDevice = Class.forName("android.hardware.input.IInputManager")
                .getMethod("getInputDevice", int.class);
            listener = new NativeWindowListener(); listener.start();
        }
        public Object[] tokens() throws Exception {
            ArrayList<Object> tokens = new ArrayList<Object>();
            for (Object handle : listener.snapshot()) {
                if (handle.getClass().getField("displayId").getInt(handle) != 0) continue;
                Object token = handle.getClass().getField("token").get(handle);
                if (token != null) tokens.add(token);
            }
            return tokens.toArray();
        }
        public boolean setIcon(int deviceId, Object token, boolean hidden) throws Exception {
            return Boolean.TRUE.equals(setPointerIcon.invoke(manager, hidden ? nullIcon : defaultIcon,
                0, deviceId, 0, token));
        }
        public boolean deviceExists(int deviceId) throws Exception {
            // Query the service directly; a shell process need not run a main Looper for cache updates.
            return getInputDevice.invoke(inputService, deviceId) != null;
        }
        public void close() throws Exception { if (listener != null) { listener.close(); listener = null; } }
    }
}
