import java.io.*;
import java.net.*;
import java.nio.file.Files;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;
import java.util.function.BooleanSupplier;

/** Uses the real clipboard adapter lock and real session, without any Android services. */
public class AckLockMain {
    public static void main(String[] args)throws Exception {
        CountDownLatch holdingClipboard=new CountDownLatch(1),allowChanged=new CountDownLatch(1),revisionEntered=new CountDownLatch(1),senderReleased=new CountDownLatch(1);
        AtomicBoolean armed=new AtomicBoolean(),candidateStarted=new AtomicBoolean();
        AndroidMain.Fake platform=new AndroidMain.Fake();platform.clip=ClipboardWire.text("baseline");
        ServerSocket server=new ServerSocket(0,8,InetAddress.getByName("127.0.0.1"));server.setSoTimeout(3500);
        File root=Files.createTempDirectory("pckvm-ack-lock-").toFile();Socket socket=null;boolean completed=false;
        ClipboardSession session=new ClipboardSession(new byte[32],server.getLocalPort(),root,(cache,changed)->{
            AndroidClipboard clipboard=new AndroidClipboard(platform,()->{
                if(armed.get()){
                    holdingClipboard.countDown();
                    try{if(!allowChanged.await(3,TimeUnit.SECONDS))throw new AssertionError("callback release timeout");}catch(InterruptedException e){throw new RuntimeException(e);}
                }
                changed.run();
            });
            return new ClipboardSession.Adapter(){
                public ClipboardWire.Snapshot capture()throws Exception{return clipboard.capture();}
                public long revision(){if(armed.get())revisionEntered.countDown();return clipboard.revision();}
                public Object identity()throws Exception{return clipboard.identity();}
                public boolean publish(ClipboardWire.Snapshot s,long revision,Object identity,BooleanSupplier allowed)throws Exception{return clipboard.publish(s,revision,identity,allowed);}
                public void release(ClipboardWire.Snapshot s){clipboard.release(s);if(candidateStarted.get())senderReleased.countDown();}
                public void close(){clipboard.close();}
            };
        });
        try {
            session.control(1);socket=server.accept();socket.setSoTimeout(3500);byte[] hello=new byte[37];new DataInputStream(socket.getInputStream()).readFully(hello);socket.getOutputStream().write(1);
            if(socket.getInputStream().read()!=0)throw new AssertionError("baseline not ready");
            candidateStarted.set(true);platform.copy("candidate");ClipboardWire.Snapshot sent=SessionMain.receive(socket,root);
            if(!senderReleased.await(2,TimeUnit.SECONDS))throw new AssertionError("sender release timeout");
            armed.set(true);final AtomicReference<Throwable> callbackFailure=new AtomicReference<Throwable>();
            Thread nativeCopy=new Thread(()->{try{platform.copy("new native content");}catch(Throwable failure){callbackFailure.set(failure);}},"test-native-clipboard-callback");nativeCopy.setDaemon(true);nativeCopy.start();
            if(!holdingClipboard.await(2,TimeUnit.SECONDS))throw new AssertionError("adapter lock not held");
            SessionMain.reply(socket,sent.id,0);
            if(!revisionEntered.await(2,TimeUnit.SECONDS))throw new AssertionError("ACK did not sample native revision");
            allowChanged.countDown();nativeCopy.join(3000);
            if(nativeCopy.isAlive())throw new AssertionError("successful ACK deadlocked with native clipboard callback");
            if(callbackFailure.get()!=null)throw new AssertionError(callbackFailure.get());
            ClipboardWire.Snapshot fresh=SessionMain.receive(socket,root);
            if(!ClipboardWire.textContent(fresh).equals("new native content"))throw new AssertionError("new local copy was not transmitted");
            completed=true;System.out.println("PASS ACK/native notification lock order; new copy transmitted");
        } finally {
            allowChanged.countDown();server.close();if(socket!=null)socket.close();
            // On a failed lock-order assertion do not block the test process trying to take that lock.
            if(completed)session.close();SessionMain.remove(root);
        }
    }
}
