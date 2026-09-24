using System;
namespace PcKvm {
 public sealed class PointerSession {
  readonly Action<byte[]> _send;
  readonly object _gate=new object();
  uint _epoch,_sequence,_acked,_minSequence;
  int _x,_y,_ackX,_ackY;
  bool _ready;
  public PointerSession(Action<byte[]> send) { _send=send; }
  public bool Ready { get { lock(_gate) return _ready; } }
  public void Reset() { lock(_gate) { _ready=false; _acked=0; _sequence=0; ++_epoch; } }
  public void Configure(int width,int height,int rotation) {
   lock(_gate) { ++_epoch; _ready=false; _sequence=0; _acked=0; _send(Protocol.EncodeGeometry(_epoch,(ushort)width,(ushort)height,(byte)rotation)); }
  }
  public void Move(int x,int y) {
   lock(_gate) { if(!_ready) return; if(x!=_x) _minSequence=_sequence+1; _x=x; _y=y; _send(Protocol.EncodePointer(_epoch,++_sequence,(ushort)x,(ushort)y)); }
  }
  public void Begin(int x,int y) { lock(_gate) { _acked=0; _minSequence=_sequence+1; Move(x,y); } }
  public bool ConfirmedX(int x) { lock(_gate) return _ready && _acked>0 && _acked>=_minSequence && _ackX==x && _x==x; }
  public bool Confirmed(int x,int y) {
   lock(_gate) return _ready && _acked>0 && _ackX==x && _ackY==y && _x==x && _y==y;
  }
  public void Receive(byte type,byte[] payload) {
   if(type!=Protocol.MsgPointerReady && type!=Protocol.MsgPointerAck) return;
   lock(_gate) {
    if(Protocol.GetU32(payload,0)!=_epoch) return;
    if(type==Protocol.MsgPointerReady) { _ready=true; return; }
    uint seq=Protocol.GetU32(payload,4);
    if(seq<=_acked || seq>_sequence) return;
    _acked=seq; _ackX=payload[8]|payload[9]<<8; _ackY=payload[10]|payload[11]<<8;
   }
  }
 }
}
