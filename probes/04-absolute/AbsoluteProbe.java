import java.io.RandomAccessFile;
/** Isolated bounded capability probe; does not replace the production injector. */
public class AbsoluteProbe {
    static final int[] DESC = {
        0x05,1, 0x09,1, 0xa1,1, 0x09,1, 0xa1,0,
        0x05,9, 0x19,1, 0x29,3, 0x15,0, 0x25,1, 0x95,3, 0x75,1, 0x81,2,
        0x95,1, 0x75,5, 0x81,1,
        0x05,1, 0x09,0x30, 0x09,0x31, 0x15,0, 0x26,0xff,0x7f,
        0x75,16, 0x95,2, 0x81,2,
        0x09,0x38, 0x15,0x81, 0x25,0x7f, 0x75,8, 0x95,1, 0x81,6,
        0x05,0x0d, 0x09,0x21, 0xa1,0, 0x09,0x32, 0x09,0x42,
        0x15,0, 0x25,1, 0x75,1, 0x95,2, 0x81,2, 0x75,6, 0x95,1, 0x81,1, 0xc0,
        0xc0,0xc0
    };
    static void put(byte[] b,int off,int n) { b[off]=(byte)n; b[off+1]=(byte)(n>>>8); }
    public static void main(String[] args) throws Exception {
        // Hard lifetime even if kernel device close blocks.
        Thread watchdog = new Thread(new Runnable() { public void run() {
            try { Thread.sleep(25000); } catch (Exception e) { }
            Runtime.getRuntime().halt(0);
        }}); watchdog.setDaemon(true); watchdog.start();
        RandomAccessFile dev = new RandomAccessFile("/dev/uhid", "rw");
        byte[] e = new byte[4376]; e[0]=11;
        byte[] name = "PC-KVM Absolute Probe".getBytes("UTF-8");
        System.arraycopy(name,0,e,4,name.length);
        put(e,260,DESC.length); put(e,262,3); put(e,264,0x1234); put(e,268,0x5680); put(e,272,1);
        for(int i=0;i<DESC.length;i++) e[280+i]=(byte)DESC[i];
        dev.write(e); Thread.sleep(1500);
        int x=args.length>0?Integer.parseInt(args[0]):16384;
        int y=args.length>1?Integer.parseInt(args[1]):16384;
        for(int i=0;i<100;i++) {
            e=new byte[4376]; e[0]=12; put(e,4,7); put(e,7,x); put(e,9,y); e[12]=1;
            dev.write(e); if(i==0) System.out.println("ABS sent "+x+","+y);
            Thread.sleep(150);
        }
        System.out.println("PROBE complete");
        Runtime.getRuntime().halt(0);
    }
}
