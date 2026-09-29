using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PcKvm;

class HostTest {
 [StructLayout(LayoutKind.Sequential)] struct Point { public int X,Y; }
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd,int id,uint mods,uint key);
 [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd,int id);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
 static void Check(bool okay,string name) { if(!okay) throw new Exception(name); Console.WriteLine("PASS "+name); }
 static T FindControl<T>(Control parent,string caption) where T:Control {
  foreach(Control control in parent.Controls) if(control is T && control.Text==caption) return (T)control;
  return null;
 }
 [STAThread] static int Main() {
  try {
   Application.EnableVisualStyles();
   Config setting=new Config();
   setting.EnableEdgeSwitch=false;
   using(SettingsForm form=new SettingsForm(setting,delegate(HotkeyBinding binding){return true;})) {
    CheckBox edge=FindControl<CheckBox>(form,"启用双次贴边切换");
    Check(edge!=null && !edge.Checked,"Settings shows disabled edge switching");
    edge.Checked=true;
    Button okay=FindControl<Button>(form,"确定");
    Check(okay!=null,"Settings has confirm button");
    form.Show(); Application.DoEvents();
    okay.PerformClick();
    Application.DoEvents();
    Check(form.EnableEdgeSwitch,"Settings reads enabled edge option");
   }
   using(RecreatedHost host=new RecreatedHost()) {
    IntPtr hwnd=host.Handle;
    RawInput raw=new RawInput(hwnd);
    raw.Register();
    Check(RawInput.LastRegisterOk,"raw input registers on first handle");
    var suppressor=new Suppressor(hwnd,delegate(string message){Console.WriteLine(message);},
      delegate(bool active){host.SetCursorHidden(active);},
      delegate{host.ShowForCapture();},delegate{host.HideAfterCapture();});
    host.HandleRecreated += delegate(IntPtr newHandle) {
     raw.Rebind(newHandle);
     suppressor.SetOwnWindow(newHandle);
    };
    bool registered=false;
    for(int n=12;n>=1;n--) {
     HotkeyBinding binding;
     HotkeyBinding.TryParse("Ctrl+Alt+Shift+F"+n,out binding);
     if(host.TrySetSwitchHotkey(binding)) { registered=true; break; }
    }
    Check(registered,"global shortcut registers");
    int toggles=0;
    host.ToggleRequested += delegate { toggles++; };
    int hotkeyId=(int)typeof(MessageHost).GetField("_activeHotkeyId",
     System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(host);
    host.InputReady=false;
    host.SwitchingEnabled=false;
    host.SwitchingEnabled=true; // opening and cancelling Settings must not clear input failure
    Check(PostMessage(hwnd,0x0312,(IntPtr)hotkeyId,IntPtr.Zero),"posts shortcut while input unavailable");
    Application.DoEvents();
    Check(toggles==0,"settings cancellation cannot enable shortcut without raw input");
    host.InputReady=true;
    host.SwitchingEnabled=false;
    Check(PostMessage(hwnd,0x0312,(IntPtr)hotkeyId,IntPtr.Zero),"posts shortcut while Settings is open");
    Application.DoEvents();
    Check(toggles==0,"settings dialog blocks shortcut");
    host.SwitchingEnabled=true;
    Check(PostMessage(hwnd,0x0312,(IntPtr)hotkeyId,IntPtr.Zero),"posts shortcut after recovery");
    Application.DoEvents();
    Check(toggles==1,"shortcut works after raw input recovers");
    host.RecreateForTest();
    hwnd=host.Handle;
    Check(host.IsSwitchHotkeyActive,"global shortcut survives handle recreation");
    Check(RawInput.LastRegisterOk && raw.Handle==hwnd,
     "raw input rebinds after handle recreation");
    Check(suppressor.OwnWindow==hwnd,"capture owner rebinds after handle recreation");
    using(Form blocker=new Form()) {
     HotkeyBinding blocked=null;
     for(int n=11;n>=1;n--) {
      HotkeyBinding candidate;
      HotkeyBinding.TryParse("Ctrl+Alt+Shift+F"+n,out candidate);
      if(RegisterHotKey(blocker.Handle,0x7777,candidate.Modifiers|0x4000,(uint)candidate.VirtualKey)) {
       blocked=candidate; break;
      }
     }
     Check(blocked!=null,"conflict test reserves a second shortcut");
     try {
      Check(!host.TrySetSwitchHotkey(blocked) && host.IsSwitchHotkeyActive,
       "conflicting shortcut keeps previous registration");
     } finally { UnregisterHotKey(blocker.Handle,0x7777); }
     Check(host.TrySetSwitchHotkey(blocked),"shortcut can change after conflict clears");
    }
    host.Show(); Application.DoEvents();
    host.Hide(); Application.DoEvents();
    Check(!IsWindowVisible(hwnd),"idle host is hidden");
    Check(WindowFromPoint(new Point{X=3,Y=3})!=hwnd,"idle click reaches underlying window");
    host.ShowForCapture(); Application.DoEvents();
    Check(IsWindowVisible(hwnd),"capture host can become visible");
    Check(WindowFromPoint(new Point{X=3,Y=3})==hwnd,"capture host receives pinned mouse click");
    host.HideAfterCapture(); Application.DoEvents();
    Check(!IsWindowVisible(hwnd),"capture release hides host");
    if(GetForegroundWindow()==IntPtr.Zero) {
     Console.WriteLine("SKIP foreground takeover check: no interactive foreground window");
    } else {
     try {
      Check(suppressor.Engage(),"hidden host can acquire foreground for takeover");
     } finally { suppressor.Release(); Application.DoEvents(); }
     Check(!IsWindowVisible(hwnd),"takeover release restores click-through idle state");
    }
   }
   return 0;
  } catch(Exception e) { Console.WriteLine("FAIL "+e); return 1; }
 }
}

class RecreatedHost : MessageHost {
 public void RecreateForTest() { RecreateHandle(); }
}
