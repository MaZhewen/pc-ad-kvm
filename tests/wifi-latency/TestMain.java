import java.util.ArrayList;
import java.util.List;

public class TestMain {
    static final class Radio implements WifiLatencyGuard.Backend {
        int acquired, released;
        boolean held, failAcquire, failRelease, holdOnAcquire = true;
        public void acquire() throws Exception {
            acquired++; held = holdOnAcquire;
            if (failAcquire) throw new Exception("permission denied after partial acquire");
        }
        public boolean isHeld() { return held; }
        public void release() throws Exception {
            released++;
            if (failRelease) throw new Exception("release failed");
            held = false;
        }
    }
    static void check(boolean ok, String message) {
        if (!ok) throw new AssertionError(message);
        System.out.println("PASS " + message);
    }
    public static void main(String[] args) {
        String token = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";
        check(!Injector.wifiLatencyRequested(new String[]{"--session", token}), "legacy USB session arguments remain valid");
        check(Injector.wifiLatencyRequested(new String[]{"--session", token, "--wifi-low-latency"}), "wireless arguments enable the latency request");
        boolean rejected = false;
        try { Injector.wifiLatencyRequested(new String[]{"--session", token, "--unknown"}); }
        catch (IllegalArgumentException expected) { rejected = true; }
        check(rejected, "unknown session options are rejected");
        final List<String> reports = new ArrayList<String>();
        WifiLatencyGuard.Reporter log = new WifiLatencyGuard.Reporter() {
            public void report(String message) { reports.add(message); }
        };
        Radio radio = new Radio();
        WifiLatencyGuard guard = new WifiLatencyGuard(true, radio, log);
        guard.start(); guard.start();
        check(radio.acquired == 1 && guard.isRequested(), "wireless guard requests one low-latency lock");
        check(reports.get(0).contains("activation=unverified"), "holding a lock does not claim activated Wi-Fi mode");
        guard.close(); guard.close(); guard.start();
        check(radio.released == 1 && !radio.held && radio.acquired == 1,
            "session close releases exactly once and cannot reacquire");
        radio = new Radio(); radio.failAcquire = true;
        guard = new WifiLatencyGuard(true, radio, log);
        guard.start(); guard.close();
        check(!guard.isRequested() && radio.released == 1 && !radio.held,
            "acquisition failure stays nonfatal and cleans partial ownership");
        check(reports.get(reports.size() - 1).contains("unavailable"), "permission failure is reported as unavailable");
        radio = new Radio(); radio.holdOnAcquire = false;
        guard = new WifiLatencyGuard(true, radio, log);
        guard.start(); guard.close();
        check(!guard.isRequested() && radio.released == 1,
            "an unheld lock is unavailable rather than a successful latency request");
        radio = new Radio();
        guard = new WifiLatencyGuard(false, radio, log);
        guard.start(); guard.close();
        check(radio.acquired == 0 && radio.released == 0, "USB mode never changes Wi-Fi lock state");
        radio = new Radio();
        guard = new WifiLatencyGuard(true, radio, log);
        guard.start(); radio.failRelease = true; guard.close();
        radio.failRelease = false; guard.close();
        check(radio.released == 2 && !radio.held, "failed cleanup can retry without interrupting UHID cleanup");
    }
}
