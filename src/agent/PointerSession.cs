using System;
namespace PcKvm {
 public sealed class PointerSession {
  readonly Action<byte[]> _send;
  readonly object _gate=new object();
  uint _epoch,_sequence,_acked,_minSequence;
  int _x,_y,_ackX,_ackY;
  bool _ready;
  long _directGeneration;
  bool _confirmationQueued,_confirmationSent;
  public PointerSession(Action<byte[]> send) { _send=send; }
  public bool Ready { get { lock(_gate) return InputReady; } }
  bool InputReady { get { return _ready && (_directGeneration==0 || _confirmationSent); } }
  void ResetEpoch() {
   _ready=false; _confirmationQueued=false; _confirmationSent=false; _acked=0; _sequence=0;
   if(++_epoch==0) ++_epoch;
  }
  public void Reset() { lock(_gate) ResetEpoch(); }
  public void BeginDirect(long generation) { lock(_gate) { _directGeneration=generation; ResetEpoch(); } }
  public void EndDirect(long generation) {
   lock(_gate) { if(_directGeneration==generation) { _directGeneration=0; ResetEpoch(); } }
  }
  public byte[] TakeReadyConfirmation(long generation,bool heartbeatHealthy) {
   lock(_gate) {
    if(_directGeneration==0 || _directGeneration!=generation || !_ready || !heartbeatHealthy || _confirmationQueued) return null;
    _confirmationQueued=true;
    return Protocol.EncodeSessionReady(_epoch);
   }
  }
  public bool ConfirmReadySent(long generation,byte[] frame,bool heartbeatHealthy) {
   lock(_gate) {
    if(_directGeneration==0 || _directGeneration!=generation || !_ready || !heartbeatHealthy || !_confirmationQueued
      || _confirmationSent || frame.Length!=5 || frame[0]!=Protocol.MsgSessionReady || Protocol.GetU32(frame,1)!=_epoch)
     return false;
    _confirmationSent=true;
    return true;
   }
  }
  public void Configure(int width,int height,int rotation) {
   lock(_gate) { ResetEpoch(); _send(Protocol.EncodeGeometry(_epoch,(ushort)width,(ushort)height,(byte)rotation)); }
  }
  public bool ConfigureDirect(long generation,int width,int height,int rotation) {
   lock(_gate) {
    if(_directGeneration==0 || _directGeneration!=generation) return false;
    Configure(width,height,rotation);
    return true;
   }
  }
  public void Move(int x,int y) {
   lock(_gate) { if(!InputReady) return; if(x!=_x) _minSequence=_sequence+1; _x=x; _y=y; _send(Protocol.EncodePointer(_epoch,++_sequence,(ushort)x,(ushort)y)); }
  }
  public void Begin(int x,int y) { lock(_gate) { _acked=0; _minSequence=_sequence+1; Move(x,y); } }
  public bool ConfirmedX(int x) { lock(_gate) return InputReady && _acked>0 && _acked>=_minSequence && _ackX==x && _x==x; }
  public bool Confirmed(int x,int y) {
   lock(_gate) return InputReady && _acked>0 && _ackX==x && _ackY==y && _x==x && _y==y;
  }
  public void Receive(byte type,byte[] payload) { Receive(0,type,payload); }
  public void Receive(long generation,byte type,byte[] payload) {
   if(type!=Protocol.MsgPointerReady && type!=Protocol.MsgPointerAck) return;
   lock(_gate) {
    if(_directGeneration!=0 && _directGeneration!=generation) return;
    if(Protocol.GetU32(payload,0)!=_epoch) return;
    if(type==Protocol.MsgPointerReady) { _ready=true; return; }
    uint seq=Protocol.GetU32(payload,4);
    if(seq<=_acked || seq>_sequence) return;
    _acked=seq; _ackX=payload[8]|payload[9]<<8; _ackY=payload[10]|payload[11]<<8;
   }
  }
 }
}
