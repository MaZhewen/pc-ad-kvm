namespace PcKvm
{
    /// <summary>
    /// 纯 scancode → HID 修饰位映射表。Ruling 26：Program.cs 已 315 行、红线 360，
    /// 纯映射不进 Program.cs；独立文件也便于离线测试（本类只用内建类型，无 I/O）。
    /// </summary>
    public static class KeyMap
    {
        /// <summary>把 Windows scancode 映射为 HID 修饰位（不是普通键）。返回 0 表示不是修饰键。</summary>
        public static byte ModifierBit(int scancode, bool isE0)
        {
            int mk = scancode & 0xFF;
            if (!isE0)
            {
                if (mk == 0x2A) return 0x02;   // 左 Shift
                if (mk == 0x36) return 0x20;   // 右 Shift
                if (mk == 0x1D) return 0x01;   // 左 Ctrl
                if (mk == 0x38) return 0x04;   // 左 Alt
                if (mk == 0x5B) return 0x08;   // 左 Win（历史形态；现代键盘走下面的 E0 分支）
            }
            else
            {
                // Task 9 审查 Important #1 修正：**真机的左 Win 就是 E0 0x5B**，
                // 原表只写了非 E0 的 0x5B（死条目）、E0 分支又漏了 0x5B，导致
                // 按左 Win 时 LGUI 位永不置位 → 手机上左 Win 完全无效。
                if (mk == 0x5B) return 0x08;   // 左 Win（真机形态 E0 0x5B）
                if (mk == 0x1D) return 0x10;   // 右 Ctrl
                if (mk == 0x38) return 0x40;   // 右 Alt
                if (mk == 0x5C) return 0x80;   // 右 Win
            }
            return 0;
        }
    }
}
