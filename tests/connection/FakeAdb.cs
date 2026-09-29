using System;
using System.IO;
using System.Threading;

class FakeAdb
{
    static int Main(string[] args)
    {
        string mode = Environment.GetEnvironmentVariable("PCKVM_FAKE_MODE");
        if (mode == "slow") { Thread.Sleep(5000); return 0; }
        if (mode == "existing-reverse" && Array.IndexOf(args, "--list") >= 0)
        { Console.WriteLine("USB-123 tcp:27183 tcp:27183"); return 0; }
        if (mode == "flood")
        {
            for (int i = 0; i < 10000; i++) { Console.WriteLine("stdout long line 1234567890"); Console.Error.WriteLine("stderr long line 1234567890"); }
            return 0;
        }
        string path = Environment.GetEnvironmentVariable("PCKVM_FAKE_CAPTURE");
        using (StreamWriter writer = new StreamWriter(path, false))
        {
            for (int i = 0; i < args.Length; i++) writer.WriteLine("arg" + i + "=" + args[i]);
            if (args.Length > 0 && args[0] == "pair") writer.WriteLine("stdin=" + Console.In.ReadLine());
        }
        Console.WriteLine("ok");
        return 0;
    }
}
