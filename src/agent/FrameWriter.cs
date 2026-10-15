using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
namespace PcKvm {
 // Per-connection writer: UI input never blocks on socket writes. Frames cannot
 // leak to a new connection; overflow disconnects instead of losing key releases.
 sealed class FrameWriter {
  readonly Stream _stream;
  readonly FrameQueue _queue=new FrameQueue(256);
  readonly object _gate=new object();
  readonly Thread _thread;
  readonly Action<byte[],long,int,long> _onWriting;
  readonly Action<long,long> _onWritten;
  bool _stopped;
  [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint period);
  [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint period);
  public FrameWriter(Stream stream) : this(stream,null,null) { }
  public FrameWriter(Stream stream,Action<byte[],long,int,long> onWriting,Action<long,long> onWritten) {
   _stream=stream; _onWriting=onWriting; _onWritten=onWritten;
   _thread=new Thread(Run); _thread.IsBackground=true; _thread.Start();
  }
  public void Send(byte[] frame) {
   bool overflow=false;
   lock(_gate) { if(_stopped) return; overflow=!_queue.Add(frame); Monitor.Pulse(_gate); }
   if(overflow) Stop();
  }
  public void SendControl(byte[] frame) {
   bool overflow=false;
   lock(_gate) { if(_stopped) return; overflow=!_queue.AddControl(frame); Monitor.Pulse(_gate); }
   if(overflow) Stop();
  }
  void Run() {
   bool timer=timeBeginPeriod(1)==0;
   long lastMove=0;
   try {
    while(true) {
     byte[] frame;
     long queuedAt;
     int pending;
     lock(_gate) {
      while(!_stopped && _queue.Count==0) Monitor.Wait(_gate);
      if(_stopped) return;
      // Pace isolated positions before dequeueing so adjacent moves can replace them.
      // A following barrier wakes this wait and flushes the position first.
      int wait=_queue.GetDispatchWaitMilliseconds(lastMove,Stopwatch.GetTimestamp(),Stopwatch.Frequency);
      if(wait>0) { Monitor.Wait(_gate,wait); continue; }
      frame=_queue.Take(out queuedAt);
      pending=_queue.Count;
     }
     if(_onWriting!=null) {
      try { _onWriting(frame,queuedAt,pending,Stopwatch.GetTimestamp()); }
      catch(Exception) { /* diagnostics cannot interrupt input */ }
     }
     long writeStarted=Stopwatch.GetTimestamp();
     try { _stream.Write(frame,0,frame.Length); }
     finally {
      if(_onWritten!=null) {
       try { _onWritten(writeStarted,Stopwatch.GetTimestamp()); }
       catch(Exception) { /* diagnostics cannot interrupt input */ }
      }
     }
     if(frame[0]==Protocol.MsgPointer) lastMove=Stopwatch.GetTimestamp();
    }
   } catch(Exception) { try { _stream.Close(); } catch(Exception) {} }
   finally { if(timer) timeEndPeriod(1); }
  }
  public void Stop() {
   lock(_gate) { _stopped=true; _queue.Clear(); Monitor.PulseAll(_gate); }
   try { _stream.Close(); } catch(Exception) {}
   if(Thread.CurrentThread!=_thread) _thread.Join(2000);
  }
 }
}
