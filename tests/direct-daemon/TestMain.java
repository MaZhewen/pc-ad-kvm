import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.Arrays;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.Executors;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Future;
import java.util.concurrent.TimeoutException;
import java.util.function.BooleanSupplier;

public final class TestMain {
    static final String ID="0123456789abcdef0123456789abcdef";
    static final String PIN=ID+ID;
    static int checks;
    static void check(boolean value,String message){checks++;if(!value)throw new AssertionError(message);}
    static String config(){return "1\n192.168.1.23\n27185\n"+PIN+"\n"+PIN+"\n"+ID+"\n";}
    static void invalid(String text)throws Exception {
        try{DirectSessionConfig.parse(text);throw new AssertionError("Accepted invalid config");}
        catch(IllegalArgumentException expected){checks++;}
    }
    public static void main(String[] args)throws Exception {
        if (args.length == 2 && args[0].equals("--hold-owner-lock")) {
            try (RandomAccessFile file = new RandomAccessFile(new File(args[1], "pckvm-direct.owner.lock"), "rw");
                    java.nio.channels.FileLock lock = file.getChannel().lock()) {
                System.out.println("LOCKED");
                System.out.flush();
                System.in.read();
            }
            return;
        }
        DirectSessionConfig c=DirectSessionConfig.parse(config());
        check(c.host.equals("192.168.1.23")&&c.port==27185&&c.token.length==32,"valid config");
        invalid(config().replace("192.168.1.23","localhost;reboot"));
        invalid(config().replace("192.168.1.23","256.1.2.3"));
        invalid(config().replace("192.168.1.23","1.2.3.04"));
        invalid(config().replace("27185","0"));invalid(config().replace("27185","65536"));
        invalid(config().replace(PIN,"z"+PIN.substring(1)));
        invalid(config()+"extra\n");invalid(config().replace(ID+"\n","../escape\n"));
        invalid(config().replace("192.168.1.23","192.168.1.23\u0000"));
        try{DirectSessionConfig.load("/data/local/tmp/../evil.conf");throw new AssertionError("path injection");}
        catch(IllegalArgumentException expected){checks++;}
        File owner=File.createTempFile("pckvm-owner-",".txt");
        try {
            Files.write(owner.toPath(),(ID+"\n").getBytes(StandardCharsets.US_ASCII));
            check(c.owns(owner),"owns matching marker");
            Files.write(owner.toPath(),("f"+ID.substring(1)).getBytes(StandardCharsets.US_ASCII));
            check(!c.owns(owner),"replacement revokes owner");
            owner.delete();check(!c.owns(owner),"missing marker revokes owner");
        }finally{owner.delete();}
        check(Arrays.equals(DeviceGeometry.encode(1080,2400,1),new byte[]{16,56,4,96,9,1}),"geometry wire encoding");
        try{DeviceGeometry.encode(0,2400,1);throw new AssertionError("invalid geometry");}catch(IllegalArgumentException expected){checks++;}
        try{DeviceGeometry.encode(1080,2400,4);throw new AssertionError("invalid rotation");}catch(IllegalArgumentException expected){checks++;}
        testLoop(false);testLoop(true);testOwnership();testOwnerFiles();testBackoff();testExpiredReady();
        check(SessionLink.payloadLength(0x11)==0,"authenticated stop frame recognized");
        c.stop();check(!c.owns(new File("ignored")),"stopped instance cannot reconnect");
        System.out.println("Direct daemon tests passed: "+checks);
    }

    static void testBackoff() throws Exception {
        Clock clock = new Clock();
        List<Long> attempts = new ArrayList<Long>();
        new DirectDaemon(clock, () -> true, () -> {
            attempts.add(clock.now());
            return new DirectDaemon.Session() {
                public void run(Runnable ready, BooleanSupplier allowed) { }
                public void close() { }
            };
        }, () -> 0).run();
        check(attempts.subList(0, 6).equals(Arrays.asList(0L, 1000L, 3000L, 7000L, 12000L, 17000L)),
            "retry grows 1, 2, 4, 5 seconds then caps");
        check(clock.time == 60000, "transport-only attempts do not refresh the bootstrap deadline");

        Clock jittered = new Clock();
        List<Long> jitteredAttempts = new ArrayList<Long>();
        new DirectDaemon(jittered, () -> true, () -> {
            jitteredAttempts.add(jittered.now());
            return new DirectDaemon.Session() {
                public void run(Runnable ready, BooleanSupplier allowed) { }
                public void close() { }
            };
        }, () -> 250).run();
        check(jitteredAttempts.subList(0, 5).equals(Arrays.asList(0L, 1250L, 3500L, 7750L, 13000L)),
            "jitter adds at most 250 ms to each backoff");
    }

    static void testExpiredReady() throws Exception {
        Clock clock = new Clock();
        new DirectDaemon(clock, () -> true, () -> new DirectDaemon.Session() {
            public void run(Runnable ready, BooleanSupplier allowed) {
                clock.time += 120000; // A monotonic jump while an attempt is in flight.
                check(!allowed.getAsBoolean(), "suspend consumes initial budget");
                ready.run();
                check(!allowed.getAsBoolean(), "late readiness cannot revive an expired attempt");
            }
            public void close() { }
        }, () -> 0).run();
        check(clock.time == 120000, "expired late readiness cannot extend the deadline");
    }

    static void testOwnerFiles() throws Exception {
        File root = Files.createTempDirectory("pckvm-owner-").toFile();
        DirectOwner older = new DirectOwner(root, ID);
        DirectOwner newer = new DirectOwner(root, "f" + ID.substring(1));
        DirectOwner unverified = new DirectOwner(root, "a" + ID.substring(1));
        File lock = new File(root, "pckvm-direct.owner.lock");
        ExecutorService executor = Executors.newSingleThreadExecutor();
        Process holder = null;
        try {
            Files.createDirectory(older.directory().toPath());
            Files.createDirectory(newer.directory().toPath());
            older.publish();
            try (Closeable live = older.acquireRunLock()) {
                newer.publish();
                older.stop();
                older.cleanup();
                check(older.directory().exists() && newer.owns(), "replacement preserves live instance files");
            }
            older.cleanup();
            check(newer.owns() && !older.owns() && !older.directory().exists(), "old cleanup preserves new owner");
            Files.createDirectory(older.directory().toPath());
            Files.createDirectory(unverified.directory().toPath());
            older.publish();
            check(older.owns() && !newer.directory().exists() && unverified.directory().exists(),
                "replacement prunes only previously published inactive instances");
            Files.createDirectory(newer.directory().toPath());
            newer.publish();
            String java = new File(new File(System.getProperty("java.home"), "bin"), "java.exe").getPath();
            holder = new ProcessBuilder(java, "-cp", System.getProperty("java.class.path"),
                "TestMain", "--hold-owner-lock", root.getPath()).start();
            check("LOCKED".equals(new BufferedReader(new InputStreamReader(holder.getInputStream())).readLine()), "child holds stable lock");
            Future<?> operation = executor.submit(() -> {
                try { newer.cleanup(); }
                catch (IOException e) { throw new UncheckedIOException(e); }
            });
            try {
                operation.get(500, TimeUnit.MILLISECONDS);
                throw new AssertionError("Cleanup bypassed the owner lock");
            } catch (TimeoutException expected) { }
            check(newer.owns(), "lock prevents cleanup before holder exits");
            holder.destroyForcibly();
            check(holder.waitFor(5, TimeUnit.SECONDS), "holder exits");
            operation.get(5, TimeUnit.SECONDS);
            check(!newer.owns() && !newer.directory().exists() && lock.isFile(), "crash releases lock without deleting it");
        } finally {
            if (holder != null && holder.isAlive()) holder.destroyForcibly();
            executor.shutdownNow();
            older.cleanup();
            newer.cleanup();
            unverified.cleanup();
            Files.deleteIfExists(lock.toPath());
            Files.deleteIfExists(root.toPath());
        }
    }
    static final class Clock implements DirectDaemon.Clock {
        long time,maxSleep;
        public long now(){return time;}
        public void sleep(long millis){time+=millis;maxSleep=Math.max(maxSleep,millis);}
    }
    static void testLoop(final boolean success)throws Exception {
        Clock clock=new Clock();final int[] created={0},closed={0},active={0};
        DirectDaemon daemon=new DirectDaemon(clock,()->true,()->{
            check(active[0]==0,"previous resources closed before retry");created[0]++;active[0]++;
            return new DirectDaemon.Session(){
                public void run(Runnable established,BooleanSupplier allowed)throws Exception {
                    check(allowed.getAsBoolean(),"attempt initially allowed");
                    if(success&&created[0]==1){established.run();clock.time+=100000;}
                    throw new IOException("disconnect");
                }
                public void close(){closed[0]++;active[0]--;}
            };
        }, () -> 0);
        daemon.run();
        check(created[0]>2&&created[0]==closed[0]&&active[0]==0,"all failed attempts cleaned up");
        long expected=success?1900000:60000;
        check(clock.time==expected,"bootstrap/idle deadline: "+clock.time);
        check(clock.maxSleep<=5000,"bounded retry backoff");
    }
    static void testOwnership()throws Exception {
        Clock clock=new Clock();final boolean[] owner={true};final int[] count={0},closed={0};
        new DirectDaemon(clock,()->owner[0],()->new DirectDaemon.Session(){
            public void run(Runnable established,BooleanSupplier allowed){count[0]++;established.run();owner[0]=false;check(!allowed.getAsBoolean(),"active session observes ownership replacement");}
            public void close(){closed[0]++;}
        }, () -> 0).run();
        check(count[0]==1&&closed[0]==1,"replaced daemon exits after cleanup");
    }
}
