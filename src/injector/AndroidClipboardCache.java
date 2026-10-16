import java.io.*;
import java.nio.file.Files;
import java.util.*;
import java.util.function.LongSupplier;

/** Only explicit receipts may authorize deletion of this feature's own MediaStore rows. */
public final class AndroidClipboardCache {
    interface Deleter { boolean delete(String uri)throws Exception; }
    final File root;final long byteLimit;final int itemLimit;final LongSupplier clock;
    final Set<File> pending=new HashSet<File>();
    static final long DAY=24L*60*60*1000,IMAGE_GRACE=5L*60*1000,IMAGE_BYTES=64L*1024*1024;
    static final class Receipt{File file;List<String> uris=new ArrayList<String>();long bytes;boolean image;}
    public AndroidClipboardCache(File root){this(root,2L<<30,4096,()->System.currentTimeMillis());}
    AndroidClipboardCache(File root,long byteLimit,int itemLimit,LongSupplier clock){this.root=root;this.byteLimit=byteLimit;this.itemLimit=itemLimit;this.clock=clock;}
    public synchronized File record(List<String> uris,List<Long> lengths)throws IOException{return record(uris,lengths,false);}
    public synchronized File record(List<String> uris,List<Long> lengths,boolean image)throws IOException{
        if(uris.size()<1||uris.size()>128||uris.size()!=lengths.size())throw new IOException("Clipboard receipt count");
        long total=0;for(int i=0;i<uris.size();i++){
            String uri=uris.get(i);long length=lengths.get(i);
            if(uri==null||!uri.startsWith("content://media/")||uri.length()>4096||length<0||length>(1L<<30)||length>(2L<<30)-total)throw new IOException("Clipboard receipt ownership");total+=length;
        }
        if(!root.isDirectory()&&!root.mkdirs())throw new IOException("Clipboard receipt cache unavailable");
        String id=UUID.randomUUID().toString();File partial=new File(root,id+".partial"),file=new File(root,id+".receipt");
        try{
            try(DataOutputStream out=new DataOutputStream(new FileOutputStream(partial))){out.writeUTF("PC-KVM-MEDIA-2");out.writeBoolean(image);out.writeInt(uris.size());for(int i=0;i<uris.size();i++){out.writeUTF(uris.get(i));out.writeLong(lengths.get(i));}}
            if(!partial.renameTo(file))throw new IOException("Clipboard receipt commit failed");file.setLastModified(clock.getAsLong());return file;
        }finally{partial.delete();}
    }
    synchronized File recordPending(List<String> uris,List<Long> lengths,boolean image)throws IOException{
        File file=record(uris,lengths,image);pending.add(file);return file;
    }
    synchronized void complete(File file){if(file!=null)pending.remove(file);}
    synchronized void forget(File file){if(file!=null&&root.equals(file.getParentFile())&&file.getName().endsWith(".receipt")){pending.remove(file);file.delete();}}
    public synchronized void sweep(Set<String> current,Deleter deleter)throws IOException{
        File[] files=root.listFiles();if(files==null)return;List<Receipt> retired=new ArrayList<Receipt>();long total=0,imageBytes=0;int count=0,imageCount=0;long now=clock.getAsLong();
        for(File file:files){
            if(pending.contains(file))continue;
            if(!file.getName().matches("[0-9a-f-]{36}\\.receipt")||Files.isSymbolicLink(file.toPath())||file.length()>600000)continue;
            Receipt r=new Receipt();r.file=file;
            try(DataInputStream in=new DataInputStream(new FileInputStream(file))){
                String version=in.readUTF();boolean legacy="PC-KVM-MEDIA-1".equals(version);
                if(!legacy&&!"PC-KVM-MEDIA-2".equals(version))continue;
                if(!legacy)r.image=in.readBoolean();int size=in.readInt();if(size<1||size>128)continue;
                boolean invalid=false;for(int i=0;i<size;i++){String uri=in.readUTF();long bytes=in.readLong();if(!uri.startsWith("content://media/")||uri.length()>4096||bytes<0||bytes>(1L<<30)||bytes>(2L<<30)-r.bytes){invalid=true;break;}r.uris.add(uri);r.bytes+=bytes;if(legacy&&uri.contains("/images/media/"))r.image=true;}
                if(invalid||in.read()!=-1)continue;
            }catch(IOException invalid){continue;}
            boolean referenced=false;for(String uri:r.uris)if(current.contains(uri)){referenced=true;break;}
            if(referenced){file.setLastModified(now);continue;}
            total+=r.bytes;count+=r.uris.size();if(r.image){imageBytes+=r.bytes;imageCount+=r.uris.size();}retired.add(r);
        }
        Collections.sort(retired,(a,b)->Long.compare(a.file.lastModified(),b.file.lastModified()));
        for(Receipt r:retired){
            long lifetime=r.image?IMAGE_GRACE:DAY;
            if(now-r.file.lastModified()<lifetime&&total<=byteLimit&&count<=itemLimit&&(!r.image||(imageBytes<=IMAGE_BYTES&&imageCount<=8)))continue;
            boolean deleted=true;for(String uri:r.uris)try{if(!deleter.delete(uri))deleted=false;}catch(Exception error){deleted=false;}
            if(deleted&&r.file.delete()){total-=r.bytes;count-=r.uris.size();if(r.image){imageBytes-=r.bytes;imageCount-=r.uris.size();}}
        }
    }
}
