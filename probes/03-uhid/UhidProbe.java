import java.io.FileOutputStream;
import java.io.IOException;
import java.io.RandomAccessFile;

/**
 * UHID 探针：在应用进程之外创建自定义 HID 鼠标设备。
 * 只用 JDK 标准库，不引用 android.*。
 *
 * 用法: app_process / UhidProbe <mode>
 *   mode = create  创建设备并保持存活，之后每 200ms 写一次向右下的相对位移
 *   mode = once    创建一个极简鼠标，写 20 次位移后退出
 */
public class UhidProbe {

    static final int UHID_CREATE2 = 11;
    static final int UHID_INPUT2  = 12;
    static final int UHID_DESTROY = 2;

    static final int UHID_EVENT_SIZE = 4376;   // sizeof(struct uhid_event)

    // 偏移
    static final int OFF_NAME     = 4;
    static final int OFF_PHYS     = 132;
    static final int OFF_UNIQ     = 196;
    static final int OFF_RD_SIZE  = 260;
    static final int OFF_BUS      = 262;
    static final int OFF_VENDOR   = 264;
    static final int OFF_PRODUCT  = 268;
    static final int OFF_VERSION  = 272;
    static final int OFF_COUNTRY  = 276;
    static final int OFF_RD_DATA  = 280;

    static final int OFF_INPUT_SIZE = 4;
    static final int OFF_INPUT_DATA = 6;

    static final int BUS_USB = 0x03;

    /**
     * 极简相对鼠标的 HID 报告描述符。
     * 用途 0x01(Generic Desktop) / 0x02(Mouse)
     * 报告: [buttons(1B)] [dx(1B)] [dy(1B)] [wheel(1B)] = 4 字节
     */
    static final int[] REPORT_DESCRIPTOR = {
        0x05, 0x01,        // Usage Page (Generic Desktop)
        0x09, 0x02,        // Usage (Mouse)
        0xA1, 0x01,        // Collection (Application)
        0x09, 0x01,        //   Usage (Pointer)
        0xA1, 0x00,        //   Collection (Physical)
        0x05, 0x09,        //     Usage Page (Button)
        0x19, 0x01,        //     Usage Minimum (1)
        0x29, 0x03,        //     Usage Maximum (3)
        0x15, 0x00,        //     Logical Minimum (0)
        0x25, 0x01,        //     Logical Maximum (1)
        0x95, 0x03,        //     Report Count (3)
        0x75, 0x01,        //     Report Size (1)
        0x81, 0x02,        //     Input (Data,Var,Abs)   -- 3 个按钮位
        0x95, 0x01,        //     Report Count (1)
        0x75, 0x05,        //     Report Size (5)
        0x81, 0x01,        //     Input (Const)          -- 5 位填充
        0x05, 0x01,        //     Usage Page (Generic Desktop)
        0x09, 0x30,        //     Usage (X)
        0x09, 0x31,        //     Usage (Y)
        0x15, 0x81,        //     Logical Minimum (-127)
        0x25, 0x7F,        //     Logical Maximum (127)
        0x75, 0x08,        //     Report Size (8)
        0x95, 0x02,        //     Report Count (2)
        0x81, 0x06,        //     Input (Data,Var,Rel)   -- dx, dy
        0x09, 0x38,        //     Usage (Wheel)
        0x15, 0x81,        //     Logical Minimum (-127)
        0x25, 0x7F,        //     Logical Maximum (127)
        0x75, 0x08,        //     Report Size (8)
        0x95, 0x01,        //     Report Count (1)
        0x81, 0x06,        //     Input (Data,Var,Rel)   -- wheel
        0xC0,              //   End Collection
        0xC0               // End Collection
    };

    public static void main(String[] args) throws Exception {
        String mode = args.length > 0 ? args[0] : "once";
        System.out.println("UHIDPROBE start mode=" + mode);

        RandomAccessFile dev;
        try {
            dev = new RandomAccessFile("/dev/uhid", "rw");
        } catch (Exception e) {
            System.out.println("UHIDPROBE FAIL open /dev/uhid: " + e);
            return;
        }
        System.out.println("UHIDPROBE open /dev/uhid OK");

        byte[] ev = new byte[UHID_EVENT_SIZE];
        buildCreate2(ev, "PC-KVM Probe Mouse");
        int n = writeAll(dev, ev);
        System.out.println("UHIDPROBE CREATE2 wrote " + n + " bytes");

        if ("create".equals(mode)) {
            // 每 200ms 往右下推一点，方便肉眼确认光标在动
            int iter = 0;
            while (iter < 200) {
                byte[] inp = new byte[UHID_EVENT_SIZE];
                buildInput2(inp, (byte) 0, (byte) 3, (byte) 3, (byte) 0);
                int w = writeAll(dev, inp);
                if (w <= 0) {
                    System.out.println("UHIDPROBE INPUT2 write failed at iter=" + iter);
                    break;
                }
                Thread.sleep(200);
                iter++;
            }
            System.out.println("UHIDPROBE done " + iter + " moves");
        } else {
            byte[] inp = new byte[UHID_EVENT_SIZE];
            buildInput2(inp, (byte) 0, (byte) 5, (byte) 5, (byte) 0);
            writeAll(dev, inp);
            System.out.println("UHIDPROBE wrote one move");
        }

        dev.close();
        System.out.println("UHIDPROBE exit");
    }

    static void buildCreate2(byte[] ev, String name) {
        putU32(ev, 0, UHID_CREATE2);
        putStr(ev, OFF_NAME, name, 128);
        putStr(ev, OFF_PHYS, "pc-kvm", 64);
        putStr(ev, OFF_UNIQ, "", 64);
        putU16(ev, OFF_RD_SIZE, REPORT_DESCRIPTOR.length);
        putU16(ev, OFF_BUS, BUS_USB);
        putU32(ev, OFF_VENDOR, 0x1234);
        putU32(ev, OFF_PRODUCT, 0x5678);
        putU32(ev, OFF_VERSION, 1);
        putU32(ev, OFF_COUNTRY, 0);
        for (int i = 0; i < REPORT_DESCRIPTOR.length; i++) {
            ev[OFF_RD_DATA + i] = (byte) REPORT_DESCRIPTOR[i];
        }
    }

    static void buildInput2(byte[] ev, byte buttons, byte dx, byte dy, byte wheel) {
        putU32(ev, 0, UHID_INPUT2);
        putU16(ev, OFF_INPUT_SIZE, 4);           // 报告长度
        ev[OFF_INPUT_DATA + 0] = buttons;
        ev[OFF_INPUT_DATA + 1] = dx;
        ev[OFF_INPUT_DATA + 2] = dy;
        ev[OFF_INPUT_DATA + 3] = wheel;
    }

    static int writeAll(RandomAccessFile dev, byte[] buf) {
        try {
            dev.write(buf);
            return buf.length;
        } catch (IOException e) {
            System.out.println("UHIDPROBE write IOException: " + e);
            return -1;
        }
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
        int len = Math.min(raw.length, cap - 1);
        System.arraycopy(raw, 0, b, off, len);
    }
}
