import java.io.*;import java.util.*;
public class CacheMain {
 static void hiddenImageLifetime()throws Exception{
  File root=new File(System.getProperty("java.io.tmpdir"),"pckvm-image-cache-"+UUID.randomUUID());root.mkdirs();
  final long[] clock={100000000L};final List<String> removed=new ArrayList<String>();
  try{
   String uri="content://media/external_primary/images/media/901";
   File legacy=new File(root,UUID.randomUUID()+".receipt");
   try(DataOutputStream out=new DataOutputStream(new FileOutputStream(legacy))){out.writeUTF("PC-KVM-MEDIA-1");out.writeInt(1);out.writeUTF(uri);out.writeLong(12L);}legacy.setLastModified(clock[0]);
   AndroidClipboardCache c=new AndroidClipboardCache(root,100000,128,()->clock[0]);
   clock[0]+=4L*60*1000;c.sweep(Collections.emptySet(),u->{removed.add(u);return true;});
   if(!removed.isEmpty())throw new AssertionError("image preview grace was not respected");
   clock[0]+=2L*60*1000;c.sweep(Collections.emptySet(),u->{removed.add(u);return true;});
   if(!removed.equals(Arrays.asList(uri)))throw new AssertionError("retired clipboard image must not remain for 24 hours");
   removed.clear();String live="content://media/external_primary/file/902";
   c.record(Arrays.asList(live),Arrays.asList(12L),true);clock[0]+=25L*60*60*1000;
   c.sweep(new HashSet<String>(Arrays.asList(live)),u->{removed.add(u);return true;});
   if(!removed.isEmpty())throw new AssertionError("current hidden image was deleted");
   clock[0]+=4L*60*1000;c.sweep(Collections.emptySet(),u->{removed.add(u);return true;});
   if(!removed.isEmpty())throw new AssertionError("replaced hidden image lost preview grace");
   clock[0]+=2L*60*1000;c.sweep(Collections.emptySet(),u->{removed.add(u);return true;});
   if(!removed.equals(Arrays.asList(live)))throw new AssertionError("hidden image not retired after grace");
   removed.clear();String current="content://media/external_primary/file/1010";
   for(int i=1001;i<=1010;i++){c.record(Arrays.asList("content://media/external_primary/file/"+i),Arrays.asList(1L),true);clock[0]++;}
   c.sweep(new HashSet<String>(Arrays.asList(current)),u->{removed.add(u);return true;});
   if(!removed.equals(Arrays.asList("content://media/external_primary/file/1001")))throw new AssertionError("retired image count is not bounded to eight");
   clock[0]+=6L*60*1000;c.sweep(Collections.emptySet(),u->false);
   if(root.listFiles().length!=9)throw new AssertionError("failed image cleanup lost its receipt");
   c.sweep(Collections.emptySet(),u->true);if(root.listFiles().length!=0)throw new AssertionError("image cleanup cannot retry");
  }finally{for(File file:root.listFiles())file.delete();root.delete();}
 }
 static void pendingPublicationSurvivesCleanup()throws Exception{
  File root=new File(System.getProperty("java.io.tmpdir"),"pckvm-pending-cache-"+UUID.randomUUID());root.mkdirs();final long[] clock={100000000L};
  AndroidClipboardCache c=new AndroidClipboardCache(root,2L<<30,4096,()->clock[0]);String old="content://media/external_primary/file/1900",incoming="content://media/external_primary/file/1901";
  try{
   c.record(Arrays.asList(old),Arrays.asList(1L),true);clock[0]++;
   File pending=c.recordPending(Arrays.asList(incoming),Arrays.asList(64L<<20),true);final List<String> removed=new ArrayList<String>();
   c.sweep(Collections.emptySet(),u->{if(u.equals(old))throw new IOException("old cache temporarily unavailable");removed.add(u);return true;});
   if(!removed.isEmpty()||!pending.exists())throw new AssertionError("timer deleted incoming image before clipboard publication");
   clock[0]+=25L*60*60*1000;c.sweep(Collections.emptySet(),u->{if(u.equals(old))throw new IOException("old unavailable");removed.add(u);return true;});
   if(!removed.isEmpty()||!pending.exists())throw new AssertionError("pending protection expired before commit");
   c.complete(pending);c.sweep(new HashSet<String>(Arrays.asList(incoming)),u->{if(u.equals(incoming))throw new AssertionError("current committed image deleted");return true;});
   c.forget(pending);if(pending.exists()||c.pending.contains(pending))throw new AssertionError("rollback did not forget pending receipt");
  }finally{for(File file:root.listFiles())file.delete();root.delete();}
 }
 public static void main(String[] args)throws Exception{
  pendingPublicationSurvivesCleanup();
  hiddenImageLifetime();
  File root=new File(System.getProperty("java.io.tmpdir"),"pckvm-media-cache-"+UUID.randomUUID());root.mkdirs();
  final long[] clock={100000000L};final List<String> removed=new ArrayList<String>();
  AndroidClipboardCache c=new AndroidClipboardCache(root,100,4,()->clock[0]);
  c.record(Arrays.asList("content://media/live"),Arrays.asList(80L));
  c.record(Arrays.asList("content://media/old"),Arrays.asList(80L));
  c.sweep(new HashSet<String>(Arrays.asList("content://media/live")),uri->{removed.add(uri);return true;});
  if(!removed.isEmpty())throw new AssertionError("protected file counted toward retired quota");
  clock[0]+=25L*60*60*1000;c.sweep(new HashSet<String>(Arrays.asList("content://media/live")),uri->{removed.add(uri);return true;});
  if(!removed.equals(Arrays.asList("content://media/old")))throw new AssertionError("age cleanup protects current");
  c.record(Arrays.asList("content://media/new1"),Arrays.asList(80L));clock[0]++;
  c.record(Arrays.asList("content://media/new2"),Arrays.asList(80L));
  c.sweep(new HashSet<String>(Arrays.asList("content://media/live")),uri->{removed.add(uri);return true;});
  if(!removed.contains("content://media/new1")||removed.contains("content://media/live"))throw new AssertionError("retired quota");
  try{c.record(Arrays.asList("content://foreign/source"),Arrays.asList(1L));throw new AssertionError("unowned accepted");}catch(IOException expected){}
  File unrelated=new File(root,"user-file.txt");try(FileWriter w=new FileWriter(unrelated)){w.write("keep");}
  clock[0]+=25L*60*60*1000;c.sweep(Collections.emptySet(),uri->false);
  if(!unrelated.exists())throw new AssertionError("unrelated deleted");
  AndroidClipboardCache restarted=new AndroidClipboardCache(root,100,4,()->clock[0]);restarted.sweep(new HashSet<String>(Arrays.asList("content://media/live")),uri->{if(uri.equals("content://media/live"))throw new AssertionError("restart current deleted");return true;});
  for(File f:root.listFiles())f.delete();root.delete();System.out.println("PASS Android media retention ownership/age/quota/current/restart/failure");
 }
}
