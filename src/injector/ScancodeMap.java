/** Windows scancode → HID Usage ID（Keyboard/Keypad page 0x07）。 */
public class ScancodeMap {

    /** 返回 -1 表示未映射。scancode 低字节为 MakeCode；E0 前缀键（低字节前带 0xE0）单走一张表。 */
    public static int toHidUsage(int scancode) {
        boolean e0 = (scancode & 0xE000) == 0xE000;
        int mk = scancode & 0xFF;

        if (e0) {
            switch (mk) {
                case 0x1C: return 0x58;   // 小键盘 Enter
                // ↓ 四个 E0 修饰键必须返回 -1（Task 9 审查 Important #1/#2 + Minor #1 修正）：
                // 它们是修饰字节里的位，不是按键槽。原表把它们映射成槽位 usage
                // 0xE4/0xE6/0xE3/0xE7，而那些值**超过了 HID 描述符按键数组的
                // Usage Maximum（0x65）**，解析器直接丢弃 → 既进不了修饰字节、
                // 又白占一个槽位（按住右 Ctrl+右 Alt 再按 5 个键，第 6 个会被静默丢）。
                case 0x1D: return -1;     // 右 Ctrl（修饰位 0x10，由 PC 侧折算）
                case 0x35: return 0x54;   // 小键盘 /
                case 0x37: return 0x46;   // PrintScreen
                case 0x38: return -1;     // 右 Alt（修饰位 0x40，由 PC 侧折算）
                case 0x47: return 0x4A;   // Home
                case 0x48: return 0x52;   // Up
                case 0x49: return 0x4B;   // PgUp
                case 0x4B: return 0x50;   // Left
                case 0x4D: return 0x4F;   // Right
                case 0x4F: return 0x4D;   // End
                case 0x50: return 0x51;   // Down
                case 0x51: return 0x4E;   // PgDn
                case 0x52: return 0x49;   // Insert
                case 0x53: return 0x4C;   // Delete
                case 0x5B: return -1;     // 左 Win（真机左 Win 就是 E0 0x5B！修饰位 0x08）
                case 0x5C: return -1;     // 右 Win（修饰位 0x80）
                case 0x5D: return 0x65;   // Menu（0x65 恰在 Usage Max 上，合法）
                default:   return -1;
            }
        }

        // 非 E0 表。修饰键（0x2A/0x36 左右 Shift、0x1D 左 Ctrl、0x38 左 Alt、0x5B 左 Win）
        // 刻意不在此表：它们是报告里的修饰位而非按键槽位，由 PC 侧 KeyMap.ModifierBit
        // 折算进 mods 字节随 MSG_KEY 下发，这里返回 -1 走"忽略"路径。
        switch (mk) {
            case 0x01: return 0x29;   // Esc
            case 0x02: return 0x1E;   // 1
            case 0x03: return 0x1F;   // 2
            case 0x04: return 0x20;   // 3
            case 0x05: return 0x21;   // 4
            case 0x06: return 0x22;   // 5
            case 0x07: return 0x23;   // 6
            case 0x08: return 0x24;   // 7
            case 0x09: return 0x25;   // 8
            case 0x0A: return 0x26;   // 9
            case 0x0B: return 0x27;   // 0
            case 0x0C: return 0x2D;   // -
            case 0x0D: return 0x2E;   // =
            case 0x0E: return 0x2A;   // Backspace
            case 0x0F: return 0x2B;   // Tab
            case 0x10: return 0x14;   // Q
            case 0x11: return 0x1A;   // W
            case 0x12: return 0x08;   // E
            case 0x13: return 0x15;   // R
            case 0x14: return 0x17;   // T
            case 0x15: return 0x1C;   // Y
            case 0x16: return 0x18;   // U
            case 0x17: return 0x0C;   // I
            case 0x18: return 0x12;   // O
            case 0x19: return 0x13;   // P
            case 0x1A: return 0x2F;   // [
            case 0x1B: return 0x30;   // ]
            case 0x1C: return 0x28;   // Enter
            case 0x1E: return 0x04;   // A
            case 0x1F: return 0x16;   // S
            case 0x20: return 0x07;   // D
            case 0x21: return 0x09;   // F
            case 0x22: return 0x0A;   // G
            case 0x23: return 0x0B;   // H
            case 0x24: return 0x0D;   // J
            case 0x25: return 0x0E;   // K
            case 0x26: return 0x0F;   // L
            case 0x27: return 0x33;   // ;
            case 0x28: return 0x34;   // '
            case 0x29: return 0x35;   // `
            case 0x2B: return 0x31;   // backslash
            case 0x2C: return 0x1D;   // Z
            case 0x2D: return 0x1B;   // X
            case 0x2E: return 0x06;   // C
            case 0x2F: return 0x19;   // V
            case 0x30: return 0x05;   // B
            case 0x31: return 0x11;   // N
            case 0x32: return 0x10;   // M
            case 0x33: return 0x36;   // ,
            case 0x34: return 0x37;   // .
            case 0x35: return 0x38;   // /
            case 0x39: return 0x2C;   // Space
            case 0x3A: return 0x39;   // CapsLock
            case 0x3B: return 0x3A; case 0x3C: return 0x3B; case 0x3D: return 0x3C;
            case 0x3E: return 0x3D; case 0x3F: return 0x3E; case 0x40: return 0x3F;
            case 0x41: return 0x40; case 0x42: return 0x41; case 0x43: return 0x42;
            case 0x44: return 0x43;                       // F1-F10
            case 0x57: return 0x44; case 0x58: return 0x45;   // F11, F12
            case 0x45: return 0x53;   // NumLock
            case 0x46: return 0x48;   // ScrollLock

            // ↓ 数字键盘（NumLock 开着时的形态；NumLock 关时同样这些物理键发 E0 前缀，
            // 走上面 E0 表的 Home/End/箭头那一组）。原来整段缺失 → 返回 -1 → 注入器静默丢弃。
            // 真机缺陷现场：接管期内用户按的 6 个键全是这一段（0x52 0x52 0x47 0x52 0x47 0x52
            // = 小键盘 0 0 7 0 7 0），手机上毫无反应；用户平时就用小键盘打数字
            //（早先会话实测 0x50 0x52 0x50 0x4D = "2026"）。
            // 这 15 个 usage（0x55-0x63）都在 HID 描述符按键数组的 Usage Maximum 0x65 之内，
            // 不会被解析器丢弃（对比：E0 修饰键那轮就是因为映射到 0xE3+ 而整段失效）。
            case 0x37: return 0x55;   // 小键盘 *
            case 0x47: return 0x5F;   // 小键盘 7
            case 0x48: return 0x60;   // 小键盘 8
            case 0x49: return 0x61;   // 小键盘 9
            case 0x4A: return 0x56;   // 小键盘 -
            case 0x4B: return 0x5C;   // 小键盘 4
            case 0x4C: return 0x5D;   // 小键盘 5
            case 0x4D: return 0x5E;   // 小键盘 6
            case 0x4E: return 0x57;   // 小键盘 +
            case 0x4F: return 0x59;   // 小键盘 1
            case 0x50: return 0x5A;   // 小键盘 2
            case 0x51: return 0x5B;   // 小键盘 3
            case 0x52: return 0x62;   // 小键盘 0
            case 0x53: return 0x63;   // 小键盘 .
            default:   return -1;
        }
    }
}
