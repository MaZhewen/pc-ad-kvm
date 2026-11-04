import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.File;
import java.io.IOException;
import java.io.RandomAccessFile;
import java.net.Socket;
import java.nio.channels.FileLock;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.List;

public final class DirectProbe {
    private static final String ID = "[0-9a-f]{32}";

    public static void main(String[] args) throws Exception {
        if (args.length != 3 || !args[1].matches("/data/local/tmp/pckvm-direct-probe-" + ID)
                || !args[2].matches(ID)) throw new IllegalArgumentException("Invalid probe path or instance");
        File root = new File(args[1]);
        if (!root.getCanonicalPath().equals(args[1])) throw new IOException("Invalid probe root");
        File instance = new File(root, args[2]);
        if ("run".equals(args[0])) {
            run(root, instance);
        } else {
            owner(args[0], root, instance);
        }
    }

    private static void owner(String command, File root, File instance) throws Exception {
        Path marker = new File(root, "owner").toPath();
        try (RandomAccessFile lockFile = new RandomAccessFile(new File(root, "owner.lock"), "rw");
                FileLock lock = lockFile.getChannel().lock()) {
            if ("publish".equals(command)) {
                Path pending = new File(instance, "owner.new").toPath();
                Files.write(pending, instance.getName().getBytes(StandardCharsets.US_ASCII));
                Files.move(pending, marker, StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING);
            } else if ("cleanup".equals(command)) {
                if (owns(root, instance)) Files.delete(marker);
            } else if ("hold".equals(command)) {
                System.out.println("LOCK_HELD pid=" + pid());
                System.out.flush();
                Thread.sleep(15000);
            } else {
                throw new IllegalArgumentException("Invalid owner operation");
            }
        }
    }

    private static boolean owns(File root, File instance) throws IOException {
        Path marker = new File(root, "owner").toPath();
        return Files.exists(marker) && new String(Files.readAllBytes(marker), StandardCharsets.US_ASCII).equals(instance.getName());
    }

    private static void run(File root, File instance) throws Exception {
        Path config = new File(instance, "config").toPath();
        List<String> values = Files.readAllLines(config, StandardCharsets.US_ASCII);
        String host = values.get(0), pin = values.get(2);
        int port = Integer.parseInt(values.get(1));
        byte[] token = new byte[32];
        for (int i = 0; i < token.length; i++) token[i] = (byte)Integer.parseInt(values.get(3).substring(2 * i, 2 * i + 2), 16);
        long end = elapsedMillis() + Long.parseLong(values.get(4)) * 1000L;
        Files.delete(config);
        int pid = pid();
        System.out.println("START pid=" + pid);
        int sequence = 0;
        boolean checkedCredentials = false;
        while (elapsedMillis() < end && owns(root, instance) && !new File(instance, "stop").exists()) {
            try {
                if (!checkedCredentials) {
                    String wrongPin = (pin.charAt(0) == '0' ? "1" : "0") + pin.substring(1);
                    byte[] wrongToken = token.clone();
                    wrongToken[0] ^= 1;
                    expectRejected(host, port, wrongPin, token, instance.getName(), "pin mismatch");
                    expectRejected(host, port, pin, wrongToken, instance.getName(), "authentication rejected");
                    checkedCredentials = true;
                }
                try (PinnedTls.Connection connection = PinnedTls.connect(host, port, pin, token, instance.getName(), 1, new byte[16])) {
                    Socket socket = connection.socket;
                    socket.setSoTimeout(2500);
                    DataOutputStream output = new DataOutputStream(socket.getOutputStream());
                    DataInputStream input = new DataInputStream(socket.getInputStream());
                    System.out.println("TLS_CONNECTED pid=" + pid + " source=" + socket.getLocalAddress().getHostAddress());
                    while (elapsedMillis() < end && owns(root, instance) && !new File(instance, "stop").exists()) {
                        int current = ++sequence;
                        output.writeInt(pid);
                        output.writeInt(current);
                        output.flush();
                        if (input.readInt() != pid || input.readInt() != current) throw new IOException("Echo mismatch");
                        Thread.sleep(1000);
                    }
                }
            } catch (IOException failure) {
                System.out.println("RETRY " + failure.getClass().getSimpleName());
                Thread.sleep(1000);
            }
        }
        System.out.println("EXIT pid=" + pid + " sequence=" + sequence);
    }

    private static int pid() throws Exception {
        return (Integer)Class.forName("android.os.Process").getMethod("myPid").invoke(null);
    }

    private static long elapsedMillis() throws Exception {
        return (Long)Class.forName("android.os.SystemClock").getMethod("elapsedRealtime").invoke(null);
    }

    private static void expectRejected(String host, int port, String pin, byte[] token,
                                       String instanceId, String reason) throws IOException {
        try (PinnedTls.Connection unexpected = PinnedTls.connect(host, port, pin, token, instanceId, 1, new byte[16])) {
            throw new AssertionError("Accepted invalid credentials");
        } catch (IOException failure) {
            for (Throwable cause = failure; cause != null; cause = cause.getCause()) {
                if (cause.getMessage() != null && cause.getMessage().contains(reason)) {
                    System.out.println("PASS rejected " + reason);
                    return;
                }
            }
            throw failure;
        }
    }
}
