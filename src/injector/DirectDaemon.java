import java.io.IOException;
import java.io.Closeable;
import java.lang.reflect.Method;
import java.net.Socket;
import java.util.concurrent.ThreadLocalRandom;
import java.util.function.BooleanSupplier;
import java.util.function.IntSupplier;

/** Reconnection owns session lifetime, never input state or queued events. */
public final class DirectDaemon {
    static final long BOOTSTRAP_TIMEOUT = 60000, OFFLINE_TIMEOUT = 1800000;
    interface Clock { long now(); void sleep(long millis) throws InterruptedException; }
    interface Session extends AutoCloseable {
        void run(Runnable ready, BooleanSupplier allowed) throws Exception;
        void close();
    }
    interface SessionFactory { Session create() throws Exception; }
    private final Clock clock;
    private final BooleanSupplier owner;
    private final SessionFactory factory;
    private final IntSupplier jitter;

    DirectDaemon(Clock clock, BooleanSupplier owner, SessionFactory factory, IntSupplier jitter) {
        this.clock = clock;
        this.owner = owner;
        this.factory = factory;
        this.jitter = jitter;
    }

    static Clock androidClock() throws ReflectiveOperationException {
        final Method elapsed = Class.forName("android.os.SystemClock").getMethod("elapsedRealtime");
        return new Clock() {
            public long now() {
                try { return (Long) elapsed.invoke(null); }
                catch (ReflectiveOperationException e) { throw new IllegalStateException("Android elapsed clock unavailable", e); }
            }
            public void sleep(long millis) throws InterruptedException { Thread.sleep(millis); }
        };
    }

    public static void run(final DirectSessionConfig config) throws Exception {
        Closeable lease = config.acquireRunLock();
        try (Closeable running = lease) {
            Clock clock = androidClock();
            new DirectDaemon(clock, config::owns, () -> new Session() {
                Socket socket;
                public void run(Runnable ready, BooleanSupplier allowed) throws Exception {
                    PinnedTls.Connection connection = PinnedTls.connect(config.host, config.port, config.pin,
                        config.token, config.instanceId, 1, new byte[16], allowed);
                    socket = connection.socket;
                    if (allowed.getAsBoolean())
                        Injector.runSession(socket, config.token, false, config, connection.sessionId(), ready, allowed, clock::now);
                }
                public void close() {
                    if (socket != null) try { socket.close(); } catch (IOException ignored) { }
                }
            }, () -> ThreadLocalRandom.current().nextInt(251)).run();
        } finally {
            try { config.cleanup(); }
            catch (IOException e) { System.out.println("DIRECT cleanup " + e.getClass().getSimpleName()); }
        }
    }

    private final class Attempt {
        final long expires;
        boolean ready, closed;
        Attempt(long expires) { this.expires = expires; }
        synchronized boolean allowed() {
            return !closed && owner.getAsBoolean() && (ready || clock.now() < expires);
        }
        synchronized void markReady() {
            if (!closed && clock.now() < expires && owner.getAsBoolean()) ready = true;
        }
        synchronized boolean finish() { closed = true; return ready; }
    }

    void run() throws InterruptedException {
        long deadline = clock.now() + BOOTSTRAP_TIMEOUT;
        long backoff = 1000;
        boolean everReady = false;
        String lastFailure = null;
        while (owner.getAsBoolean() && clock.now() < deadline) {
            Session session = null;
            Attempt attempt = new Attempt(deadline);
            long ended;
            boolean ready;
            try {
                session = factory.create();
                session.run(attempt::markReady, attempt::allowed);
                lastFailure = null;
            } catch (InterruptedException e) { throw e; }
            catch (Exception e) {
                String failure = e.getClass().getSimpleName();
                if (!failure.equals(lastFailure)) System.out.println("DIRECT reconnect " + failure);
                lastFailure = failure;
            } finally {
                ended = clock.now();
                ready = attempt.finish();
                if (session != null) session.close();
            }
            if (ready) {
                everReady = true;
                deadline = ended + OFFLINE_TIMEOUT;
                backoff = 1000;
            }
            long until = Math.min(deadline, clock.now() + backoff + jitter.getAsInt());
            long remaining;
            while (owner.getAsBoolean() && (remaining = until - clock.now()) > 0)
                clock.sleep(Math.min(200, remaining));
            backoff = Math.min(5000, backoff * 2);
        }
        System.out.println("DIRECT exit " + (!owner.getAsBoolean() ? "owner-revoked" : everReady ? "idle-timeout" : "bootstrap-timeout"));
    }
}
