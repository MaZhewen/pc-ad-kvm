using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace PcKvm
{
    /// <summary>
    /// exe 同目录 pc-kvm.ini 的读写。解析与磁盘 I/O 分开：Parse 是纯函数（可离线测），
    /// Load/Save 才碰文件。
    ///
    /// 设计原则：**配置坏掉绝不影响可用性**。解析失败 / 键缺失 / 值越界一律回退默认值，
    /// 回退明细经 warnings 交调用方记**一行**日志。绝不弹模态框
    ///（本项目栽过"模态 MessageBox 永久阻塞"的坑）。
    ///
    /// 默认值：除 MouseSensitivity 外与阶段二一致（PhoneSide=Right、AllowKillAdb=false、
    /// ReconnectSeconds=5）。MouseSensitivity 默认 0.50 是**阶段二硬编码 1.0 的一半**——
    /// 刻意如此：用户反馈手机上光标移动太快；速度可在设置对话框里实时调整。
    /// </summary>
    public class Config
    {
        public const double SensitivityMin = 0.10;
        public const double SensitivityMax = 3.00;
        public const int ReconnectSecondsMin = 2;
        public const int ReconnectSecondsMax = 60;

        public const string FileName = "pc-kvm.ini";

        public double MouseSensitivity = 0.50;
        public bool PhoneOnLeft = false;      // false = 手机在 PC 右侧（阶段二行为）
        public bool AllowKillAdb = false;
        public int ReconnectSeconds = 5;
        public bool EnableEdgeSwitch = true;
        public HotkeyBinding SwitchHotkey = HotkeyBinding.Default;

        /// <summary>纯函数：解析 INI 文本。warnings 收到每一条回退/忽略的说明（可为 null）。</summary>
        public static Config Parse(string text, List<string> warnings)
        {
            Config c = new Config();
            if (text == null) return c;

            string[] lines = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string s = lines[i].Trim();
                if (s.Length == 0 || s[0] == ';' || s[0] == '#') continue;
                int eq = s.IndexOf('=');
                if (eq <= 0)
                {
                    Warn(warnings, "无法解析的行（已忽略）: " + s);
                    continue;
                }
                string k = s.Substring(0, eq).Trim();
                string v = s.Substring(eq + 1).Trim();

                if (Same(k, "MouseSensitivity"))
                {
                    double d;
                    // 必须用 InvariantCulture：本机是中文区，但配置文件里的小数点是句点
                    if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)
                        && d >= SensitivityMin && d <= SensitivityMax)
                        c.MouseSensitivity = d;
                    else
                        Warn(warnings, "MouseSensitivity=" + v + " 无效（需 "
                             + SensitivityMin + "–" + SensitivityMax + "），用默认 " + c.MouseSensitivity);
                }
                else if (Same(k, "PhoneSide"))
                {
                    if (Same(v, "Left")) c.PhoneOnLeft = true;
                    else if (Same(v, "Right")) c.PhoneOnLeft = false;
                    else Warn(warnings, "PhoneSide=" + v + " 无效（需 Left/Right），用默认 Right");
                }
                else if (Same(k, "AllowKillAdb"))
                {
                    bool b;
                    if (TryParseBool(v, out b)) c.AllowKillAdb = b;
                    else Warn(warnings, "AllowKillAdb=" + v + " 无效（需 true/false），用默认 false");
                }
                else if (Same(k, "ReconnectSeconds"))
                {
                    int n;
                    if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)
                        && n >= ReconnectSecondsMin && n <= ReconnectSecondsMax)
                        c.ReconnectSeconds = n;
                    else
                        Warn(warnings, "ReconnectSeconds=" + v + " 无效（需 "
                             + ReconnectSecondsMin + "–" + ReconnectSecondsMax + "），用默认 "
                             + c.ReconnectSeconds);
                }
                else if (Same(k, "SwitchHotkey"))
                {
                    HotkeyBinding binding;
                    if (HotkeyBinding.TryParse(v, out binding)) c.SwitchHotkey = binding;
                    else Warn(warnings, "SwitchHotkey=" + v + " 无效，使用默认 Ctrl+Alt+Space");
                }
                else if (Same(k, "EnableEdgeSwitch"))
                {
                    bool b;
                    if (TryParseBool(v, out b)) c.EnableEdgeSwitch = b;
                    else Warn(warnings, "EnableEdgeSwitch=" + v + " 无效，使用默认 true");
                }
                else
                {
                    Warn(warnings, "未知配置项（已忽略）: " + k);
                }
            }
            return c;
        }

        /// <summary>读 exe 同目录的 pc-kvm.ini。文件不存在 = 首次运行，直接返回默认值（不生成文件）。</summary>
        public static Config Load(Action<string> log)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
            List<string> warnings = new List<string>();
            Config c;
            try
            {
                if (!File.Exists(path)) return new Config();
                c = Parse(File.ReadAllText(path), warnings);
            }
            catch (Exception ex)
            {
                if (log != null) log("# 配置读取失败，全部用默认值：" + ex.Message);
                return new Config();
            }
            if (log != null && warnings.Count > 0)
                for (int i = 0; i < warnings.Count; i++) log("# 配置: " + warnings[i]);
            return c;
        }

        /// <summary>写回。失败返回 false（不抛）——调用方应记一行日志，但不要因此中断程序。</summary>
        public bool Save()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("; PC-KVM 配置。运行中不会重读本文件：手动改这里要重启 exe 才生效；");
                sb.AppendLine("; 在设置对话框里改则立即生效（改完自动写回本文件）。");
                sb.AppendLine("; 删掉本文件 = 全部回到默认值。exe 单独拷走也能跑。");
                sb.AppendLine("; MouseSensitivity: " + SensitivityMin + "–" + SensitivityMax + "，默认 0.50");
                sb.AppendLine("; PhoneSide: Left | Right，默认 Right");
                sb.AppendLine("; AllowKillAdb: true | false，默认 false（会打断本机其它用 adb 的工具）");
                sb.AppendLine("; ReconnectSeconds: " + ReconnectSecondsMin + "–" + ReconnectSecondsMax + "，默认 5");
                sb.AppendLine("; SwitchHotkey: Ctrl/Alt 加一个按键，可加 Shift；默认 Ctrl+Alt+Space");
                sb.AppendLine("; EnableEdgeSwitch: true | false，默认 true（双向双次贴边切换）");
                sb.AppendLine();
                sb.AppendLine("MouseSensitivity=" + MouseSensitivity.ToString("F2", CultureInfo.InvariantCulture));
                sb.AppendLine("PhoneSide=" + (PhoneOnLeft ? "Left" : "Right"));
                sb.AppendLine("AllowKillAdb=" + (AllowKillAdb ? "true" : "false"));
                sb.AppendLine("ReconnectSeconds=" + ReconnectSeconds);
                sb.AppendLine("SwitchHotkey=" + SwitchHotkey);
                sb.AppendLine("EnableEdgeSwitch=" + (EnableEdgeSwitch ? "true" : "false"));
                // 带 BOM：这个文件是给人用记事本改的，BOM 让任何编辑器都能正确识别编码
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static void Warn(List<string> warnings, string s)
        {
            if (warnings != null) warnings.Add(s);
        }

        static bool Same(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        static bool TryParseBool(string v, out bool result)
        {
            result = false;
            if (Same(v, "true") || v == "1") { result = true; return true; }
            if (Same(v, "false") || v == "0") { result = false; return true; }
            return false;
        }
    }

    /// <summary>全局快捷键配置及 INI 解析；设置窗口按键录制也使用同一验证规则。</summary>
    public sealed class HotkeyBinding
    {
        public const uint Alt = 1;
        public const uint Control = 2;
        public const uint Shift = 4;

        public uint Modifiers { get; private set; }
        public int VirtualKey { get; private set; }
        public string KeyName { get; private set; }

        HotkeyBinding(uint modifiers, int virtualKey, string keyName)
        {
            Modifiers = modifiers;
            VirtualKey = virtualKey;
            KeyName = keyName;
        }

        public static HotkeyBinding Default
        {
            get { return new HotkeyBinding(Control | Alt, 0x20, "Space"); }
        }

        public static bool TryParse(string value, out HotkeyBinding binding)
        {
            binding = null;
            if (string.IsNullOrEmpty(value)) return false;
            string[] parts = value.Split('+');
            if (parts.Length < 2 || parts.Length > 4) return false;
            uint mods = 0;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string part = parts[i].Trim();
                uint bit = 0;
                if (string.Equals(part, "Ctrl", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(part, "Control", StringComparison.OrdinalIgnoreCase)) bit = Control;
                else if (string.Equals(part, "Alt", StringComparison.OrdinalIgnoreCase)) bit = Alt;
                else if (string.Equals(part, "Shift", StringComparison.OrdinalIgnoreCase)) bit = Shift;
                if (bit == 0 || (mods & bit) != 0) return false;
                mods |= bit;
            }
            if ((mods & (Control | Alt)) == 0) return false;

            string key = parts[parts.Length - 1].Trim().ToUpperInvariant();
            int vk = 0;
            string name = null;
            if (key.Length == 1 && key[0] >= 'A' && key[0] <= 'Z')
            {
                vk = key[0]; name = key;
            }
            else if (key.Length == 1 && key[0] >= '0' && key[0] <= '9')
            {
                vk = key[0]; name = key;
            }
            else if (key.StartsWith("F", StringComparison.Ordinal))
            {
                int n;
                if (int.TryParse(key.Substring(1), out n) && n >= 1 && n <= 12)
                { vk = 0x6F + n; name = "F" + n; }
            }
            else if (key == "SPACE") { vk = 0x20; name = "Space"; }
            else if (key == "TAB") { vk = 0x09; name = "Tab"; }
            else if (key == "LEFT") { vk = 0x25; name = "Left"; }
            else if (key == "UP") { vk = 0x26; name = "Up"; }
            else if (key == "RIGHT") { vk = 0x27; name = "Right"; }
            else if (key == "DOWN") { vk = 0x28; name = "Down"; }
            else if (key == "ENTER" || key == "RETURN") { vk = 0x0D; name = "Enter"; }
            else
            {
                Keys parsed;
                if (Enum.TryParse<Keys>(key, true, out parsed)
                    && Enum.IsDefined(typeof(Keys), parsed))
                {
                    int code = (int)parsed;
                    if (code >= 0x08 && code <= 0xFE
                        && code != 0x10 && code != 0x11 && code != 0x12
                        && code != 0x1B && code != 0x5B && code != 0x5C
                        && (code < 0xA0 || code > 0xA5))
                    {
                        vk = code;
                        name = parsed.ToString();
                    }
                }
            }
            if (vk == 0) return false;  // Escape 和单独的修饰键均不可作为切换键
            binding = new HotkeyBinding(mods, vk, name);
            return true;
        }

        public override string ToString()
        {
            return ((Modifiers & Control) != 0 ? "Ctrl+" : "")
                + ((Modifiers & Alt) != 0 ? "Alt+" : "")
                + ((Modifiers & Shift) != 0 ? "Shift+" : "") + KeyName;
        }

        public bool SameAs(HotkeyBinding other)
        {
            return other != null && Modifiers == other.Modifiers && VirtualKey == other.VirtualKey;
        }

        public bool MatchesRawKey(int virtualKey, byte hidModifiers)
        {
            if (virtualKey != VirtualKey) return false;
            bool ctrl = (hidModifiers & (0x01 | 0x10)) != 0;
            bool alt = (hidModifiers & (0x04 | 0x40)) != 0;
            bool shift = (hidModifiers & (0x02 | 0x20)) != 0;
            bool win = (hidModifiers & (0x08 | 0x80)) != 0;
            return ctrl == ((Modifiers & Control) != 0)
                && alt == ((Modifiers & Alt) != 0)
                && shift == ((Modifiers & Shift) != 0) && !win;
        }

        public static bool TryFromKeyData(Keys keyData, out HotkeyBinding binding)
        {
            Keys key = keyData & Keys.KeyCode;
            string keyName = key >= Keys.D0 && key <= Keys.D9
                ? ((int)key - (int)Keys.D0).ToString(CultureInfo.InvariantCulture)
                : key.ToString();
            string text = ((keyData & Keys.Control) != 0 ? "Ctrl+" : "")
                + ((keyData & Keys.Alt) != 0 ? "Alt+" : "")
                + ((keyData & Keys.Shift) != 0 ? "Shift+" : "") + keyName;
            return TryParse(text, out binding);
        }
    }
}
