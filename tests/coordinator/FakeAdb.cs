using System;
using System.IO;
using System.Threading;

class FakeAdb
{
    static int Main(string[] args)
    {
        File.AppendAllText(Environment.GetEnvironmentVariable("PCKVM_COORD_TRACE"), String.Join(" ", args) + "\n");
        string command = String.Join(" ", args);
        if (command.StartsWith("pair "))
        {
            File.AppendAllText(Environment.GetEnvironmentVariable("PCKVM_COORD_TRACE"), "stdin=" + Console.In.ReadToEnd().Trim() + "\n");
            int pairDelay;
            if (int.TryParse(Environment.GetEnvironmentVariable("PCKVM_COORD_PAIR_DELAY_MS"), out pairDelay) && pairDelay > 0)
                Thread.Sleep(pairDelay);
            Console.WriteLine("Successfully paired");
            return 0;
        }
        string mode = Environment.GetEnvironmentVariable("PCKVM_COORD_MODE") ?? "";
        string endpoint = Environment.GetEnvironmentVariable("PCKVM_COORD_ENDPOINT") ?? "192.168.1.3:40001";
        if (command.Contains("devices -l"))
            Console.WriteLine(mode == "nonadb" || mode == "failedconnect" ? "List of devices attached"
                : "List of devices attached\n" + endpoint + " "
                  + (mode == "unauthorized" ? "unauthorized" : mode == "existingoffline" ? "offline" : "device") + " transport_id:3");
        else if (command.Contains("getprop ro.serialno"))
            Console.WriteLine(mode == "matching" ? "tablet-A" : mode == "unknownidentity" ? "unknown" : "tablet-B");
        else if (command.Contains("connect ") && (mode == "failedconnect" || mode == "existingoffline"))
        { Console.WriteLine("failed to connect"); return 1; }
        else if (command.Contains("mdns services"))
        {
            string service = Environment.GetEnvironmentVariable("PCKVM_COORD_SERVICE");
            Console.WriteLine("List of discovered mdns services");
            if (!String.IsNullOrEmpty(service)) Console.WriteLine("adb-test _adb-tls-connect._tcp. " + service);
        }
        else if (command.Contains("version")) Console.WriteLine("Android Debug Bridge version 1.0.41");
        return 0;
    }
}
