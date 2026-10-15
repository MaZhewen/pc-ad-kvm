import java.io.*;import java.util.*;
public class CacheMain {
 public static void main(String[] args)throws Exception{
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
