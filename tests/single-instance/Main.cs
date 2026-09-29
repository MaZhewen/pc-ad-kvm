using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using PcKvm;

class SingleInstanceTest {
    static void Check(bool okay, string name) {
        if (!okay) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }

    static bool PumpUntil(Func<bool> done, int timeoutMs) {
        Stopwatch timer = Stopwatch.StartNew();
        while (!done() && timer.ElapsedMilliseconds < timeoutMs) {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Application.DoEvents();
        return done();
    }

    [STAThread]
    static int Main() {
        try {
            Application.EnableVisualStyles();
            string suffix = Guid.NewGuid().ToString("N");
            string mutexName = "Local\\PC-KVM-Test-" + suffix + "-Mutex";
            string requestName = "Local\\PC-KVM-Test-" + suffix + "-OpenSettings";
            string ackName = "Local\\PC-KVM-Test-" + suffix + "-RequestHandled";
            string gateName = "Local\\PC-KVM-Test-" + suffix + "-RequestGate";

            bool firstRequestAccepted = false;
            Thread firstRequester = new Thread(new ThreadStart(delegate {
                firstRequestAccepted = SingleInstanceGuard.RequestSettings(
                    requestName, ackName, gateName, 5000);
            }));
            firstRequester.IsBackground = true;
            firstRequester.Start();
            Thread.Sleep(100); // second launch starts before the primary has created named objects

            SingleInstanceGuard primary;
            Check(SingleInstanceGuard.TryAcquire(
                mutexName, requestName, ackName, gateName, out primary),
                "first launch acquires the instance mutex");
            using (primary)
            using (Form ui = new Form()) {
                IntPtr handle = ui.Handle;
                int uiThread = Thread.CurrentThread.ManagedThreadId;
                int callbacks = 0;
                bool callbackOnUiThread = true;

                SingleInstanceGuard competing;
                Check(!SingleInstanceGuard.TryAcquire(
                    mutexName, requestName, ackName, gateName, out competing)
                    && competing == null, "second launch does not acquire a duplicate instance");

                Thread.Sleep(100); // the request event is signaled before its UI listener starts
                primary.Listen(ui, delegate {
                    callbackOnUiThread &= Thread.CurrentThread.ManagedThreadId == uiThread;
                    primary.AcknowledgeSettingsRequest();
                    callbacks++;
                });
                Check(PumpUntil(delegate { return callbacks == 1; }, 1500),
                    "an early launch request is delivered after the UI listener starts");
                Check(callbackOnUiThread, "settings callback runs on the UI thread");
                Check(firstRequester.Join(1500) && firstRequestAccepted,
                    "an immediate second launch waits for named objects and receives its acknowledgment");

                bool secondAccepted = false;
                bool thirdAccepted = false;
                Thread secondRequester = new Thread(new ThreadStart(delegate {
                    secondAccepted = SingleInstanceGuard.RequestSettings(
                        requestName, ackName, gateName, 2500);
                }));
                Thread thirdRequester = new Thread(new ThreadStart(delegate {
                    thirdAccepted = SingleInstanceGuard.RequestSettings(
                        requestName, ackName, gateName, 2500);
                }));
                secondRequester.IsBackground = true;
                thirdRequester.IsBackground = true;
                secondRequester.Start();
                thirdRequester.Start();
                Check(PumpUntil(delegate { return callbacks == 3; }, 3000),
                    "concurrent launches are serialized and each reaches the UI");
                Check(secondRequester.Join(1500) && thirdRequester.Join(1500)
                    && secondAccepted && thirdAccepted,
                    "concurrent launchers receive their own acknowledgments");
            }

            SingleInstanceGuard failedStartup;
            string failedMutex = mutexName + "-Failed";
            string failedRequest = requestName + "-Failed";
            string failedAck = ackName + "-Failed";
            string failedGate = gateName + "-Failed";
            Check(SingleInstanceGuard.TryAcquire(
                failedMutex, failedRequest, failedAck, failedGate, out failedStartup),
                "a primary can start before its Settings callback is ready");
            using (failedStartup) {
                Check(!SingleInstanceGuard.RequestSettings(
                    failedRequest, failedAck, failedGate, 100),
                    "unhandled startup request does not receive false success");
            }

            SingleInstanceGuard next;
            Check(SingleInstanceGuard.TryAcquire(
                mutexName, requestName, ackName, gateName, out next),
                "mutex is released when the primary shuts down");
            next.Dispose();
            return 0;
        } catch (Exception e) {
            Console.WriteLine("FAIL " + e);
            return 1;
        }
    }
}