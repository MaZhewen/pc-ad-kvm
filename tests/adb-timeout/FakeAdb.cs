using System;
using System.Threading;

class FakeAdb
{
    static void Main()
    {
        if (Environment.GetEnvironmentVariable("PCKVM_ADB_TEST_MODE") == "slow")
        {
            Thread.Sleep(7500);
            return;
        }
        if (Environment.GetEnvironmentVariable("PCKVM_ADB_TEST_MODE") == "flood")
            Console.Error.Write(new string('x', 131072));
        Console.WriteLine("List of devices attached");
        Console.WriteLine("test-serial\tdevice");
    }
}
