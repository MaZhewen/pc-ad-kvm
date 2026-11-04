import java.io.*;
import java.net.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;
import javax.net.ssl.SSLSocket;
import javax.net.ssl.SSLContext;
import javax.net.ssl.TrustManager;
import javax.net.ssl.X509TrustManager;
import java.security.cert.X509Certificate;

public final class TestMain {
    static int port;
    static String pin;
    static byte[] token = new byte[32];
    static String instance = "0123456789abcdef1032547698badcfe";
    static final byte[] EMPTY = new byte[16];
    static final BufferedReader control = new BufferedReader(new InputStreamReader(System.in));

    static PinnedTls.Connection open(String fingerprint, byte[] secret, int channel, byte[] session) throws IOException {
        return PinnedTls.connect("127.0.0.1", port, fingerprint, secret, instance, channel, session);
    }

    static void denied(String fingerprint, byte[] secret) throws Exception {
        try (PinnedTls.Connection connection = open(fingerprint, secret, 1, EMPTY)) {
            throw new AssertionError("Rejected authentication unexpectedly succeeded");
        } catch (IOException expected) { }
    }

    static void echo(Socket socket, byte[] bytes) throws Exception {
        socket.setSoTimeout(3000);
        socket.getOutputStream().write(bytes);
        socket.getOutputStream().flush();
        byte[] received = new byte[bytes.length];
        new DataInputStream(socket.getInputStream()).readFully(received);
        if (!Arrays.equals(bytes, received)) throw new AssertionError("Forwarding changed frame bytes");
    }

    static SSLSocket rawTls() throws Exception {
        SSLContext context = SSLContext.getInstance("TLSv1.2");
        context.init(null, new TrustManager[] { new X509TrustManager() {
            public X509Certificate[] getAcceptedIssuers() { return new X509Certificate[0]; }
            public void checkClientTrusted(X509Certificate[] chain, String authType) { }
            public void checkServerTrusted(X509Certificate[] chain, String authType) { }
        } }, null);
        SSLSocket socket = (SSLSocket)context.getSocketFactory().createSocket("127.0.0.1", port);
        socket.setEnabledProtocols(new String[] { "TLSv1.2" });
        socket.setSoTimeout(3000); socket.startHandshake();
        return socket;
    }

    static void reject(byte[] header, int status) throws Exception {
        try (SSLSocket socket = rawTls()) {
            socket.getOutputStream().write(header);
            socket.getOutputStream().flush();
            byte[] expected = new byte[17]; expected[0] = (byte)status;
            if (!Arrays.equals(expected, response(socket))) throw new AssertionError("Expected rejection status " + status);
            closed(socket);
        }
    }

    static byte[] response(Socket socket) throws IOException {
        byte[] response = new byte[17];
        new DataInputStream(socket.getInputStream()).readFully(response);
        return response;
    }

    static byte[] hex(String value) {
        byte[] bytes = new byte[value.length() / 2];
        for (int i = 0; i < bytes.length; i++) bytes[i] = (byte)Integer.parseInt(value.substring(i * 2, i * 2 + 2), 16);
        return bytes;
    }

    static byte[] request(int channel, byte[] session) {
        byte[] bytes = hex("444b564d0200" + instance + "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f"
            + "00000000000000000000000000000000");
        bytes[5] = (byte)channel;
        System.arraycopy(session, 0, bytes, 54, 16);
        return bytes;
    }

    static void closed(Socket socket) throws IOException {
        socket.setSoTimeout(2500);
        try { if (socket.getInputStream().read() != -1) throw new AssertionError("Closed connection returned data"); }
        catch (SocketException expected) { }
    }

    static void clipboardCapability(String command) throws IOException {
        System.out.println(command); System.out.flush();
        if (!"CAPABILITY_CHANGED".equals(control.readLine())) throw new IOException("Clipboard capability update failed");
    }

    static void truncatedDeadline() throws Exception {
        long before = System.nanoTime();
        try (SSLSocket socket = rawTls()) {
            Thread.sleep(3500);
            socket.getOutputStream().write(request(1, EMPTY), 0, 7);
            socket.getOutputStream().flush();
            closed(socket);
        }
        long millis = (System.nanoTime() - before) / 1000000;
        if (millis < 4200 || millis > 6500) throw new AssertionError("Split handshake deadline: " + millis);
    }

    static void clientTimeout() throws Exception {
        int realPort = port;
        try (ServerSocket stalledServer = new ServerSocket(0, 1, InetAddress.getByAddress(new byte[] {127, 0, 0, 1}))) {
            port = stalledServer.getLocalPort();
            long started = System.nanoTime();
            denied(pin, token);
            long millis = (System.nanoTime() - started) / 1000000;
            if (millis < 4000 || millis > 6500) throw new AssertionError("Client handshake deadline: " + millis);
        } finally { port = realPort; }
        System.out.println("PASS client handshake deadline");
    }

    static void cancelHandshake() throws Exception {
        AtomicBoolean allowed = new AtomicBoolean(true);
        try (ServerSocket stalled = new ServerSocket(0, 1, InetAddress.getByAddress(new byte[] {127, 0, 0, 1}))) {
            Thread revoke = new Thread(() -> {
                try { Thread.sleep(250); } catch (InterruptedException e) { return; }
                allowed.set(false);
            });
            revoke.start();
            long before = System.nanoTime();
            try (PinnedTls.Connection connection = PinnedTls.connect("127.0.0.1", stalled.getLocalPort(), pin,
                    token, instance, 1, EMPTY, allowed::get)) {
                throw new AssertionError("Canceled TLS handshake succeeded");
            } catch (IOException expected) {
                if ((System.nanoTime() - before) / 1000000 > 2000) throw new AssertionError("TLS handshake cancellation was delayed");
            } finally { revoke.join(); }
        }
        System.out.println("PASS owner revocation cancels a stalled TLS handshake");
    }

    public static void main(String[] args) throws Exception {
        port = Integer.parseInt(args[0]); pin = args[1];
        for (int i = 0; i < token.length; i++) token[i] = (byte)i;
        reject(hex("444b564d0101"), 1);
        reject(hex("444b564d0203"), 1);
        reject(hex("004b564d0201"), 1);
        byte[] wrongInstance = request(1, EMPTY); wrongInstance[6] ^= 1; reject(wrongInstance, 2);
        byte[] nonzero = new byte[16]; nonzero[15] = 1;
        reject(request(1, nonzero), 1);
        reject(request(2, nonzero), 4);
        SSLContext globalContext = SSLContext.getDefault();
        denied((pin.charAt(0) == '0' ? "1" : "0") + pin.substring(1), token);
        byte[] wrong = token.clone(); wrong[31] ^= 1;
        byte[] wrongToken = request(1, EMPTY); wrongToken[53] ^= 1; reject(wrongToken, 2);
        denied(pin, wrong);
        truncatedDeadline();
        System.out.println("PASS fixed prefix, authentication statuses and shared split handshake deadline");

        byte[] oldId;
        try (SSLSocket input = rawTls()) {
            byte[] vector = request(1, EMPTY);
            input.getOutputStream().write(vector, 0, 3); input.getOutputStream().flush();
            input.getOutputStream().write(vector, 3, 5); input.getOutputStream().flush();
            input.getOutputStream().write(vector, 8, 62); input.getOutputStream().flush();
            byte[] reply = response(input);
            oldId = Arrays.copyOfRange(reply, 1, reply.length);
            if (reply[0] != 0 || Arrays.equals(EMPTY, oldId)) throw new AssertionError("Input response vector");
            try (PinnedTls.Connection oldClipboard = open(pin, token, 2, oldId)) {
                echo(oldClipboard.socket, token);
                reject(request(1, EMPTY), 3);
                reject(request(2, oldId), 3);
                echo(input, token);
                input.close();
                closed(oldClipboard.socket);
            }
        }

        try (PinnedTls.Connection current = open(pin, token, 1, EMPTY);
                PinnedTls.Connection clip = open(pin, token, 2, current.sessionId())) {
            Socket input = current.socket, clipboard = clip.socket;
            if (Arrays.equals(oldId, current.sessionId())) throw new AssertionError("Reused old input session ID");
            reject(request(2, oldId), 4);
            if (!Arrays.equals(clip.sessionId(), current.sessionId())) throw new AssertionError("Clipboard response session mismatch");
            if (!(input instanceof SSLSocket) || !"TLSv1.2".equals(((SSLSocket)input).getSession().getProtocol()))
                throw new AssertionError("TLS 1.2 required");
            if (!input.getTcpNoDelay()) throw new AssertionError("TCP_NODELAY required");
            byte[] bytes = new byte[65537]; new Random(7).nextBytes(bytes);
            echo(input, bytes); echo(clipboard, token);
            denied(pin, wrong);
            reject(request(1, EMPTY), 3);
            echo(input, bytes);
            System.out.println("PASS both TLS channels preserve bytes and unauthorized clients do not interrupt input");
            Socket stalled = new Socket("127.0.0.1", port);
            stalled.setSoTimeout(7500);
            long before = System.nanoTime();
            try { if (stalled.getInputStream().read() != -1) throw new AssertionError("Plaintext accepted"); }
            catch (SocketException expected) { }
            long millis = (System.nanoTime() - before) / 1000000;
            if (millis > 6500) throw new AssertionError("Handshake deadline exceeded: " + millis);
            stalled.close();
            echo(input, token);
            System.out.println("PASS bounded handshake deadline and no plaintext fallback");
            clientTimeout();
            cancelHandshake();
            if (globalContext != SSLContext.getDefault()) throw new AssertionError("Process-wide TLS trust changed");
            Socket[] candidates = new Socket[5];
            for (int i = 0; i < candidates.length; i++) candidates[i] = new Socket("127.0.0.1", port);
            closed(candidates[4]);
            echo(input, token);
            System.out.println("PASS bounded pending connections and isolated TLS trust");
            System.out.println("DISPOSE"); System.out.flush();
            if (!"DISPOSED".equals(control.readLine()))
                throw new AssertionError("Dispose synchronization failed");
            ArrayList<Socket> all = new ArrayList<Socket>(Arrays.asList(candidates));
            all.add(input); all.add(clipboard);
            for (Socket socket : all) {
                socket.setSoTimeout(2000);
                try { if (socket.getInputStream().read() != -1) throw new AssertionError("Disposed connection remained open"); }
                catch (SocketException expected) { }
                socket.close();
            }
            denied(pin, token);
            System.out.println("PASS disposal closes active channels, pending handshake, and listener");
        }
        port = Integer.parseInt(args[2]); pin = args[3];
        try (PinnedTls.Connection input = open(pin, token, 1, EMPTY)) {
            reject(request(2, input.sessionId()), 5);
            echo(input.socket, token);
            clipboardCapability("ENABLE_OPTIONAL");
            try (PinnedTls.Connection optional = open(pin, token, 2, input.sessionId())) {
                echo(optional.socket, token);
                clipboardCapability("DISABLE_OPTIONAL");
                closed(optional.socket);
                reject(request(2, input.sessionId()), 5);
                echo(input.socket, token);
            }
        }
        System.out.println("PASS optional clipboard leaves the input channel available");
    }
}
