import java.io.*;
import java.nio.file.Files;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;

public class ImagesMain {
    static int checks;
    static void check(boolean value,String reason){if(!value)throw new AssertionError(reason);checks++;}
    static ClipboardWire.Item item(byte[] bytes){ClipboardWire.Item i=new ClipboardWire.Item();i.name="../source-name.png";i.mime="image/png";i.length=bytes.length;i.open=()->new ByteArrayInputStream(bytes);return i;}
    static final class Provider implements AndroidClipboardImages.Publisher {
        File image;int scans,deletes;Runnable scanned;
        public String scan(File file){scans++;image=file;if(scanned!=null)scanned.run();return "content://media/external_primary/file/9001";}
        public void delete(String uri){deletes++;if(image!=null)image.delete();}
    }
    static long files(File root){File[] files=root.listFiles(f->f.getName().startsWith("clipboard-"));return files==null?0:files.length;}
    public static void main(String[] args)throws Exception {
        File parent=Files.createTempDirectory("pckvm-hidden-images-").toFile(),root=new File(parent,".PC-KVM-Clipboard");
        Provider provider=new Provider();AndroidClipboardImages store=new AndroidClipboardImages(root,provider);byte[] bytes={1,2,3,4};
        try {
            String uri=store.store(item(bytes),()->true);
            check(uri.endsWith("/9001")&&provider.scans==1,"exact owned file registered once");
            check(provider.image.getParentFile().equals(root)&&root.getName().startsWith("."),"cache is in hidden directory");
            check(new File(root,".nomedia").isFile(),"cache has media scanner exclusion marker");
            check(Arrays.equals(Files.readAllBytes(provider.image.toPath()),bytes),"image bytes preserved");
            check(!provider.image.getName().contains("source-name"),"source filename cannot escape cache");
            provider.image.delete();
            AtomicBoolean active=new AtomicBoolean(true);ClipboardWire.Item canceled=item(bytes);canceled.open=()->new ByteArrayInputStream(bytes){public synchronized int read(byte[] b,int o,int n){int count=super.read(b,o,n);active.set(false);return count;}};
            try{store.store(canceled,()->active.get());throw new AssertionError("canceled copy accepted");}catch(InterruptedIOException expected){}
            check(files(root)==0&&provider.scans==1,"canceled copy removes partial and never registers URI");
            active.set(true);provider.scanned=()->active.set(false);
            try{store.store(item(bytes),()->active.get());throw new AssertionError("canceled publish accepted");}catch(InterruptedIOException expected){}
            check(provider.deletes==1&&files(root)==0,"cancel after registration removes URI and owned file");
            provider.scanned=null;ClipboardWire.Item shortSource=item(bytes);shortSource.length=5;
            try{store.store(shortSource,()->true);throw new AssertionError("short image accepted");}catch(IOException expected){}
            check(files(root)==0,"short source leaves no image");
            ClipboardWire.Item huge=item(bytes);huge.length=AndroidClipboardImages.MAX_IMAGE+1;
            try{store.store(huge,()->true);throw new AssertionError("oversized image accepted");}catch(IOException expected){}
            check(files(root)==0,"image bound checked before materialization");
            File unrelated=new File(root,"keep.txt");Files.write(unrelated.toPath(),bytes);
            try{store.store(item(bytes),()->false);throw new AssertionError("disabled accepted");}catch(InterruptedIOException expected){}
            check(unrelated.exists(),"cleanup preserves unrelated files");
            System.out.println("PASS hidden clipboard images "+checks+" assertions");
        } finally {File[] files=root.listFiles();if(files!=null)for(File file:files)file.delete();root.delete();parent.delete();}
    }
}
