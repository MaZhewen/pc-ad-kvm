using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Threading;

namespace PcKvm
{
    /// <summary>Authenticated TLS bridge to the unchanged loopback input and clipboard protocols.</summary>
    public sealed class DirectNetworkGateway : IDisposable
    {
        const int HandshakeTimeoutMs = 5000;
        const int MaxPending = 4;
        const byte Success = 0, InvalidFormat = 1, Unauthorized = 2, Duplicate = 3, StaleSession = 4, Unavailable = 5;
        const int Input = 0, Clipboard = 1;
        readonly object _gate = new object();
        readonly TcpListener _listener;
        readonly int?[] _localPorts;
        readonly byte[] _instanceId, _token;
        readonly Func<long> _beginInput;
        readonly Action<long, byte[]> _inputFrameSent;
        readonly HashSet<Connection> _connections = new HashSet<Connection>();
        readonly Connection[] _active = new Connection[2];
        readonly DirectTlsCertificate _tls;
        bool _started, _disposed, _accepting = true;
        volatile bool _versionMismatch, _inputAuthenticated;
        int _pending;

        sealed class Connection
        {
            internal TcpClient Remote;
            internal TcpClient Local;
            internal Timer Deadline;
            internal bool Ready;
            internal bool Admitted;
            internal bool Closed;
            internal int Channel = -1;
            internal long Generation;
            internal byte[] SessionId;
        }

        public DirectNetworkGateway(IPAddress bindAddress, int inputLocalPort, int? clipboardLocalPort, string instanceId,
                                    byte[] token, Func<long> beginInput = null, Action<long, byte[]> inputFrameSent = null)
        {
            if (bindAddress == null || bindAddress.AddressFamily != AddressFamily.InterNetwork || bindAddress.Equals(IPAddress.Any))
                throw new ArgumentException("An IPv4 bind address is required", "bindAddress");
            if (inputLocalPort < 1 || inputLocalPort > 65535 || clipboardLocalPort < 1 || clipboardLocalPort > 65535)
                throw new ArgumentOutOfRangeException("inputLocalPort", "Loopback ports must be between 1 and 65535");
            if (token == null || token.Length != 32) throw new ArgumentException("Session token must be 32 bytes", "token");
            _instanceId = ParseInstanceId(instanceId);
            _token = (byte[])token.Clone();
            _beginInput = beginInput;
            _inputFrameSent = inputFrameSent;
            _localPorts = new int?[] { inputLocalPort, clipboardLocalPort };
            _listener = new TcpListener(bindAddress, 0);
            try { _tls = new DirectTlsCertificate(); }
            catch
            {
                _listener.Stop();
                Array.Clear(_token, 0, _token.Length);
                throw;
            }
        }

        public int Port { get; private set; }
        public string CertificateSha256 { get { return _tls.Sha256; } }
        public bool VersionMismatch { get { return _versionMismatch; } }
        public bool InputAuthenticated { get { return _inputAuthenticated; } }

        public void Start()
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException("DirectNetworkGateway");
                if (_started || !_accepting) return;
                _listener.Start(16);
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _started = true;
                new Thread(AcceptLoop) { IsBackground = true, Name = "PcKvm TLS listener" }.Start();
            }
        }

        void AcceptLoop()
        {
            try
            {
                while (true)
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    Stopwatch accepted = Stopwatch.StartNew();
                    client.NoDelay = true;
                    Connection connection = new Connection { Remote = client };
                    lock (_gate)
                    {
                        int remaining = HandshakeTimeoutMs - (int)accepted.ElapsedMilliseconds;
                        if (!_accepting || _pending >= MaxPending || remaining <= 0) { client.Close(); continue; }
                        _connections.Add(connection);
                        _pending++;
                        connection.Deadline = new Timer(delegate { Expire(connection); }, null, remaining, Timeout.Infinite);
                    }
                    new Thread(delegate() { Serve(connection); }) { IsBackground = true, Name = "PcKvm TLS candidate" }.Start();
                }
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        void Expire(Connection connection)
        {
            lock (_gate)
            {
                if (connection.Ready) return;
                CloseLocked(connection);
            }
        }

        void Serve(Connection connection)
        {
            SslStream secure = null;
            try
            {
                secure = new SslStream(connection.Remote.GetStream(), false);
                secure.ReadTimeout = HandshakeTimeoutMs;
                secure.WriteTimeout = HandshakeTimeoutMs;
                secure.AuthenticateAsServer(_tls.Certificate, false, SslProtocols.Tls12, false);
                byte[] prefix = new byte[6];
                ReadFully(secure, prefix);
                if (prefix[0] != 'D' || prefix[1] != 'K' || prefix[2] != 'V' || prefix[3] != 'M'
                    || prefix[4] != 2 || prefix[5] < 1 || prefix[5] > 2)
                {
                    if (prefix[0] == 'D' && prefix[1] == 'K' && prefix[2] == 'V' && prefix[3] == 'M' && prefix[4] != 2)
                        _versionMismatch = true;
                    Respond(secure, InvalidFormat, null);
                    return;
                }
                byte[] credentials = new byte[64];
                ReadFully(secure, credentials);
                int channel = prefix[5] - 1;
                byte status;
                TcpClient local = null;
                int localPort = 0;
                lock (_gate)
                {
                    if (!_accepting || connection.Closed) return;
                    status = AdmitLocked(connection, channel, credentials);
                    if (status == Success)
                    {
                        local = new TcpClient(AddressFamily.InterNetwork);
                        connection.Local = local;
                        local.NoDelay = true;
                        localPort = _localPorts[channel].Value;
                    }
                }
                if (status != Success)
                {
                    Respond(secure, status, null);
                    return;
                }
                if (channel == Input && _beginInput != null)
                {
                    connection.Generation = _beginInput();
                    if (connection.Generation == 0)
                    {
                        Respond(secure, Unavailable, null);
                        return;
                    }
                }
                try { local.Connect(IPAddress.Loopback, localPort); }
                catch (SocketException)
                {
                    Respond(secure, Unavailable, null);
                    return;
                }
                lock (_gate)
                {
                    if (connection.Closed) return;
                    connection.Admitted = true;
                }
                Respond(secure, Success, connection.SessionId);
                if (channel == Input) _inputAuthenticated = true;
                lock (_gate)
                {
                    if (connection.Closed) return;
                    connection.Ready = true;
                    _pending--;
                    connection.Deadline.Dispose();
                    connection.Deadline = null;
                }
                secure.ReadTimeout = Timeout.Infinite;
                secure.WriteTimeout = Timeout.Infinite;
                NetworkStream backend = local.GetStream();
                new Thread(delegate()
                {
                    if (channel == Input && _inputFrameSent != null) CopyInputDownstream(backend, secure, connection);
                    else Copy(backend, secure, connection);
                }) { IsBackground = true, Name = "PcKvm TLS downstream" }.Start();
                Copy(secure, backend, connection);
            }
            catch (IOException) { }
            catch (SocketException) { }
            catch (AuthenticationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            finally
            {
                lock (_gate) CloseLocked(connection);
                if (secure != null) secure.Dispose();
            }
        }

        byte AdmitLocked(Connection connection, int channel, byte[] credentials)
        {
            if (!Matches(credentials, 0, _instanceId) | !Matches(credentials, 16, _token)) return Unauthorized;
            if (channel == Input)
            {
                if (!Matches(credentials, 48, EmptySession)) return InvalidFormat;
                if (_active[Input] != null) return Duplicate;
                connection.SessionId = new byte[16];
                using (var random = RandomNumberGenerator.Create())
                {
                    do { random.GetBytes(connection.SessionId); }
                    while (Matches(connection.SessionId, 0, EmptySession));
                }
            }
            else
            {
                if (!_localPorts[Clipboard].HasValue) return Unavailable;
                Connection input = _active[Input];
                if (input == null || !input.Admitted || !Matches(credentials, 48, input.SessionId)) return StaleSession;
                if (_active[Clipboard] != null) return Duplicate;
                connection.SessionId = input.SessionId;
            }
            connection.Channel = channel;
            _active[channel] = connection;
            return Success;
        }

        static readonly byte[] EmptySession = new byte[16];

        static bool Matches(byte[] source, int offset, byte[] expected)
        {
            int mismatch = 0;
            for (int i = 0; i < expected.Length; i++) mismatch |= source[offset + i] ^ expected[i];
            return mismatch == 0;
        }

        static byte[] ParseInstanceId(string instanceId)
        {
            if (instanceId == null || instanceId.Length != 32) throw new ArgumentException("Invalid instance ID", "instanceId");
            var bytes = new byte[16];
            for (int i = 0; i < bytes.Length; i++)
            {
                int high = HexDigit(instanceId[i * 2]), low = HexDigit(instanceId[i * 2 + 1]);
                if (high < 0 || low < 0) throw new ArgumentException("Invalid instance ID", "instanceId");
                bytes[i] = (byte)((high << 4) | low);
            }
            return bytes;
        }

        static int HexDigit(char value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            value = char.ToLowerInvariant(value);
            return value >= 'a' && value <= 'f' ? value - 'a' + 10 : -1;
        }

        static void Respond(Stream stream, byte status, byte[] sessionId)
        {
            var response = new byte[17];
            response[0] = status;
            if (sessionId != null) Buffer.BlockCopy(sessionId, 0, response, 1, 16);
            stream.Write(response, 0, response.Length);
            stream.Flush();
        }

        static void ReadFully(Stream stream, byte[] bytes, int offset = 0)
        {
            while (offset < bytes.Length)
            {
                int count = stream.Read(bytes, offset, bytes.Length - offset);
                if (count == 0) throw new EndOfStreamException();
                offset += count;
            }
        }

        void CopyInputDownstream(Stream backend, Stream secure, Connection connection)
        {
            try
            {
                var source = new BufferedStream(backend);
                int acknowledgement = source.ReadByte();
                if (acknowledgement != 1) return;
                secure.WriteByte((byte)acknowledgement);
                int type;
                while ((type = source.ReadByte()) >= 0)
                {
                    int length = Protocol.PayloadLength((byte)type);
                    if (length < 0) return;
                    byte[] frame = new byte[length + 1];
                    frame[0] = (byte)type;
                    ReadFully(source, frame, 1);
                    secure.Write(frame, 0, frame.Length);
                    if (type == Protocol.MsgSessionReady || type == Protocol.MsgStopDaemon)
                        try { _inputFrameSent(connection.Generation, frame); }
                        catch (Exception) { /* a delivery observer cannot interrupt the TLS stream */ }
                }
            }
            catch (IOException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            finally { lock (_gate) CloseLocked(connection); }
        }

        void Copy(Stream source, Stream destination, Connection connection)
        {
            try
            {
                byte[] bytes = new byte[16384];
                int count;
                while ((count = source.Read(bytes, 0, bytes.Length)) != 0)
                    destination.Write(bytes, 0, count);
            }
            catch (IOException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            finally { lock (_gate) CloseLocked(connection); }
        }

        void CloseLocked(Connection connection)
        {
            if (connection.Closed) return;
            connection.Closed = true;
            if (!connection.Ready) _pending--;
            if (connection.Deadline != null) { connection.Deadline.Dispose(); connection.Deadline = null; }
            // Close the sockets first so neither TLS reads nor writes can delay shutdown.
            connection.Remote.Close();
            if (connection.Local != null) connection.Local.Close();
            if (connection.Channel >= 0 && _active[connection.Channel] == connection)
            {
                _active[connection.Channel] = null;
                if (connection.Channel == Input && _active[Clipboard] != null) CloseLocked(_active[Clipboard]);
            }
            _connections.Remove(connection);
        }

        public void StopAccepting()
        {
            lock (_gate)
            {
                if (!_accepting) return;
                _accepting = false;
                _listener.Stop();
                Connection[] connections = new Connection[_connections.Count];
                _connections.CopyTo(connections);
                foreach (Connection connection in connections)
                    if (!connection.Ready) CloseLocked(connection);
            }
        }

        public void SetClipboardPort(int? port)
        {
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException("port");
            lock (_gate)
            {
                if (_disposed || _localPorts[Clipboard] == port) return;
                _localPorts[Clipboard] = port;
                if (_active[Clipboard] != null) CloseLocked(_active[Clipboard]);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _accepting = false;
                _listener.Stop();
                Connection[] connections = new Connection[_connections.Count];
                _connections.CopyTo(connections);
                foreach (Connection connection in connections) CloseLocked(connection);
                Array.Clear(_token, 0, _token.Length);
                _tls.Dispose();
            }
        }
    }
}
