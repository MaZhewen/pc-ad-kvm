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
  bool _stopped;
  [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint period);
  [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint period);
  public FrameWriter(Stream stream) { _stream=stream; _thread=new Thread(Run); _thread.IsBackground=true; _thread.Start(); }
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
     lock(_gate) {
      while(!_stopped && _queue.Count==0) Monitor.Wait(_gate);
      if(_stopped) return;
      // Wait before dequeueing so new adjacent moves still replace the target.
      double remaining=4.0-(Stopwatch.GetTimestamp()-lastMove)*1000.0/Stopwatch.Frequency;
      if(remaining>0) { Monitor.Wait(_gate,Math.Max(1,(int)Math.Ceiling(remaining))); continue; }
      frame=_queue.Take();
     }
     _stream.Write(frame,0,frame.Length);
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
