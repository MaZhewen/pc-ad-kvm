public final class TransformTest {
 static void check(boolean ok,String message) { if(!ok) throw new AssertionError(message); }
 static String dump(String raw) {
  return "  Device 85: PC-KVM Absolute Pointer\n"+
   "    RawToDisplay Transform: (ROT_90) (SCALE ROTATE TRANSLATE)\n"+raw+
   "PointerChoreographer:\n"+
   "  MousePointerControllers:\n"+
   "    DisplayViewport[id=0]\n"+
   "      Width=3200, Height=2136\n"+
   "      Transform (ROT_90) (ROTATE TRANSLATE)\n"+
   "          0.0000  -1.0000  3200.0000\n"+
   "          1.0000  0.0000  0.0000\n"+
   "          0.0000  0.0000  1.0000\n";
 }
 public static void main(String[] args) {
  AbsoluteTransform turned=AbsoluteTransform.parse(dump(
   "        0.0000  -0.0977  2136.0000\n"+
   "        0.0977  0.0000  0.0000\n"+
   "        0.0000  0.0000  1.0000\n"),"PC-KVM Absolute Pointer");
  turned.validate(3200,2136);
  int[] a=turned.raw(800,800), b=turned.raw(1600,800);
  check(a[0]>b[0] && a[1]==b[1],"ROT_90 horizontal movement changes raw X only");
  check(Math.abs((3200-0.0977*a[0])-800)<0.2,"ROT_90 visual X");
  check(Math.abs((2136-0.0977*a[1])-800)<0.2,"ROT_90 visual Y");
  AbsoluteTransform flipped=AbsoluteTransform.parse(dump(
   "        0.0000  0.0977  0.0000\n"+
   "        -0.0977  0.0000  3200.0000\n"+
   "        0.0000  0.0000  1.0000\n"),"PC-KVM Absolute Pointer");
  flipped.validate(3200,2136);
  a=flipped.raw(800,800); b=flipped.raw(1600,800);
  check(a[0]<b[0] && a[1]==b[1],"ROT_270 horizontal movement changes raw X only");
  check(Math.abs(0.0977*a[0]-800)<0.2,"ROT_270 visual X");
  check(Math.abs(0.0977*a[1]-800)<0.2,"ROT_270 visual Y");
  AbsoluteTransform upright=AbsoluteTransform.parse(
   "  Device 86: PC-KVM Absolute Pointer\n"+
   "    RawToDisplay Transform: (ROT_0) (SCALE)\n"+
   "        0.0652  0.0000  0.0000\n"+
   "        0.0000  0.0977  0.0000\n"+
   "        0.0000  0.0000  1.0000\n"+
   "PointerChoreographer:\n"+
   "  MousePointerControllers:\n"+
   "    DisplayViewport[id=0]\n"+
   "      Width=2136, Height=3200\n"+
   "      Transform (ROT_0) (IDENTITY)\n"+
   "  TouchPointerControllers:\n", "PC-KVM Absolute Pointer");
  upright.validate(2136,3200);
  a=upright.raw(800,800); b=upright.raw(1600,800);
  check(a[0]<b[0] && a[1]==b[1],"portrait identity viewport preserves horizontal movement");
  AbsoluteTransform rotating=AbsoluteTransform.parse(
   "  Device 87: PC-KVM Absolute Pointer\n"+
   "    RawToDisplay Transform: (ROT_0) (SCALE)\n"+
   "        0.0652  0.0000  0.0000\n"+
   "        0.0000  0.0977  0.0000\n"+
   "        0.0000  0.0000  1.0000\n"+
   "PointerChoreographer:\n"+
   "  MousePointerControllers:\n"+
   "    Pointer Display ID: 0\n"+
   "    Viewports:\n"+
   "      DisplayViewport[id=-1]\n"+
   "        Width=2136, Height=3200\n"+
   "        Transform (ROT_0) (IDENTITY)\n", "PC-KVM Absolute Pointer");
  rotating.validate(2136,3200);
  byte[] report=AbsolutePointer.rawReport(3,a[0],a[1],-1,true);
  check(report.length==8 && (report[1]&255)==3 && report[6]==-1 && report[7]==1,"raw report layout");
  System.out.println("PASS runtime transforms: both landscape orientations and report layout");
 }
}
