import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.io.StringReader;

/** Inverts Android's two input/display transforms for one registered UHID mouse. */
public final class AbsoluteTransform {
 private final double a,b,c,d,e,f,det;

 private AbsoluteTransform(double[] raw,double[] viewport) {
  a=viewport[0]*raw[0]+viewport[1]*raw[3];
  b=viewport[0]*raw[1]+viewport[1]*raw[4];
  c=viewport[0]*raw[2]+viewport[1]*raw[5]+viewport[2];
  d=viewport[3]*raw[0]+viewport[4]*raw[3];
  e=viewport[3]*raw[1]+viewport[4]*raw[4];
  f=viewport[3]*raw[2]+viewport[4]*raw[5]+viewport[5];
  det=a*e-b*d;
  if(Math.abs(det)<0.000001) throw new IllegalArgumentException("Singular absolute transform");
 }

 public int[] raw(int x,int y) {
  int rx=(int)Math.round(((x-c)*e-b*(y-f))/det);
  int ry=(int)Math.round((a*(y-f)-d*(x-c))/det);
  return new int[]{Math.max(0,Math.min(32767,rx)),Math.max(0,Math.min(32767,ry))};
 }

 public void validate(int width,int height) {
  int[][] corners={{0,0},{width-1,0},{0,height-1},{width-1,height-1}};
  for(int[] p:corners) {
   double rx=((p[0]-c)*e-b*(p[1]-f))/det;
   double ry=(a*(p[1]-f)-d*(p[0]-c))/det;
   if(rx < -20 || rx > 32787 || ry < -20 || ry > 32787)
    throw new IllegalStateException("Display corner outside absolute mouse axes: "+p[0]+","+p[1]+" => "+rx+","+ry);
  }
 }

 public static AbsoluteTransform capture(String deviceName) throws Exception {
  Process p=Runtime.getRuntime().exec(new String[]{"dumpsys","input"});
  BufferedReader reader=new BufferedReader(new InputStreamReader(p.getInputStream()));
  try { return parse(reader,deviceName); }
  finally { reader.close(); }
 }

 public static AbsoluteTransform parse(String dump,String deviceName) {
  try { return parse(new BufferedReader(new StringReader(dump)),deviceName); }
  catch(java.io.IOException e) { throw new IllegalStateException(e); }
 }

 private static AbsoluteTransform parse(BufferedReader reader,String deviceName) throws java.io.IOException {
  double[] raw=new double[6], viewport=new double[6];
  boolean inDevice=false,inPointerControllers=false,lookingViewport=false;
  int rawRows=0,viewportRows=0;
  boolean rawDone=false,viewportDone=false;
  String line;
  while((line=reader.readLine())!=null) {
   String trimmed=line.trim();
   if(line.startsWith("  Device ")) inDevice=line.contains(deviceName);
   if(trimmed.equals("PointerChoreographer:")) { inDevice=false; inPointerControllers=true; }
   if(inDevice && trimmed.startsWith("RawToDisplay Transform:")) {
    if(trimmed.contains("(IDENTITY)")) { identity(raw); rawDone=true; }
    else rawRows=1;
    continue;
   }
   if(rawRows>0) {
    row(raw,rawRows-1,trimmed);
    if(++rawRows>2) { rawRows=0; rawDone=true; }
    continue;
   }
   // Android may label the sole pointer viewport id=-1 while rebinding it
   // after a display rotation, even though Pointer Display ID remains 0.
   if(inPointerControllers && (trimmed.startsWith("DisplayViewport[id=0]")
       || trimmed.startsWith("DisplayViewport[id=-1]"))) lookingViewport=true;
   if(lookingViewport && trimmed.startsWith("Transform (")) {
    lookingViewport=false;
    if(trimmed.contains("(IDENTITY)")) { identity(viewport); viewportDone=true; }
    else viewportRows=1;
    if(rawDone && viewportDone) return new AbsoluteTransform(raw,viewport);
    continue;
   }
   if(viewportRows>0) {
    row(viewport,viewportRows-1,trimmed);
    if(++viewportRows>2) { viewportRows=0; viewportDone=true; }
   }
   if(rawDone && viewportDone) return new AbsoluteTransform(raw,viewport);
  }
  throw new IllegalStateException("Absolute mouse display transform unavailable: raw="+rawDone+" viewport="+viewportDone);
 }

 private static void row(double[] matrix,int index,String line) {
  java.util.StringTokenizer values=new java.util.StringTokenizer(line);
  if(values.countTokens()!=3) throw new IllegalArgumentException("Invalid transform matrix");
  for(int col=0;col<3;col++) matrix[index*3+col]=Double.parseDouble(values.nextToken());
 }
 private static void identity(double[] matrix) {
  matrix[0]=1; matrix[1]=0; matrix[2]=0;
  matrix[3]=0; matrix[4]=1; matrix[5]=0;
 }
}
