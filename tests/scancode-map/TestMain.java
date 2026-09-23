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

        // 数字键盘（NumLock 开）——本次新增。
        // 真机缺陷现场：接管期内用户按的 6 个键全是这一段（0x52 0x52 0x47 0x52 0x47 0x52
        // = 小键盘 0 0 7 0 7 0），而这一段在非 E0 表里整段缺失 → 返回 -1 → 注入器静默丢弃
        // → 手机上毫无反应。用户平时就用小键盘打数字（早先会话实测 0x50 0x52 0x50 0x4D = "2026"）。
        check("nonE0 0x37 (Num *)  -> 0x55", ScancodeMap.toHidUsage(0x37), 0x55);
        check("nonE0 0x47 (Num 7)  -> 0x5F", ScancodeMap.toHidUsage(0x47), 0x5F);
        check("nonE0 0x48 (Num 8)  -> 0x60", ScancodeMap.toHidUsage(0x48), 0x60);
        check("nonE0 0x49 (Num 9)  -> 0x61", ScancodeMap.toHidUsage(0x49), 0x61);
        check("nonE0 0x4A (Num -)  -> 0x56", ScancodeMap.toHidUsage(0x4A), 0x56);
        check("nonE0 0x4B (Num 4)  -> 0x5C", ScancodeMap.toHidUsage(0x4B), 0x5C);
        check("nonE0 0x4C (Num 5)  -> 0x5D", ScancodeMap.toHidUsage(0x4C), 0x5D);
        check("nonE0 0x4D (Num 6)  -> 0x5E", ScancodeMap.toHidUsage(0x4D), 0x5E);
        check("nonE0 0x4E (Num +)  -> 0x57", ScancodeMap.toHidUsage(0x4E), 0x57);
        check("nonE0 0x4F (Num 1)  -> 0x59", ScancodeMap.toHidUsage(0x4F), 0x59);
        check("nonE0 0x50 (Num 2)  -> 0x5A", ScancodeMap.toHidUsage(0x50), 0x5A);
        check("nonE0 0x51 (Num 3)  -> 0x5B", ScancodeMap.toHidUsage(0x51), 0x5B);
        check("nonE0 0x52 (Num 0)  -> 0x62", ScancodeMap.toHidUsage(0x52), 0x62);
        check("nonE0 0x53 (Num .)  -> 0x63", ScancodeMap.toHidUsage(0x53), 0x63);

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
        // 对照：NumLock 开时走普通码那一段，必须是数字键盘 usage
        check("NumLock开 小键盘7→KP7", ScancodeMap.toHidUsage(0x47), 0x5F);
        check("NumLock开 小键盘0→KP0", ScancodeMap.toHidUsage(0x52), 0x62);

        System.out.println("TOTAL: " + (total - fails) + "/" + total + " passed, " + fails + " failed");
        System.exit(fails);
    }
}
