using System;
using System.Collections.Generic;
using System.Diagnostics;
namespace PcKvm {
 // Caller holds its queue lock. Only adjacent positions can be coalesced.
 public sealed class FrameQueue {
  sealed class Entry { public byte[] Frame; public long QueuedAt; public Entry(byte[] frame,long queuedAt) { Frame=frame; QueuedAt=queuedAt; } }
  readonly int _capacity;
  readonly LinkedList<Entry> _items = new LinkedList<Entry>();
  public FrameQueue(int capacity) { _capacity=capacity; }
  public int Count { get { return _items.Count; } }
  public bool Add(byte[] frame) { return Add(frame,Stopwatch.GetTimestamp()); }
  public bool Add(byte[] frame,long queuedAt) {
   if(frame[0]==Protocol.MsgPointer && _items.Last!=null && _items.Last.Value.Frame[0]==Protocol.MsgPointer) {
    _items.Last.Value=new Entry(frame,queuedAt); return true;
   }
   if(_items.Count>=_capacity) return false;
   _items.AddLast(new Entry(frame,queuedAt)); return true;
  }
  public bool AddControl(byte[] frame) { return AddControl(frame,Stopwatch.GetTimestamp()); }
  public bool AddControl(byte[] frame,long queuedAt) {
   Entry pendingGeometry=null,pendingReady=null;
   if(frame[0]==Protocol.MsgLeave && _capacity>=2) {
    for(var node=_items.First;node!=null;node=node.Next)
     if(node.Value.Frame[0]==Protocol.MsgGeometry) pendingGeometry=node.Value;
     else if(node.Value.Frame[0]==Protocol.MsgSessionReady) pendingReady=node.Value;
   }
   _items.Clear();
   _items.AddLast(new Entry(frame,queuedAt));
   if(pendingGeometry!=null) _items.AddLast(pendingGeometry);
   else if(pendingReady!=null) _items.AddLast(pendingReady);
   return true;
  }
  public byte[] Take() { long ignored; return Take(out ignored); }
  public byte[] Take(out long queuedAt) {
   queuedAt=0;
   if(_items.First==null) return null;
   Entry entry=_items.First.Value; _items.RemoveFirst(); queuedAt=entry.QueuedAt; return entry.Frame;
  }
  // Caller holds the queue lock. A position before a barrier must flush in order.
  public int GetDispatchWaitMilliseconds(long lastPointerSentAt,long now,long frequency) {
   var head=_items.First;
   if(head==null || head.Value.Frame[0]!=Protocol.MsgPointer
    || (head.Next!=null && head.Next.Value.Frame[0]!=Protocol.MsgPointer)) return 0;
   double remaining=4.0-(now-lastPointerSentAt)*1000.0/frequency;
   return remaining>0 ? Math.Max(1,(int)Math.Ceiling(remaining)) : 0;
  }
  public void Clear() { _items.Clear(); }
 }
}
