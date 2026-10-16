import java.io.*;
import java.lang.reflect.*;
import java.util.*;
import java.util.function.BooleanSupplier;

/** Android clipboard access is optional and independent of keyboard/mouse input. */
public final class AndroidClipboard implements AutoCloseable {
    interface Platform extends AutoCloseable {
        ClipboardWire.Snapshot capture() throws Exception;
        void publish(ClipboardWire.Snapshot snapshot,String label,BooleanSupplier allowed) throws Exception;
        String label() throws Exception;
        Object identity() throws Exception;
        boolean isOwnClip(String label) throws Exception;
        boolean available() throws Exception;
        void listen(Runnable changed) throws Exception;
        void close() throws Exception;
    }
    final Platform platform;
    final Runnable changed;
    volatile long revision;
    String ownLabel;
    volatile boolean closed;
    public AndroidClipboard(File cache,Runnable changed)throws Exception{this(new ReflectivePlatform(cache),changed);}
    AndroidClipboard(Platform platform,Runnable changed)throws Exception{
        this.platform=platform;this.changed=changed;
        try{platform.listen(()->nativeChanged());}catch(Exception|Error failure){try{platform.close();}catch(Exception cleanup){failure.addSuppressed(cleanup);}throw failure;}
    }
    synchronized void nativeChanged(){
        if(closed)return;
        try{if(ownLabel!=null&&ownLabel.equals(platform.label())&&platform.isOwnClip(ownLabel))return;}catch(Exception ignored){}
        revision++;changed.run();
    }
    public synchronized long revision(){return revision;}
    public Object identity()throws Exception{return platform.identity();}
    public ClipboardWire.Snapshot capture()throws Exception{
        if(closed||!platform.available())return null;
        ClipboardWire.Snapshot snapshot=platform.capture();
        if(snapshot!=null&&snapshot.id<=0)snapshot.id=Math.max(1,System.currentTimeMillis());
        return snapshot;
    }
    public boolean publish(ClipboardWire.Snapshot snapshot,long expectedRevision)throws Exception{return publish(snapshot,expectedRevision,()->true);}
    public boolean publish(ClipboardWire.Snapshot snapshot,long expectedRevision,BooleanSupplier allowed)throws Exception{
        return publish(snapshot,expectedRevision,platform.identity(),allowed);
    }
    public boolean publish(ClipboardWire.Snapshot snapshot,long expectedRevision,Object baseline,BooleanSupplier allowed)throws Exception{
        if(closed||revision!=expectedRevision||!platform.available())return false;
        if(!Objects.equals(baseline,platform.identity()))return false;
        String old,label="PC-KVM:"+UUID.randomUUID().toString();synchronized(this){old=ownLabel;ownLabel=label;}
        try{platform.publish(snapshot,label,()->allowedNow(expectedRevision,baseline,allowed));return true;}
        catch(InterruptedIOException e){synchronized(this){if(label.equals(ownLabel))ownLabel=old;}return false;}
        catch(Exception e){synchronized(this){if(label.equals(ownLabel))ownLabel=old;}throw e;}
    }
    boolean allowedNow(long expected,Object baseline,BooleanSupplier allowed){try{return !closed&&revision==expected&&allowed.getAsBoolean()&&platform.available()&&Objects.equals(baseline,platform.identity());}catch(Exception failure){return false;}}
    public void release(ClipboardWire.Snapshot snapshot){if(platform instanceof ReflectivePlatform)((ReflectivePlatform)platform).release(snapshot);}
    public synchronized void close(){if(closed)return;closed=true;try{platform.close();}catch(Exception ignored){}}
    static String fileName(String name,int index){
        if(name==null)name="";name=name.replace('\\','/');int slash=name.lastIndexOf('/');if(slash>=0)name=name.substring(slash+1);
        name=name.replaceAll("[\\x00-\\x1f<>:\"/\\\\|?*]","_").replaceAll("[ .]+$","");
        if(name.length()>180)name=name.substring(0,180);
        if(name.isEmpty()||name.equals(".")||name.equals(".."))name="clipboard-"+index+".bin";
        if(name.matches("(?i)(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\\..*)?"))name="_"+name;
        return name;
    }

    static void closeAll(AutoCloseable... resources)throws Exception{
        Exception failure=null;
        for(AutoCloseable resource:resources)try{if(resource!=null)resource.close();}catch(Exception error){if(failure==null)failure=error;else failure.addSuppressed(error);}
        if(failure!=null)throw failure;
    }
    // A capture owns every partial spool until the entire URI list succeeds.
    static final class CaptureBatch implements AutoCloseable {
        final File cache;final long fileLimit,totalLimit;long total;boolean retained;
        final List<File> files=new ArrayList<File>();
        CaptureBatch(File cache,long fileLimit,long totalLimit){this.cache=cache;this.fileLimit=fileLimit;this.totalLimit=totalLimit;}
        void reserve(long length)throws IOException{if(length<0||length>fileLimit||length>totalLimit-total)throw new IOException("Clipboard content exceeds size limit");total+=length;}
        void spool(ClipboardWire.Item item)throws IOException{
            File file=File.createTempFile("capture-",".bin",cache);files.add(file);
            try(InputStream in=item.open.open();OutputStream out=new FileOutputStream(file)){
                ReflectivePlatform.copy(in,out,Math.min(fileLimit,totalLimit-total));
            }
            reserve(file.length());item.file=file;item.length=file.length();item.open=()->new FileInputStream(file);
        }
        void retain(Set<File> owned){owned.addAll(files);retained=true;}
        public void close(){if(!retained)for(File file:files)file.delete();}
    }
    static final class ReflectivePlatform implements Platform {
        final Class<?> contextClass=Class.forName("android.content.Context"),clipClass=Class.forName("android.content.ClipData");
        final Class<?> managerClass=Class.forName("android.content.ClipboardManager"),resolverClass=Class.forName("android.content.ContentResolver");
        final Class<?> uriClass=Class.forName("android.net.Uri"),itemClass=Class.forName("android.content.ClipData$Item");
        final Object context,manager,resolver,handlerThread,keyguard;
        final AndroidProviders providers;
        final File cache;
        final AndroidClipboardCache mediaCache;
        final AndroidClipboardImages imageCache;
        final java.util.Timer cacheTimer;
        Object listener;Class<?> listenerType;String publishedContent;volatile boolean platformClosed;
        final Set<File> captures=new HashSet<File>();
        ReflectivePlatform(File cache)throws Exception{
            this.cache=cache;this.mediaCache=new AndroidClipboardCache(new File(cache,"media-receipts"));if(!cache.isDirectory()&&!cache.mkdirs())throw new IOException("Clipboard cache unavailable");
            Class<?> looper=Class.forName("android.os.Looper");
            if(looper.getMethod("getMainLooper").invoke(null)==null)looper.getMethod("prepareMainLooper").invoke(null);
            Class<?> at=Class.forName("android.app.ActivityThread");Object thread=at.getMethod("currentActivityThread").invoke(null);
            if(thread==null)thread=at.getMethod("systemMain").invoke(null);
            Object system=at.getMethod("getSystemContext").invoke(thread);
            context=contextClass.getMethod("createPackageContext",String.class,int.class).invoke(system,"com.android.shell",0);
            // ContextImpl inherits the system context attribution under app_process.
            // Attribute calls to our real UID/package (shell), never the system UID.
            Field op=context.getClass().getDeclaredField("mOpPackageName");op.setAccessible(true);op.set(context,"com.android.shell");
            try{
                Field attr=context.getClass().getDeclaredField("mAttributionSource");attr.setAccessible(true);
                Class<?> b=Class.forName("android.content.AttributionSource$Builder");Object builder=b.getConstructor(int.class).newInstance(2000);
                b.getMethod("setPackageName",String.class).invoke(builder,"com.android.shell");attr.set(context,b.getMethod("build").invoke(builder));
            }catch(NoSuchFieldException ignored){}catch(ClassNotFoundException ignored){}
            Object baseResolver=contextClass.getMethod("getContentResolver").invoke(context);
            providers=new AndroidProviders(context,baseResolver);
            Object createdThread=null;java.util.Timer createdTimer=null;
            try{
                resolver=providers.resolver();
                keyguard=contextClass.getMethod("getSystemService",String.class).invoke(context,"keyguard");
                createdThread=Class.forName("android.os.HandlerThread").getConstructor(String.class).newInstance("pckvm-clipboard-events");
                handlerThread=createdThread;((Thread)handlerThread).start();
                Object handler=Class.forName("android.os.Handler").getConstructor(looper).newInstance(handlerThread.getClass().getMethod("getLooper").invoke(handlerThread));
                manager=managerClass.getConstructor(contextClass,Class.forName("android.os.Handler")).newInstance(context,handler);
                // app_process has no Application attribution for Environment's static storage query.
                // The shell context and tested primary user use Android's standard /sdcard alias.
                File shared=new File("/sdcard");
                imageCache=new AndroidClipboardImages(new File(shared,"Documents/.PC-KVM-Clipboard"),new AndroidClipboardImages.Publisher(){
                    public String scan(File file)throws Exception{return scanHiddenImage(file);}
                    public void delete(String uri)throws Exception{ReflectivePlatform.this.delete(uriClass.getMethod("parse",String.class).invoke(null,uri));}
                });
                createdTimer=new java.util.Timer("pckvm-clipboard-cache",true);cacheTimer=createdTimer;
                cacheTimer.schedule(new java.util.TimerTask(){public void run(){if(!platformClosed)sweepMedia();}},60000,60000);
            }catch(Exception|Error failure){
                final Object failedThread=createdThread;final java.util.Timer failedTimer=createdTimer;
                try{closeAll(()->{if(failedTimer!=null)failedTimer.cancel();},()->{if(failedThread!=null)failedThread.getClass().getMethod("quitSafely").invoke(failedThread);},providers);}catch(Exception cleanup){failure.addSuppressed(cleanup);}
                throw failure;
            }
        }
        Object clip()throws Exception{return managerClass.getMethod("getPrimaryClip").invoke(manager);}
        public boolean available()throws Exception{
            if(keyguard==null)return false;
            Class<?> keyguardClass=Class.forName("android.app.KeyguardManager");
            return !(Boolean)keyguardClass.getMethod("isDeviceLocked").invoke(keyguard)&&!(Boolean)keyguardClass.getMethod("isKeyguardLocked").invoke(keyguard);
        }
        public String label()throws Exception{
            Object description=managerClass.getMethod("getPrimaryClipDescription").invoke(manager);
            if(description==null)return null;
            Object label=Class.forName("android.content.ClipDescription").getMethod("getLabel").invoke(description);return label==null?null:label.toString();
        }
        public Object identity()throws Exception{
            Object description=managerClass.getMethod("getPrimaryClipDescription").invoke(manager);
            if(description==null)return null;
            try{return String.valueOf(description.getClass().getMethod("getTimestamp").invoke(description))+":"+label();}
            catch(NoSuchMethodException unsupported){return signature(clip());}
        }
        public boolean isOwnClip(String label)throws Exception{return publishedContent!=null&&publishedContent.equals(signature(clip()));}
        String signature(Object clip)throws Exception{
            if(clip==null)return "none";
            StringBuilder value=new StringBuilder();int count=(Integer)clipClass.getMethod("getItemCount").invoke(clip);value.append(count).append(':');
            Object description=clipClass.getMethod("getDescription").invoke(clip);Class<?> descriptionClass=Class.forName("android.content.ClipDescription");
            int mimes=(Integer)descriptionClass.getMethod("getMimeTypeCount").invoke(description);
            for(int i=0;i<mimes;i++)part(value,descriptionClass.getMethod("getMimeType",int.class).invoke(description,i));
            for(int i=0;i<count;i++){
                Object item=clipClass.getMethod("getItemAt",int.class).invoke(clip,i);
                part(value,itemClass.getMethod("getText").invoke(item));part(value,itemClass.getMethod("getUri").invoke(item));
                Object intent=itemClass.getMethod("getIntent").invoke(item);
                part(value,intent==null?null:intent.getClass().getMethod("toUri",int.class).invoke(intent,0));
            }
            return value.toString();
        }
        static void part(StringBuilder target,Object value){String text=value==null?"":value.toString();target.append(text.length()).append(':').append(text);}
        public void listen(final Runnable changed)throws Exception{
            listenerType=Class.forName("android.content.ClipboardManager$OnPrimaryClipChangedListener");
            listener=Proxy.newProxyInstance(listenerType.getClassLoader(),new Class<?>[]{listenerType},(p,m,args)->{
                if(m.getName().equals("onPrimaryClipChanged")){changed.run();return null;}
                if(m.getName().equals("hashCode"))return System.identityHashCode(p);
                if(m.getName().equals("equals"))return p==args[0];return "PC-KVM clipboard listener";
            });
            managerClass.getMethod("addPrimaryClipChangedListener",listenerType).invoke(manager,listener);
        }
        public ClipboardWire.Snapshot capture()throws Exception{
            sweepMedia();
            Object clip=clip();if(clip==null)return null;
            int count=(Integer)clipClass.getMethod("getItemCount").invoke(clip);if(count<1||count>128)return null;
            ClipboardWire.Snapshot snapshot=new ClipboardWire.Snapshot();snapshot.kind=3;
            try(CaptureBatch batch=new CaptureBatch(cache,1L<<30,2L<<30)){
            for(int i=0;i<count;i++){
                Object item=clipClass.getMethod("getItemAt",int.class).invoke(clip,i),uri=itemClass.getMethod("getUri").invoke(item);
                if(uri==null){
                    if(count!=1)return null;Object text=itemClass.getMethod("getText").invoke(item);
                    return text==null?null:ClipboardWire.text(text.toString());
                }
                final Object capturedUri=uri;
                String mime=(String)resolverClass.getMethod("getType",uriClass).invoke(resolver,uri);
                ClipboardWire.Item entry=new ClipboardWire.Item();entry.mime=mime==null?"application/octet-stream":mime;
                entry.name="clipboard-"+i+".bin";entry.length=-1;
                Object cursor=resolverClass.getMethod("query",uriClass,String[].class,String.class,String[].class,String.class)
                    .invoke(resolver,uri,new String[]{"_display_name","_size"},null,null,null);
                if(cursor!=null){Class<?> cur=Class.forName("android.database.Cursor");try{
                    if((Boolean)cur.getMethod("moveToFirst").invoke(cursor)){
                        int ni=(Integer)cur.getMethod("getColumnIndex",String.class).invoke(cursor,"_display_name");
                        int si=(Integer)cur.getMethod("getColumnIndex",String.class).invoke(cursor,"_size");
                        if(ni>=0)entry.name=(String)cur.getMethod("getString",int.class).invoke(cursor,ni);
                        if(si>=0&&!(Boolean)cur.getMethod("isNull",int.class).invoke(cursor,si))entry.length=(Long)cur.getMethod("getLong",int.class).invoke(cursor,si);
                    }
                }finally{cur.getMethod("close").invoke(cursor);}}
                entry.name=fileName(entry.name,i);
                entry.open=()->{try{return (InputStream)resolverClass.getMethod("openInputStream",uriClass).invoke(resolver,capturedUri);}catch(Exception e){throw new IOException("Clipboard URI unreadable",e);}};
                if(entry.length<0){
                    batch.spool(entry);
                }
                else batch.reserve(entry.length);
                snapshot.items.add(entry);
            }
            if(count==1&&snapshot.items.get(0).mime.startsWith("image/"))snapshot.kind=2;
            synchronized(captures){if(platformClosed)throw new InterruptedIOException("Clipboard capture canceled");batch.retain(captures);}
            return snapshot;
            }
        }
        public void publish(ClipboardWire.Snapshot snapshot,String label,BooleanSupplier allowed)throws Exception{
            Object clip;
            if(snapshot.kind==1){
                clip=clipClass.getMethod("newPlainText",CharSequence.class,CharSequence.class).invoke(null,label,ClipboardWire.textContent(snapshot));
                checkAllowed(allowed);publishedContent=signature(clip);managerClass.getMethod("setPrimaryClip",clipClass).invoke(manager,clip);return;
            }
            List<Object> created=new ArrayList<Object>();File receipt=null;
            try{
                List<String> mimeList=new ArrayList<String>();for(ClipboardWire.Item item:snapshot.items)if(!mimeList.contains(item.mime))mimeList.add(item.mime);
                clip=null;
                for(ClipboardWire.Item entry:snapshot.items){
                    checkAllowed(allowed);Object uri=importFile(entry,snapshot.kind==2,allowed);created.add(uri);
                    Object item=itemClass.getConstructor(uriClass).newInstance(uri);
                    if(clip==null)clip=clipClass.getConstructor(CharSequence.class,String[].class,itemClass).newInstance(label,mimeList.toArray(new String[0]),item);
                    else clipClass.getMethod("addItem",itemClass).invoke(clip,item);
                }
                List<String> uris=new ArrayList<String>();List<Long> sizes=new ArrayList<Long>();
                for(int i=0;i<created.size();i++){uris.add(created.get(i).toString());sizes.add(snapshot.items.get(i).length);}
                receipt=mediaCache.recordPending(uris,sizes,snapshot.kind==2);
                checkAllowed(allowed);publishedContent=signature(clip);managerClass.getMethod("setPrimaryClip",clipClass).invoke(manager,clip);
                mediaCache.complete(receipt);sweepMedia();
            }catch(Exception e){for(Object uri:created)try{delete(uri);}catch(Exception ignored){}mediaCache.forget(receipt);throw e;}
        }
        Set<String> currentUris()throws Exception{
            Set<String> result=new HashSet<String>();Object current=clip();if(current==null)return result;
            int count=(Integer)clipClass.getMethod("getItemCount").invoke(current);
            for(int i=0;i<count;i++){Object item=clipClass.getMethod("getItemAt",int.class).invoke(current,i),uri=itemClass.getMethod("getUri").invoke(item);if(uri!=null)result.add(uri.toString());}
            return result;
        }
        void sweepMedia(){
            try{if(!available())return;mediaCache.sweep(currentUris(),uri->{
                if(!available()||currentUris().contains(uri))return false;
                delete(uriClass.getMethod("parse",String.class).invoke(null,uri));return true;
            });}catch(Exception ignored){}
        }
        void checkAllowed(BooleanSupplier allowed)throws Exception{if(!allowed.getAsBoolean()||!available())throw new InterruptedIOException("Clipboard publication canceled");}
        String scanHiddenImage(File file)throws Exception{
            Class<?> bundleClass=Class.forName("android.os.Bundle");
            Object result=resolverClass.getMethod("call",String.class,String.class,String.class,bundleClass).invoke(resolver,"media","scan_file",file.getAbsolutePath(),null);
            Object uri=bundleClass.getMethod("getParcelable",String.class).invoke(result,"android.intent.extra.STREAM");
            if(uri==null)throw new IOException("Hidden image provider unavailable");
            Object cursor=resolverClass.getMethod("query",uriClass,String[].class,String.class,String[].class,String.class).invoke(resolver,uri,new String[]{"_data","media_type"},null,null,null);
            Class<?> cursorClass=Class.forName("android.database.Cursor");boolean owned=false;
            try{
                if(cursor==null||!(Boolean)cursorClass.getMethod("moveToFirst").invoke(cursor))throw new IOException("Hidden image registration missing");
                String path=(String)cursorClass.getMethod("getString",int.class).invoke(cursor,0);
                owned=path!=null&&file.getCanonicalFile().equals(new File(path).getCanonicalFile());
                if(!owned)throw new IOException("Hidden image provider path changed");
                if((Integer)cursorClass.getMethod("getInt",int.class).invoke(cursor,1)!=0)throw new IOException("Clipboard image would be visible in gallery");
                return uri.toString();
            }catch(Exception error){if(owned)try{delete(uri);}catch(Exception ignored){}throw error;}
            finally{if(cursor!=null)cursorClass.getMethod("close").invoke(cursor);}
        }
        Object importFile(ClipboardWire.Item item,boolean image,BooleanSupplier allowed)throws Exception{
            if(image)return uriClass.getMethod("parse",String.class).invoke(null,imageCache.store(item,allowed));
            Class<?> valuesClass=Class.forName("android.content.ContentValues");Object values=valuesClass.getConstructor().newInstance();
            Method put=valuesClass.getMethod("put",String.class,String.class);
            put.invoke(values,"_display_name",item.name);put.invoke(values,"mime_type",item.mime);
            put.invoke(values,"relative_path","Download/PC-KVM/");
            valuesClass.getMethod("put",String.class,Integer.class).invoke(values,"is_pending",Integer.valueOf(1));
            Object collection=uriClass.getMethod("parse",String.class).invoke(null,"content://media/external_primary/downloads");
            Object uri=resolverClass.getMethod("insert",uriClass,valuesClass).invoke(resolver,collection,values);
            if(uri==null)throw new IOException("Clipboard destination unavailable");
            try{
                try(InputStream in=item.open.open();OutputStream out=(OutputStream)resolverClass.getMethod("openOutputStream",uriClass).invoke(resolver,uri)){
                    byte[] buffer=new byte[65536];long total=0;int count;
                    while((count=in.read(buffer))!=-1){checkAllowed(allowed);total+=count;if(total>item.length)throw new IOException("Clipboard source length changed");out.write(buffer,0,count);}
                    if(total!=item.length)throw new IOException("Clipboard source length changed");
                }
                Object ready=valuesClass.getConstructor().newInstance();valuesClass.getMethod("put",String.class,Integer.class).invoke(ready,"is_pending",Integer.valueOf(0));
                resolverClass.getMethod("update",uriClass,valuesClass,String.class,String[].class).invoke(resolver,uri,ready,null,null);
                return uri;
            }catch(Exception e){try{delete(uri);}catch(Exception ignored){}throw e;}
        }
        void delete(Object uri)throws Exception{resolverClass.getMethod("delete",uriClass,String.class,String[].class).invoke(resolver,uri,null,null);}
        static void copy(InputStream in,OutputStream out,long limit)throws IOException{
            if(in==null||out==null)throw new IOException("Clipboard stream unavailable");
            byte[] buffer=new byte[65536];long size=0;int n;
            while((n=in.read(buffer))!=-1){size+=n;if(size>limit)throw new IOException("Clipboard item exceeds size limit");out.write(buffer,0,n);}
        }
        void release(ClipboardWire.Snapshot snapshot){if(snapshot==null)return;for(ClipboardWire.Item i:snapshot.items)synchronized(captures){if(i.file!=null&&captures.remove(i.file))i.file.delete();}}
        public void close()throws Exception{
            platformClosed=true;
            closeAll(
                ()->cacheTimer.cancel(),
                ()->{if(listener!=null)managerClass.getMethod("removePrimaryClipChangedListener",listenerType).invoke(manager,listener);},
                ()->handlerThread.getClass().getMethod("quitSafely").invoke(handlerThread),
                providers,
                ()->{synchronized(captures){for(File file:captures)file.delete();captures.clear();}}
            );
        }
    }
}
