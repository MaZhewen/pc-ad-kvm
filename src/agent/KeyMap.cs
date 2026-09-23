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
                if (mk == 0x5B) return 0x08;   // 左 Win
            }
            else
            {
                if (mk == 0x1D) return 0x10;   // 右 Ctrl
                if (mk == 0x38) return 0x40;   // 右 Alt
                if (mk == 0x5C) return 0x80;   // 右 Win
            }
            return 0;
        }
    }
}
