/** Session-scoped, best-effort Wi-Fi latency request. */
public final class WifiLatencyGuard implements AutoCloseable {
    interface Backend {
        void acquire() throws Exception;
        boolean isHeld() throws Exception;
        void release() throws Exception;
    }
    interface Reporter { void report(String message); }
    final boolean enabled;
    final Backend backend;
    final Reporter reporter;
    boolean started, closed, requested, needsRelease;

    WifiLatencyGuard(boolean enabled, Backend backend, Reporter reporter) {
        this.enabled = enabled; this.backend = backend; this.reporter = reporter;
    }
    public static WifiLatencyGuard forAndroid(boolean enabled) {
        return new WifiLatencyGuard(enabled, new AndroidBackend(), new Reporter() {
            public void report(String message) { System.out.println(message); }
        });
    }
    public synchronized void start() {
        if (started || closed || !enabled) return;
        started = true;
        try {
            needsRelease = true;
            backend.acquire();
            requested = backend.isHeld();
            if (!requested) throw new IllegalStateException("Wi-Fi lock not held");
            report("WIFI_LATENCY requested held=true activation=unverified (foreground, screen, hardware dependent)");
        } catch (Exception error) {
            requested = false;
            releaseQuietly();
            report("WIFI_LATENCY unavailable: " + cause(error));
        } catch (LinkageError error) {
            requested = false;
            releaseQuietly();
            report("WIFI_LATENCY unavailable: " + error);
        }
    }
    public synchronized boolean isRequested() { return requested; }
    public synchronized void close() {
        closed = true;
        releaseQuietly();
        requested = false;
    }
    void releaseQuietly() {
        if (!needsRelease) return;
        try { backend.release(); needsRelease = false; }
        catch (Exception error) { report("WIFI_LATENCY cleanup-failed: " + cause(error)); }
        catch (LinkageError error) { report("WIFI_LATENCY cleanup-failed: " + error); }
    }
    void report(String message) {
        if (reporter != null) try { reporter.report(message); } catch (RuntimeException ignored) { }
    }
    static Throwable cause(Throwable error) {
        while (error.getCause() != null && error.getCause() != error) error = error.getCause();
        return error;
    }

    /** Public WifiLock API reached by reflection, like the existing input adapters. */
    static final class AndroidBackend implements Backend {
        Object lock;
        Class<?> lockClass;
        public void acquire() throws Exception {
            Class<?> looper = Class.forName("android.os.Looper");
            if (looper.getMethod("getMainLooper").invoke(null) == null)
                looper.getMethod("prepareMainLooper").invoke(null);
            Class<?> activityThread = Class.forName("android.app.ActivityThread");
            Object thread = activityThread.getMethod("currentActivityThread").invoke(null);
            if (thread == null) thread = activityThread.getMethod("systemMain").invoke(null);
            Object systemContext = activityThread.getMethod("getSystemContext").invoke(thread);
            Class<?> contextClass = Class.forName("android.content.Context");
            Object shellContext = contextClass.getMethod("createPackageContext", String.class, int.class)
                .invoke(systemContext, "com.android.shell", 0);
            Object wifi = contextClass.getMethod("getSystemService", String.class).invoke(shellContext, "wifi");
            if (wifi == null) throw new IllegalStateException("Wi-Fi service unavailable");
            Class<?> wifiClass = Class.forName("android.net.wifi.WifiManager");
            int mode = wifiClass.getField("WIFI_MODE_FULL_LOW_LATENCY").getInt(null);
            lock = wifiClass.getMethod("createWifiLock", int.class, String.class)
                .invoke(wifi, mode, "PC-KVM wireless input");
            lockClass = Class.forName("android.net.wifi.WifiManager$WifiLock");
            lockClass.getMethod("setReferenceCounted", boolean.class).invoke(lock, false);
            lockClass.getMethod("acquire").invoke(lock);
        }
        public boolean isHeld() throws Exception {
            return lock != null && (Boolean)lockClass.getMethod("isHeld").invoke(lock);
        }
        public void release() throws Exception {
            if (isHeld()) lockClass.getMethod("release").invoke(lock);
            lock = null;
        }
    }
}
