/** Read-only metadata check; never prints clipboard text contents. */
public class ClipboardMetadataProbe {
 public static void main(String[] args)throws Exception{
  AndroidClipboard.ReflectivePlatform p=new AndroidClipboard.ReflectivePlatform(new java.io.File("/data/local/tmp/pckvm-clipboard-meta-cache"));
  try{
   Object clip=p.clip();if(clip==null){System.out.println("META empty");return;}
   Object description=p.clipClass.getMethod("getDescription").invoke(clip);Class<?> dc=Class.forName("android.content.ClipDescription");
   int m=(Integer)dc.getMethod("getMimeTypeCount").invoke(description);for(int i=0;i<m;i++)System.out.println("META mime="+dc.getMethod("getMimeType",int.class).invoke(description,i));
   int n=(Integer)p.clipClass.getMethod("getItemCount").invoke(clip);System.out.println("META items="+n);
   for(int i=0;i<n;i++){Object item=p.clipClass.getMethod("getItemAt",int.class).invoke(clip,i),uri=p.itemClass.getMethod("getUri").invoke(item),text=p.itemClass.getMethod("getText").invoke(item);System.out.println("META item="+i+" uri="+(uri==null?"none":uri.toString())+" hasText="+(text!=null));}
   if(args.length==1){
    ClipboardWire.Snapshot snapshot=p.capture();
    if(snapshot==null||snapshot.kind!=3||snapshot.items.size()!=1||!ClipboardDeviceProbe.sha(snapshot.items.get(0).open.open()).equals(args[0]))throw new AssertionError("Own file capture digest mismatch");
    System.out.println("META verified owned file bytes through production adapter");
   }
  }finally{p.close();}
  System.exit(0);
 }
}
