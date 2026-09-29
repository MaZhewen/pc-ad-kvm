using System;

namespace PcKvm
{
    /** Monotonic, generation-bound PING/PONG lease. */
    public sealed class HeartbeatLease
    {
        readonly long _frequency;
        readonly object _gate = new object();
        long _generation, _lastPong;
        uint _sent, _acked;
        bool _connected;

        public HeartbeatLease(long frequency)
        {
            if (frequency <= 0) throw new ArgumentOutOfRangeException("frequency");
            _frequency = frequency;
        }
        public void Connected(long generation, long now)
        {
            lock (_gate) { _generation = generation; _lastPong = now; _sent = _acked = 0; _connected = true; }
        }
        public void Disconnected(long generation)
        {
            lock (_gate) if (_generation == generation) _connected = false;
        }
        public void Sent(uint sequence)
        {
            lock (_gate) if (_connected) _sent = sequence;
        }
        public bool Pong(long generation, uint sequence, long now)
        {
            lock (_gate)
            {
                if (!_connected || generation != _generation || sequence <= _acked || sequence > _sent) return false;
                _acked = sequence; _lastPong = now;
                return true;
            }
        }
        public bool Healthy(long now)
        {
            lock (_gate) return _connected && _acked > 0 && now - _lastPong <= 2 * _frequency;
        }
        public bool Expired(long now)
        {
            lock (_gate) return _connected && now - _lastPong > 2 * _frequency;
        }
    }
}
