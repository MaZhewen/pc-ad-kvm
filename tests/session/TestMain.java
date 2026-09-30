import java.io.InputStream;
import java.io.OutputStream;
import java.net.ServerSocket;
import java.net.Socket;

public class TestMain {
    static void check(boolean ok, String message) { if (!ok) throw new AssertionError(message); System.out.println("PASS " + message); }
    public static void main(String[] args) throws Exception {
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
        link.start();
        remoteOut.write(new byte[] {8, 7, 0, 0, 0}); remoteOut.flush();
        server[0].setSoTimeout(2000);
        byte[] pong = new byte[5]; got = 0;
        while (got < 5) got += remoteIn.read(pong, got, 5 - got);
        check(pong[0] == 9 && pong[1] == 7, "initialization answers PING");
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
