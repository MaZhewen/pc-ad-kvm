import java.util.ArrayDeque;
import java.util.concurrent.TimeUnit;

/** Bounded FIFO; only its adjacent same-epoch absolute positions are replaceable. */
public final class InputFrameQueue {
    private final int capacity;
    private final ArrayDeque<SessionLink.Frame> frames = new ArrayDeque<SessionLink.Frame>();
    private boolean closed;

    public InputFrameQueue(int capacity) {
        if (capacity <= 0) throw new IllegalArgumentException("Queue capacity must be positive");
        this.capacity = capacity;
    }

    public synchronized boolean offer(SessionLink.Frame frame) {
        if (frame == null) throw new NullPointerException("frame");
        if (closed) return false;
        SessionLink.Frame tail = frames.peekLast();
        if (samePointerEpoch(tail, frame)) {
            frames.removeLast();
        } else if (frames.size() >= capacity) {
            return false;
        }
        frames.addLast(frame);
        notifyAll();
        return true;
    }

    public synchronized SessionLink.Frame poll(long timeout, TimeUnit unit) throws InterruptedException {
        long remaining = unit.toNanos(timeout);
        long started = System.nanoTime();
        while (frames.isEmpty() && !closed && remaining > 0) {
            TimeUnit.NANOSECONDS.timedWait(this, remaining);
            remaining = unit.toNanos(timeout) - (System.nanoTime() - started);
        }
        return frames.pollFirst();
    }

    public synchronized void close() {
        closed = true;
        notifyAll();
    }

    private static boolean samePointerEpoch(SessionLink.Frame first, SessionLink.Frame second) {
        if (first == null || first.type != 0x0b || second.type != 0x0b
                || first.payload.length != 12 || second.payload.length != 12) return false;
        for (int i = 0; i < 4; i++) if (first.payload[i] != second.payload[i]) return false;
        return true;
    }
}
