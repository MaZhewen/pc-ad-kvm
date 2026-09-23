public class TestMain {
    static int fails = 0;
    static int total = 0;

    static void check(String name, int actual, int expected) {
        total++;
        boolean ok = actual == expected;
        if (!ok) fails++;
        String fmt = (expected < 0 || actual < 0)
            ? " got=" + actual + " want=" + expected
            : String.format(" got=0x%02X want=0x%02X", actual, expected);
        System.out.println((ok ? "PASS" : "FAIL") + "  " + name + fmt);
    }

    public static void main(String[] args) {
        // 非 E0 基本键
        check("nonE0 0x01 (Esc)       -> 0x29", ScancodeMap.toHidUsage(0x01), 0x29);
        check("nonE0 0x0E (Backspace) -> 0x2A", ScancodeMap.toHidUsage(0x0E), 0x2A);
        check("nonE0 0x1C (Enter)     -> 0x28", ScancodeMap.toHidUsage(0x1C), 0x28);
        check("nonE0 0x1E (A)         -> 0x04", ScancodeMap.toHidUsage(0x1E), 0x04);
        check("nonE0 0x39 (Space)     -> 0x2C", ScancodeMap.toHidUsage(0x39), 0x2C);

        // 功能键
        check("nonE0 0x3B (F1)  -> 0x3A", ScancodeMap.toHidUsage(0x3B), 0x3A);
        check("nonE0 0x44 (F10) -> 0x43", ScancodeMap.toHidUsage(0x44), 0x43);
        check("nonE0 0x57 (F11) -> 0x44", ScancodeMap.toHidUsage(0x57), 0x44);
        check("nonE0 0x58 (F12) -> 0x45", ScancodeMap.toHidUsage(0x58), 0x45);

        // 标点
        check("nonE0 0x0C (-)  -> 0x2D", ScancodeMap.toHidUsage(0x0C), 0x2D);
        check("nonE0 0x0D (=)  -> 0x2E", ScancodeMap.toHidUsage(0x0D), 0x2E);
        check("nonE0 0x1A ([)  -> 0x2F", ScancodeMap.toHidUsage(0x1A), 0x2F);
        check("nonE0 0x27 (;)  -> 0x33", ScancodeMap.toHidUsage(0x27), 0x33);
        check("nonE0 0x3A (CapsLock) -> 0x39", ScancodeMap.toHidUsage(0x3A), 0x39);

        // 数字键盘的数字与小数点 —— 必须映到**数字行** usage，不能映到 Keypad usage。
        // 理由（2026-09-23 真机铁证）：Android 的 Virtual.kcm 里 NUMPAD_0..9/NUMPAD_DOT 都是
        //   key NUMPAD_7 { label: '7'  base: fallback MOVE_HOME  numlock: '7' }
        // 只有"手机自己的 NumLock"亮着才产字符；而本虚拟键盘**没有 LED Output 报告**，
        // Android 永远点不亮它 → base 永远生效 → 小键盘永远不产字符（与 PC 的 NumLock 无关）。
        // 数字行的 base 是字面字符（key 7 { base: '7' }），任何状态下都产字符。
        check("nonE0 0x47 (Num 7) -> 0x24 ('7')", ScancodeMap.toHidUsage(0x47), 0x24);
        check("nonE0 0x48 (Num 8) -> 0x25 ('8')", ScancodeMap.toHidUsage(0x48), 0x25);
        check("nonE0 0x49 (Num 9) -> 0x26 ('9')", ScancodeMap.toHidUsage(0x49), 0x26);
        check("nonE0 0x4B (Num 4) -> 0x21 ('4')", ScancodeMap.toHidUsage(0x4B), 0x21);
        check("nonE0 0x4C (Num 5) -> 0x22 ('5')", ScancodeMap.toHidUsage(0x4C), 0x22);
        check("nonE0 0x4D (Num 6) -> 0x23 ('6')", ScancodeMap.toHidUsage(0x4D), 0x23);
        check("nonE0 0x4F (Num 1) -> 0x1E ('1')", ScancodeMap.toHidUsage(0x4F), 0x1E);
        check("nonE0 0x50 (Num 2) -> 0x1F ('2')", ScancodeMap.toHidUsage(0x50), 0x1F);
        check("nonE0 0x51 (Num 3) -> 0x20 ('3')", ScancodeMap.toHidUsage(0x51), 0x20);
        check("nonE0 0x52 (Num 0) -> 0x27 ('0')", ScancodeMap.toHidUsage(0x52), 0x27);
        check("nonE0 0x53 (Num .) -> 0x37 ('.')", ScancodeMap.toHidUsage(0x53), 0x37);
        // 运算符不受 numlock 影响（kcm 里 base 就是字面字符），保持 Keypad usage
        check("nonE0 0x37 (Num *)  -> 0x55", ScancodeMap.toHidUsage(0x37), 0x55);
        check("nonE0 0x4A (Num -)  -> 0x56", ScancodeMap.toHidUsage(0x4A), 0x56);
        check("nonE0 0x4E (Num +)  -> 0x57", ScancodeMap.toHidUsage(0x4E), 0x57);

        // E0 前缀键
        // 四个 E0 修饰键必须返回 -1——它们是修饰字节里的位（PC 侧 KeyMap 折算），
        // 且原槽位值超过 Usage Max 0x65
        check("E0 0x1D (RCtrl)   -> -1", ScancodeMap.toHidUsage(0xE01D), -1);
        check("E0 0x38 (RAlt)    -> -1", ScancodeMap.toHidUsage(0xE038), -1);
        check("E0 0x5B (LWin)    -> -1", ScancodeMap.toHidUsage(0xE05B), -1);
        check("E0 0x5C (RWin)    -> -1", ScancodeMap.toHidUsage(0xE05C), -1);
        check("E0 0x48 (Up)      -> 0x52", ScancodeMap.toHidUsage(0xE048), 0x52);
        check("E0 0x4B (Left)    -> 0x50", ScancodeMap.toHidUsage(0xE04B), 0x50);
        check("E0 0x53 (Delete)  -> 0x4C", ScancodeMap.toHidUsage(0xE053), 0x4C);
        check("E0 0x47 (Home)    -> 0x4A", ScancodeMap.toHidUsage(0xE047), 0x4A);
        check("E0 0x4F (End)     -> 0x4D", ScancodeMap.toHidUsage(0xE04F), 0x4D);
        check("E0 0x52 (Insert)  -> 0x49", ScancodeMap.toHidUsage(0xE052), 0x49);
        check("E0 0x1C (NumEnter)-> 0x58", ScancodeMap.toHidUsage(0xE01C), 0x58);
        check("E0 0x5D (Menu)    -> 0x65", ScancodeMap.toHidUsage(0xE05D), 0x65);

        // 修饰键必须返回 -1（它们是 mods 字节的位，不走 6 键槽位）
        check("nonE0 0x2A (LShift) -> -1", ScancodeMap.toHidUsage(0x2A), -1);
        check("nonE0 0x36 (RShift) -> -1", ScancodeMap.toHidUsage(0x36), -1);
        check("nonE0 0x1D (LCtrl)  -> -1", ScancodeMap.toHidUsage(0x1D), -1);
        check("nonE0 0x38 (LAlt)   -> -1", ScancodeMap.toHidUsage(0x38), -1);

        // 不变量：任何映射出的 usage 都必须 <= 0x65（HID 描述符按键数组的 Usage Maximum），
        // 否则设备侧解析器会静默丢弃——这正是 E0 修饰键那一轮栽过的坑
        int over = 0;
        for (int sc = 0; sc < 0x80; sc++) {
            int u = ScancodeMap.toHidUsage(sc);
            if (u > 0x65) { over++; System.out.println("  !! 0x" + Integer.toHexString(sc) + " -> 0x" + Integer.toHexString(u) + " 超过 Usage Max"); }
        }
        for (int sc = 0xE000; sc < 0xE080; sc++) {
            int u = ScancodeMap.toHidUsage(sc);
            if (u > 0x65) { over++; System.out.println("  !! 0x" + Integer.toHexString(sc) + " -> 0x" + Integer.toHexString(u) + " 超过 Usage Max"); }
        }
        check("全表 usage 均 <= 0x65", over, 0);

        // NumLock 关时，PC 侧（KeyMap.NumpadNavE0Scancode）会把数字键盘普通码翻成 E0 形态，
        // 期望设备侧这些 E0 码映到【导航】usage。下面逐条钉住这个跨语言契约——
        // 若哪天有人改了 E0 表，这里会立刻红。
        check("NumLock关 小键盘7→Home", ScancodeMap.toHidUsage(0xE047), 0x4A);
        check("NumLock关 小键盘8→Up", ScancodeMap.toHidUsage(0xE048), 0x52);
        check("NumLock关 小键盘9→PgUp", ScancodeMap.toHidUsage(0xE049), 0x4B);
        check("NumLock关 小键盘4→Left", ScancodeMap.toHidUsage(0xE04B), 0x50);
        check("NumLock关 小键盘6→Right", ScancodeMap.toHidUsage(0xE04D), 0x4F);
        check("NumLock关 小键盘1→End", ScancodeMap.toHidUsage(0xE04F), 0x4D);
        check("NumLock关 小键盘2→Down", ScancodeMap.toHidUsage(0xE050), 0x51);
        check("NumLock关 小键盘3→PgDn", ScancodeMap.toHidUsage(0xE051), 0x4E);
        check("NumLock关 小键盘0→Insert", ScancodeMap.toHidUsage(0xE052), 0x49);
        check("NumLock关 小键盘.→Delete", ScancodeMap.toHidUsage(0xE053), 0x4C);
        check("NumLock关 小键盘5→无对应", ScancodeMap.toHidUsage(0xE04C), -1);
        // 对照：NumLock 开时走普通码那一段，必须是**字面字符** usage（见上面 kcm 的理由）
        check("NumLock开 小键盘7→'7'", ScancodeMap.toHidUsage(0x47), 0x24);
        check("NumLock开 小键盘0→'0'", ScancodeMap.toHidUsage(0x52), 0x27);

        // ---- PointerPlan：相对位移的拆分（MSG_MOVE / MSG_ENTER / MSG_HOME 共用）----
        // 不变量：各条之和必须**恰好**等于目标——少一条丢位移，多一条会多发一条空报告。
        checkd("PointerPlan 条数(355,0) = 3", PointerPlan.stepCount(355, 0), 3);
        checkd("PointerPlan 条数(0,0) = 0", PointerPlan.stepCount(0, 0), 0);
        checkd("PointerPlan 条数(254,0) = 2（整倍不许多一条）", PointerPlan.stepCount(254, 0), 2);
        checkd("PointerPlan 条数(3199,1018) = 26（取较长轴）", PointerPlan.stepCount(3199, 1018), 26);
        checkd("PointerPlan 条数(-5080,-5080) = 40（= HOME 原固定 40 条）",
               PointerPlan.stepCount(-5080, -5080), 40);
        checkd("PointerPlan 和 x(3199) = 3199", sumX(3199, 1018), 3199);
        checkd("PointerPlan 和 y(1018) = 1018", sumY(3199, 1018), 1018);
        checkd("PointerPlan 和 x(-3199) = -3199", sumX(-3199, 0), -3199);
        checkd("PointerPlan 末条取余数 (3199 第 25 条) = 24", PointerPlan.stepX(3199, 25), 24);
        checkd("PointerPlan 越界返回 0 (3199 第 26 条) = 0", PointerPlan.stepX(3199, 26), 0);
        // 每条都必须落在 int8 相对轴的合法范围里，否则设备侧解析器把它当别的值
        int overstep = 0;
        int[] samples = {3199, 1018, -3199, -5080, 127, 128, 254, 1, 0, -1};
        for (int s = 0; s < samples.length; s++) {
            int v = samples[s];
            for (int i = 0; i < PointerPlan.stepCount(v, v); i++) {
                int a = PointerPlan.stepX(v, i), b = PointerPlan.stepY(v, i);
                if (a > 127 || a < -127 || b > 127 || b < -127) overstep++;
            }
        }
        checkd("PointerPlan 每条 |v| <= 127", overstep, 0);

        System.out.println("TOTAL: " + (total - fails) + "/" + total + " passed, " + fails + " failed");
        System.exit(fails);
    }

    /** 十进制打印版本（PointerPlan 的期望值写成十六进制反而看不清）。 */
    static void checkd(String name, int actual, int expected) {
        total++;
        boolean ok = actual == expected;
        if (!ok) fails++;
        System.out.println((ok ? "PASS" : "FAIL") + "  " + name + " got=" + actual + " want=" + expected);
    }

    static int sumX(int dx, int dy) {
        int s = 0;
        for (int i = 0; i < PointerPlan.stepCount(dx, dy); i++) s += PointerPlan.stepX(dx, i);
        return s;
    }

    static int sumY(int dx, int dy) {
        int s = 0;
        for (int i = 0; i < PointerPlan.stepCount(dx, dy); i++) s += PointerPlan.stepY(dy, i);
        return s;
    }
}
