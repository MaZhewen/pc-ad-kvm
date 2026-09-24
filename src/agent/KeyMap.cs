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

        /// <summary>
        /// NumLock **关**时，数字键盘的普通 scancode 应改送哪个 scancode。
        ///
        /// 实测依据（2026-09-23 真机日志，铁证）：物理数字键盘**不受 NumLock 影响，
        /// 恒发同一个普通 scancode**——小键盘 7 是 0x47，而导航区那颗独立的 Home 才是 0xE047。
        /// 是 Windows 依据 NumLock 把 0x47 解释成 NUMPAD7 还是 HOME。
        /// 原 ScancodeMap 注释写成"NumLock 关时同样这些物理键发 E0 前缀"是**错的**，
        /// 于是 0x47→0x5F 无条件生效 → 手机永远小键盘模式 → 与 PC 键盘的 NumLock 灯相反。
        ///
        /// 修法：把"NumLock 关"的小键盘码翻成它**自己的 E0 形态**，交给设备侧既有的
        /// E0 表去映成 Home/PgUp/… —— 那张表的"关"列与导航键区本来就逐项相同，
        /// 于是**设备侧零改动**。返回的是 scancode 而非 usage，因为线上协议送的就是 scancode。
        ///
        /// 返回 0 = 该键在 NumLock 关时无 HID 对应（小键盘 5 = Clear），调用方**丢弃**。
        /// 返回 -1 = 不受 NumLock 影响，调用方**原样发送**。
        /// </summary>
        public static int NumpadNavE0Scancode(int mk)
        {
            switch (mk)
            {
                case 0x47:   // 7 → Home
                case 0x48:   // 8 → Up
                case 0x49:   // 9 → PgUp
                case 0x4B:   // 4 → Left
                case 0x4D:   // 6 → Right
                case 0x4F:   // 1 → End
                case 0x50:   // 2 → Down
                case 0x51:   // 3 → PgDn
                case 0x52:   // 0 → Insert
                case 0x53:   // . → Delete
                    return 0xE000 | mk;
                case 0x4C:   // 5（NumLock 关）= Clear，HID 无对应
                    return 0;
                default:     // 0x37 * / 0x4A - / 0x4E + / 0x35 斜杠：不受 NumLock 影响
                    return -1;
            }
        }
    }
}
