using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using PcKvm;

class DirectBootstrapTests
{
    const string Hardware = "real-tablet";
    static readonly string Token = new string('a', 64), Pin = new string('b', 64);

    sealed class AdbFixture
    {
        internal readonly AdbTarget Target = AdbTarget.ForTransport(17, "10.0.0.7:23456");
        internal readonly List<string> Commands = new List<string>();
        internal string Config, Failure, CancelOn;
        internal string Serial = Hardware;
        internal readonly CancellationTokenSource Cancel = new CancellationTokenSource();
        readonly string _jar;

        internal AdbFixture(string jar) { _jar = jar; }

        internal AdbResult Execute(string[] args, AdbTarget target, int timeout, string stdin, CancellationToken cancel)
        {
            if (target != Target) throw new Exception("Bootstrap changed its selected target");
            string command = String.Join(" ", args);
            Commands.Add(command);
            if (command.Contains(Token) || command.Contains(Pin)) throw new Exception("Credentials leaked into command");
            var result = new AdbResult();
            if (command == "shell getprop ro.serialno") result.Stdout = Serial + "\n";
            if (command.Contains("settings get global adb_enabled")) result.Stdout = "1\n";
            if (command.Contains("cat >")) Config = stdin;
            if (command.Contains("sha256sum"))
            {
                byte[] data = command.EndsWith("/config") ? Encoding.ASCII.GetBytes(Config) : File.ReadAllBytes(_jar);
                using (SHA256 sha = SHA256.Create()) result.Stdout = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "") + "  file\n";
            }
            if (Failure != null && command.Contains(Failure)) result.ExitCode = 1;
            if (CancelOn != null && command.Contains(CancelOn)) Cancel.Cancel(); // Remote command reports a late success.
            return result;
        }

        internal bool Called(string text) { return Commands.Exists(command => command.Contains(text)); }
    }

    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }

    static void Run(DirectBootstrap bootstrap, string jar, AdbFixture adb)
    {
        bootstrap.Start(jar, IPAddress.Parse("10.0.0.2"), 1234, Pin, Token, adb.Cancel.Token);
    }

    static void DeploymentTests()
    {
        string jar = Path.GetTempFileName();
        try
        {
            File.WriteAllText(jar, "fixture");
            var normal = new AdbFixture(jar);
            var bootstrap = new DirectBootstrap(normal.Execute, normal.Target, Hardware);
            Run(bootstrap, jar, normal);
            Check(normal.Config == DirectBootstrap.FormatConfig("10.0.0.2", 1234, Pin, Token, bootstrap.InstanceId)
                && normal.Commands[normal.Commands.Count - 2].Contains("DirectOwner publish")
                && normal.Commands[normal.Commands.Count - 1].Contains("nohup setsid"),
                "verified files precede owner publication and detached startup");

            var mismatch = new AdbFixture(jar) { Serial = "other-device" };
            try { Run(new DirectBootstrap(mismatch.Execute, mismatch.Target, Hardware), jar, mismatch); throw new Exception("Wrong device deployed"); }
            catch (IOException) { }
            Check(mismatch.Commands.Count == 1, "verified hardware identity gates every upload");

            foreach (string stage in new[] { "push ", "cat >", "chmod 600", "sha256sum", "DirectOwner publish", "nohup setsid" })
            {
                var failure = new AdbFixture(jar) { Failure = stage };
                try { Run(new DirectBootstrap(failure.Execute, failure.Target, Hardware), jar, failure); throw new Exception("Ignored failure " + stage); }
                catch (IOException) { }
                bool published = stage == "DirectOwner publish" || stage == "nohup setsid";
                Check(published ? failure.Called("DirectOwner stop") && failure.Called("DirectOwner cleanup")
                    : !failure.Called("DirectOwner publish") && failure.Called("rmdir /data/local/tmp/pckvm-direct-"),
                    "owned rollback at " + stage);
            }

            foreach (string stage in new[] { "cat >", "DirectOwner publish", "nohup setsid" })
            {
                var canceled = new AdbFixture(jar) { CancelOn = stage };
                try { Run(new DirectBootstrap(canceled.Execute, canceled.Target, Hardware), jar, canceled); throw new Exception("Ignored late cancellation"); }
                catch (OperationCanceledException) { }
                Check(stage == "cat >" ? !canceled.Called("DirectOwner publish") && canceled.Called("rmdir ")
                    : canceled.Called("DirectOwner cleanup") && (stage == "nohup setsid" || !canceled.Called("nohup setsid")),
                    "cancellation rejects late success at " + stage);
            }
        }
        finally { File.Delete(jar); }
    }
    static int Main()
    {
        try {
            Check(DirectBootstrap.ParseWifiAddress("10.0.0.0/24 dev wlan0 proto kernel scope link src 10.0.0.7 \n") == "10.0.0.7", "Wi-Fi route selects source address");
            Check(DirectBootstrap.ParseWifiAddress("default dev tun0 src 10.8.0.1\n10.0.0.0/24 dev wlan0 src 10.0.0.7") == "10.0.0.7", "VPN route does not replace Wi-Fi source");
            Check(DirectBootstrap.ParseWifiAddress("default dev rmnet0 src 10.0.0.9") == "", "no Wi-Fi fails explicitly");
            string token = new string('a', 64), pin = new string('b', 64), id = new string('c', 32);
            string config = DirectBootstrap.FormatConfig("10.0.0.2", 1234, pin, token, id);
            Check(config == "1\n10.0.0.2\n1234\n" + pin + "\n" + token + "\n" + id + "\n", "bootstrap config matches Android contract");
            bool rejected = false;
            try { DirectBootstrap.FormatConfig("10.0.0.2; reboot", 1234, pin, token, id); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "configuration rejects shell injection");
            string command = DirectBootstrap.StartCommand(id);
            Check(command.Contains("nohup setsid") && command.Contains("</dev/null") && command.Contains("2>&1 &"), "startup detaches all standard streams and session");
            Check(!command.Contains(token) && !command.Contains(pin), "no bootstrap secrets in process command line");
            Check(command.Contains("/pckvm-direct-" + id + "/config") && !command.Contains("mv "),
                "daemon starts from its private directory after owner publication");
            string stop = DirectBootstrap.StopCommand(id);
            Check(stop.Contains("/pckvm-direct-" + id + "/stop") && !stop.Contains("pckvm-direct.owner"),
                "stop is isolated to the current instance");
            Check(Protocol.PayloadLength(0x10) == 5 && Protocol.PayloadLength(0x11) == 0, "direct display and stop protocol lengths");
            DeploymentTests();
            return 0;
        } catch(Exception e) { Console.WriteLine("FAIL " + e); return 1; }
    }
}
