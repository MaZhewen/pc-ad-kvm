/** Inspect the ranges Android exposes for the current production descriptor. */
public final class AxisProbe {
 public static void main(String[] args) throws Exception {
  int width=Integer.parseInt(args[0]), height=Integer.parseInt(args[1]), rotation=Integer.parseInt(args[2]);
  boolean square=args.length>3 && "square".equals(args[3]);
  UhidDevice mouse=new UhidDevice("PC-KVM Axis Probe",square?AbsolutePointer.DESCRIPTOR:AbsolutePointer.descriptor(width,height,rotation),0x5681);
  try {
   Class<?> input=Class.forName("android.view.InputDevice");
   for(int attempt=0;attempt<100;attempt++) {
    int[] ids=(int[])input.getMethod("getDeviceIds").invoke(null);
    for(int id:ids) {
     Object d=input.getMethod("getDevice",int.class).invoke(null,id);
     if(d==null || !"PC-KVM Axis Probe".equals(input.getMethod("getName").invoke(d))) continue;
     System.out.println("DEVICE id="+id+" sources="+input.getMethod("getSources").invoke(d));
     for(int axis=0;axis<2;axis++) {
      Object range=d.getClass().getMethod("getMotionRange",int.class,int.class).invoke(d,axis,0x2002);
      System.out.println("AXIS "+axis+" range="+(range==null?"null":range.getClass().getMethod("getMin").invoke(range)+".."+range.getClass().getMethod("getMax").invoke(range)));
     }
     AbsoluteTransform transform=AbsoluteTransform.capture("PC-KVM Axis Probe");
     int[] point=transform.raw(800,800);
     System.out.println("TRANSFORM RAW "+point[0]+","+point[1]);
     if(args.length>5) {
      int rx=Integer.parseInt(args[4]),ry=Integer.parseInt(args[5]);
      mouse.sendReport(new byte[]{1,0,(byte)rx,(byte)(rx>>>8),(byte)ry,(byte)(ry>>>8),0,1});
      System.out.println("RAW "+rx+","+ry);
      System.out.flush();
      Thread.sleep(200);
      Process p=Runtime.getRuntime().exec(new String[]{"dumpsys","input"});
      java.io.BufferedReader reader=new java.io.BufferedReader(new java.io.InputStreamReader(p.getInputStream()));
      String line; boolean inDevice=false, printMatrix=false;
      while((line=reader.readLine())!=null) {
       if(line.startsWith("  Device ")) inDevice=line.contains("PC-KVM Axis Probe");
       if(!inDevice) continue;
       if(line.contains("RawToDisplay Transform")) printMatrix=true;
       if(line.contains("Last Raw Touch")||line.contains("Last Cooked Touch")||
          line.contains("[0]: id=")||line.contains("DisplayBounds")||printMatrix)
        System.out.println(line.trim());
       if(printMatrix && line.contains("1.0000")) printMatrix=false;
      }
      p.waitFor();
     }
     return;
    }
    Thread.sleep(20);
   }
   System.out.println("DEVICE NOT FOUND");
  } finally { mouse.close(); }
 }
}
