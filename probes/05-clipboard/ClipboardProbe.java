import java.lang.reflect.*;
import java.io.*;

/** Restores original ClipData without logging its contents. */
public class ClipboardProbe {
    static Class<?> cls(String n) throws Exception { return Class.forName(n); }
    static Object call(Object o,String n,Class<?>[] t,Object... a)throws Exception{return o.getClass().getMethod(n,t).invoke(o,a);}
    public static void main(String[] args)throws Exception{
        Class<?> looper=cls("android.os.Looper");
        if(looper.getMethod("getMainLooper").invoke(null)==null)looper.getMethod("prepareMainLooper").invoke(null);
        Class<?> at=cls("android.app.ActivityThread");Object thread=at.getMethod("currentActivityThread").invoke(null);
        if(thread==null)thread=at.getMethod("systemMain").invoke(null);
        Object context=at.getMethod("getSystemContext").invoke(thread);Class<?> ctx=cls("android.content.Context");
        context=ctx.getMethod("createPackageContext",String.class,int.class).invoke(context,"com.android.shell",0);
        System.out.println("PROBE context="+context.getClass().getName()+" opPackage="+ctx.getMethod("getOpPackageName").invoke(context));
        Field op=context.getClass().getDeclaredField("mOpPackageName");op.setAccessible(true);op.set(context,"com.android.shell");
        for(Field f:context.getClass().getDeclaredFields())if(f.getName().toLowerCase().contains("attribution")){
            System.out.println("PROBE attribution field="+f.getName());
            if(f.getType().getName().equals("android.content.AttributionSource")){
                Class<?> builder=cls("android.content.AttributionSource$Builder");Object b=builder.getConstructor(int.class).newInstance(2000);
                builder.getMethod("setPackageName",String.class).invoke(b,"com.android.shell");
                f.setAccessible(true);f.set(context,builder.getMethod("build").invoke(b));
            }
        }
        Object ht=cls("android.os.HandlerThread").getConstructor(String.class).newInstance("clipboard-probe");((Thread)ht).start();
        Object handler=cls("android.os.Handler").getConstructor(looper).newInstance(call(ht,"getLooper",new Class<?>[0]));
        Class<?> cm=cls("android.content.ClipboardManager"),cd=cls("android.content.ClipData");
        Object manager=cm.getConstructor(ctx,cls("android.os.Handler")).newInstance(context,handler);
        Object original=cm.getMethod("getPrimaryClip").invoke(manager);
        System.out.println("PROBE clipboard available originalPresent="+(original!=null));
        final java.util.concurrent.atomic.AtomicInteger changes=new java.util.concurrent.atomic.AtomicInteger();
        Class<?> lt=cls("android.content.ClipboardManager$OnPrimaryClipChangedListener");
        Object listener=Proxy.newProxyInstance(lt.getClassLoader(),new Class<?>[]{lt},(p,m,a)->{
            if(m.getName().equals("onPrimaryClipChanged"))changes.incrementAndGet();
            if(m.getName().equals("hashCode"))return System.identityHashCode(p);
            if(m.getName().equals("equals"))return p==a[0];return null;});
        cm.getMethod("addPrimaryClipChangedListener",lt).invoke(manager,listener);
        Object baseResolver=ctx.getMethod("getContentResolver").invoke(context);
        Class<?> cr=cls("android.content.ContentResolver"),uri=cls("android.net.Uri");Object fileUri=null;
        AndroidProviders providers=new AndroidProviders(context,baseResolver);
        Object resolver=providers.resolver();
        try{
            Object text=cd.getMethod("newPlainText",CharSequence.class,CharSequence.class).invoke(null,"PC-KVM probe","PC-KVM probe 测试\nclipboard");
            cm.getMethod("setPrimaryClip",cd).invoke(manager,text);
            Object read=cm.getMethod("getPrimaryClip").invoke(manager),item=cd.getMethod("getItemAt",int.class).invoke(read,0);
            if(!"PC-KVM probe 测试\nclipboard".contentEquals((CharSequence)call(item,"getText",new Class<?>[0])))throw new AssertionError("text");
            Thread.sleep(300);System.out.println("PROBE text roundtrip=true listenerEvents="+changes.get());
            Class<?> cv=cls("android.content.ContentValues");Object values=cv.getConstructor().newInstance();Method put=cv.getMethod("put",String.class,String.class);
            put.invoke(values,"_display_name","pc-kvm-clipboard-probe.txt");put.invoke(values,"mime_type","text/plain");put.invoke(values,"relative_path","Download/PC-KVM-Probe/");
            Object collection=uri.getMethod("parse",String.class).invoke(null,"content://media/external_primary/downloads");
            fileUri=cr.getMethod("insert",uri,cv).invoke(resolver,collection,values);
            if(fileUri==null)throw new AssertionError("insert");
            OutputStream out=(OutputStream)cr.getMethod("openOutputStream",uri).invoke(resolver,fileUri);out.write("PC-KVM test data".getBytes("UTF-8"));out.close();
            Object clip=cd.getMethod("newUri",cr,CharSequence.class,uri).invoke(null,resolver,"PC-KVM probe",fileUri);
            cm.getMethod("setPrimaryClip",cd).invoke(manager,clip);
            read=cm.getMethod("getPrimaryClip").invoke(manager);item=cd.getMethod("getItemAt",int.class).invoke(read,0);
            InputStream in=(InputStream)cr.getMethod("openInputStream",uri).invoke(resolver,call(item,"getUri",new Class<?>[0]));
            int count=0;while(in.read()!=-1)count++;in.close();if(count!=16)throw new AssertionError("URI bytes="+count);
            System.out.println("PROBE file URI publish/read=true");
        }catch(Throwable ex){while(ex.getCause()!=null)ex=ex.getCause();System.out.println("PROBE failure="+ex.getClass().getSimpleName()+": "+ex.getMessage());ex.printStackTrace();}
        finally{
            if(original!=null)cm.getMethod("setPrimaryClip",cd).invoke(manager,original);else cm.getMethod("clearPrimaryClip").invoke(manager);
            if(fileUri!=null)cr.getMethod("delete",uri,String.class,String[].class).invoke(resolver,fileUri,null,null);
            cm.getMethod("removePrimaryClipChangedListener",lt).invoke(manager,listener);call(ht,"quitSafely",new Class<?>[0]);
            providers.close();
            System.out.println("PROBE restored original clipboard");
        }
        System.exit(0);
    }
}
