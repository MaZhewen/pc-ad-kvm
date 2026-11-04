import java.io.DataInputStream;
import java.io.IOException;
import java.io.InterruptedIOException;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.security.GeneralSecurityException;
import java.security.MessageDigest;
import java.security.cert.CertificateException;
import java.security.cert.X509Certificate;
import java.util.Arrays;
import java.util.Timer;
import java.util.TimerTask;
import java.util.function.BooleanSupplier;
import javax.net.ssl.SSLContext;
import javax.net.ssl.SSLSocket;
import javax.net.ssl.TrustManager;
import javax.net.ssl.X509TrustManager;

/** Session-scoped TLS trust: only the exact certificate delivered by ADB is accepted. */
public final class PinnedTls {
    private static final int CONNECT_TIMEOUT_MS = 5000;
    private static final byte[] EMPTY_SESSION = new byte[16];
    private PinnedTls() { }

    public static final class Connection implements AutoCloseable {
        public final Socket socket;
        private final byte[] sessionId;

        private Connection(Socket socket, byte[] sessionId) {
            this.socket = socket;
            this.sessionId = sessionId;
        }

        public byte[] sessionId() { return sessionId.clone(); }
        public void close() throws IOException { socket.close(); }
    }

    public static Connection connect(String host, int port, String sha256, byte[] token,
                                     String instanceId, int channel, byte[] sessionId) throws IOException {
        return connect(host, port, sha256, token, instanceId, channel, sessionId, () -> true);
    }

    static Connection connect(String host, int port, String sha256, byte[] token,
                              String instanceId, int channel, byte[] sessionId, BooleanSupplier allowed) throws IOException {
        if (token == null || token.length != 32 || (channel != 1 && channel != 2)
                || sessionId == null || sessionId.length != 16 || (channel == 1) != Arrays.equals(sessionId, EMPTY_SESSION))
            throw new IOException("Invalid direct network authentication parameters");
        if (!allowed.getAsBoolean()) throw new InterruptedIOException("Direct connection canceled");
        final byte[] expected = decodeHex(sha256, 32, "certificate fingerprint");
        byte[] instance = decodeHex(instanceId, 16, "instance ID");
        // The launcher supplies a numeric IPv4 address, avoiding unbounded DNS resolution.
        final InetAddress address = ipv4(host);
        final Socket raw = new Socket();
        final Object state = new Object();
        final boolean[] ready = new boolean[1];
        Timer deadline = new Timer("PcKvm TLS deadline", true);
        deadline.schedule(new TimerTask() {
            public void run() {
                synchronized (state) {
                    if (!ready[0]) try { raw.close(); } catch (IOException ignored) { }
                }
            }
        }, CONNECT_TIMEOUT_MS);
        deadline.schedule(new TimerTask() {
            public void run() {
                synchronized (state) {
                    if (!ready[0] && !allowed.getAsBoolean())
                        try { raw.close(); } catch (IOException ignored) { }
                }
            }
        }, 0, 100);
        SSLSocket socket = null;
        boolean success = false;
        try {
            SSLContext context = SSLContext.getInstance("TLSv1.2");
            context.init(null, new TrustManager[] { new X509TrustManager() {
                public X509Certificate[] getAcceptedIssuers() { return new X509Certificate[0]; }
                public void checkClientTrusted(X509Certificate[] chain, String authType) throws CertificateException {
                    throw new CertificateException("Client certificates are not supported");
                }
                public void checkServerTrusted(X509Certificate[] chain, String authType) throws CertificateException {
                    if (chain == null || chain.length == 0) throw new CertificateException("Missing server certificate");
                    try {
                        byte[] actual = MessageDigest.getInstance("SHA-256").digest(chain[0].getEncoded());
                        if (!MessageDigest.isEqual(expected, actual)) throw new CertificateException("Server certificate pin mismatch");
                    } catch (GeneralSecurityException e) { throw new CertificateException(e); }
                }
            } }, null);
            raw.setTcpNoDelay(true);
            raw.connect(new InetSocketAddress(address, port), CONNECT_TIMEOUT_MS);
            socket = (SSLSocket)context.getSocketFactory().createSocket(raw, host, port, true);
            socket.setEnabledProtocols(new String[] { "TLSv1.2" });
            socket.setTcpNoDelay(true);
            socket.setSoTimeout(CONNECT_TIMEOUT_MS);
            socket.startHandshake();
            byte[] header = new byte[70];
            header[0] = 'D'; header[1] = 'K'; header[2] = 'V'; header[3] = 'M';
            header[4] = 2; header[5] = (byte)channel;
            System.arraycopy(instance, 0, header, 6, instance.length);
            System.arraycopy(token, 0, header, 22, token.length);
            System.arraycopy(sessionId, 0, header, 54, sessionId.length);
            socket.getOutputStream().write(header);
            socket.getOutputStream().flush();
            byte[] response = new byte[17];
            new DataInputStream(socket.getInputStream()).readFully(response);
            byte[] assigned = Arrays.copyOfRange(response, 1, response.length);
            if (response[0] != 0) {
                if (!Arrays.equals(assigned, EMPTY_SESSION)) throw new IOException("Invalid direct network response");
                throw new IOException(rejection(response[0]));
            }
            if (Arrays.equals(assigned, EMPTY_SESSION) || (channel == 2 && !MessageDigest.isEqual(assigned, sessionId)))
                throw new IOException("Invalid direct network session ID");
            synchronized (state) {
                if (!allowed.getAsBoolean()) throw new InterruptedIOException("Direct connection canceled");
                if (raw.isClosed()) throw new IOException("Direct network authentication timed out");
                ready[0] = true;
            }
            socket.setSoTimeout(0);
            success = true;
            return new Connection(socket, assigned);
        } catch (GeneralSecurityException e) { throw new IOException("Cannot initialize pinned TLS", e); }
        finally {
            deadline.cancel();
            if (!success) {
                try { raw.close(); } catch (IOException ignored) { }
                if (socket != null) try { socket.close(); } catch (IOException ignored) { }
            }
        }
    }

    private static String rejection(int status) {
        switch (status) {
            case 1: return "Direct network version or format mismatch";
            case 2: return "Direct network authentication rejected";
            case 3: return "Direct network channel already active";
            case 4: return "Direct network session expired";
            case 5: return "Direct network channel unavailable";
            default: return "Invalid direct network response";
        }
    }

    private static byte[] decodeHex(String value, int length, String field) throws IOException {
        if (value == null || value.length() != length * 2) throw new IOException("Invalid " + field);
        byte[] bytes = new byte[length];
        for (int i = 0; i < bytes.length; i++) {
            int high = "0123456789abcdef".indexOf(Character.toLowerCase(value.charAt(i * 2)));
            int low = "0123456789abcdef".indexOf(Character.toLowerCase(value.charAt(i * 2 + 1)));
            if (high < 0 || low < 0) throw new IOException("Invalid " + field);
            bytes[i] = (byte)((high << 4) | low);
        }
        return bytes;
    }

    private static InetAddress ipv4(String host) throws IOException {
        if (host == null) throw new IOException("Expected a numeric IPv4 host");
        String[] parts = host.split("\\.", -1);
        if (parts.length != 4) throw new IOException("Expected a numeric IPv4 host");
        byte[] address = new byte[4];
        for (int i = 0; i < 4; i++) {
            if (!parts[i].matches("[0-9]{1,3}")) throw new IOException("Invalid IPv4 host");
            int value = Integer.parseInt(parts[i]);
            if (value > 255) throw new IOException("Invalid IPv4 host");
            address[i] = (byte)value;
        }
        return InetAddress.getByAddress(address);
    }
}
