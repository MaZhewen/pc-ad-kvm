import java.io.ByteArrayOutputStream;
/** Exercises production geometry readiness and one hover move without TCP. */
public final class SessionProbe {
 static void put32(byte[] p,int o,int n) { for(int i=0;i<4;i++) p[o+i]=(byte)(n>>>(8*i)); }
 public static void main(String[] args) throws Exception {
  System.out.println("START"); System.out.flush();
  AbsoluteSession session=new AbsoluteSession();
  try {
   int targetX=args.length>0?Integer.parseInt(args[0]):800;
   int targetY=args.length>1?Integer.parseInt(args[1]):800;
   int width=args.length>2?Integer.parseInt(args[2]):3200;
   int height=args.length>3?Integer.parseInt(args[3]):2136;
   int rotation=args.length>4?Integer.parseInt(args[4]):90;
   ByteArrayOutputStream out=new ByteArrayOutputStream();
   byte[] geometry=new byte[9]; put32(geometry,0,1);
   geometry[4]=(byte)width; geometry[5]=(byte)(width>>>8);
   geometry[6]=(byte)height; geometry[7]=(byte)(height>>>8); geometry[8]=(byte)rotation;
   System.out.println("BEFORE GEOMETRY"); System.out.flush();
   session.handle(0x0c,geometry,out);
   for(int attempt=0;out.size()==0 && attempt<8;attempt++) {
    Thread.sleep(1000);
    try { session.handle(0x08,new byte[4],out); }
    catch(Exception e) {
     System.out.println("RETRY ERROR "+e); e.printStackTrace(System.out);
     Process p=Runtime.getRuntime().exec(new String[]{"dumpsys","input"});
     java.io.BufferedReader reader=new java.io.BufferedReader(new java.io.InputStreamReader(p.getInputStream()));
     String line; boolean section=false; int left=0;
     while((line=reader.readLine())!=null) {
      if(line.trim().equals("PointerChoreographer:")) section=true;
      if(section && line.contains("DisplayViewport[id=0]")) left=14;
      if(left>0) { System.out.println("DUMP "+line); left--; }
     }
     reader.close(); p.waitFor(); return;
    }
    System.out.println("RETRY "+attempt+" readyBytes="+out.size()); System.out.flush();
   }
   if(out.size()!=5 || out.toByteArray()[0]!=0x0d) { System.out.println("MISSING READY"); return; }
   System.out.println("READY");
   int[] raw=session.transform.raw(targetX,targetY);
   System.out.println("TARGET "+targetX+","+targetY+" RAW "+raw[0]+","+raw[1]);
   byte[] pointer=new byte[12]; put32(pointer,0,1); put32(pointer,4,1);
   pointer[8]=(byte)targetX; pointer[9]=(byte)(targetX>>>8);
   pointer[10]=(byte)targetY; pointer[11]=(byte)(targetY>>>8);
   session.handle(0x0b,pointer,out);
   if(out.size()!=18 || out.toByteArray()[5]!=0x0e) throw new AssertionError("missing ack");
   System.out.println("ACK");
   Thread.sleep(4000);
  } finally { session.close(); }
 }
}
