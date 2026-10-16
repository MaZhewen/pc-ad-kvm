import java.io.*;
import java.net.*;
import java.util.ArrayDeque;
import java.util.function.BooleanSupplier;

/** Optional clipboard channel. No work or Android service access occurs in the constructor. */
public final class ClipboardSession implements AutoCloseable {
    interface Adapter extends AutoCloseable {
        ClipboardWire.Snapshot capture() throws Exception;
        long revision();
        Object identity() throws Exception;
        boolean publish(ClipboardWire.Snapshot snapshot,long revision,Object expectedIdentity,BooleanSupplier allowed) throws Exception;
        void release(ClipboardWire.Snapshot snapshot);
        void close();
    }
    interface AdapterFactory { Adapter create(File cache,Runnable changed) throws Exception; }
    private final byte[] token;
    private final int port;
    private final File cacheRoot,incoming;
    private final AdapterFactory factory;
    private final Object state=new Object();
    private volatile boolean enabled,closed;
    private volatile Link connection;
    private volatile Adapter adapter;
    private volatile String lastFingerprint,lastFailure;
    private Thread worker;
    private boolean pendingChange,forcePending;
    private long changeVersion,nextId,publicationVersion;
    private volatile long generation;

    public ClipboardSession(byte[] token) {
        this(token,27184,new File("/data/local/tmp/pckvm-clipboard"),(cache,changed)->{
            final AndroidClipboard clipboard=new AndroidClipboard(cache,changed);
            return new Adapter() {
                public ClipboardWire.Snapshot capture()throws Exception{return clipboard.capture();}
                public long revision(){return clipboard.revision();}
                public Object identity()throws Exception{return clipboard.identity();}
                public boolean publish(ClipboardWire.Snapshot s,long revision,Object identity,BooleanSupplier allowed)throws Exception{return clipboard.publish(s,revision,identity,allowed);}
                public void release(ClipboardWire.Snapshot s){clipboard.release(s);}
                public void close(){clipboard.close();}
            };
        });
    }
    ClipboardSession(byte[] token,int port,File root,AdapterFactory factory) {
        if(token==null||token.length!=32)throw new IllegalArgumentException("Clipboard token length");
        this.token=token.clone();this.port=port;cacheRoot=root;incoming=new File(root,"incoming");this.factory=factory;
    }
    public void control(int flags) {
        if(closed)return;
        if(flags==0) {
            enabled=false;Link link=connection;if(link!=null)link.cancel();
            synchronized(state){generation++;forcePending=false;pendingChange=false;changeVersion++;state.notifyAll();}
        } else if(flags==1) {
            synchronized(state){if(closed)return;enabled=true;if(worker==null){worker=new Thread(()->run(),"pckvm-clipboard");worker.setDaemon(true);worker.start();}state.notifyAll();}
        } else if(flags==2) {
            synchronized(state){if(enabled&&!closed){forcePending=true;state.notifyAll();}}
        }
    }
    public void close() {
        closed=true;enabled=false;Link link=connection;if(link!=null)link.cancel();
        synchronized(state){forcePending=false;pendingChange=false;state.notifyAll();}
    }
    private void changed() {
        if(!enabled||closed)return;
        synchronized(state){pendingChange=true;changeVersion++;Link link=connection;if(link!=null&&link.sending){forcePending=true;link.cancel();}state.notifyAll();}
    }
    private boolean alive(Link link,Adapter local) {
        return !closed&&enabled&&link.active&&connection==link&&adapter==local&&link.generation==generation;
    }
    private void run() {
        while(!closed) {
            synchronized(state){while(!closed&&!enabled)try{state.wait();}catch(InterruptedException e){Thread.currentThread().interrupt();return;}}
            if(closed)return;
            long activation=generation;
            Adapter local=null;
            try {
                local=factory.create(new File(cacheRoot,"capture"),()->changed());
                adapter=local;
                if(!enabled||closed||generation!=activation)continue;
                baseline(local);
                while(enabled&&!closed&&generation==activation) {
                    Link link=new Link(activation);connection=link;
                    try {
                        connect(link,local);
                        if(!alive(link,local))continue;
                        link.out.write(0);link.out.flush();
                        final Adapter current=local;
                        link.reader=new Thread(()->read(link,current),"pckvm-clipboard-reader");link.reader.setDaemon(true);link.reader.start();
                        send(link,local);
                    } catch(Exception e) { if(enabled&&!closed)unavailable(e); }
                    finally {link.cancel();synchronized(state){if(link.awaitingId!=0&&enabled&&!closed&&generation==activation)forcePending=true;}if(connection==link)connection=null;}
                    pauseRetry();
                }
            } catch(Exception e) {if(enabled&&!closed)unavailable(e);}
            finally {if(local!=null){try{local.close();}catch(RuntimeException e){unavailable(e);}}if(adapter==local)adapter=null;lastFingerprint=null;}
            pauseRetry();
        }
    }
    private void baseline(Adapter local) {
        ClipboardWire.Snapshot snapshot=null;long revision=local.revision();
        try {
            snapshot=local.capture();
            // Local baseline observation does not prove the peer has that content.
            if(local.revision()==revision)lastFingerprint=null;
            else synchronized(state){forcePending=true;}
        } catch(Exception e) {unavailable(e);}
        finally {if(snapshot!=null)local.release(snapshot);}
    }
    private void connect(Link link,Adapter local)throws IOException {
        if(!alive(link,local))throw new InterruptedIOException();
        link.socket.connect(new InetSocketAddress("127.0.0.1",port),2000);link.socket.setSoTimeout(2000);
        link.in=new BufferedInputStream(link.socket.getInputStream(),65536);link.out=new BufferedOutputStream(link.socket.getOutputStream(),65536);
        link.out.write(new byte[]{'P','K','C','B',1});link.out.write(token);link.out.flush();
        if(link.in.read()!=1)throw new IOException("Clipboard authentication rejected");link.socket.setSoTimeout(0);lastFailure=null;
    }
    private void send(Link link,Adapter local)throws Exception {
        while(alive(link,local)) {
            Ack ack=null;boolean work=false,forced=false;long version=0;
            synchronized(state) {
                if(!alive(link,local))return;
                if(!link.acks.isEmpty())ack=link.acks.removeFirst();
                else if(forcePending||pendingChange){work=true;forced=forcePending;forcePending=false;pendingChange=false;version=changeVersion;}
                else {state.wait(500);continue;}
            }
            if(ack!=null){link.out.write(3);number(link.out,ack.id);link.out.write(ack.status);link.out.flush();continue;}
            if(work)sendSnapshot(link,local,forced,version);
        }
    }
    private void sendSnapshot(Link link,Adapter local,boolean forced,long version) {
        ClipboardWire.Snapshot snapshot=null;boolean writing=false;final boolean[] sourceFailed={false};
        try {
            long published; synchronized(state){published=publicationVersion;}
            long revision=local.revision();snapshot=local.capture();if(snapshot==null){status("pending");return;}
            String fingerprint=snapshot.fingerprint();
            if(!alive(link,local)){if(forced)synchronized(state){forcePending=true;}return;}
            if(local.revision()!=revision){synchronized(state){forcePending=true;state.notifyAll();}return;}
            if(!forced&&fingerprint.equals(lastFingerprint))return;
            synchronized(state){if(version!=changeVersion){forcePending=true;return;}link.sending=true;}
            writing=true;ClipboardWire.Snapshot outgoing=tracked(snapshot,sourceFailed);outgoing.id=++nextId;
            Transmission sent=new Transmission(outgoing.id,revision,version,published);
            synchronized(state){link.outgoing=sent;link.awaitingId=outgoing.id;lastFingerprint=null;}status("pending");
            link.out.write(1);ClipboardWire.write(link.out,outgoing,()->!alive(link,local)||local.revision()!=revision);link.out.flush();
            long currentRevision=local.revision();
            synchronized(state){if(alive(link,local)){sent.fingerprint=outgoing.fingerprint();confirmOutgoing(link,sent,currentRevision);}}
        } catch(Exception e) {
            if(enabled&&!closed)unavailable(e);
            if(writing){if(sourceFailed[0])link.awaitingId=0;link.cancel();}
        } finally {link.sending=false;if(snapshot!=null)local.release(snapshot);}
    }
    private void confirmOutgoing(Link link,Transmission sent,long currentRevision) {
        // A successful ACK and a completed write may arrive in either order.
        if(link.outgoing==sent&&sent.result==0&&sent.fingerprint!=null&&sent.change==changeVersion&&
           sent.publication==publicationVersion&&currentRevision==sent.revision)lastFingerprint=sent.fingerprint;
    }
    private static ClipboardWire.Snapshot tracked(ClipboardWire.Snapshot original,boolean[] sourceFailed) {
        ClipboardWire.Snapshot outgoing=new ClipboardWire.Snapshot();outgoing.kind=original.kind;
        for(ClipboardWire.Item source:original.items) {
            ClipboardWire.Item item=new ClipboardWire.Item();item.name=source.name;item.mime=source.mime;item.length=source.length;item.file=source.file;
            item.open=()->{
                try {
                    InputStream opened=source.open.open();if(opened==null)throw new IOException("Missing clipboard source");
                    return new FilterInputStream(opened) {
                        long remaining=source.length;
                        int checked(int n){if(n<0&&remaining>0)sourceFailed[0]=true;if(n>0){remaining-=n;if(remaining<0)sourceFailed[0]=true;}return n;}
                        public int read()throws IOException{try{int n=in.read();checked(n<0?-1:1);return n;}catch(IOException e){sourceFailed[0]=true;throw e;}}
                        public int read(byte[] b,int off,int length)throws IOException{try{return checked(in.read(b,off,length));}catch(IOException e){sourceFailed[0]=true;throw e;}}
                        public void close()throws IOException{try{super.close();}catch(IOException e){sourceFailed[0]=true;throw e;}}
                    };
                } catch(IOException|RuntimeException e){sourceFailed[0]=true;throw e;}
            };
            outgoing.items.add(item);
        }
        return outgoing;
    }
    private void read(Link link,Adapter local) {
        try {
            while(alive(link,local)) {
                int marker=link.in.read();if(marker<0)throw new EOFException("Clipboard closed");
                if(marker==0)continue;
                if(marker==2){synchronized(state){if(alive(link,local)){forcePending=true;state.notifyAll();}}continue;}
                if(marker==3){
                    long id=number(link.in);int result=link.in.read();if(id<=0||result<0||result>2)throw new IOException("Invalid clipboard ACK");
                    long currentRevision=local.revision();
                    synchronized(state){if(alive(link,local)&&link.awaitingId==id){
                        link.awaitingId=0;Transmission sent=link.outgoing;sent.result=result;confirmOutgoing(link,sent,currentRevision);
                        status(result==0?"sent":result==1?"superseded":"failed");
                    }}continue;
                }
                if(marker!=1)throw new IOException("Invalid clipboard marker");
                long revision=local.revision();Object nativeIdentity=local.identity();ClipboardWire.Snapshot snapshot=null;
                try {
                    snapshot=ClipboardWire.read(link.in,incoming,()->!alive(link,local));int result=1;
                    try {
                        if(alive(link,local)&&local.revision()==revision&&local.publish(snapshot,revision,nativeIdentity,()->alive(link,local))) {
                            synchronized(state){if(alive(link,local)){lastFingerprint=snapshot.fingerprint();publicationVersion++;}}result=0;status("received");
                        }
                    } catch(Exception e){result=2;unavailable(e);}
                    synchronized(state){if(alive(link,local)){if(link.acks.size()>=128)throw new IOException("Clipboard ACK queue limit");link.acks.addLast(new Ack(snapshot.id,result));state.notifyAll();}}
                } finally {releaseIncoming(snapshot);}
            }
        } catch(Exception e) {if(alive(link,local))unavailable(e);}
        finally {link.cancel();synchronized(state){state.notifyAll();}}
    }
    private void releaseIncoming(ClipboardWire.Snapshot snapshot) {
        if(snapshot==null||snapshot.items.isEmpty())return;
        try {
            File item=snapshot.items.get(0).file;if(item==null)return;
            File dir=item.getCanonicalFile().getParentFile().getParentFile();
            if(dir.getName().matches("[a-f0-9]{32}")&&incoming.getCanonicalFile().equals(dir.getParentFile()))removeOwned(dir);
        } catch(IOException ignored){}
    }
    private static void removeOwned(File file) {
        File[] children=file.listFiles();if(children!=null)for(File child:children)removeOwned(child);file.delete();
    }
    private void pauseRetry() {
        synchronized(state){if(enabled&&!closed)try{state.wait(500);}catch(InterruptedException e){Thread.currentThread().interrupt();closed=true;}}
    }
    private void unavailable(Exception e) {
        String type=e.getClass().getSimpleName();if(!type.equals(lastFailure)){lastFailure=type;System.out.println("CLIPBOARD unavailable "+type);}
    }
    private static void status(String status){System.out.println("CLIPBOARD "+status);}
    private static void number(OutputStream out,long value)throws IOException {for(int n=0;n<8;n++)out.write((int)(value>>>(8*n))&255);}
    private static long number(InputStream in)throws IOException {
        long value=0;for(int n=0;n<8;n++){int b=in.read();if(b<0)throw new EOFException();value|=(long)b<<(8*n);}if(value<0)throw new IOException("Clipboard ACK overflow");return value;
    }
    private static final class Ack {final long id;final int status;Ack(long id,int status){this.id=id;this.status=status;}}
    private static final class Transmission {
        final long id,revision,change,publication;String fingerprint;int result=-1;
        Transmission(long id,long revision,long change,long publication){this.id=id;this.revision=revision;this.change=change;this.publication=publication;}
    }
    private static final class Link {
        final Socket socket=new Socket();final ArrayDeque<Ack> acks=new ArrayDeque<Ack>();
        final long generation;Link(long generation){this.generation=generation;}
        volatile boolean active=true,sending;
        volatile long awaitingId;Transmission outgoing;
        InputStream in;OutputStream out;Thread reader;
        void cancel(){active=false;try{socket.close();}catch(IOException ignored){}}
    }
}
