import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.Socket;

public class Injector {

    static final int PORT = 27183;
    static final byte MSG_ENTER = 0x01, MSG_MOVE = 0x02, MSG_BUTTON = 0x03,
                      MSG_SCROLL = 0x04, MSG_KEY = 0x05, MSG_LEAVE = 0x06,
                      MSG_CONFIG = 0x07, MSG_PING = 0x08, MSG_PONG = 0x09;

    /** 当前按下的鼠标按钮位图（bit0 左, bit1 右, bit2 中）。 */
    static int buttonsDown = 0;

    public static void main(String[] args) throws Exception {
        if (args.length > 0 && args[0].equals("--stdin")) {
            StdinMode.run();
            return;
        }

        System.out.println("INJECTOR start");
        UhidDevice dev;
        try {
            dev = new UhidDevice("PC-KVM Virtual Input", HidDescriptor.MOUSE_KEYBOARD);
        } catch (Exception e) {
            System.out.println("INJECTOR FAIL open /dev/uhid: " + e);
            return;
        }
        System.out.println("INJECTOR ready");

        // 连不上就重试：PC 端可能比我们先起好，也可能刚重连
        Socket sock = null;
        for (int i = 0; i < 100 && sock == null; i++) {
            try {
                sock = new Socket();
                sock.connect(new InetSocketAddress("127.0.0.1", PORT), 1000);
                sock.setTcpNoDelay(true);
            } catch (Exception e) {
                sock = null;
                Thread.sleep(100);
            }
        }
        if (sock == null) {
            System.out.println("INJECTOR FAIL cannot connect to PC");
            dev.close();
            return;
        }
        System.out.println("INJECTOR connected");

        InputStream in = sock.getInputStream();
        OutputStream out = sock.getOutputStream();

        try {
            while (true) {
                int type = in.read();
                if (type < 0) break;
                int plen = payloadLength(type);
                if (plen < 0) break;
                byte[] p = new byte[plen];
                if (plen > 0 && !readExact(in, p, plen)) break;
                handle(dev, out, (byte) type, p);
            }
        } catch (Exception e) {
            System.out.println("INJECTOR loop end: " + e);
        }

        dev.close();
        sock.close();
        System.out.println("INJECTOR exit");
    }

    static void handle(UhidDevice dev, OutputStream out, byte type, byte[] p) throws Exception {
        if (type == MSG_MOVE) {
            int dx = i16(p, 0), dy = i16(p, 1 + 1);
            // int8 轴：超过 ±127 要拆成多条报告，否则会丢位移
            while (dx != 0 || dy != 0) {
                int sx = clamp127(dx), sy = clamp127(dy);
                dev.sendMouse((byte) buttonsDown, (byte) sx, (byte) sy, (byte) 0);
                dx -= sx; dy -= sy;
            }
        } else if (type == MSG_BUTTON) {
            int btn = p[0] & 0xFF, down = p[1] & 0xFF;
            int bit = btnToBit(btn);
            if (down != 0) buttonsDown |= bit; else buttonsDown &= ~bit;
            dev.sendMouse((byte) buttonsDown, (byte) 0, (byte) 0, (byte) 0);
        } else if (type == MSG_SCROLL) {
            int dy = i16(p, 2);
            dev.sendMouse((byte) buttonsDown, (byte) 0, (byte) 0, (byte) clamp127(dy));
        } else if (type == MSG_KEY) {
            KeyState.apply(dev, u16(p, 0), p[2] != 0, p[3]);
        } else if (type == MSG_PING) {
            int seq = i32(p, 0);
            byte[] r = new byte[5];
            r[0] = MSG_PONG;
            r[1] = (byte) (seq & 0xFF);
            r[2] = (byte) ((seq >>> 8) & 0xFF);
            r[3] = (byte) ((seq >>> 16) & 0xFF);
            r[4] = (byte) ((seq >>> 24) & 0xFF);
            out.write(r);
            out.flush();
        }
        // MSG_ENTER / MSG_LEAVE / MSG_CONFIG 由后续任务接管
    }

    static int btnToBit(int btn) {
        if (btn == 1) return 1;   // 左
        if (btn == 2) return 2;   // 右
        if (btn == 3) return 4;   // 中
        return 0;
    }

    static int payloadLength(int type) {
        switch (type) {
            case MSG_ENTER:  return 4;
            case MSG_MOVE:   return 4;
            case MSG_BUTTON: return 2;
            case MSG_SCROLL: return 4;
            case MSG_KEY:    return 4;
            case MSG_LEAVE:  return 0;
            case MSG_CONFIG: return 7;
            case MSG_PING:   return 4;
            case MSG_PONG:   return 4;
            default:         return -1;
        }
    }

    static boolean readExact(InputStream in, byte[] buf, int n) throws Exception {
        int got = 0;
        while (got < n) {
            int r = in.read(buf, got, n - got);
            if (r <= 0) return false;
            got += r;
        }
        return true;
    }

    static int clamp127(int v) {
        if (v > 127) return 127;
        if (v < -127) return -127;
        return v;
    }

    static int i16(byte[] b, int off) {
        return (short) ((b[off] & 0xFF) | ((b[off + 1] & 0xFF) << 8));
    }

    static int u16(byte[] b, int off) {
        return (b[off] & 0xFF) | ((b[off + 1] & 0xFF) << 8);
    }

    static int i32(byte[] b, int off) {
        return (b[off] & 0xFF) | ((b[off + 1] & 0xFF) << 8)
             | ((b[off + 2] & 0xFF) << 16) | ((b[off + 3] & 0xFF) << 24);
    }
}
