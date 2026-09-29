using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace PcKvm
{
    /// <summary>
    /// Elects one UI process and forwards each later launch request to its UI thread.
    /// A per-request acknowledgment prevents stale startup or shutdown signals from
    /// being mistaken for a request handled by the current primary.
    /// </summary>
    sealed class SingleInstanceGuard : IDisposable
    {
        readonly Mutex _mutex;
        readonly Mutex _requestGate;
        readonly EventWaitHandle _openSettings;
        readonly EventWaitHandle _requestHandled;
        RegisteredWaitHandle _registeredWait;
        Control _uiTarget;
        Action _openSettingsCallback;
        volatile bool _disposed;

        SingleInstanceGuard(Mutex mutex, Mutex requestGate,
                            EventWaitHandle openSettings, EventWaitHandle requestHandled)
        {
            _mutex = mutex;
            _requestGate = requestGate;
            _openSettings = openSettings;
            _requestHandled = requestHandled;
        }

        public static bool TryAcquire(string mutexName, string eventName,
                                      string acknowledgmentName, string requestGateName,
                                      out SingleInstanceGuard guard)
        {
            guard = null;
            bool createdNew;
            Mutex mutex = new Mutex(true, mutexName, out createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                return false;
            }

            Mutex requestGate = null;
            EventWaitHandle openSettings = null;
            EventWaitHandle requestHandled = null;
            try
            {
                openSettings = new EventWaitHandle(false,
                    EventResetMode.AutoReset, eventName);
                requestHandled = new EventWaitHandle(false,
                    EventResetMode.AutoReset, acknowledgmentName);
                // Publish the gate last: its existence means both request events are ready.
                requestGate = new Mutex(false, requestGateName);
                guard = new SingleInstanceGuard(mutex, requestGate,
                    openSettings, requestHandled);
                return true;
            }
            catch
            {
                if (requestHandled != null) requestHandled.Dispose();
                if (openSettings != null) openSettings.Dispose();
                if (requestGate != null) requestGate.Dispose();
                try { mutex.ReleaseMutex(); }
                finally { mutex.Dispose(); }
                throw;
            }
        }
        /// <summary>Retry object discovery, serialize launch requests, and wait for this request's UI acknowledgment.</summary>
        public static bool RequestSettings(string eventName, string acknowledgmentName,
                                           string requestGateName, int timeoutMilliseconds)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            Mutex requestGate = null;
            bool ownsRequestGate = false;
            try
            {
                do
                {
                    try
                    {
                        requestGate = Mutex.OpenExisting(requestGateName);
                    }
                    catch (WaitHandleCannotBeOpenedException) { }
                    catch (ObjectDisposedException) { }

                    if (requestGate != null) break;
                    if (elapsed.ElapsedMilliseconds >= timeoutMilliseconds) return false;
                    Thread.Sleep(50);
                }
                while (elapsed.ElapsedMilliseconds <= timeoutMilliseconds);

                if (requestGate == null) return false;
                try
                {
                    int remaining = Math.Max(0,
                        timeoutMilliseconds - (int)elapsed.ElapsedMilliseconds);
                    ownsRequestGate = requestGate.WaitOne(remaining, false);
                }
                catch (AbandonedMutexException)
                {
                    ownsRequestGate = true;
                }
                if (!ownsRequestGate) return false;

                using (EventWaitHandle requestHandled =
                    EventWaitHandle.OpenExisting(acknowledgmentName))
                using (EventWaitHandle openSettings =
                    EventWaitHandle.OpenExisting(eventName))
                {
                    // Clear an acknowledgment left behind if a previous caller exited early.
                    requestHandled.Reset();
                    if (!openSettings.Set()) return false;

                    int remaining = Math.Max(0,
                        timeoutMilliseconds - (int)elapsed.ElapsedMilliseconds);
                    return requestHandled.WaitOne(remaining, false);
                }
            }
            catch (WaitHandleCannotBeOpenedException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            finally
            {
                if (ownsRequestGate)
                {
                    try { requestGate.ReleaseMutex(); }
                    catch (ApplicationException) { }
                }
                if (requestGate != null) requestGate.Dispose();
            }
        }
        public void AcknowledgeSettingsRequest()
        {
            if (_disposed) return;
            _requestHandled.Set();
        }

        public void Listen(Control uiTarget, Action openSettingsCallback)
        {
            if (_disposed) throw new ObjectDisposedException("SingleInstanceGuard");
            if (uiTarget == null) throw new ArgumentNullException("uiTarget");
            if (openSettingsCallback == null)
                throw new ArgumentNullException("openSettingsCallback");
            if (_registeredWait != null)
                throw new InvalidOperationException("Settings listener is already registered.");

            _uiTarget = uiTarget;
            _openSettingsCallback = openSettingsCallback;
            _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                _openSettings,
                delegate(object state, bool timedOut) { DispatchToUiThread(); },
                null, Timeout.Infinite, false);
        }

        void DispatchToUiThread()
        {
            if (_disposed) return;
            Control target = _uiTarget;
            if (target == null || target.IsDisposed) return;

            try
            {
                target.BeginInvoke((MethodInvoker)delegate
                {
                    if (_disposed || target.IsDisposed) return;
                    Action callback = _openSettingsCallback;
                    if (callback != null) callback();
                });
            }
            catch (InvalidOperationException) { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            RegisteredWaitHandle registeredWait = _registeredWait;
            _registeredWait = null;
            if (registeredWait != null) registeredWait.Unregister(null);

            try { _openSettings.Dispose(); }
            finally
            {
                try { _requestHandled.Dispose(); }
                finally
                {
                    try { _requestGate.Dispose(); }
                    finally
                    {
                        try { _mutex.ReleaseMutex(); }
                        finally { _mutex.Dispose(); }
                    }
                }
            }
        }
    }
}