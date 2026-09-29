using System;
using System.IO;

class FakeAdb
{
    static int Main(string[] args)
    {
        File.AppendAllText(Environment.GetEnvironmentVariable("PCKVM_COORD_TRACE"), String.Join(" ", args) + "\n");
        string command = String.Join(" ", args);
        if (command.Contains("devices -l"))
            Console.WriteLine("List of devices attached\n192.168.1.3:40001 device transport_id:3");
        else if (command.Contains("getprop ro.serialno")) Console.WriteLine("tablet-B");
        else if (command.Contains("version")) Console.WriteLine("Android Debug Bridge version 1.0.41");
        return 0;
    }
}
