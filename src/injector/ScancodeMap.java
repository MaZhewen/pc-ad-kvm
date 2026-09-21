/** Windows scancode → HID Usage ID（Keyboard/Keypad page 0x07）。 */
public class ScancodeMap {

    /** 返回 -1 表示未映射。scancode 低字节为 MakeCode。 */
    public static int toHidUsage(int scancode) {
        int mk = scancode & 0xFF;
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
            default:   return -1;
        }
    }
}
