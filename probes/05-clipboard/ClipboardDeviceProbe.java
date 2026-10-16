import java.io.*;
import java.security.MessageDigest;

/** Exercises production session/adapter without creating input devices or sending messages. */
public class ClipboardDeviceProbe {
 static final String PC="PC-KVM clipboard PC 测试\nsecond line",ANDROID="PC-KVM clipboard Android 测试\nsecond line";
 static String sha(InputStream in)throws Exception{try(InputStream stream=in){MessageDigest d=MessageDigest.getInstance("SHA-256");byte[] b=new byte[65536];int n;while((n=stream.read(b))!=-1)d.update(b,0,n);StringBuilder out=new StringBuilder();for(byte v:d.digest())out.append(String.format("%02x",v&255));return out.toString();}}
 static ClipboardWire.Snapshot file(String path,String mime,int kind)throws Exception{File f=new File(path);ClipboardWire.Item i=new ClipboardWire.Item();i.name=f.getName();i.mime=mime;i.length=f.length();i.file=f;i.open=()->new FileInputStream(f);ClipboardWire.Snapshot s=new ClipboardWire.Snapshot();s.kind=kind;s.id=System.currentTimeMillis();s.items.add(i);return s;}
 static void hiddenImage(AndroidClipboard.ReflectivePlatform p)throws Exception{
  Object clip=p.clip(),item=p.clipClass.getMethod("getItemAt",int.class).invoke(clip,0),uri=p.itemClass.getMethod("getUri").invoke(item);
  if(uri==null||!uri.toString().contains("/file/"))throw new AssertionError("image is not a generic hidden file URI");
  Object cursor=p.resolverClass.getMethod("query",p.uriClass,String[].class,String.class,String[].class,String.class).invoke(p.resolver,uri,new String[]{"_data","media_type"},null,null,null);Class<?> c=Class.forName("android.database.Cursor");
  try{
   if(!(Boolean)c.getMethod("moveToFirst").invoke(cursor))throw new AssertionError("image row missing");
   String path=(String)c.getMethod("getString",int.class).invoke(cursor,0);int media=(Integer)c.getMethod("getInt",int.class).invoke(cursor,1);
   if(media!=0||!new File(path).getParentFile().getName().equals(".PC-KVM-Clipboard"))throw new AssertionError("clipboard image visible in public media");
  }finally{c.getMethod("close").invoke(cursor);}
  String id=(String)p.uriClass.getMethod("getLastPathSegment").invoke(uri);Object images=p.uriClass.getMethod("parse",String.class).invoke(null,"content://media/external_primary/images/media");
  cursor=p.resolverClass.getMethod("query",p.uriClass,String[].class,String.class,String[].class,String.class).invoke(p.resolver,images,new String[]{"_id"},"_id=?",new String[]{id},null);
  try{if((Integer)c.getMethod("getCount").invoke(cursor)!=0)throw new AssertionError("clipboard image indexed by gallery");}finally{c.getMethod("close").invoke(cursor);}
  System.out.println("DEVICE HIDDEN_IMAGE_OK");
 }
 public static void main(String[] args){try{run(args);}catch(Throwable error){while(error.getCause()!=null)error=error.getCause();System.out.println("DEVICE INIT FAIL "+error.getClass().getSimpleName()+": "+error.getMessage());System.out.flush();System.exit(1);}}
 static void run(String[] args)throws Exception{
  AndroidClipboard.ReflectivePlatform platform=new AndroidClipboard.ReflectivePlatform(new File("/data/local/tmp/pckvm-clipboard-probe-cache"));
  Object original=platform.clip();ClipboardSession session=new ClipboardSession(SessionLink.parseToken(args[0]));
  session.control(1);System.out.println("DEVICE READY");System.out.flush();
  try{
   BufferedReader in=new BufferedReader(new InputStreamReader(System.in,"UTF-8"));String line;
   while((line=in.readLine())!=null){
    if(line.equals("QUIT"))break;
    try{
     if(line.equals("SET_TEXT"))platform.publish(ClipboardWire.text(ANDROID),"PC-KVM native test",()->true);
     else if(line.equals("SET_IMAGE")){platform.publish(file("/data/local/tmp/pckvm-clipboard-test-image.png","image/png",2),"PC-KVM native image",()->true);hiddenImage(platform);}
     else if(line.equals("SET_FILE"))platform.publish(file("/data/local/tmp/pckvm-clipboard-test-file.txt","application/octet-stream",3),"PC-KVM native file",()->true);
     else if(line.equals("EXPECT_TEXT")){
      ClipboardWire.Snapshot s=platform.capture();if(s==null||s.kind!=1||!PC.equals(ClipboardWire.textContent(s))){System.out.println("DEVICE TEXT_DIAG kind="+(s==null?0:s.kind)+" expected="+sha(new ByteArrayInputStream(PC.getBytes("UTF-8")))+" actual="+(s!=null&&s.kind==1&&platform.label()!=null&&platform.label().startsWith("PC-KVM:")?sha(s.items.get(0).open.open()):"not-owned-test-text"));throw new AssertionError("text mismatch");}
     }else if(line.startsWith("EXPECT_IMAGE ")||line.startsWith("EXPECT_FILE ")){
      ClipboardWire.Snapshot s=platform.capture();int kind=line.startsWith("EXPECT_IMAGE ")?2:3;
      if(s==null||s.kind!=kind||s.items.size()!=1||!sha(s.items.get(0).open.open()).equals(line.split(" ")[1]))throw new AssertionError("binary mismatch");
      if(kind==2)hiddenImage(platform);
     }else throw new IllegalArgumentException("unknown probe command");
     System.out.println("DEVICE OK "+line.split(" ")[0]);
    }catch(Throwable e){while(e.getCause()!=null)e=e.getCause();System.out.println("DEVICE FAIL "+line.split(" ")[0]+" "+e.getClass().getSimpleName());}
    System.out.flush();
   }
  }finally{
   session.control(0);session.close();Thread.sleep(500);
   if(original!=null)platform.managerClass.getMethod("setPrimaryClip",platform.clipClass).invoke(platform.manager,original);
   else platform.managerClass.getMethod("clearPrimaryClip").invoke(platform.manager);
   platform.close();System.out.println("DEVICE RESTORED");System.out.flush();
  }
  System.exit(0);
 }
}
