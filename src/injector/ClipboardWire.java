import java.io.*;
import java.nio.ByteBuffer;
import java.nio.charset.CharacterCodingException;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.*;
import java.util.function.BooleanSupplier;

/** Bounded snapshot codec. Authentication and connection ownership belong to the session. */
public final class ClipboardWire {
    private static final int CHUNK=65536, METADATA_LIMIT=4096, TEXT_LIMIT=1048576;
    private static final long FILE_LIMIT=1073741824L, TOTAL_LIMIT=2147483648L;
    public interface Source { InputStream open() throws IOException; }
    public static final class Item {
        public String name,mime;
        public long length;
        public Source open;
        public File file;
    }
    public static final class Snapshot {
        public int kind;
        public long id;
        public List<Item> items=new ArrayList<Item>();
        private String cachedFingerprint;
        // First evaluation for captured files belongs on the background transfer worker.
        public String fingerprint() throws IOException {
            if(cachedFingerprint==null)write(new OutputStream(){public void write(int b){}public void write(byte[] b,int o,int n){}},this,null);
            return cachedFingerprint;
        }
    }
    private static void cancel(BooleanSupplier canceled)throws IOException{
        if(canceled!=null&&canceled.getAsBoolean())throw new InterruptedIOException("Clipboard transfer canceled");
    }
    private static MessageDigest sha(){try{return MessageDigest.getInstance("SHA-256");}catch(NoSuchAlgorithmException e){throw new AssertionError(e);}}
    private static void number(OutputStream out,long value,int bytes)throws IOException{for(int n=0;n<bytes;n++)out.write((int)(value>>>(n*8))&255);}
    private static long number(InputStream in,int bytes)throws IOException{
        long value=0;for(int n=0;n<bytes;n++){int b=in.read();if(b<0)throw new EOFException("Truncated clipboard record");value|=(long)b<<(n*8);}if(value<0)throw new IOException("Clipboard integer overflow");return value;
    }
    private static byte[] encoded(String s)throws IOException{
        if(s==null)throw new IOException("Missing metadata");
        try{ByteBuffer b=StandardCharsets.UTF_8.newEncoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).encode(java.nio.CharBuffer.wrap(s));byte[] out=new byte[b.remaining()];b.get(out);if(out.length<1||out.length>METADATA_LIMIT)throw new IOException("Metadata length");return out;}catch(CharacterCodingException e){throw new IOException("Invalid metadata UTF-8",e);}
    }
    private static void string(OutputStream out,String s)throws IOException{byte[] b=encoded(s);number(out,b.length,4);out.write(b);}
    private static void exact(InputStream in,byte[] b,int count)throws IOException{int off=0;while(off<count){int n=in.read(b,off,count-off);if(n<=0)throw new EOFException("Truncated clipboard content");off+=n;}}
    private static String decode(byte[] b)throws IOException{try{return StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(b)).toString();}catch(CharacterCodingException e){throw new IOException("Invalid UTF-8",e);}}
    private static String string(InputStream in)throws IOException{long length=number(in,4);if(length<1||length>METADATA_LIMIT)throw new IOException("Metadata length");byte[] b=new byte[(int)length];exact(in,b,b.length);return decode(b);}
    private static void name(String name)throws IOException{
        encoded(name);if(name.equals(".")||name.equals("..")||name.endsWith(" ")||name.endsWith("."))throw new IOException("Unsafe clipboard name");
        for(int n=0;n<name.length();n++){char c=name.charAt(n);if(Character.isISOControl(c)||"<>:\"/\\|?*".indexOf(c)>=0)throw new IOException("Unsafe clipboard name");}
        String stem=name.split("\\.",-1)[0].replaceAll(" +$","").toUpperCase(Locale.ROOT);
        if(stem.equals("CON")||stem.equals("PRN")||stem.equals("AUX")||stem.equals("NUL")||stem.equals("CONIN$")||stem.equals("CONOUT$")||(stem.length()==4&&(stem.startsWith("COM")||stem.startsWith("LPT"))&&"123456789\u00b9\u00b2\u00b3".indexOf(stem.charAt(3))>=0))throw new IOException("Reserved clipboard name");
    }
    private static void header(int kind,long id,int count)throws IOException{if(id<=0||count<1||count>128||kind<1||kind>3||(kind!=3&&count!=1))throw new IOException("Invalid clipboard header");}
    private static long item(int kind,Item item,long total)throws IOException{
        if(item==null)throw new IOException("Missing item");name(item.name);encoded(item.mime);
        if(item.length<0||item.length>FILE_LIMIT||(kind==1&&item.length>TEXT_LIMIT)||item.length>TOTAL_LIMIT-total)throw new IOException("Clipboard content limit");return total+item.length;
    }
    private static byte[] copy(InputStream in,OutputStream out,long length,BooleanSupplier canceled)throws IOException{
        byte[] buffer=new byte[CHUNK];MessageDigest hash=sha();while(length>0){cancel(canceled);int n=in.read(buffer,0,(int)Math.min(buffer.length,length));if(n<=0)throw new EOFException("Clipboard source ended early");out.write(buffer,0,n);hash.update(buffer,0,n);length-=n;}cancel(canceled);return hash.digest();
    }
    private static ByteArrayOutputStream fingerStart(int kind,int count)throws IOException{ByteArrayOutputStream b=new ByteArrayOutputStream();b.write(kind);number(b,count,4);return b;}
    private static void fingerItem(OutputStream out,Item i,byte[] digest)throws IOException{string(out,i.name);string(out,i.mime);number(out,i.length,8);out.write(digest);}
    private static String fingerEnd(ByteArrayOutputStream b){byte[] digest=sha().digest(b.toByteArray());StringBuilder hex=new StringBuilder(64);for(byte v:digest){hex.append(Character.forDigit((v>>>4)&15,16));hex.append(Character.forDigit(v&15,16));}return hex.toString();}
    public static Snapshot text(String text)throws IOException{
        byte[] bytes;
        try{ByteBuffer b=StandardCharsets.UTF_8.newEncoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).encode(java.nio.CharBuffer.wrap(text==null?"":text));bytes=new byte[b.remaining()];b.get(bytes);}catch(CharacterCodingException e){throw new IOException("Invalid text UTF-8",e);}
        if(bytes.length>TEXT_LIMIT)throw new IOException("Clipboard text limit");Snapshot s=new Snapshot();s.kind=1;s.id=Math.max(1,System.currentTimeMillis());Item item=new Item();item.name="text.txt";item.mime="text/plain";item.length=bytes.length;item.open=()->new ByteArrayInputStream(bytes);s.items.add(item);return s;
    }
    public static String textContent(Snapshot s)throws IOException{
        if(s==null||s.kind!=1||s.items.size()!=1||s.items.get(0).length<0||s.items.get(0).length>TEXT_LIMIT)throw new IOException("Not a text snapshot");Item item=s.items.get(0);byte[] bytes=new byte[(int)item.length];try(InputStream in=item.open.open()){exact(in,bytes,bytes.length);if(in.read()!=-1)throw new IOException("Text source changed");}return decode(bytes);
    }
    public static void write(OutputStream out,Snapshot s,BooleanSupplier canceled)throws IOException{
        if(s==null||s.items==null)throw new IOException("Missing snapshot");header(s.kind,s.id,s.items.size());long total=0;for(Item item:s.items){total=item(s.kind,item,total);if(item.open==null)throw new IOException("Missing content source");}
        cancel(canceled);s.cachedFingerprint=null;out.write(s.kind);number(out,s.id,8);number(out,s.items.size(),4);ByteArrayOutputStream fingerprint=fingerStart(s.kind,s.items.size());
        for(Item item:s.items){cancel(canceled);string(out,item.name);string(out,item.mime);number(out,item.length,8);byte[] digest;try(InputStream in=item.open.open()){if(in==null)throw new IOException("Missing source stream");digest=copy(in,out,item.length,canceled);if(in.read()!=-1)throw new IOException("Clipboard source grew");}out.write(digest);fingerItem(fingerprint,item,digest);}
        cancel(canceled);s.cachedFingerprint=fingerEnd(fingerprint);
    }
    public static Snapshot read(InputStream in,File cacheRoot,BooleanSupplier canceled)throws IOException{
        cancel(canceled);int kind=in.read();if(kind<0)throw new EOFException("Clipboard connection closed");Snapshot s=new Snapshot();s.kind=kind;s.id=number(in,8);long count=number(in,4);if(count>128)throw new IOException("Clipboard item limit");header(s.kind,s.id,(int)count);
        if(!cacheRoot.isDirectory()&&!cacheRoot.mkdirs())throw new IOException("Cannot create clipboard cache");String token=UUID.randomUUID().toString().replace("-","");File partial=new File(cacheRoot,token+".partial"),complete=new File(cacheRoot,token);boolean owned=false;
        try{
            if(complete.exists()||!partial.mkdir())throw new IOException("Cache collision");owned=true;long total=0;ByteArrayOutputStream fingerprint=fingerStart(s.kind,(int)count);
            for(int n=0;n<count;n++){
                cancel(canceled);Item item=new Item();item.name=string(in);item.mime=string(in);item.length=number(in,8);total=item(s.kind,item,total);File itemDir=new File(partial,Integer.toString(n));if(!itemDir.mkdir())throw new IOException("Cannot create item cache");item.file=new File(itemDir,item.name);if(!item.file.createNewFile())throw new IOException("Cache collision");byte[] digest;
                try(OutputStream out=new FileOutputStream(item.file)){digest=copy(in,out,item.length,canceled);}byte[] expected=new byte[32];exact(in,expected,32);if(!MessageDigest.isEqual(digest,expected))throw new IOException("Clipboard digest mismatch");item.open=()->new FileInputStream(item.file);s.items.add(item);fingerItem(fingerprint,item,digest);
            }
            if(s.kind==1)textContent(s);s.cachedFingerprint=fingerEnd(fingerprint);cancel(canceled);for(int n=0;n<s.items.size();n++)s.items.get(n).file=new File(new File(complete,Integer.toString(n)),s.items.get(n).name);
            if(complete.exists()||!partial.renameTo(complete))throw new IOException("Cannot commit clipboard cache");return s;
        }catch(IOException|RuntimeException|Error e){if(owned)deleteOwned(partial);throw e;}
    }
    // Called only for the unique partial directory created by this read operation.
    private static void deleteOwned(File file){File[] children=file.listFiles();if(children!=null)for(File child:children)deleteOwned(child);file.delete();}
}
