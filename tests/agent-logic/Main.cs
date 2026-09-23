using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using PcKvm;

static class AgentLogicTest
{
    static int _pass = 0, _fail = 0;

    static void Check(string name, bool ok, string detail)
    {
        if (ok) { _pass++; Console.WriteLine("PASS " + name + "  " + detail); }
        else { _fail++; Console.WriteLine("FAIL " + name + "  " + detail); }
    }

    static Config P(string text)
    {
        List<string> w = new List<string>();
        return Config.Parse(text, w);
    }

    static void Main()
    {
        // ---- C1: 空文本 / null → 全默认 ----
        Config c = P("");
        Check("C1 空文本=默认", c.MouseSensitivity == 0.50 && !c.PhoneOnLeft
            && !c.AllowKillAdb && c.ReconnectSeconds == 5,
            "sens=" + c.MouseSensitivity + " left=" + c.PhoneOnLeft
            + " kill=" + c.AllowKillAdb + " rec=" + c.ReconnectSeconds);

        // ---- C2: 完整读取 ----
        c = P("MouseSensitivity=1.25\nPhoneSide=Left\nAllowKillAdb=true\nReconnectSeconds=12\n");
        Check("C2 完整读取", c.MouseSensitivity == 1.25 && c.PhoneOnLeft
            && c.AllowKillAdb && c.ReconnectSeconds == 12,
            "sens=" + c.MouseSensitivity + " left=" + c.PhoneOnLeft
            + " kill=" + c.AllowKillAdb + " rec=" + c.ReconnectSeconds);

        // ---- C3: 注释、空行、空白、大小写、CRLF ----
        c = P("; 注释\r\n\r\n  mouseSENSITIVITY = 0.75  \r\n# 另一种注释\r\nphoneside=right\r\n");
        Check("C3 注释/空白/大小写/CRLF", c.MouseSensitivity == 0.75 && !c.PhoneOnLeft,
            "sens=" + c.MouseSensitivity + " left=" + c.PhoneOnLeft);

        // ---- C4: 越界与非法值一律回退默认，且**不影响其它项** ----
        List<string> w4 = new List<string>();
        c = Config.Parse("MouseSensitivity=99\nReconnectSeconds=abc\nPhoneSide=Left\n", w4);
        Check("C4 坏值回退且不牵连", c.MouseSensitivity == 0.50 && c.ReconnectSeconds == 5
            && c.PhoneOnLeft && w4.Count == 2,
            "sens=" + c.MouseSensitivity + " rec=" + c.ReconnectSeconds
            + " left=" + c.PhoneOnLeft + " warnings=" + w4.Count);

        // ---- C5: 小数用不变文化解析（避免逗号小数点地区踩坑） ----
        // 本机 locale 是 zh-CN，小数分隔符恰好也是 "."，直接解析无法证伪——若有人
        // 误删 Config.Parse 里的 InvariantCulture 参数，本用例在这台机器上仍会绿，
        // 而逗号小数地区的用户会静默拿到默认值。故先切到逗号小数文化 de-DE 再解析：
        // 只有真正走了 InvariantCulture 才能把 "0.35" 读成 0.35（否则会被当作
        // 35 → 越界回退 0.50），用例随之转红。
        CultureInfo prevCulture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        c = P("MouseSensitivity=0.35\n");
        Thread.CurrentThread.CurrentCulture = prevCulture;
        Check("C5 小数点为句点", c.MouseSensitivity == 0.35, "sens=" + c.MouseSensitivity);

        // ---- C6: 未知键被忽略但不致命 ----
        List<string> w6 = new List<string>();
        c = Config.Parse("NotAKey=1\nMouseSensitivity=2.00\n", w6);
        Check("C6 未知键忽略", c.MouseSensitivity == 2.00 && w6.Count == 1,
            "sens=" + c.MouseSensitivity + " warnings=" + w6.Count);

        // ---- C7: 边界值恰好合法 ----
        Check("C7 边界合法", P("MouseSensitivity=0.10\n").MouseSensitivity == 0.10
            && P("MouseSensitivity=3.00\n").MouseSensitivity == 3.00
            && P("ReconnectSeconds=2\n").ReconnectSeconds == 2
            && P("ReconnectSeconds=60\n").ReconnectSeconds == 60,
            "0.10/3.00/2/60 均应被接受");

        // ---- C8: 恰好越界的边界值要被拒 ----
        Check("C8 边界越界被拒", P("MouseSensitivity=0.09\n").MouseSensitivity == 0.50
            && P("MouseSensitivity=3.01\n").MouseSensitivity == 0.50
            && P("ReconnectSeconds=1\n").ReconnectSeconds == 5
            && P("ReconnectSeconds=61\n").ReconnectSeconds == 5,
            "0.09/3.01/1/61 均应回退");

        // ---- C9: Save() → Load() 写回往返（spec §11 的缺口，R10 补上）----
        // Save/Load 用 AppDomain.CurrentDomain.BaseDirectory——对本 harness 是
        // %TEMP%\pckvm-tests\agent-logic\ 输出目录，所以完全自包含。
        Config c9 = new Config();
        c9.MouseSensitivity = 1.25;
        c9.PhoneOnLeft = true;
        c9.AllowKillAdb = true;
        c9.ReconnectSeconds = 12;
        bool saved9 = c9.Save();
        Config c9b = Config.Load(null);
        Check("C9 Save/Load 写回往返", saved9
            && c9b.MouseSensitivity == 1.25 && c9b.PhoneOnLeft
            && c9b.AllowKillAdb && c9b.ReconnectSeconds == 12,
            "saved=" + saved9 + " sens=" + c9b.MouseSensitivity + " left=" + c9b.PhoneOnLeft
            + " kill=" + c9b.AllowKillAdb + " rec=" + c9b.ReconnectSeconds);

        // ---- C10: Save() 写出的文件是带 BOM 的 UTF-8 且含预期键 ----
        string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Config.FileName);
        bool bom10 = false, keys10 = false;
        try
        {
            byte[] head = new byte[3];
            using (FileStream fs = File.OpenRead(iniPath))
            {
                int n = fs.Read(head, 0, 3);
                bom10 = n == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
            }
            string text10 = File.ReadAllText(iniPath);
            keys10 = text10.Contains("MouseSensitivity=1.25") && text10.Contains("PhoneSide=Left");
        }
        catch (Exception) { }
        Check("C10 写盘 UTF-8 BOM + 预期键", bom10 && keys10,
            "bom=" + bom10 + " keys=" + keys10);
        // 清理 C9 写下的 ini（C10 还要用它，所以删在最后），保证重跑从零开始
        try { File.Delete(iniPath); } catch (Exception) { }

        // ---- S1: 系数 1.0 时逐位透传 ----
        MouseScaler ms = new MouseScaler(1.0);
        Check("S1 系数1透传", ms.ApplyX(7) == 7 && ms.ApplyX(-3) == -3 && ms.ApplyY(11) == 11,
            "7/-3/11");

        // ---- S2: 低系数下小位移【不丢】——这正是原实现 (int)(dx*s) 的病 ----
        // 系数 0.5 时连推 3 次 dx=1 应累计出 1 像素（1*0.5=0.5，第三次到 1.5 → 1，
        // 余 0.5）。原写法 (int)(1*0.5)=0 → 三次全丢，表现为"慢速走不动"。
        ms = new MouseScaler(0.5);
        int a1 = ms.ApplyX(1), a2 = ms.ApplyX(1), a3 = ms.ApplyX(1);
        Check("S2 低系数小位移不丢", a1 == 0 && a2 == 1 && a3 == 0,
            "三次 dx=1 @0.5 → " + a1 + "/" + a2 + "/" + a3 + "（期望 0/1/0）");

        // ---- S3: 反向对称（负方向同样累积，不能单向吞掉） ----
        ms = new MouseScaler(0.5);
        int b1 = ms.ApplyX(-1), b2 = ms.ApplyX(-1);
        Check("S3 负方向对称", b1 == 0 && b2 == -1,
            "-1/-1 @0.5 → " + b1 + "/" + b2 + "（期望 0/-1）");

        // ---- S4: 两轴独立 ----
        ms = new MouseScaler(0.5);
        ms.ApplyX(1); ms.ApplyX(1);          // X 余量到 1
        Check("S4 两轴互不干扰", ms.ApplyY(1) == 0,
            "X 累积不应影响 Y");

        // ---- S5: 高系数（放大） ----
        ms = new MouseScaler(2.0);
        Check("S5 放大", ms.ApplyX(3) == 6 && ms.ApplyY(-4) == -8, "3*2=6 / -4*2=-8");

        // ---- S6: Reset 清掉小数余量 ----
        ms = new MouseScaler(0.5);
        ms.ApplyX(1);                         // 余 0.5
        ms.Reset();
        Check("S6 Reset 清残差", ms.ApplyX(1) == 0,
            "Reset 后单次 dx=1 @0.5 应为 0（若残差未清会是 1）");

        // ---- S7: SetSensitivity 立即生效 ----
        ms = new MouseScaler(0.5);
        ms.SetSensitivity(2.0);
        Check("S7 改系数立即生效", ms.ApplyX(3) == 6, "改为 2.0 后 3*2=6");

        // ---- S8: 长时间小位移不漂移（累积误差必须闭合） ----
        // 200 次 dx=1 @0.5 恰好应发出 100 像素，一个不多一个不少。
        ms = new MouseScaler(0.5);
        int sum = 0;
        for (int i = 0; i < 200; i++) sum += ms.ApplyX(1);
        Check("S8 长期不漂移", sum == 100, "200 次 dx=1 @0.5 共发 " + sum + "（期望 100）");

        Console.WriteLine("TOTAL: pass=" + _pass + " fail=" + _fail);
        if (_fail > 0) Environment.Exit(1);
    }
}
