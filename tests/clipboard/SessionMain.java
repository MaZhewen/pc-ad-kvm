import java.io.*;
import java.net.*;
import java.nio.file.Files;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;
import java.util.function.BooleanSupplier;

public final class SessionMain {
 static int checks;
 static void check(boolean yes,String label){if(!yes)throw new AssertionError(label);checks++;}
 static final byte[] TOKEN=new byte[32];
 static final class Fake implements ClipboardSession.Adapter {
  volatile ClipboardWire.Snapshot current;volatile long revision;volatile int captures,publishes,releases;volatile boolean locked,closed;
  final Runnable changed;volatile CountDownLatch publishing,allowPublish,identityRead;volatile Object nativeIdentity=new Object();
  Fake(Runnable changed)throws Exception{this.changed=changed;current=ClipboardWire.text("preconnect 中文");}
  public ClipboardWire.Snapshot capture(){captures++;return locked?null:current;}
  public long revision(){return revision;}
  public Object identity(){CountDownLatch latch=identityRead;if(latch!=null)latch.countDown();return nativeIdentity;}
  public boolean publish(ClipboardWire.Snapshot s,long expected,BooleanSupplier allowed)throws Exception{return publish(s,expected,identity(),allowed);}
  public boolean publish(ClipboardWire.Snapshot s,long expected,Object identity,BooleanSupplier allowed)throws Exception{
   CountDownLatch entered=publishing;if(entered!=null){entered.countDown();allowPublish.await(3,TimeUnit.SECONDS);}
   if(locked||closed||revision!=expected||identity!=nativeIdentity||!allowed.getAsBoolean())return false;
   current=ClipboardWire.text(ClipboardWire.textContent(s));nativeIdentity=new Object();publishes++;return true;
  }
  public void release(ClipboardWire.Snapshot s){releases++;}
  public void close(){closed=true;}
  void copy(String text)throws Exception{current=ClipboardWire.text(text);nativeIdentity=new Object();revision++;changed.run();}
  void copy(ClipboardWire.Snapshot s){current=s;nativeIdentity=new Object();revision++;changed.run();}
 }
 static final class Harness implements AutoCloseable {
  final ServerSocket server=new ServerSocket(0,8,InetAddress.getByName("127.0.0.1"));
  final File root=Files.createTempDirectory("pckvm-session-").toFile();
  final AtomicReference<Fake> fake=new AtomicReference<Fake>();final AtomicInteger factories=new AtomicInteger();
  final ClipboardSession session;
  Harness()throws Exception{server.setSoTimeout(3500);session=new ClipboardSession(TOKEN,server.getLocalPort(),root,(cache,changed)->{Fake f=new Fake(changed);fake.set(f);factories.incrementAndGet();return f;});}
  Socket connect(int ack)throws Exception{Socket s;try{s=server.accept();}catch(SocketTimeoutException e){throw new AssertionError("enabled session establishes authenticated connection");}s.setSoTimeout(3500);InputStream in=s.getInputStream();byte[] h=new byte[37];exact(in,h);check(h[0]=='P'&&h[1]=='K'&&h[2]=='C'&&h[3]=='B'&&h[4]==1,"handshake preamble");check(Arrays.equals(Arrays.copyOfRange(h,5,37),TOKEN),"session token");s.getOutputStream().write(ack);s.getOutputStream().flush();return s;}
  Socket ready()throws Exception{Socket s=connect(1);check(s.getInputStream().read()==0,"baseline ready marker");return s;}
  public void close()throws Exception{session.close();server.close();remove(root);}
 }
 static void remove(File f){File[] children=f.listFiles();if(children!=null)for(File c:children)remove(c);f.delete();}
 static void exact(InputStream in,byte[] b)throws Exception{int p=0;while(p<b.length){int n=in.read(b,p,b.length-p);if(n<0)throw new EOFException();p+=n;}}
 static long number(InputStream in)throws Exception{long n=0;for(int i=0;i<8;i++){int b=in.read();if(b<0)throw new EOFException();n|=(long)b<<(8*i);}return n;}
 static ClipboardWire.Snapshot receive(Socket s,File root)throws Exception{int marker=s.getInputStream().read();check(marker==1,"snapshot marker, actual "+marker);return ClipboardWire.read(s.getInputStream(),new File(root,"peer"),null);}
 static void send(Socket s,String text,long id)throws Exception{ClipboardWire.Snapshot snapshot=ClipboardWire.text(text);snapshot.id=id;s.getOutputStream().write(1);ClipboardWire.write(s.getOutputStream(),snapshot,null);s.getOutputStream().flush();}
 static void ack(Socket s,long id,int status)throws Exception{check(s.getInputStream().read()==3,"ACK marker");check(number(s.getInputStream())==id,"ACK update id");check(s.getInputStream().read()==status,"ACK publication status");}
 static void quiet(Socket s,String label)throws Exception{s.setSoTimeout(220);try{int marker=s.getInputStream().read();throw new AssertionError(label+" unexpected marker "+marker);}catch(SocketTimeoutException expected){checks++;}finally{s.setSoTimeout(3500);}}
 static void basics()throws Exception{
  try(Harness h=new Harness()){
   check(h.factories.get()==0,"constructor inert");h.session.control(2);check(h.factories.get()==0,"disabled force inert");h.session.control(1);
   try(Socket refused=h.connect(0)){check(refused.getInputStream().read()==-1,"rejected authentication closes connection");}
   try(Socket s=h.ready()){
    check(h.factories.get()==1,"adapter reused on reconnect");quiet(s,"baseline preserves clipboards");h.session.control(2);check(ClipboardWire.textContent(receive(s,h.root)).equals("preconnect 中文"),"forced preconnect content");
    send(s,"remote copied",700);ack(s,700,0);check(h.fake.get().publishes==1,"remote published");quiet(s,"remote has no echo");
    h.fake.get().copy("remote copied");quiet(s,"normal same-content copy deduplicated");h.session.control(2);check(ClipboardWire.textContent(receive(s,h.root)).equals("remote copied"),"force bypasses dedup");
    s.getOutputStream().write(2);s.getOutputStream().flush();check(ClipboardWire.textContent(receive(s,h.root)).equals("remote copied"),"peer force marker");
    h.fake.get().locked=true;send(s,"must not overwrite",701);ack(s,701,1);check(h.fake.get().publishes==1,"lock refuses publish");h.fake.get().locked=false;
    h.fake.get().copy("local latest");check(ClipboardWire.textContent(receive(s,h.root)).equals("local latest"),"native change sends");
    h.session.control(0);h.session.control(1);check(s.getInputStream().read()==-1,"disable closes connected socket");try(Socket renewed=h.ready()){check(h.factories.get()==2,"rapid disable enable recreates listener and baseline");quiet(renewed,"reenable baseline preserves clipboard");}
   }
  }
 }
 static void canceledPublication()throws Exception{
  try(Harness h=new Harness()){h.session.control(1);try(Socket s=h.ready()){
   Fake f=h.fake.get();f.publishing=new CountDownLatch(1);f.allowPublish=new CountDownLatch(1);send(s,"delayed publication",800);check(f.publishing.await(2,TimeUnit.SECONDS),"publish started");h.session.control(0);f.allowPublish.countDown();long until=System.nanoTime()+TimeUnit.SECONDS.toNanos(2);while(!f.closed&&System.nanoTime()<until)Thread.yield();check(f.publishes==0,"disabled session cannot publish delayed result");
  }}
 }
 static void unacknowledgedReconnect()throws Exception{
  try(Harness h=new Harness()){h.session.control(1);try(Socket first=h.ready()){h.session.control(2);receive(first,h.root);}
   try(Socket second=h.ready()){ClipboardWire.Snapshot retried=receive(second,h.root);check(ClipboardWire.textContent(retried).equals("preconnect 中文"),"unacknowledged source retried after reconnect");OutputStream out=second.getOutputStream();out.write(3);for(int n=0;n<8;n++)out.write((int)(retried.id>>>(8*n))&255);out.write(1);out.flush();quiet(second,"superseded ACK consumed");}
   try(Socket third=h.ready()){quiet(third,"acknowledged refused publication not retried");}
  }
 }
 static void failedSourceNoRetry()throws Exception{
  try(Harness h=new Harness()){h.session.control(1);try(Socket s=h.ready()){
   ClipboardWire.Snapshot broken=new ClipboardWire.Snapshot();broken.kind=3;broken.id=1;ClipboardWire.Item item=new ClipboardWire.Item();item.name="marked.bin";item.mime="application/octet-stream";item.length=1;item.open=()->{throw new IOException("marked source unavailable");};broken.items.add(item);Fake f=h.fake.get();f.copy(broken);quiet(s,"failed source does not send incomplete frame");int captures=f.captures;quiet(s,"failed source no retry one");quiet(s,"failed source no retry two");check(f.captures==captures,"invalid source not retried without new change");f.copy("source recovered");check(ClipboardWire.textContent(receive(s,h.root)).equals("source recovered"),"valid new event recovers after source failure");
  }}
 }
 static void supersededPublication()throws Exception{
  try(Harness h=new Harness()){h.session.control(1);try(Socket s=h.ready()){
   Fake f=h.fake.get();f.publishing=new CountDownLatch(1);f.allowPublish=new CountDownLatch(1);send(s,"obsolete remote",801);check(f.publishing.await(2,TimeUnit.SECONDS),"stale publication entered");f.revision++;f.allowPublish.countDown();ack(s,801,1);check(f.publishes==0,"newer native revision rejects delayed publication");
  }}
 }
 static void mutatedSourceNoRetry()throws Exception{
  try(Harness h=new Harness()){h.session.control(1);try(Socket first=h.ready()){
   AtomicInteger opens=new AtomicInteger();ClipboardWire.Snapshot snapshot=new ClipboardWire.Snapshot();snapshot.kind=3;snapshot.id=1;ClipboardWire.Item item=new ClipboardWire.Item();item.name="marked.bin";item.mime="application/octet-stream";item.length=2;item.open=()->new ByteArrayInputStream(opens.incrementAndGet()==1?new byte[2]:new byte[0]);snapshot.items.add(item);h.fake.get().copy(snapshot);while(first.getInputStream().read()!=-1){}check(opens.get()==2,"source mutation detected during actual streaming");
   try(Socket next=h.ready()){quiet(next,"mutated source not replayed one");quiet(next,"mutated source not replayed two");quiet(next,"mutated source not replayed three");check(opens.get()==2,"invalid source is consumed across reconnect");}
  }}
 }
 static void delayedNativeCallback()throws Exception{
  try(Harness h=new Harness()){h.session.control(1);try(Socket s=h.ready()){
   Fake f=h.fake.get();f.identityRead=new CountDownLatch(1);ClipboardWire.Snapshot remote=ClipboardWire.text("obsolete wire data");remote.id=900;ByteArrayOutputStream bytes=new ByteArrayOutputStream();bytes.write(1);ClipboardWire.write(bytes,remote,null);byte[] record=bytes.toByteArray();s.getOutputStream().write(record,0,14);s.getOutputStream().flush();f.identityRead.await(250,TimeUnit.MILLISECONDS);
   f.current=ClipboardWire.text("native copy without callback");f.nativeIdentity=new Object();s.getOutputStream().write(record,14,record.length-14);s.getOutputStream().flush();ack(s,900,1);check(f.publishes==0,"delayed native callback cannot overwrite native copy");check(ClipboardWire.textContent(f.current).equals("native copy without callback"),"native identity preserved");
  }}
 }
 static void canceledSendReconnect()throws Exception{
  try(Harness h=new Harness()){h.session.control(1);try(Socket first=h.ready()){
   CountDownLatch blocked=new CountDownLatch(1),unblock=new CountDownLatch(1);AtomicInteger opens=new AtomicInteger();ClipboardWire.Snapshot file=new ClipboardWire.Snapshot();file.kind=3;file.id=1;ClipboardWire.Item item=new ClipboardWire.Item();item.name="marked.bin";item.mime="application/octet-stream";item.length=200000;item.open=()->{int n=opens.incrementAndGet();return new ByteArrayInputStream(new byte[200000]){boolean waited;public synchronized int read(byte[] b,int o,int c){if(n==2&&!waited){waited=true;blocked.countDown();try{unblock.await(3,TimeUnit.SECONDS);}catch(InterruptedException e){Thread.currentThread().interrupt();}}return super.read(b,o,c);}};};file.items.add(item);h.fake.get().copy(file);check(blocked.await(2,TimeUnit.SECONDS),"outbound source streaming");h.fake.get().copy("new copy wins");unblock.countDown();while(first.getInputStream().read()!=-1){}checks++;
   try(Socket next=h.ready()){check(ClipboardWire.textContent(receive(next,h.root)).equals("new copy wins"),"newer native copy retained across reconnect");}
  }}
 }
 static void initialCopiedAgain()throws Exception{try(Harness h=new Harness()){h.session.control(1);try(Socket s=h.ready()){h.fake.get().copy("preconnect 中文");check(ClipboardWire.textContent(receive(s,h.root)).equals("preconnect 中文"),"new copy of initial clipboard must synchronize");}}}
 static void reply(Socket s,long id,int status)throws Exception{OutputStream out=s.getOutputStream();out.write(3);for(int n=0;n<8;n++)out.write((int)(id>>>(8*n))&255);out.write(status);out.flush();}
 static void rejectedCopiesCanRetry()throws Exception{
  for(int result:new int[]{1,2})try(Harness h=new Harness()){h.session.control(1);try(Socket s=h.ready()){
   h.fake.get().copy("retry same content");ClipboardWire.Snapshot first=receive(s,h.root);reply(s,first.id,result);quiet(s,"failure ACK must not automatically retry");
   h.fake.get().copy("retry same content");ClipboardWire.Snapshot retry;
   try{retry=receive(s,h.root);}catch(SocketTimeoutException e){throw new AssertionError("fresh same-content copy retries after failed ACK="+result);}
   check(retry.id!=first.id&&ClipboardWire.textContent(retry).equals("retry same content"),"retry has fresh ID and exact content");
   reply(s,retry.id,0);quiet(s,"success ACK consumed");reply(s,first.id,result);h.fake.get().copy("retry same content");quiet(s,"late failed ACK cannot invalidate newer success");
  }}
 }
 static void acknowledgmentCannotReplaceNewerPublication()throws Exception{
  try(Harness h=new Harness()){h.session.control(1);try(Socket s=h.ready()){
   h.fake.get().copy("old outgoing");ClipboardWire.Snapshot outgoing=receive(s,h.root);send(s,"new remote publication",9901);ack(s,9901,0);
   reply(s,outgoing.id,0);quiet(s,"old outgoing ACK consumed");h.fake.get().copy("new remote publication");quiet(s,"old success ACK cannot overwrite newer publication fingerprint");
  }}
 }
 public static void main(String[] args)throws Exception{rejectedCopiesCanRetry();acknowledgmentCannotReplaceNewerPublication();initialCopiedAgain();basics();canceledPublication();supersededPublication();failedSourceNoRetry();unacknowledgedReconnect();mutatedSourceNoRetry();delayedNativeCallback();canceledSendReconnect();System.out.println("PASS clipboard session "+checks+" assertions");}
}
