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

        Console.WriteLine("TOTAL: pass=" + _pass + " fail=" + _fail);
        if (_fail > 0) Environment.Exit(1);
    }
}
