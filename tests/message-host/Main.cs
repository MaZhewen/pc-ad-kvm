using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PcKvm;

class HostTest {
 [StructLayout(LayoutKind.Sequential)] struct Point { public int X,Y; }
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
 static void Check(bool okay,string name) { if(!okay) throw new Exception(name); Console.WriteLine("PASS "+name); }
 [STAThread] static int Main() {
  try {
   Application.EnableVisualStyles();
   using(MessageHost host=new MessageHost()) {
    IntPtr hwnd=host.Handle;
    host.Show(); Application.DoEvents();
    host.Hide(); Application.DoEvents();
    Check(!IsWindowVisible(hwnd),"idle host is hidden");
    Check(WindowFromPoint(new Point{X=3,Y=3})!=hwnd,"idle click reaches underlying window");
    host.ShowForCapture(); Application.DoEvents();
    Check(IsWindowVisible(hwnd),"capture host can become visible");
    Check(WindowFromPoint(new Point{X=3,Y=3})==hwnd,"capture host receives pinned mouse click");
    host.HideAfterCapture(); Application.DoEvents();
    Check(!IsWindowVisible(hwnd),"capture release hides host");
    var suppressor=new Suppressor(hwnd,delegate(string message){Console.WriteLine(message);},
      delegate(bool active){host.SetCursorHidden(active);},
      delegate{host.ShowForCapture();},delegate{host.HideAfterCapture();});
    try {
     Check(suppressor.Engage(),"hidden host can acquire foreground for takeover");
    } finally { suppressor.Release(); Application.DoEvents(); }
    Check(!IsWindowVisible(hwnd),"takeover release restores click-through idle state");
   }
   return 0;
  } catch(Exception e) { Console.WriteLine("FAIL "+e); return 1; }
 }
}
