import java.io.InputStream;
import java.io.OutputStream;
import java.io.IOException;
import java.net.Socket;
import java.net.SocketTimeoutException;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.TimeUnit;

/** Sole socket reader for the production injector. PING remains live during UHID initialization. */
public final class SessionLink {
    public static final class Frame {
        public final int type;
        public final byte[] payload;
        Frame(int type, byte[] payload) { this.type = type; this.payload = payload; }
    }

    final Socket socket;
    final InputStream input;
    final OutputStream output;
    final ArrayBlockingQueue<Frame> frames = new ArrayBlockingQueue<Frame>(128);
    final Object geometryGate = new Object();
    final long initializedAt = System.nanoTime();
    volatile long lastPing = initializedAt;
    volatile boolean formal;
    volatile boolean alive = true;
    volatile Frame latestGeometry;
    Thread reader, watcher;

    public SessionLink(Socket socket) throws IOException {
        this.socket = socket;
        socket.setSoTimeout(250);
        input = socket.getInputStream();
        final OutputStream raw = socket.getOutputStream();
        output = new OutputStream() {
            public synchronized void write(int b) throws IOException { raw.write(b); }
            public synchronized void write(byte[] b, int off, int len) throws IOException { raw.write(b, off, len); }
            public synchronized void flush() throws IOException { raw.flush(); }
        };
    }

    public static byte[] parseToken(String hex) {
        if (hex == null || hex.length() != 64) throw new IllegalArgumentException("Session token must be 64 hex digits");
        byte[] token = new byte[32];
        for (int i = 0; i < token.length; i++) {
            int a = Character.digit(hex.charAt(2 * i), 16);
            int b = Character.digit(hex.charAt(2 * i + 1), 16);
            if (a < 0 || b < 0) throw new IllegalArgumentException("Invalid session token");
            token[i] = (byte)((a << 4) | b);
        }
        return token;
    }

    public static void handshake(Socket socket, byte[] token) throws IOException {
        if (token == null || token.length != 32) throw new IllegalArgumentException("Invalid session token");
        byte[] preamble = new byte[37];
        preamble[0] = 'P'; preamble[1] = 'K'; preamble[2] = 'V'; preamble[3] = 'M'; preamble[4] = 1;
        System.arraycopy(token, 0, preamble, 5, 32);
        OutputStream out = socket.getOutputStream();
        out.write(preamble); out.flush();
        socket.setSoTimeout(2000);
        if (socket.getInputStream().read() != 1) throw new IOException("Session acknowledgement failed");
        socket.setSoTimeout(250);
    }

    public OutputStream output() { return output; }
    public boolean isAlive() { return alive; }
    public Frame poll() throws InterruptedException { return frames.poll(200, TimeUnit.MILLISECONDS); }
    public void activate() {
        synchronized (geometryGate) {
            if (latestGeometry != null && !frames.offer(latestGeometry)) { close(); return; }
            latestGeometry = null;
            lastPing = System.nanoTime(); formal = true;
        }
    }

    public void start() {
        reader = new Thread(new Runnable() { public void run() { readLoop(); } }, "pckvm-socket-reader");
        reader.setDaemon(true); reader.start();
        watcher = new Thread(new Runnable() { public void run() {
            while (alive) {
                long now = System.nanoTime();
                if (formal ? now - lastPing > TimeUnit.SECONDS.toNanos(3)
                           : now - initializedAt > TimeUnit.SECONDS.toNanos(20)) { close(); return; }
                try { Thread.sleep(100); } catch (InterruptedException e) { return; }
            }
        } }, "pckvm-session-watchdog");
        watcher.setDaemon(true); watcher.start();
    }

    void readLoop() {
        try {
            while (alive) {
                int type;
                try { type = input.read(); }
                catch (SocketTimeoutException e) { continue; }
                if (type < 0) return;
                int length = payloadLength(type);
                if (length < 0) throw new IOException("Unknown frame type");
                byte[] payload = new byte[length];
                long deadline = System.nanoTime() + TimeUnit.SECONDS.toNanos(1);
                int got = 0;
                while (got < length) {
                    if (System.nanoTime() >= deadline) throw new IOException("Partial frame deadline");
                    try {
                        int n = input.read(payload, got, length - got);
                        if (n < 0) return;
                        got += n;
                    } catch (SocketTimeoutException e) { /* bounded by the whole-frame deadline */ }
                }
                if (type == 8) {
                    lastPing = System.nanoTime();
                    byte[] pong = new byte[5]; pong[0] = 9;
                    System.arraycopy(payload, 0, pong, 1, 4);
                    output.write(pong); output.flush();
                } else if (type == 12) {
                    synchronized (geometryGate) {
                        if (!formal) latestGeometry = new Frame(type, payload);
                        else if (!frames.offer(new Frame(type, payload))) throw new IOException("Input queue full");
                    }
                } else if (!frames.offer(new Frame(type, payload))) throw new IOException("Input queue full");
            }
        } catch (Exception e) { /* broken or expired stream; close below */ }
        finally { close(); }
    }

    public void close() {
        alive = false;
        try { socket.close(); } catch (IOException e) { }
    }

    static int payloadLength(int type) {
        switch (type) {
            case 0x0b: return 12; case 0x0c: return 9;
            case 0x01: case 0x02: case 0x04: case 0x05: case 0x08: case 0x09: return 4;
            case 0x03: return 2; case 0x06: case 0x0a: return 0;
            case 0x07: return 7;
            default: return -1;
        }
    }
}
