import java.io.Closeable;
import java.io.File;
import java.io.IOException;
import java.io.RandomAccessFile;
import java.nio.channels.FileLock;
import java.nio.channels.OverlappingFileLockException;
import java.nio.charset.StandardCharsets;
import java.nio.file.FileAlreadyExistsException;
import java.nio.file.Files;
import java.nio.file.NoSuchFileException;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.nio.file.StandardOpenOption;

/** Serializes publication and cleanup for detached direct-mode instances. */
public final class DirectOwner {
    static final File BASE = new File("/data/local/tmp");
    private static final String PREFIX = "pckvm-direct-";
    private static final String[] INSTANCE_FILES = {"config", "injector.jar", "log", "stop", "owner.new", "published", "running.lock"};

    private final String id;
    private final File base;
    private final File directory;
    private final File lockFile;
    private final Path marker;

    DirectOwner(String id) { this(BASE, id); }

    DirectOwner(File base, String id) {
        if (id == null || !id.matches("[a-f0-9]{32}")) throw new IllegalArgumentException("Invalid direct instance ID");
        this.base = base;
        this.id = id;
        directory = new File(base, PREFIX + id);
        lockFile = new File(base, "pckvm-direct.owner.lock");
        marker = new File(base, "pckvm-direct.owner").toPath();
    }

    File directory() { return directory; }

    public static void main(String[] args) throws IOException {
        if (args.length != 2) throw new IllegalArgumentException("Expected direct owner operation and instance ID");
        DirectOwner owner = new DirectOwner(args[1]);
        switch (args[0]) {
            case "publish": owner.publish(); break;
            case "stop": owner.stop(); break;
            case "cleanup": owner.cleanup(); break;
            default: throw new IllegalArgumentException("Invalid direct owner operation");
        }
    }

    boolean owns() {
        try {
            return !Files.exists(new File(directory, "stop").toPath()) && isPublished();
        } catch (IOException e) { return false; }
    }

    void publish() throws IOException {
        withLock(() -> {
            requirePrivatePath();
            if (Files.exists(new File(directory, "stop").toPath())) throw new IOException("Direct instance was stopped");
            mark("published");
            Path pending = new File(directory, "owner.new").toPath();
            Files.deleteIfExists(pending);
            Files.write(pending, id.getBytes(StandardCharsets.US_ASCII), StandardOpenOption.CREATE_NEW);
            Files.move(pending, marker, StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING);
            pruneOrphans();
            return null;
        });
    }

    void stop() throws IOException {
        requirePrivatePath();
        mark("stop");
    }

    private void mark(String name) throws IOException {
        Path flag = new File(directory, name).toPath();
        try { Files.createFile(flag); }
        catch (FileAlreadyExistsException ignored) { }
    }

    Closeable acquireRunLock() throws IOException {
        return withLock(() -> {
            requirePrivatePath();
            if (!owns()) throw new IOException("Direct instance is no longer current");
            RandomAccessFile lease = tryRunLock();
            if (lease == null) throw new IOException("Direct instance already running");
            return lease;
        });
    }

    void cleanup() throws IOException {
        withLock(() -> {
            requirePrivatePath();
            if (isPublished()) Files.deleteIfExists(marker);
            removeIfInactive();
            return null;
        });
    }

    private void pruneOrphans() {
        File[] candidates = base.listFiles((root, name) -> name.matches(PREFIX + "[a-f0-9]{32}"));
        if (candidates == null) return;
        for (File candidate : candidates) {
            if (candidate.equals(directory) || !new File(candidate, "published").isFile()) continue;
            DirectOwner older = new DirectOwner(base, candidate.getName().substring(PREFIX.length()));
            try {
                older.requirePrivatePath();
                older.removeIfInactive();
            } catch (IOException e) { System.out.println("DIRECT orphan cleanup " + e.getClass().getSimpleName()); }
        }
    }

    // The shared owner lock prevents a daemon from starting between the liveness check and deletion.
    private void removeIfInactive() throws IOException {
        if (!directory.isDirectory()) return;
        RandomAccessFile lease = tryRunLock();
        if (lease == null) return;
        lease.close();
        for (String name : INSTANCE_FILES) Files.deleteIfExists(new File(directory, name).toPath());
        Files.deleteIfExists(directory.toPath());
    }

    private RandomAccessFile tryRunLock() throws IOException {
        RandomAccessFile lease = new RandomAccessFile(new File(directory, "running.lock"), "rw");
        boolean acquired = false;
        try {
            try { acquired = lease.getChannel().tryLock() != null; }
            catch (OverlappingFileLockException ignored) { }
            return acquired ? lease : null;
        } finally { if (!acquired) lease.close(); }
    }

    private boolean isPublished() throws IOException {
        try { return DirectSessionConfig.read(marker.toFile(), 32).equals(id); }
        catch (NoSuchFileException e) { return false; }
    }

    private void requirePrivatePath() throws IOException {
        if (!directory.getCanonicalFile().equals(directory.getAbsoluteFile()))
            throw new IOException("Direct instance directory must not be a symlink");
    }

    private interface Operation<T> { T run() throws IOException; }

    private <T> T withLock(Operation<T> operation) throws IOException {
        synchronized (DirectOwner.class) {
            try (RandomAccessFile file = new RandomAccessFile(lockFile, "rw");
                    FileLock lock = file.getChannel().lock()) {
                return operation.run();
            }
        }
    }
}
