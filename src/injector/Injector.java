import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.io.RandomAccessFile;
import java.nio.channels.FileLock;
import java.nio.channels.OverlappingFileLockException;
import java.util.concurrent.atomic.AtomicBoolean;

public class Injector {

    static final int PORT = 27183;
    static final byte MSG_ENTER = 0x01, MSG_MOVE = 0x02, MSG_BUTTON = 0x03,
                      MSG_SCROLL = 0x04, MSG_KEY = 0x05, MSG_LEAVE = 0x06,
                      MSG_CONFIG = 0x07, MSG_PING = 0x08, MSG_PONG = 0x09,
                      MSG_HOME = 0x0A;

    /** 当前按下的鼠标按钮位图（bit0 左, bit1 右, bit2 中）。 */
    static int buttonsDown = 0;

    public static void main(String[] args) throws Exception {
        if (args.length > 0 && args[0].equals("--stdin")) {
            StdinMode.run();
            return;
        }

        if (args.length != 2 || !"--session".equals(args[0]))
            throw new IllegalArgumentException("A session token is required");
        byte[] token = SessionLink.parseToken(args[1]);
        Socket sock = null;
        for (int i = 0; i < 100 && sock == null; i++) {
            try {
                sock = new Socket();
                sock.connect(new InetSocketAddress("127.0.0.1", PORT), 1000);
                sock.setTcpNoDelay(true);
            } catch (Exception e) {
                if (sock != null) try { sock.close(); } catch (Exception ignored) { }
                sock = null;
                Thread.sleep(100);
            }
        }
        if (sock == null) throw new IllegalStateException("Cannot connect to PC reverse tunnel");
        SessionLink link = null;
        UhidDevice dev = null;
        AbsoluteSession pointer = null;
        RandomAccessFile lockFile = null;
        FileLock processLock = null;
        final AtomicBoolean finished = new AtomicBoolean(false);
        try {
            SessionLink.handshake(sock, token);
            link = new SessionLink(sock);
            link.start();
            final SessionLink observed = link;
            Thread shutdownGuard = new Thread(new Runnable() { public void run() {
                while (!finished.get() && observed.isAlive()) {
                    try { Thread.sleep(100); } catch (InterruptedException e) { return; }
                }
                if (finished.get()) return;
                try { Thread.sleep(1000); } catch (InterruptedException e) { return; }
                if (!finished.get()) Runtime.getRuntime().halt(0);
            } }, "pckvm-shutdown-guard");
            shutdownGuard.setDaemon(true);
            shutdownGuard.start();

            lockFile = new RandomAccessFile("/data/local/tmp/pckvm.lock", "rw");
            long lockDeadline = System.nanoTime() + 20000000000L;
            while (processLock == null && link.isAlive() && System.nanoTime() < lockDeadline) {
                try { processLock = lockFile.getChannel().tryLock(); }
                catch (OverlappingFileLockException busy) { /* previous session in same process */ }
                if (processLock == null) Thread.sleep(50);
            }
            if (processLock == null || !link.isAlive()) throw new IllegalStateException("Injector lock unavailable");
            dev = new UhidDevice("PC-KVM Keyboard", HidDescriptor.keyboardOnly());
            pointer = new AbsoluteSession();
            OutputStream out = link.output();
            link.activate();
            SessionLink.Frame frame;
            while (link.isAlive()) {
                frame = link.poll();
                if (frame == null) continue;
                if (!pointer.handle(frame.type, frame.payload, out))
                    handle(dev, out, (byte)frame.type, frame.payload);
            }
        } catch (Exception e) {
            System.out.println("INJECTOR loop end: " + e);
        } finally {
            if (link != null) link.close();
            else try { sock.close(); } catch (Exception ignored) { }
            // Every release step is independent: a failed UHID report must not skip FD closure.
            buttonsDown = 0;
            if (dev != null) try { KeyState.releaseAll(dev); } catch (Exception ignored) { }
            if (pointer != null) try { pointer.close(); } catch (Exception ignored) { }
            if (dev != null) try { dev.close(); } catch (Exception ignored) { }
            if (processLock != null) try { processLock.release(); } catch (Exception ignored) { }
            if (lockFile != null) try { lockFile.close(); } catch (Exception ignored) { }
            finished.set(true);
        }
        System.out.println("INJECTOR exit");
    }

    static void handle(UhidDevice dev, OutputStream out, byte type, byte[] p) throws Exception {
        if (type == MSG_MOVE) {
            sendRelative(dev, i16(p, 0), i16(p, 1 + 1));
        } else if (type == MSG_ENTER) {
            // 入屏定位。PC 侧发 ENTER 之前**总是**先发 HOME（把设备光标硬顶到 (0,0)），
            // 所以这里只需按相对位移把光标挪到 (x,y)——与 PC 侧 CursorModel.SetPosition
            // 的记账就此一致。
            //
            // ⚠️ 原先这个分支不存在（handle() 结尾只留了句"由后续任务接管"），后果是
            // 2026-09-23 真机实测的两个缺陷：PC 侧模型以为光标在 (3199,y)，设备却停在 (0,0)，
            // 两者相差整整一个屏宽 →
            //   ① 手机在左侧时，指针从**左**缘冒出来（应该从与 PC 相邻的右缘）；
            //   ② 模型一进场就已"贴在回程边"上，向右推 40 mickey 立刻弹回 PC；
            //      且阈值按**原始 mickey** 计而可见位移 = 40×灵敏度 → "速度越慢越容易回 PC"。
            sendRelative(dev, i16(p, 0), i16(p, 1 + 1));
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
        } else if (type == MSG_HOME) {
            // 硬顶到左上角：位移取得比任何手机屏都大，靠设备把光标钳在边缘，
            // 于是 HOME 之后设备光标必然在 (0,0)——这是 MSG_ENTER 相对定位的前提。
            sendRelative(dev, HOME_PUSH, HOME_PUSH);
        } else if (type == MSG_LEAVE) {
            // 防修饰键/鼠标键卡在按下态（遥控类软件经典 bug）：接管期间按着 Ctrl 或鼠标键
            // 退出，不清理的话手机上会一直"按住"。PC 侧所有放弃路径都会补发 LEAVE。
            buttonsDown = 0;
            KeyState.releaseAll(dev);
        }
        // MSG_CONFIG 由后续任务接管（MSG_ENTER 已实现，见 MSG_ENTER 分支）
    }

    /** HOME 的"硬顶"位移：5080 = 40×127，大于任何手机屏的宽/高（3200/2136）。 */
    static final int HOME_PUSH = -5080;

    /**
     * 把一次相对位移拆成多条 ≤127 的报告发出去（HID 相对轴是 int8）。
     * MSG_MOVE / MSG_ENTER / MSG_HOME 共用它；拆分算法在 PointerPlan（纯函数，有离线用例）。
     */
    static void sendRelative(UhidDevice dev, int dx, int dy) throws Exception {
        int n = PointerPlan.stepCount(dx, dy);
        for (int i = 0; i < n; i++) {
            dev.sendMouse((byte) buttonsDown,
                          (byte) PointerPlan.stepX(dx, i),
                          (byte) PointerPlan.stepY(dy, i), (byte) 0);
        }
    }

    static int btnToBit(int btn) {
        if (btn == 1) return 1;   // 左
        if (btn == 2) return 2;   // 右
        if (btn == 3) return 4;   // 中
        return 0;
    }

    static int payloadLength(int type) {
        switch (type) {
            case 0x0b: return 12; // epoch, sequence, x, y
            case 0x0c: return 9;  // epoch, width, height, display rotation
            case MSG_ENTER:  return 4;
            case MSG_MOVE:   return 4;
            case MSG_BUTTON: return 2;
            case MSG_SCROLL: return 4;
            case MSG_KEY:    return 4;
            case MSG_LEAVE:  return 0;
            case MSG_CONFIG: return 7;
            case MSG_PING:   return 4;
            case MSG_PONG:   return 4;
            case MSG_HOME:   return 0;
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
