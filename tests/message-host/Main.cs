using System;
using System.Drawing;
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
  foreach(Control control in parent.Controls) {
   if(control is T && (control.Name==caption || control.Text==caption)) return (T)control;
   T nested=FindControl<T>(control,caption);
   if(nested!=null) return nested;
  }
  return null;
 }
 static T FindFirstControl<T>(Control parent) where T:Control {
  foreach(Control control in parent.Controls) {
   if(control is T) return (T)control;
   T nested=FindFirstControl<T>(control);
   if(nested!=null) return nested;
  }
  return null;
 }
 static System.Drawing.Rectangle BoundsIn(Control ancestor,Control control) {
  System.Drawing.Point location=ancestor.PointToClient(control.Parent.PointToScreen(control.Location));
  return new System.Drawing.Rectangle(location,control.Size);
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
    Button cancel=FindControl<Button>(form,"取消");
    Button openConnection=FindControl<Button>(form,"OpenConnection");
    Check(okay!=null,"Settings has confirm button");
    Check(cancel!=null,"Settings has cancel button");
    Check(openConnection!=null,"Settings has a connection entry");
    Panel recovery=FindControl<Panel>(form,"断联恢复");
    Panel speed=FindControl<Panel>(form,"鼠标速度");
    Panel position=FindControl<Panel>(form,"手机位置");
    Panel switching=FindControl<Panel>(form,"切换方式");
    Panel viewport=FindControl<Panel>(form,"SettingsContentViewport");
    TrackBar slider=FindFirstControl<TrackBar>(form);
    Check(form.BackColor==Color.FromArgb(244,247,251),"Settings uses a light blue-gray canvas");
    Check(form.Font.Name=="Microsoft YaHei UI","Settings uses a sans-serif Chinese UI font");
    Label speedHeading=FindControl<Label>(form,"鼠标速度");
    Check(speedHeading!=null && speedHeading.Font.Name=="Microsoft YaHei UI",
     "Settings card headings use the sans-serif UI font");
    Check(speed!=null && speed.BackColor==Color.White,"Settings uses white section cards");
    Check(okay.FlatStyle==FlatStyle.Flat && okay.BackColor==Color.FromArgb(37,99,235),
     "Settings confirm button uses the blue primary style");
    Check(slider!=null && slider.TickStyle==TickStyle.None,"Settings slider hides redundant ticks");
    Check(recovery!=null && speed!=null && position!=null && switching!=null,"Settings has all sections");
    form.Show(); Application.DoEvents();
    System.Drawing.Rectangle confirmBounds=BoundsIn(form,okay);
    System.Drawing.Rectangle connectionBounds=BoundsIn(form,openConnection);
    System.Drawing.Rectangle recoveryBounds=BoundsIn(form,recovery);
    System.Drawing.Rectangle cancelBounds=BoundsIn(form,cancel);
    Check(form.ClientRectangle.Contains(confirmBounds),"Settings confirm button fits in dialog");
    Check(form.ClientRectangle.Contains(connectionBounds),"Settings connection entry fits in dialog");
    Check(form.ClientRectangle.Contains(cancelBounds),"Settings cancel button fits in dialog");
    Check(viewport!=null && viewport.AutoScroll,"Settings content can scroll while the footer stays fixed");
    Check(form.ClientRectangle.Contains(BoundsIn(form,speed)),"Settings speed section fits in dialog");
    Check(form.ClientRectangle.Contains(BoundsIn(form,position)),"Settings phone-position section fits in dialog");
    Check(form.ClientRectangle.Contains(BoundsIn(form,switching)),"Settings switching section fits in dialog");
    Check(form.ClientRectangle.Contains(recoveryBounds),"Settings recovery section fits in dialog");
    Check(viewport!=null && viewport.ClientRectangle.Contains(BoundsIn(viewport,recovery)),
     "Settings recovery section is visible above the fixed footer");
    okay.PerformClick();
    Application.DoEvents();
    Check(form.EnableEdgeSwitch,"Settings reads enabled edge option");
    setting.MouseSensitivity=form.MouseSensitivity;
    setting.PhoneOnLeft=form.PhoneOnLeft;
    setting.AllowKillAdb=form.AllowKillAdb;
    setting.EnableEdgeSwitch=form.EnableEdgeSwitch;
    setting.SwitchHotkey=form.SwitchHotkey;
    Check(setting.Save(),"confirmed settings save to INI");
    Config saved=Config.Load(null);
    Check(saved.EnableEdgeSwitch && saved.MouseSensitivity==form.MouseSensitivity
     && saved.PhoneOnLeft==form.PhoneOnLeft && saved.AllowKillAdb==form.AllowKillAdb
     && saved.SwitchHotkey.ToString()==form.SwitchHotkey.ToString(),"saved settings reload from INI");
   }
   bool modalConnectionApplied=false;
   using(SettingsForm modal=new SettingsForm(setting,delegate(HotkeyBinding binding){return true;})) {
    modal.Shown+=delegate { FindControl<Button>(modal,"OpenConnection").PerformClick(); };
    DialogResult navigationResult=modal.ShowDialog();
    modalConnectionApplied=navigationResult==DialogResult.OK && modal.ConnectAfterApply
     && modal.MouseSensitivity>0;
   }
   Check(modalConnectionApplied,"Settings saves and closes before opening connection");
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
