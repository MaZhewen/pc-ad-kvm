/** 维护按下的修饰键与 6 个按键槽位，并在变化时发出键盘报告。 */
public class KeyState {

    static int modifiers = 0;
    static int[] slots = new int[6];

    /** scancode 是 Windows 形式（低字节 MakeCode，E0 置 0xE000），此处先只处理非 E0 的普通键。 */
    public static void apply(UhidDevice dev, int scancode, boolean down, int mods) throws Exception {
        modifiers = mods & 0xFF;
        int usage = ScancodeMap.toHidUsage(scancode);
        if (usage < 0) return;   // 未映射的键直接忽略，不破坏报告

        if (down) {
            if (!contains(usage)) {
                int free = freeSlot();
                if (free >= 0) slots[free] = usage;
            }
        } else {
            for (int i = 0; i < 6; i++) if (slots[i] == usage) slots[i] = 0;
        }
        byte[] keys = new byte[6];
        for (int i = 0; i < 6; i++) keys[i] = (byte) slots[i];
        dev.sendKeyboard((byte) modifiers, keys);
    }

    static boolean contains(int usage) {
        for (int i = 0; i < 6; i++) if (slots[i] == usage) return true;
        return false;
    }

    /** 全部抬起：清修饰键与所有按键槽位，各发一条报告。 */
    public static void releaseAll(UhidDevice dev) throws Exception {
        modifiers = 0;
        for (int i = 0; i < 6; i++) slots[i] = 0;
        dev.sendKeyboard((byte) 0, new byte[6]);
    }

    static int freeSlot() {
        for (int i = 0; i < 6; i++) if (slots[i] == 0) return i;
        return -1;
    }
}
