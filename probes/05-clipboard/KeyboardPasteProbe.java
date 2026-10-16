/** One Ctrl+V using exactly the production UHID keyboard report path. No Enter key. */
public class KeyboardPasteProbe {
 public static void main(String[] args)throws Exception{
  UhidDevice dev=new UhidDevice("PC-KVM clipboard acceptance",HidDescriptor.keyboardOnly());
  try{
   Thread.sleep(700);
   KeyState.apply(dev,0x1d,true,1);Thread.sleep(60);
   KeyState.apply(dev,0x2f,true,1);Thread.sleep(60);
   KeyState.apply(dev,0x2f,false,1);Thread.sleep(60);
   KeyState.apply(dev,0x1d,false,0);Thread.sleep(100);
  }finally{try{KeyState.releaseAll(dev);}finally{dev.close();}}
  System.out.println("PROBE production UHID Ctrl+V injected");
 }
}
