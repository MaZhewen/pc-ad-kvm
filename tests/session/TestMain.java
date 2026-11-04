import java.io.InputStream;
import java.io.OutputStream;
import java.io.ByteArrayOutputStream;
import java.io.DataInputStream;
import java.io.PrintStream;
import java.net.ServerSocket;
import java.net.Socket;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;
import java.util.concurrent.atomic.AtomicLong;

public class TestMain {
    static void check(boolean ok, String message) { if (!ok) throw new AssertionError(message); System.out.println("PASS " + message); }
    static SessionLink.Frame pointer(int epoch, int sequence) {
        byte[] payload = new byte[12];
        for (int i = 0; i < 4; i++) {
            payload[i] = (byte)(epoch >>> (i * 8));
            payload[i + 4] = (byte)(sequence >>> (i * 8));
        }
        payload[8] = (byte)sequence;
        return new SessionLink.Frame(0x0b, payload);
    }
    static SessionLink.Frame event(int type, int value) {
        byte[] payload = new byte[SessionLink.payloadLength(type)];
        for (int i = 0; i < Math.min(4, payload.length); i++) payload[i] = (byte)(value >>> (i * 8));
        if (type == 3) { payload[0] = 1; payload[1] = (byte)value; }
        return new SessionLink.Frame(type, payload);
    }
    static int integer(byte[] payload, int offset) {
        return (payload[offset] & 255) | ((payload[offset + 1] & 255) << 8)
            | ((payload[offset + 2] & 255) << 16) | (payload[offset + 3] << 24);
    }
    static void queueTests(SessionLink link) throws Exception {
        SessionLink.Frame p1 = pointer(1, 1), p2 = pointer(1, 2), p3 = pointer(1, 3);
        SessionLink.Frame down = event(3, 1), up = event(3, 0);
        link.frames.offer(p1); link.frames.offer(p2); link.frames.offer(down);
        link.frames.offer(p3); link.frames.offer(up);
        check(link.poll() == p2, "adjacent same-epoch pointers retain newest pre-click coordinates");
        check(link.poll() == down && link.poll() == p3 && link.poll() == up,
              "down drag pointer and up retain FIFO order");
        SessionLink.Frame epoch2 = pointer(0x101, 4);
        link.frames.offer(p1); link.frames.offer(epoch2);
        check(link.poll() == p1 && link.poll() == epoch2, "different full-width epochs never replace");
        for (int type : new int[] {1, 2, 3, 4, 5, 6, 7, 9, 10, 12}) {
            SessionLink.Frame barrier = event(type, 0);
            link.frames.offer(p1); link.frames.offer(barrier); link.frames.offer(p2);
            check(link.poll() == p1 && link.poll() == barrier && link.poll() == p2,
                  "frame type " + type + " preserves discrete barrier including releases");
        }
        for (int i = 0; i < 127; i++) if (!link.frames.offer(event(5, i))) throw new AssertionError("reliable fill " + i);
        check(link.frames.offer(p1) && link.frames.offer(p2), "capacity-full adjacent pointer can replace");
        check(!link.frames.offer(up), "capacity-full reliable event rejects");
        for (int i = 0; i < 127; i++) if (link.poll().payload[0] != (byte)i) throw new AssertionError("reliable order " + i);
        check(link.poll() == p2, "full queue preserves newest tail pointer");
        check(link.frames.poll(0, TimeUnit.MILLISECONDS) == null, "empty zero-timeout poll returns immediately");
        final AtomicReference<SessionLink.Frame> awakened = new AtomicReference<SessionLink.Frame>();
        Thread waiting = new Thread(new Runnable() { public void run() {
            try { awakened.set(link.frames.poll(5, TimeUnit.SECONDS)); }
            catch (InterruptedException e) { throw new RuntimeException(e); }
        }});
        waiting.start();
        link.frames.offer(down);
        waiting.join(1000);
        check(!waiting.isAlive() && awakened.get() == down, "enqueue wakes timed consumer immediately");
        final AtomicReference<Throwable> failure = new AtomicReference<Throwable>();
        Thread producer = new Thread(new Runnable() { public void run() {
            try {
                for (int i = 0; i < 5000; i++) {
                    while (!link.frames.offer(pointer(1, 2 * i))) Thread.yield();
                    while (!link.frames.offer(pointer(1, 2 * i + 1))) Thread.yield();
                    while (!link.frames.offer(event(5, i))) Thread.yield();
                }
            } catch (Throwable e) { failure.set(e); }
        }});
        Thread consumer = new Thread(new Runnable() { public void run() {
            try {
                int reliable = 0, lastPointer = -1;
                while (reliable < 5000) {
                    SessionLink.Frame f = link.frames.poll(1, TimeUnit.SECONDS);
                    if (f == null) throw new AssertionError("stress input timed out");
                    if (f.type == 0x0b) lastPointer = integer(f.payload, 4);
                    if (f.type == 5) {
                        if (integer(f.payload, 0) != reliable) throw new AssertionError("discrete order lost");
                        if (lastPointer != 2 * reliable + 1) throw new AssertionError("pre-event pointer lost");
                        reliable++;
                    }
                }
            } catch (Throwable e) { failure.set(e); }
        }});
        producer.setDaemon(true); consumer.setDaemon(true);
        producer.start(); consumer.start(); producer.join(10000); consumer.join(10000);
        check(!producer.isAlive() && !consumer.isAlive() && failure.get() == null,
              "concurrent 10000 pointers and 5000 reliable frames terminate in order");
    }
    static void timingTests() throws Exception {
        SessionLink.InputTimings timing = new SessionLink.InputTimings(100L);
        SessionLink.Frame first = new SessionLink.Frame(5, new byte[] {99, 88, 77, 66}, 100L);
        SessionLink.Frame second = new SessionLink.Frame(0x0b, new byte[12], 1000100L);
        timing.begin(first, 2000100L); timing.complete(first, 5000100L);
        timing.begin(second, 5000100L); timing.complete(second, 10000100L);
        check(timing.summary(4999999999L, false) == null, "timing summaries wait five seconds");
        String result = timing.summary(5000000100L, false);
        check(result.contains("queued=2 processed=2") && result.contains("queue_avg_ms=3.000")
                && result.contains("queue_max_ms=4.000") && result.contains("process_avg_ms=4.000")
                && result.contains("process_max_ms=5.000"), "timing averages and maxima use receipt and processing timestamps");
        check(!result.contains("99") && !result.contains("88") && !result.contains("payload")
                && !result.contains("key") && !result.contains("coord"), "timing summary contains no input content");
        check(timing.summary(10000000100L, false) == null, "empty timing windows emit nothing");
        timing.begin(first, 12000100L); timing.complete(first, 14000100L);
        check(timing.summary(14000100L, true) != null && timing.summary(14000100L, true) == null,
              "final summary flushes once and resets aggregate counts");
        try (ServerSocket listener = new ServerSocket(0);
             Socket client = new Socket("127.0.0.1", listener.getLocalPort());
             Socket remote = listener.accept()) {
            SessionLink link = new SessionLink(client);
            ByteArrayOutputStream log = new ByteArrayOutputStream();
            PrintStream original = System.out;
            try {
                System.setOut(new PrintStream(log, true, "UTF-8"));
                link.beginProcessing(first); link.completeProcessing(first); link.close(); link.close();
            } finally { System.setOut(original); }
            String output = log.toString("UTF-8");
            check(output.contains("queued=1 processed=1")
                    && output.indexOf("INJECTOR") == output.lastIndexOf("INJECTOR"),
                  "session close flushes final processing summary exactly once");
        }
    }
    static void writeFrame(OutputStream output, SessionLink.Frame frame) throws Exception {
        output.write(frame.type); output.write(frame.payload);
    }
    static void directHeartbeat() throws Exception {
        try (ServerSocket listener = new ServerSocket(0);
             Socket client = new Socket("127.0.0.1", listener.getLocalPort());
             Socket peer = listener.accept()) {
            AtomicLong elapsed = new AtomicLong(100);
            SessionLink link = new SessionLink(client, elapsed::get);
            link.start();
            peer.setSoTimeout(2000);
            OutputStream output = peer.getOutputStream();
            DataInputStream input = new DataInputStream(peer.getInputStream());
            check(!link.directHeartbeatValid(), "direct session requires a real PING before readiness");
            output.write(new byte[] {8, 2, 0, 0, 0}); output.flush();
            byte[] pong = new byte[5]; input.readFully(pong);
            check(pong[0] == 9 && link.directHeartbeatValid(), "direct PING refreshes liveness and sends PONG");
            output.write(new byte[] {0x12, 4, 3, 2, 1}); output.flush();
            SessionLink.Frame ready = link.poll();
            check(ready != null && ready.type == 0x12 && integer(ready.payload, 0) == 0x01020304,
                "direct SESSION_READY carries the full little-endian epoch");
            link.frames.offer(event(5, 11));
            elapsed.set(2100);
            check(link.poll() == null && !link.isAlive(), "elapsed two-second gap discards queued input before processing");
            check(input.read() == -1, "expired direct session closes independently of TCP EOF");
        }
        try (ServerSocket listener = new ServerSocket(0);
             Socket client = new Socket("127.0.0.1", listener.getLocalPort());
             Socket peer = listener.accept()) {
            SessionLink link = new SessionLink(client, () -> 100);
            link.start();
            peer.setSoTimeout(2000);
            peer.getOutputStream().write(new byte[] {9, 2, 0, 0, 0});
            check(peer.getInputStream().read() == -1, "device rejects PONG in the PC-to-Android direction");
        }
    }
    public static void main(String[] args) throws Exception {
        timingTests();
        directHeartbeat();
        byte[] token = SessionLink.parseToken("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f");
        check(token.length == 32 && token[0] == 0 && token[31] == 31, "token preserves leading zero");
        boolean rejected = false;
        try { SessionLink.parseToken("bad"); } catch (IllegalArgumentException e) { rejected = true; }
        check(rejected, "invalid token rejected");
        ServerSocket listener = new ServerSocket(0);
        final Socket[] server = new Socket[1];
        Thread accept = new Thread(new Runnable() { public void run() {
            try { server[0] = listener.accept(); } catch (Exception e) { throw new RuntimeException(e); }
        }});
        accept.start();
        Socket client = new Socket("127.0.0.1", listener.getLocalPort());
        accept.join();
        InputStream remoteIn = server[0].getInputStream();
        OutputStream remoteOut = server[0].getOutputStream();
        Thread handshake = new Thread(new Runnable() { public void run() {
            try { SessionLink.handshake(client, token); } catch (Exception e) { throw new RuntimeException(e); }
        }});
        handshake.start();
        byte[] preamble = new byte[37];
        int got = 0;
        while (got < 37) got += remoteIn.read(preamble, got, 37 - got);
        check(preamble[0] == 'P' && preamble[4] == 1 && preamble[5] == 0 && preamble[36] == 31, "versioned preamble matches token");
        remoteOut.write(1); remoteOut.flush();
        handshake.join();
        SessionLink link = new SessionLink(client);
        queueTests(link);
        link.start();
        remoteOut.write(new byte[] {8, 7, 0, 0, 0}); remoteOut.flush();
        server[0].setSoTimeout(2000);
        byte[] pong = new byte[5]; got = 0;
        while (got < 5) got += remoteIn.read(pong, got, 5 - got);
        check(pong[0] == 9 && pong[1] == 7, "initialization answers PING");
        // PONG acts as a reader progress barrier, independent of a stalled input consumer.
        for (int i = 0; i < 1000; i++) writeFrame(remoteOut, pointer(1, i));
        writeFrame(remoteOut, event(3, 1)); writeFrame(remoteOut, pointer(1, 1000));
        writeFrame(remoteOut, event(3, 0));
        remoteOut.write(new byte[] {8, 8, 0, 0, 0}); remoteOut.flush();
        got = 0;
        while (got < 5) got += remoteIn.read(pong, got, 5 - got);
        check(pong[0] == 9 && pong[1] == 8 && link.isAlive(), "heartbeat stays independent during 1000-frame input burst");
        SessionLink.Frame newest = link.poll();
        check(newest.type == 0x0b && (newest.payload[4] & 255) == (999 & 255)
                && (newest.payload[5] & 255) == (999 >>> 8), "socket reader coalesces to newest pre-click frame");
        SessionLink.Frame buttonDown = link.poll(), drag = link.poll(), buttonUp = link.poll();
        check(buttonDown.type == 3 && buttonDown.payload[1] == 1 && drag.type == 0x0b
                && integer(drag.payload, 4) == 1000 && buttonUp.type == 3 && buttonUp.payload[1] == 0,
              "socket reader preserves button drag and release order");
        remoteOut.write(new byte[] {12, 1, 0, 0, 0, 100, 0, 100, 0, 0}); remoteOut.flush();
        Thread.sleep(100);
        link.activate();
        SessionLink.Frame geometry = link.poll();
        check(geometry != null && geometry.type == 12, "geometry received during initialization reaches active loop");
        remoteOut.write(new byte[] {5, 1}); remoteOut.flush();
        Thread.sleep(1500);
        check(!link.isAlive(), "partial frame expires instead of blocking forever");
        link.close(); server[0].close(); listener.close();
    }
}
