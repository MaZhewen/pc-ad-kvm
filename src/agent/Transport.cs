using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace PcKvm
{
    /// <summary>TCP 服务端。单一连接，断线后自动等待重连。</summary>
    public class Transport
    {
        const uint HandleFlagInherit = 0x00000001;
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
        readonly int _port;
        TcpListener _listener;
        TcpClient _client;
        TcpClient _candidate;
        NetworkStream _stream;
        FrameWriter _writer;
        Thread _acceptThread;
        volatile bool _running;
        readonly object _sendLock = new object();
        byte[] _sessionToken;
        long _generation;
        volatile bool _active;

        public event Action Connected;
        public event Action Disconnected;
        public event Action<byte, byte[]> MessageReceived;   // (type, payload)
        public event Action<long> SessionConnected;
        public event Action<long> SessionDisconnected;
        public event Action<long, byte, byte[]> SessionMessageReceived;

        public bool IsConnected { get { return _active; } }

        public void ConfigureSession(byte[] token, long generation)
        {
            if (token == null || token.Length != 32) throw new ArgumentException("Session token must be 32 bytes", "token");
            CloseSession();
            lock (_sendLock)
            {
                _sessionToken = (byte[])token.Clone();
                _generation = generation;
            }
        }

        public void CloseSession()
        {
            TcpClient client, candidate;
            lock (_sendLock)
            {
                _active = false;
                client = _client;
                candidate = _candidate;
            }
            try { if (client != null) client.Close(); } catch (Exception) { }
            try { if (candidate != null) candidate.Close(); } catch (Exception) { }
        }

        public Transport(int port) { _port = port; }

        public int ListeningPort { get; private set; }
        public Exception StartError { get; private set; }

        public bool Start()
        {
            ListeningPort = 0;
            StartError = null;
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _port);
                try { _listener.Start(); }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode != SocketError.AddressAlreadyInUse) throw;
                    _listener.Stop();
                    // Keep the device port fixed; adb reverse targets this allocated PC port.
                    _listener = new TcpListener(IPAddress.Loopback, 0);
                    _listener.Start();
                }
                ListeningPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
                // adb may fork its server after the listener is created. Never let
                // that child retain this socket after the PC process exits.
                if (!SetHandleInformation(_listener.Server.Handle, HandleFlagInherit, 0))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            catch (Exception e)
            {
                StartError = e;
                if (_listener != null) _listener.Stop();
                return false;
            }
            _running = true;
            _acceptThread = new Thread(AcceptLoop);
            _acceptThread.IsBackground = true;
            _acceptThread.Start();
            return true;
        }

        void AcceptLoop()
        {
            while (_running)
            {
                TcpClient c = null;
                NetworkStream stream = null;
                FrameWriter writer = null;
                bool authenticated = false;
                long generation = 0;
                try
                {
                    c = _listener.AcceptTcpClient();
                    c.NoDelay = true;          // 输入转发对延迟敏感，禁用 Nagle
                    c.SendTimeout = 500;
                    stream = c.GetStream();
                    byte[] token;
                    lock (_sendLock)
                    {
                        _candidate = c;
                        token = _sessionToken == null ? null : (byte[])_sessionToken.Clone();
                        generation = _generation;
                    }
                    if (token != null && !VerifyPreamble(stream, token)) continue;
                    lock (_sendLock)
                    {
                        if (!_running || _generation != generation || !Object.ReferenceEquals(_candidate, c)) continue;
                        if (token != null) stream.WriteByte(1);
                        _candidate = null;
                        _client = c;
                        _stream = stream;
                        writer = new FrameWriter(stream);
                        _writer = writer;
                        _active = true;
                        authenticated = true;
                    }
                    Action<long> generationConnected = SessionConnected;
                    if (generationConnected != null) generationConnected(generation);
                    Action conn = Connected;
                    if (conn != null) conn();
                    ReadLoop(stream, generation);
                }
                catch (Exception)
                {
                    // 对端断开或监听器被 Stop，回到等待下一次连接
                }
                finally
                {
                    lock (_sendLock)
                    {
                        if (Object.ReferenceEquals(_candidate, c)) _candidate = null;
                        if (Object.ReferenceEquals(_client, c))
                        {
                            _active = false;
                            _writer = null; _client = null; _stream = null;
                        }
                    }
                    if (writer != null) writer.Stop();
                    try { if (stream != null) stream.Close(); } catch (Exception) { }
                    try { if (c != null) c.Close(); } catch (Exception) { }
                    if (authenticated)
                    {
                        Action<long> generationDisconnected = SessionDisconnected;
                        try { if (generationDisconnected != null) generationDisconnected(generation); } catch (Exception) { }
                        Action disc = Disconnected;
                        try { if (disc != null) disc(); } catch (Exception) { }
                    }
                }
            }
        }

        static bool VerifyPreamble(NetworkStream stream, byte[] token)
        {
            byte[] preamble = new byte[37];
            Stopwatch watch = Stopwatch.StartNew();
            int got = 0;
            while (got < preamble.Length)
            {
                int remaining = 2000 - (int)watch.ElapsedMilliseconds;
                if (remaining <= 0) return false;
                stream.ReadTimeout = remaining;
                int count = stream.Read(preamble, got, preamble.Length - got);
                if (count <= 0) return false;
                got += count;
            }
            if (preamble[0] != 'P' || preamble[1] != 'K' || preamble[2] != 'V' || preamble[3] != 'M' || preamble[4] != 1)
                return false;
            int mismatch = 0;
            for (int i = 0; i < token.Length; i++) mismatch |= preamble[5 + i] ^ token[i];
            if (mismatch != 0) return false;
            stream.ReadTimeout = System.Threading.Timeout.Infinite;
            return true;
        }

        void ReadLoop(NetworkStream stream, long generation)
        {
            byte[] head = new byte[1];
            while (_running)
            {
                if (!ReadExact(stream, head, 1)) return;
                int plen = Protocol.PayloadLength(head[0]);
                if (plen < 0) return;                      // 未知类型，认为流已错位
                byte[] payload = new byte[plen];
                if (plen > 0 && !ReadExact(stream, payload, plen)) return;
                if (!_active || generation != _generation) return;
                Action<long, byte, byte[]> sg = SessionMessageReceived;
                if (sg != null) sg(generation, head[0], payload);
                Action<byte, byte[]> h = MessageReceived;
                if (h != null) h(head[0], payload);
            }
        }

        static bool ReadExact(NetworkStream stream, byte[] buf, int n)
        {
            int got = 0;
            while (got < n)
            {
                int r;
                try { r = stream.Read(buf, got, n - got); }
                catch (Exception) { return false; }
                if (r <= 0) return false;
                got += r;
            }
            return true;
        }

        /// <summary>线程安全发送。连接不存在时静默丢弃——调用方不应因对端断开而崩溃。</summary>
        public void Send(byte[] frame)
        {
            FrameWriter writer = _writer;
            if (writer != null) writer.Send(frame);
        }

        public void SendControl(byte[] frame)
        {
            FrameWriter writer = _writer;
            if (writer != null) writer.SendControl(frame);
        }

        public void Stop()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch (Exception) { }
            CloseSession();
            // 有界等待 accept 线程收尾：它上面的 Connected/Disconnected 处理器会写日志，
            // 而 TrayUi 的退出流程在本方法之后才 Close 日志——不 Join 就存在
            // "后台线程写已关闭 StreamWriter" 的未处理异常 = 进程被杀而非干净退出。
            // 不会死锁：监听器/客户端已关，阻塞中的 AcceptTcpClient/Read 立刻抛出退出循环；
            // 也不会自 Join：Stop 只从 UI 线程（TrayUi）调用，绝非 accept 线程自身。
            if (_acceptThread != null && _acceptThread != Thread.CurrentThread)
                _acceptThread.Join(2000);
        }
    }
}
