using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace PcKvm
{
    /// <summary>TCP 服务端。单一连接，断线后自动等待重连。</summary>
    public class Transport
    {
        readonly int _port;
        TcpListener _listener;
        TcpClient _client;
        NetworkStream _stream;
        Thread _acceptThread;
        volatile bool _running;
        readonly object _sendLock = new object();

        public event Action Connected;
        public event Action Disconnected;
        public event Action<byte, byte[]> MessageReceived;   // (type, payload)

        public bool IsConnected { get { return _client != null && _client.Connected; } }

        public Transport(int port) { _port = port; }

        public bool Start()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Start();
            }
            catch (Exception)
            {
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
                try
                {
                    TcpClient c = _listener.AcceptTcpClient();
                    c.NoDelay = true;          // 输入转发对延迟敏感，禁用 Nagle
                    _client = c;
                    _stream = c.GetStream();
                    Action conn = Connected;
                    if (conn != null) conn();
                    ReadLoop();
                    Action disc = Disconnected;
                    if (disc != null) disc();
                }
                catch (Exception)
                {
                    // 对端断开或监听器被 Stop，回到等待下一次连接
                }
                finally
                {
                    try { if (_stream != null) _stream.Close(); } catch (Exception) { }
                    try { if (_client != null) _client.Close(); } catch (Exception) { }
                    _stream = null;
                    _client = null;
                }
            }
        }

        void ReadLoop()
        {
            byte[] head = new byte[1];
            while (_running)
            {
                if (!ReadExact(head, 1)) return;
                int plen = Protocol.PayloadLength(head[0]);
                if (plen < 0) return;                      // 未知类型，认为流已错位
                byte[] payload = new byte[plen];
                if (plen > 0 && !ReadExact(payload, plen)) return;
                Action<byte, byte[]> h = MessageReceived;
                if (h != null) h(head[0], payload);
            }
        }

        bool ReadExact(byte[] buf, int n)
        {
            int got = 0;
            while (got < n)
            {
                int r;
                try { r = _stream.Read(buf, got, n - got); }
                catch (Exception) { return false; }
                if (r <= 0) return false;
                got += r;
            }
            return true;
        }

        /// <summary>线程安全发送。连接不存在时静默丢弃——调用方不应因对端断开而崩溃。</summary>
        public void Send(byte[] frame)
        {
            NetworkStream s = _stream;
            if (s == null) return;
            lock (_sendLock)
            {
                try { s.Write(frame, 0, frame.Length); }
                catch (Exception) { /* 对端已断开，下一次 Connected 会重建 */ }
            }
        }

        public void Stop()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch (Exception) { }
            try { if (_client != null) _client.Close(); } catch (Exception) { }
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
