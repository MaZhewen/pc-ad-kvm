using System;
using System.Diagnostics;
using PcKvm;

class AdbTimeoutTest
{
    static int Main()
    {
        string originalPath = Environment.GetEnvironmentVariable("PATH");
        string originalMode = Environment.GetEnvironmentVariable("PCKVM_ADB_TEST_MODE");
        try
        {
            Environment.SetEnvironmentVariable("PATH", AppDomain.CurrentDomain.BaseDirectory + ";" + originalPath);
            Environment.SetEnvironmentVariable("PCKVM_ADB_TEST_MODE", "slow");
            Stopwatch watch = Stopwatch.StartNew();
            bool success = DeviceLauncher.EnsureTunnel();
            watch.Stop();
            if (success || watch.ElapsedMilliseconds >= 6500)
                throw new Exception("slow adb was not stopped within deadline: result=" + success
                    + " elapsed=" + watch.ElapsedMilliseconds + "ms");
            Console.WriteLine("PASS slow adb fails within bounded time (" + watch.ElapsedMilliseconds + "ms)");

            Environment.SetEnvironmentVariable("PCKVM_ADB_TEST_MODE", "ready");
            if (!DeviceLauncher.DeviceVisible())
                throw new Exception("captured stdout did not contain the ready device");
            Console.WriteLine("PASS adb stdout capture still works");

            Environment.SetEnvironmentVariable("PCKVM_ADB_TEST_MODE", "flood");
            if (!DeviceLauncher.DeviceVisible())
                throw new Exception("stderr pipe blocked stdout capture");
            Console.WriteLine("PASS large stderr is drained concurrently");
            return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL " + e); return 1; }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Environment.SetEnvironmentVariable("PCKVM_ADB_TEST_MODE", originalMode);
        }
    }
}
