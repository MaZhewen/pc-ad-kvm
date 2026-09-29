using System.Collections.Generic;
namespace PcKvm {
 // Caller holds its queue lock. Only adjacent positions can be coalesced.
 public sealed class FrameQueue {
  readonly int _capacity;
  readonly LinkedList<byte[]> _items = new LinkedList<byte[]>();
  public FrameQueue(int capacity) { _capacity=capacity; }
  public int Count { get { return _items.Count; } }
  public bool Add(byte[] frame) {
   if(frame[0]==Protocol.MsgPointer && _items.Last!=null && _items.Last.Value[0]==Protocol.MsgPointer) {
    _items.Last.Value=frame; return true;
   }
   if(_items.Count>=_capacity) return false;
   _items.AddLast(frame); return true;
  }
  public bool AddControl(byte[] frame) {
   byte[] pendingGeometry=null;
   if(frame[0]==Protocol.MsgLeave && _capacity>=2) {
    for(var node=_items.First;node!=null;node=node.Next)
     if(node.Value[0]==Protocol.MsgGeometry) pendingGeometry=node.Value;
   }
   _items.Clear();
   _items.AddLast(frame);
   if(pendingGeometry!=null) _items.AddLast(pendingGeometry);
   return true;
  }
  public byte[] Take() { if(_items.First==null) return null; var f=_items.First.Value; _items.RemoveFirst(); return f; }
  public void Clear() { _items.Clear(); }
 }
}
