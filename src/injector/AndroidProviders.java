import java.lang.reflect.*;
import java.util.*;

/** Owns external provider leases for an app_process shell process, not an AMS app. */
public final class AndroidProviders implements AutoCloseable {
    interface Lease extends AutoCloseable { Object client();void close()throws Exception; }
    interface LeaseFactory { Lease acquire(String authority)throws Exception; }
    static final class Router implements InvocationHandler,AutoCloseable {
        final LeaseFactory factory;final Map<String,Lease> providers=new HashMap<String,Lease>();boolean closed;
        Router(LeaseFactory factory){this.factory=factory;}
        public Object invoke(Object proxy,Method method,Object[] args)throws Throwable{
            if(method.getDeclaringClass()==Object.class){
                if(method.getName().equals("hashCode"))return System.identityHashCode(proxy);
                if(method.getName().equals("equals"))return proxy==args[0];return "PC-KVM content resolver";
            }
            String authority=null;
            if(args!=null)for(Object arg:args)if(arg!=null){try{
                Method getAuthority=arg.getClass().getMethod("getAuthority");
                authority=(String)getAuthority.invoke(arg);if(authority!=null)break;
            }catch(NoSuchMethodException ignored){}}
            if(authority==null||authority.isEmpty())throw new IllegalArgumentException("Clipboard URI authority unavailable");
            Lease lease;
            synchronized(this){
                if(closed)throw new IllegalStateException("Clipboard providers closed");
                lease=providers.get(authority);
                if(lease==null){if(providers.size()>=128)throw new IllegalStateException("Too many clipboard providers");lease=factory.acquire(authority);providers.put(authority,lease);}
            }
            try{return method.invoke(lease.client(),args);}catch(InvocationTargetException e){throw e.getCause();}
        }
        public void close(){
            List<Lease> leases;synchronized(this){if(closed)return;closed=true;leases=new ArrayList<Lease>(providers.values());providers.clear();}
            for(Lease lease:leases)try{lease.close();}catch(Exception ignored){}
        }
    }
    final Router router;final Object resolver;
    public AndroidProviders(Object context,final Object baseResolver)throws Exception{
        final Class<?> cr=Class.forName("android.content.ContentResolver"),binder=Class.forName("android.os.IBinder");
        final Class<?> iam=Class.forName("android.app.IActivityManager"),clientClass=Class.forName("android.content.ContentProviderClient");
        final Object am=Class.forName("android.app.ActivityManager").getMethod("getService").invoke(null);
        final int user=(Integer)Class.forName("android.content.Context").getMethod("getUserId").invoke(context);
        router=new Router(authority->{
            final Object token=Class.forName("android.os.Binder").getConstructor().newInstance();
            Object holder=iam.getMethod("getContentProviderExternal",String.class,int.class,binder,String.class).invoke(am,authority,user,token,"PC-KVM");
            if(holder==null)throw new IllegalStateException("Clipboard provider unavailable");
            try{
                Object provider=holder.getClass().getField("provider").get(holder);
                Constructor<?> constructor=clientClass.getDeclaredConstructor(cr,Class.forName("android.content.IContentProvider"),boolean.class);constructor.setAccessible(true);
                final Object client=constructor.newInstance(baseResolver,provider,false);
                return new Lease(){
                    public Object client(){return client;}
                    public void close()throws Exception{
                        try{clientClass.getMethod("close").invoke(client);}
                        finally{iam.getMethod("removeContentProviderExternal",String.class,binder).invoke(am,authority,token);}
                    }
                };
            }catch(Exception e){try{iam.getMethod("removeContentProviderExternal",String.class,binder).invoke(am,authority,token);}catch(Exception ignored){}throw e;}
        });
        Class<?> contentInterface=Class.forName("android.content.ContentInterface");
        Object proxy=Proxy.newProxyInstance(contentInterface.getClassLoader(),new Class<?>[]{contentInterface},router);
        resolver=cr.getMethod("wrap",contentInterface).invoke(null,proxy);
    }
    public Object resolver(){return resolver;}
    public void close(){router.close();}
}
