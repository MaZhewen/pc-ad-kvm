import java.io.BufferedReader;
import java.io.InputStreamReader;

/**
 * 阶段二 Task 2 的命令行版本：从 stdin 读文本指令驱动虚拟 HID 设备。
 *   M <dx> <dy> <buttons> <wheel>   发送鼠标报告（dx/dy 会被夹到 -127..127）
 *   K <mods> <k1> <k2> <k3> <k4> <k5> <k6>   发送键盘报告
 *   Q                                退出
 * 后续任务会把 stdin 换成 socket，本类保留为手工调试入口。
 */
public class Injector {

    public static void main(String[] args) throws Exception {
        System.out.println("INJECTOR start");
        UhidDevice dev;
        try {
            dev = new UhidDevice("PC-KVM Virtual Input", HidDescriptor.MOUSE_KEYBOARD);
        } catch (Exception e) {
            System.out.println("INJECTOR FAIL open /dev/uhid: " + e);
            return;
        }
        System.out.println("INJECTOR ready");

        BufferedReader in = new BufferedReader(new InputStreamReader(System.in));
        String line;
        while ((line = in.readLine()) != null) {
            line = line.trim();
            if (line.length() == 0) continue;
            if (line.equals("Q")) break;

            String[] t = line.split("\\s+");
            try {
                if (t[0].equals("M") && t.length >= 5) {
                    dev.sendMouse((byte) clampToByte(parse(t[3])),
                                  (byte) clamp127(parse(t[1])),
                                  (byte) clamp127(parse(t[2])),
                                  (byte) clamp127(parse(t[4])));
                } else if (t[0].equals("K") && t.length >= 8) {
                    byte[] keys = new byte[6];
                    for (int i = 0; i < 6; i++) keys[i] = (byte) parse(t[i + 2]);
                    dev.sendKeyboard((byte) parse(t[1]), keys);
                } else {
                    System.out.println("INJECTOR bad line: " + line);
                }
            } catch (Exception ex) {
                System.out.println("INJECTOR err on [" + line + "]: " + ex);
            }
        }

        dev.close();
        System.out.println("INJECTOR exit");
    }

    /** 把任意整数夹到有符号 8 位范围，HID 报告的轴是 int8。 */
    static int clamp127(int v) {
        if (v > 127) return 127;
        if (v < -127) return -127;
        return v;
    }

    static int clampToByte(int v) {
        if (v > 127) return 127;
        if (v < -128) return -128;
        return v;
    }

    static int parse(String s) { return Integer.parseInt(s); }
}
