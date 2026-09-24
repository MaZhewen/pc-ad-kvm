/** Absolute mouse mapping for Android POINTER mode. */
public final class AbsolutePointer {
 public static int axis(int pixel,int span) {
  if(span<=0) throw new IllegalArgumentException("Invalid display span");
  long v=Math.round(pixel*32768.0/span);
  return (int)Math.max(0,Math.min(32767,v));
 }
 public static int[] rawPoint(int x,int y,int width,int height,int rotation) {
  int span=Math.max(width,height);
  int px=Math.max(0,Math.min(width-1,x)), py=Math.max(0,Math.min(height-1,y));
  int rx=px, ry=py;
  switch(rotation) { case 90: rx=py; ry=span-px; break; case 180: rx=span-px; ry=span-py; break; case 270: rx=span-py; ry=px; break; default: break; }
  rx=Math.max(0,Math.min(span-1,rx)); ry=Math.max(0,Math.min(span-1,ry));
  return new int[]{axis(rx,span),axis(ry,span)};
 }
 /** Map display coordinates to Android's natural, unrotated surface axes. */
 public static byte[] displayReport(int buttons,int x,int y,int width,int height,int rotation,int wheel,boolean visible) {
  int px=Math.max(0,Math.min(width-1,x)), py=Math.max(0,Math.min(height-1,y));
  int rx=px, ry=py;
  // Android keeps the physical (portrait) axes in the UHID report and
  // applies the display rotation in InputReader.  For a landscape display
  // raw X is the display Y axis and raw Y is the display X axis.
  switch(rotation) { case 90: rx=py; ry=width-1-px; break; case 180: rx=width-1-px; ry=height-1-py; break; case 270: rx=height-1-py; ry=px; break; default: break; }
  int rawWidth=axisMax(width,height,rotation,0)+1, rawHeight=axisMax(width,height,rotation,1)+1;
  rx=Math.max(0,Math.min(rawWidth-1,rx)); ry=Math.max(0,Math.min(rawHeight-1,ry));
  return new byte[]{1,(byte)buttons,(byte)rx,(byte)(rx>>>8),(byte)ry,(byte)(ry>>>8),(byte)wheel,(byte)(visible?1:0)};
 }
 public static byte[] rawReport(int buttons,int rawX,int rawY,int wheel,boolean visible) {
  return new byte[]{1,(byte)buttons,(byte)rawX,(byte)(rawX>>>8),(byte)rawY,(byte)(rawY>>>8),(byte)wheel,(byte)(visible?1:0)};
 }
 public static byte[] report(int buttons,int x,int y,int width,int height,int rotation,int wheel,boolean visible) {
  int[] raw=rawPoint(x,y,width,height,rotation);
  return new byte[]{1,(byte)buttons,(byte)raw[0],(byte)(raw[0]>>>8),(byte)raw[1],(byte)(raw[1]>>>8),(byte)wheel,(byte)(visible?1:0)};
 }
 public static byte[] report(int buttons,int x,int y,int width,int height,int wheel,boolean visible) { return report(buttons,x,y,width,height,0,wheel,visible); }
 public static int axisMin(int width,int height,int rotation,int axis) { return 0; }
 public static int axisMax(int width,int height,int rotation,int axis) {
  boolean landscape=rotation==90||rotation==270;
  if(landscape) return axis==0?height-1:width-1;
  return axis==0?width-1:height-1;
 }
 public static final int[] DESCRIPTOR=descriptor(32768,32768,0);
 /** Build an absolute pointer descriptor matching the current display axes. */
 public static int[] descriptor(int width,int height,int rotation) {
  int xMin=axisMin(width,height,rotation,0), xMax=axisMax(width,height,rotation,0), yMin=axisMin(width,height,rotation,1), yMax=axisMax(width,height,rotation,1);
  java.util.ArrayList<Integer> d=new java.util.ArrayList<Integer>();
  add(d,0x05,1,0x09,1,0xa1,1,0x85,1,0x09,1,0xa1,0,0x05,9,0x19,1,0x29,3,0x15,0,0x25,1,0x95,3,0x75,1,0x81,2,0x95,1,0x75,5,0x81,1,0x05,1);
  axis(d,0x30,xMin,xMax); axis(d,0x31,yMin,yMax);
  add(d,0x09,0x38,0x15,0x81,0x25,0x7f,0x75,8,0x95,1,0x81,6,0x05,0x0d,0x09,0x21,0xa1,0,0x09,0x32,0x09,0x42,0x15,0,0x25,1,0x75,1,0x95,2,0x81,2,0x75,6,0x95,1,0x81,1,0xc0,0xc0,0xc0);
  int[] out=new int[d.size()]; for(int i=0;i<out.length;i++) out[i]=d.get(i); return out;
 }
 static void axis(java.util.ArrayList<Integer> d,int usage,int min,int max) { add(d,0x09,usage); if(min<0) add(d,0x16,min&255,(min>>>8)&255); else add(d,0x15,0); add(d,0x26,max&255,(max>>>8)&255,0x75,16,0x95,1,0x81,2); }
 static void add(java.util.ArrayList<Integer> d,int... values) { for(int v:values) d.add(v); }
}
