import java.io.IOException;
import java.io.RandomAccessFile;

/** 封装 /dev/uhid：创建一个虚拟 HID 设备并持续写入输入报告。 */
public class UhidDevice {

    static final int UHID_CREATE2 = 11;
    static final int UHID_INPUT2  = 12;
    static final int UHID_EVENT_SIZE = 4376;   // sizeof(struct uhid_event)，内核一次接受（实测）

    static final int OFF_NAME    = 4;
    static final int OFF_PHYS    = 132;
    static final int OFF_UNIQ    = 196;
    static final int OFF_RD_SIZE = 260;
    static final int OFF_BUS     = 262;
    static final int OFF_VENDOR  = 264;
    static final int OFF_PRODUCT = 268;
    static final int OFF_VERSION = 272;
    static final int OFF_COUNTRY = 276;
    static final int OFF_RD_DATA = 280;

    static final int OFF_INPUT_SIZE = 4;
    static final int OFF_INPUT_DATA = 6;

    static final int BUS_USB = 0x03;

    private final RandomAccessFile dev;

    public UhidDevice(String name, int[] descriptor) throws IOException {
        this(name, descriptor, 0x5679);
    }

    public UhidDevice(String name, int[] descriptor, int product) throws IOException {
        dev = new RandomAccessFile("/dev/uhid", "rw");
        byte[] ev = new byte[UHID_EVENT_SIZE];
        putU32(ev, 0, UHID_CREATE2);
        putStr(ev, OFF_NAME, name, 128);
        putStr(ev, OFF_PHYS, "pc-kvm", 64);
        putStr(ev, OFF_UNIQ, "", 64);
        putU16(ev, OFF_RD_SIZE, descriptor.length);   // 真实描述符长度，不是缓冲容量
        putU16(ev, OFF_BUS, BUS_USB);
        putU32(ev, OFF_VENDOR, 0x1234);
        putU32(ev, OFF_PRODUCT, product);
        putU32(ev, OFF_VERSION, 1);
        putU32(ev, OFF_COUNTRY, 0);
        for (int i = 0; i < descriptor.length; i++) {
            ev[OFF_RD_DATA + i] = (byte) descriptor[i];
        }
        dev.write(ev);
    }

    /** 鼠标报告：[reportId][buttons][dx][dy][wheel] */
    public void sendMouse(byte buttons, byte dx, byte dy, byte wheel) throws IOException {
        byte[] ev = new byte[UHID_EVENT_SIZE];
        putU32(ev, 0, UHID_INPUT2);
        putU16(ev, OFF_INPUT_SIZE, HidDescriptor.MOUSE_REPORT_SIZE);
        ev[OFF_INPUT_DATA + 0] = (byte) HidDescriptor.MOUSE_REPORT_ID;
        ev[OFF_INPUT_DATA + 1] = buttons;
        ev[OFF_INPUT_DATA + 2] = dx;
        ev[OFF_INPUT_DATA + 3] = dy;
        ev[OFF_INPUT_DATA + 4] = wheel;
        dev.write(ev);
    }

    /** 键盘报告：[reportId][modifiers][reserved][6 个 key slot] */
    public void sendKeyboard(byte modifiers, byte[] keys6) throws IOException {
        byte[] ev = new byte[UHID_EVENT_SIZE];
        putU32(ev, 0, UHID_INPUT2);
        putU16(ev, OFF_INPUT_SIZE, HidDescriptor.KEYBOARD_REPORT_SIZE);
        ev[OFF_INPUT_DATA + 0] = (byte) HidDescriptor.KEYBOARD_REPORT_ID;
        ev[OFF_INPUT_DATA + 1] = modifiers;
        ev[OFF_INPUT_DATA + 2] = 0;   // reserved，必须为 0
        for (int i = 0; i < 6; i++) {
            ev[OFF_INPUT_DATA + 3 + i] = i < keys6.length ? keys6[i] : 0;
        }
        dev.write(ev);
    }

    public void close() {
        try { dev.close(); } catch (IOException e) { /* 关 fd 时内核自动销毁设备 */ }
    }

    public void sendReport(byte[] report) throws IOException {
        byte[] ev = new byte[UHID_EVENT_SIZE];
        putU32(ev, 0, UHID_INPUT2);
        putU16(ev, OFF_INPUT_SIZE, report.length);
        System.arraycopy(report, 0, ev, OFF_INPUT_DATA, report.length);
        dev.write(ev);
    }

    static void putU32(byte[] b, int off, int v) {
        b[off]     = (byte) (v & 0xFF);
        b[off + 1] = (byte) ((v >>> 8) & 0xFF);
        b[off + 2] = (byte) ((v >>> 16) & 0xFF);
        b[off + 3] = (byte) ((v >>> 24) & 0xFF);
    }

    static void putU16(byte[] b, int off, int v) {
        b[off]     = (byte) (v & 0xFF);
        b[off + 1] = (byte) ((v >>> 8) & 0xFF);
    }

    static void putStr(byte[] b, int off, String s, int cap) {
        byte[] raw;
        try { raw = s.getBytes("UTF-8"); } catch (Exception e) { raw = s.getBytes(); }
        int len = Math.min(raw.length, cap - 1);   // 保留结尾 NUL
        System.arraycopy(raw, 0, b, off, len);
    }
}
