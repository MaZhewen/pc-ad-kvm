using System;
using System.Collections.Generic;
using System.IO;
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
        c = P("MouseSensitivity=0.35\n");
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

        Console.WriteLine("TOTAL: pass=" + _pass + " fail=" + _fail);
        if (_fail > 0) Environment.Exit(1);
    }
}
