import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

/** Strict, non-executable bootstrap data for a single detached daemon instance. */
public final class DirectSessionConfig {
    public final String host, pin, instanceId;
    public final int port;
    public final byte[] token;
    private final DirectOwner owner;
    private volatile boolean stopped;

    private DirectSessionConfig(String host,int port,String pin,byte[] token,String instanceId) {
        this.host=host;this.port=port;this.pin=pin;this.token=token;this.instanceId=instanceId;
        owner = new DirectOwner(instanceId);
    }
    public static DirectSessionConfig load(String path)throws IOException {
        if(path==null||!path.matches("/data/local/tmp/pckvm-direct-[a-f0-9]{32}/config"))
            throw new IllegalArgumentException("Invalid direct configuration path");
        File file=new File(path);
        if(!file.getCanonicalPath().equals(path))throw new IOException("Configuration must not be a symlink");
        DirectSessionConfig config=parse(read(file,512));
        if(!path.equals(new File(config.owner.directory(), "config").getPath()))
            throw new IllegalArgumentException("Configuration instance mismatch");
        return config;
    }
    static DirectSessionConfig parse(String text) {
        if(text==null||text.length()>512)throw new IllegalArgumentException("Invalid configuration length");
        for(int i=0;i<text.length();i++)if(text.charAt(i)>127||text.charAt(i)==0)
            throw new IllegalArgumentException("Configuration must be ASCII");
        String[] lines=text.split("\\r?\\n",-1);
        if(lines.length==7&&lines[6].isEmpty())lines=java.util.Arrays.copyOf(lines,6);
        if(lines.length!=6||!lines[0].equals("1"))throw new IllegalArgumentException("Invalid configuration version or fields");
        String[] address=lines[1].split("\\.",-1);
        if(address.length!=4)throw new IllegalArgumentException("IPv4 address required");
        for(String octet:address)if(!octet.matches("0|[1-9][0-9]{0,2}")||Integer.parseInt(octet)>255)
            throw new IllegalArgumentException("Invalid IPv4 address");
        if(!lines[2].matches("[1-9][0-9]{0,4}"))throw new IllegalArgumentException("Invalid direct port");
        int port=Integer.parseInt(lines[2]);
        if(port>65535||!lines[3].matches("[a-fA-F0-9]{64}")||!lines[4].matches("[a-fA-F0-9]{64}")||!lines[5].matches("[a-f0-9]{32}"))
            throw new IllegalArgumentException("Invalid direct credentials");
        return new DirectSessionConfig(lines[1],port,lines[3],SessionLink.parseToken(lines[4]),lines[5]);
    }
    public boolean owns(){return !stopped && owner.owns();}
    boolean owns(File owner) {
        if(stopped)return false;
        try {String text=read(owner,34);return text.equals(instanceId)||text.equals(instanceId+"\n")||text.equals(instanceId+"\r\n");}
        catch(IOException e){return false;}
    }
    public void stop(){stopped=true;}
    Closeable acquireRunLock() throws IOException { return owner.acquireRunLock(); }
    void cleanup() throws IOException {
        try { owner.cleanup(); }
        finally { java.util.Arrays.fill(token, (byte)0); }
    }
    static String read(File file,int limit)throws IOException {
        try(InputStream input=Files.newInputStream(file.toPath())) {
            ByteArrayOutputStream output=new ByteArrayOutputStream();int value;
            while((value=input.read())!=-1){if(output.size()>=limit||value>127)throw new IOException("Invalid configuration size or encoding");output.write(value);}
            return new String(output.toByteArray(),StandardCharsets.US_ASCII);
        }
    }
}
