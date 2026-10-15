import java.io.*;
public class AndroidMain {
 static int checks;
 static void check(boolean b,String name){if(!b)throw new AssertionError(name);checks++;}
 static class Fake implements AndroidClipboard.Platform {
  ClipboardWire.Snapshot clip;String label="local";boolean available=true,closed,failListen;Runnable changed;ClipboardWire.Snapshot own;java.util.concurrent.CountDownLatch entered,resume;
  public ClipboardWire.Snapshot capture(){return clip;}
  public void publish(ClipboardWire.Snapshot s,String l,java.util.function.BooleanSupplier allowed)throws Exception{if(entered!=null){entered.countDown();if(!resume.await(2,java.util.concurrent.TimeUnit.SECONDS))throw new IOException("test timeout");}if(!allowed.getAsBoolean()||!available)throw new InterruptedIOException();own=s;clip=s;label=l;changed.run();}
  public String label(){return label;}
  public boolean available(){return available;}
  public void listen(Runnable r)throws Exception{if(failListen)throw new IOException("listen failed");changed=r;} public Object identity(){return clip;} public boolean isOwnClip(String l){return clip==own&&l.equals(label);}
  public void close(){closed=true;}
  void copy(String text)throws Exception{clip=ClipboardWire.text(text);label="local";changed.run();}
 }
 static void concurrent(int action)throws Exception{
  Fake f=new Fake();f.clip=ClipboardWire.text("baseline");AndroidClipboard c=new AndroidClipboard(f,()->{});f.entered=new java.util.concurrent.CountDownLatch(1);f.resume=new java.util.concurrent.CountDownLatch(1);final boolean[] accepted={true};final Throwable[] failure={null};final java.util.concurrent.atomic.AtomicBoolean active=new java.util.concurrent.atomic.AtomicBoolean(true);
  Thread t=new Thread(()->{try{accepted[0]=c.publish(ClipboardWire.text("remote"),c.revision(),()->active.get());}catch(Throwable e){failure[0]=e;}});t.start();check(f.entered.await(2,java.util.concurrent.TimeUnit.SECONDS),"publication prepare latched");if(action==0)f.copy("new local");else if(action==1)active.set(false);else f.available=false;f.resume.countDown();t.join(2000);check(!t.isAlive()&&failure[0]==null&&!accepted[0],"concurrent change cancels publication "+action);check(ClipboardWire.textContent(f.clip).equals(action==0?"new local":"baseline"),"concurrent change preserves clipboard "+action);c.close();
 }
 static void resources()throws Exception{
  File root=new File(System.getProperty("java.io.tmpdir"),"pckvm-android-test-"+java.util.UUID.randomUUID());check(root.mkdir(),"test cache created");
  try{
   ClipboardWire.Item item=new ClipboardWire.Item();item.open=()->new ByteArrayInputStream(new byte[9]);boolean rejected=false;
   try(AndroidClipboard.CaptureBatch batch=new AndroidClipboard.CaptureBatch(root,8,10)){try{batch.spool(item);}catch(IOException expected){rejected=true;}}
   check(rejected&&root.list().length==0,"failed unknown URI spool rolls back partial file");
   item.open=()->new ByteArrayInputStream(new byte[6]);rejected=false;
   try(AndroidClipboard.CaptureBatch batch=new AndroidClipboard.CaptureBatch(root,8,10)){batch.spool(item);try{batch.reserve(5);}catch(IOException expected){rejected=true;}}
   check(rejected&&root.list().length==0,"later item failure rolls back earlier spool and total budget");
   final int[] cleanup={0};rejected=false;try{AndroidClipboard.closeAll(()->{cleanup[0]++;throw new IOException("remove listener failed");},()->cleanup[0]++);}catch(Exception expected){rejected=true;}
   check(rejected&&cleanup[0]==2,"failed cleanup never skips following resource");
  }finally{File[] files=root.listFiles();if(files!=null)for(File file:files)file.delete();root.delete();}
 }
 public static void main(String[] args)throws Exception{
  Fake f=new Fake();final int[] events={0};AndroidClipboard c=new AndroidClipboard(f,()->events[0]++);
  f.copy("本地\ntext");check(events[0]==1&&c.revision()==1,"external revision");
  ClipboardWire.Snapshot s=ClipboardWire.text("remote");s.id=1;
  check(c.publish(s,1),"publish current");check(c.revision()==1&&events[0]==1,"echo suppressed");
  f.copy("newer");check(!c.publish(s,1),"stale transfer rejected");check(ClipboardWire.textContent(c.capture()).equals("newer"),"new clipboard retained");
  f.available=false;check(c.capture()==null,"locked cannot read");check(!c.publish(s,c.revision()),"locked cannot overwrite");
  f.available=true;f.clip=null;f.label="unsupported";f.changed.run();check(c.revision()==3,"unsupported invalidates transfer");
  check(AndroidClipboard.fileName("../evil.txt",0).equals("evil.txt"),"safe basename");
  check(!AndroidClipboard.fileName("CON",0).equals("CON"),"reserved filename");
  check(!AndroidClipboard.fileName("a:b?.txt",0).contains(":"),"invalid Windows name");
  check(!c.publish(s,c.revision(),()->false),"disconnected transfer cannot publish");
  Object receiptIdentity=c.identity();long receiptRevision=c.revision();
  f.clip=ClipboardWire.text("copied while transfer in flight");f.label="pending native callback";
  check(!c.publish(s,receiptRevision,receiptIdentity,()->true),"delayed callback cannot overwrite native clipboard");
  ClipboardWire.Snapshot nativeFile=ClipboardWire.text("native binary capture");nativeFile.kind=3;nativeFile.id=0;f.clip=nativeFile;
  check(c.capture().fingerprint().length()==64,"native file capture has identity before hashing");
  c.publish(s,c.revision());long previous=c.revision();f.clip=ClipboardWire.text("changed again");f.changed.run();check(c.revision()==previous+1,"same own label cannot hide changed data"); c.close();check(f.closed,"listener released"); Fake failed=new Fake();failed.failListen=true;try{new AndroidClipboard(failed,()->{});}catch(IOException expected){}check(failed.closed,"failed registration releases platform"); concurrent(0);concurrent(1);concurrent(2);resources();System.out.println("PASS Android clipboard state "+checks+" assertions");
 }
}
