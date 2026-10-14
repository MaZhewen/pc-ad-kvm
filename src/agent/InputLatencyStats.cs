using System;
using System.Collections.Generic;

namespace PcKvm
{
    public sealed class LatencyValue
    {
        public readonly int Count, P95Ms, MaxMs;
        public LatencyValue(int count, int p95Ms, int maxMs)
        { Count = count; P95Ms = p95Ms; MaxMs = maxMs; }
    }

    public sealed class LatencySummary
    {
        public readonly LatencyValue Queue, PingQueue, Write, Ping, Pointer;
        public readonly int MaxPending;
        public LatencySummary(LatencyValue queue, LatencyValue pingQueue, LatencyValue write,
                              LatencyValue ping, LatencyValue pointer, int maxPending)
        { Queue = queue; PingQueue = pingQueue; Write = write; Ping = ping; Pointer = pointer; MaxPending = maxPending; }
    }

    /** Bounded, monotonic timing of already-existing protocol messages. No input content is logged. */
    public sealed class InputLatencyStats
    {
        sealed class Series
        {
            // One-millisecond bins; the final bin means 2000 ms or more.
            readonly int[] _bins = new int[2001];
            int _count, _max;
            public void Add(int ms)
            {
                ms = Math.Max(0, ms);
                _bins[Math.Min(2000, ms)]++;
                _count++;
                _max = Math.Max(_max, ms);
            }
            public LatencyValue Take()
            {
                int rank = (int)Math.Ceiling(_count * 0.95);
                int seen = 0, p95 = 0;
                for (int i = 0; i < _bins.Length && rank > 0; i++)
                {
                    seen += _bins[i];
                    if (seen >= rank) { p95 = i; break; }
                }
                LatencyValue result = new LatencyValue(_count, p95, _max);
                Array.Clear(_bins, 0, _bins.Length);
                _count = _max = 0;
                return result;
            }
        }

        readonly object _gate = new object();
        readonly long _frequency;
        readonly Series _queue = new Series(), _pingQueue = new Series(), _write = new Series();
        readonly Series _ping = new Series(), _pointer = new Series();
        readonly Dictionary<uint, long> _sentPing = new Dictionary<uint, long>();
        readonly Queue<uint> _pingOrder = new Queue<uint>();
        readonly Dictionary<ulong, long> _sentPointer = new Dictionary<ulong, long>();
        readonly Queue<ulong> _pointerOrder = new Queue<ulong>();
        int _maxPending;

        public InputLatencyStats(long frequency)
        {
            if (frequency <= 0) throw new ArgumentOutOfRangeException("frequency");
            _frequency = frequency;
        }

        int Milliseconds(long ticks) { return (int)Math.Min(int.MaxValue, Math.Max(0, ticks) * 1000.0 / _frequency); }

        public void RecordWriting(byte[] frame, long queuedAt, int pending, long now)
        {
            if (frame == null || frame.Length == 0) return;
            lock (_gate)
            {
                byte type = frame[0];
                if (type == Protocol.MsgPing && frame.Length == 5)
                {
                    _pingQueue.Add(Milliseconds(now - queuedAt));
                    uint seq = Protocol.GetU32(frame, 1);
                    _sentPing[seq] = now; _pingOrder.Enqueue(seq);
                    while (_pingOrder.Count > 16) _sentPing.Remove(_pingOrder.Dequeue());
                    return;
                }
                if (type == Protocol.MsgPointer || type == Protocol.MsgButton || type == Protocol.MsgKey
                    || type == Protocol.MsgScroll || type == Protocol.MsgMove || type == Protocol.MsgEnter
                    || type == Protocol.MsgHome || type == Protocol.MsgLeave)
                {
                    _queue.Add(Milliseconds(now - queuedAt));
                    _maxPending = Math.Max(_maxPending, pending);
                }
                if (type != Protocol.MsgPointer || frame.Length != 13) return;
                // Record every dispatched position: Android may coalesce the one that
                // would otherwise have been sampled and acknowledge a newer sequence.
                ulong key = ((ulong)Protocol.GetU32(frame, 1) << 32) | Protocol.GetU32(frame, 5);
                _sentPointer[key] = now; _pointerOrder.Enqueue(key);
                while (_pointerOrder.Count > 1024) _sentPointer.Remove(_pointerOrder.Dequeue());
            }
        }

        public void RecordWriteCompleted(long started, long finished)
        {
            lock (_gate) _write.Add(Milliseconds(finished - started));
        }

        public void RecordReceived(byte type, byte[] payload, long now)
        {
            if (payload == null) return;
            lock (_gate)
            {
                long sent;
                if (type == Protocol.MsgPong && payload.Length == 4)
                {
                    uint seq = Protocol.GetU32(payload, 0);
                    if (_sentPing.TryGetValue(seq, out sent))
                    { _sentPing.Remove(seq); _ping.Add(Milliseconds(now - sent)); }
                }
                else if (type == Protocol.MsgPointerAck && payload.Length == 12)
                {
                    ulong key = ((ulong)Protocol.GetU32(payload, 0) << 32) | Protocol.GetU32(payload, 4);
                    if (_sentPointer.TryGetValue(key, out sent))
                    { _sentPointer.Remove(key); _pointer.Add(Milliseconds(now - sent)); }
                }
            }
        }

        public LatencySummary TakeSummary()
        {
            lock (_gate) return TakeSummaryLocked();
        }

        LatencySummary TakeSummaryLocked()
        {
            LatencySummary result = new LatencySummary(_queue.Take(), _pingQueue.Take(), _write.Take(),
                _ping.Take(), _pointer.Take(), _maxPending);
            _maxPending = 0;
            return result;
        }

        void ClearPendingLocked()
        {
            _sentPing.Clear(); _pingOrder.Clear();
            _sentPointer.Clear(); _pointerOrder.Clear();
            _maxPending = 0;
        }

        public LatencySummary TakeAndResetSummary()
        {
            lock (_gate)
            {
                LatencySummary result = TakeSummaryLocked();
                ClearPendingLocked();
                return result;
            }
        }

        public void Reset()
        {
            lock (_gate)
            {
                TakeSummaryLocked();
                ClearPendingLocked();
            }
        }
    }
}
