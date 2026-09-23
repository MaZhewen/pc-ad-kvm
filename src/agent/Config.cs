using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

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
    /// 默认值 = 阶段二行为：PhoneSide=Right、AllowKillAdb=false 时，
    /// 把 exe 单独拷到一台新机器上跑起来与阶段二完全一致。
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
                sb.AppendLine("; PC-KVM 配置。改完保存即可，不需要重启 exe（下次按键事件即生效）。");
                sb.AppendLine("; 删掉本文件 = 全部回到默认值。exe 单独拷走也能跑。");
                sb.AppendLine("; MouseSensitivity: " + SensitivityMin + "–" + SensitivityMax + "，默认 0.50");
                sb.AppendLine("; PhoneSide: Left | Right，默认 Right");
                sb.AppendLine("; AllowKillAdb: true | false，默认 false（会打断本机其它用 adb 的工具）");
                sb.AppendLine("; ReconnectSeconds: " + ReconnectSecondsMin + "–" + ReconnectSecondsMax + "，默认 5");
                sb.AppendLine();
                sb.AppendLine("MouseSensitivity=" + MouseSensitivity.ToString("F2", CultureInfo.InvariantCulture));
                sb.AppendLine("PhoneSide=" + (PhoneOnLeft ? "Left" : "Right"));
                sb.AppendLine("AllowKillAdb=" + (AllowKillAdb ? "true" : "false"));
                sb.AppendLine("ReconnectSeconds=" + ReconnectSeconds);
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
}
