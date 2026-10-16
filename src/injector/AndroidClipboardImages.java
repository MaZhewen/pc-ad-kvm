import java.io.*;
import java.nio.file.Files;
import java.util.function.BooleanSupplier;

/** Hidden, bounded image cache; the provider registers only the exact file created here. */
public final class AndroidClipboardImages {
    interface Publisher {
        String scan(File file)throws Exception;
        void delete(String uri)throws Exception;
    }
    static final long MAX_IMAGE=64L*1024*1024;
    final File root;
    final Publisher publisher;
    AndroidClipboardImages(File root,Publisher publisher){this.root=root;this.publisher=publisher;}
    static void allowed(BooleanSupplier active)throws IOException {
        if(!active.getAsBoolean())throw new InterruptedIOException("Clipboard image canceled");
    }
    String store(ClipboardWire.Item item,BooleanSupplier active)throws Exception {
        if(item==null||item.open==null||item.length<1||item.length>MAX_IMAGE)throw new IOException("Clipboard image limit");
        allowed(active);
        if(Files.isSymbolicLink(root.toPath())||(!root.isDirectory()&&!root.mkdirs()))throw new IOException("Clipboard image cache unavailable");
        File marker=new File(root,".nomedia");
        if(Files.isSymbolicLink(marker.toPath()))throw new IOException("Clipboard image cache marker invalid");
        if(!marker.createNewFile()&&!marker.isFile())throw new IOException("Clipboard image cache marker unavailable");
        String suffix="image/jpeg".equals(item.mime)?".jpg":"image/webp".equals(item.mime)?".webp":"image/gif".equals(item.mime)?".gif":".png";
        File file=File.createTempFile("clipboard-",suffix,root);String uri=null;boolean committed=false;
        try {
            try(InputStream in=item.open.open();OutputStream out=new FileOutputStream(file)) {
                if(in==null)throw new IOException("Clipboard image source unavailable");
                byte[] buffer=new byte[65536];long total=0;int count;
                while((count=in.read(buffer))!=-1){allowed(active);if(count>item.length-total)throw new IOException("Clipboard image length changed");out.write(buffer,0,count);total+=count;}
                if(total!=item.length)throw new IOException("Clipboard image length changed");
            }
            allowed(active);uri=publisher.scan(file);
            if(uri==null||!uri.matches("content://media/(external_primary|external)/file/[0-9]+")){uri=null;throw new IOException("Hidden image URI unavailable");}
            allowed(active);
            committed=true;return uri;
        } finally {
            if(!committed){if(uri!=null)try{publisher.delete(uri);}catch(Exception ignored){}file.delete();}
        }
    }
}
