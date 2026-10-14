import java.lang.reflect.Method;

/** Best-effort compositor cursor. It creates no window, input channel, or input events. */
public final class DragCursor {
    public interface View {
        boolean show(int x, int y);
        void move(int x, int y);
        void hide();
        void close();
    }
    private DragCursor() { }
    public static View create() { return new CursorView(); }

    private static final class CursorView implements View {
        final AndroidLayer layer = new AndroidLayer();
        boolean initialized, visible, unavailable, closed;
        public synchronized boolean show(int x, int y) {
            if (closed || unavailable) return false;
            try {
                if (!initialized) { layer.initialize(); initialized = true; }
                layer.show(x, y); visible = true;
                return true;
            } catch (Exception error) { disable(error); }
              catch (LinkageError error) { disable(error); }
            return false;
        }
        public synchronized void move(int x, int y) {
            if (!visible || closed || unavailable) return;
            try { layer.move(x, y); }
            catch (Exception error) { disable(error); }
            catch (LinkageError error) { disable(error); }
        }
        public synchronized void hide() {
            if (!visible || closed || unavailable) return;
            try { layer.hide(); visible = false; }
            catch (Exception error) { disable(error); }
            catch (LinkageError error) { disable(error); }
        }
        public synchronized void close() {
            closed = true; visible = false;
            cleanup();
        }
        void disable(Throwable error) {
            unavailable = true; visible = false;
            report("unavailable", error);
            cleanup();
        }
        void cleanup() {
            try { layer.close(); }
            catch (Exception error) { report("cleanup-failed", error); }
            catch (LinkageError error) { report("cleanup-failed", error); }
        }
        void report(String state, Throwable error) {
            while (error.getCause() != null && error.getCause() != error) error = error.getCause();
            // Exception messages may contain coordinates or other input data.
            System.out.println("DRAG_CURSOR " + state + ": " + error.getClass().getSimpleName());
        }
    }

    /** Reflect Android APIs to keep the injector build independent of an Android SDK. */
    static final class AndroidLayer {
        static final int WIDTH = 40, HEIGHT = 48;
        Object control, surface, transaction;
        Object cursorBitmap;
        int imageWidth = WIDTH, imageHeight = HEIGHT;
        float hotSpotX, hotSpotY;
        Class<?> controlClass, surfaceClass, transactionClass;
        Method setPosition, show, hide, apply;

        void initialize() throws Exception {
            loadSystemImage();
            controlClass = Class.forName("android.view.SurfaceControl");
            Class<?> builderClass = Class.forName("android.view.SurfaceControl$Builder");
            surfaceClass = Class.forName("android.view.Surface");
            transactionClass = Class.forName("android.view.SurfaceControl$Transaction");
            Object builder = builderClass.getConstructor().newInstance();
            builderClass.getMethod("setName", String.class).invoke(builder, "PC-KVM Drag Cursor");
            builderClass.getMethod("setBufferSize", int.class, int.class).invoke(builder, imageWidth, imageHeight);
            int rgba = Class.forName("android.graphics.PixelFormat").getField("RGBA_8888").getInt(null);
            builderClass.getMethod("setFormat", int.class).invoke(builder, rgba);
            builderClass.getMethod("setHidden", boolean.class).invoke(builder, true);
            builderClass.getMethod("setOpaque", boolean.class).invoke(builder, false);
            // Root layers require ACCESS_SURFACE_FLINGER; unsupported devices fall back to touch.
            builderClass.getMethod("setParent", controlClass).invoke(builder, new Object[] {null});
            control = builderClass.getMethod("build").invoke(builder);
            surface = surfaceClass.getConstructor(controlClass).newInstance(control);
            transaction = transactionClass.getConstructor().newInstance();
            setPosition = transactionClass.getMethod("setPosition", controlClass, float.class, float.class);
            show = transactionClass.getMethod("show", controlClass);
            hide = transactionClass.getMethod("hide", controlClass);
            apply = transactionClass.getMethod("apply");
            transactionClass.getMethod("setLayerStack", controlClass, int.class).invoke(transaction, control, 0);
            transactionClass.getMethod("setLayer", controlClass, int.class).invoke(transaction, control, Integer.MAX_VALUE - 1);
            // Where supported, explicitly exclude this purely visual layer from input occlusion.
            try {
                transactionClass.getMethod("setTrustedOverlay", controlClass, boolean.class)
                    .invoke(transaction, control, true);
            } catch (NoSuchMethodException olderAndroid) { }
            drawArrow();
        }

        void loadSystemImage() {
            try {
                Class<?> looper = Class.forName("android.os.Looper");
                if (looper.getMethod("getMainLooper").invoke(null) == null)
                    looper.getMethod("prepareMainLooper").invoke(null);
                Class<?> activity = Class.forName("android.app.ActivityThread");
                Object thread = activity.getMethod("currentActivityThread").invoke(null);
                if (thread == null) thread = activity.getMethod("systemMain").invoke(null);
                Object context = activity.getMethod("getSystemContext").invoke(thread);
                Class<?> iconClass = Class.forName("android.view.PointerIcon");
                Object icon = iconClass.getMethod("getLoadedSystemIcon", Class.forName("android.content.Context"),
                    int.class, boolean.class, float.class).invoke(null, context,
                    iconClass.getField("TYPE_DEFAULT").getInt(null), false, 1f);
                Object bitmap = iconClass.getMethod("getBitmap").invoke(icon);
                Class<?> bitmapClass = Class.forName("android.graphics.Bitmap");
                int w = (Integer)bitmapClass.getMethod("getWidth").invoke(bitmap);
                int h = (Integer)bitmapClass.getMethod("getHeight").invoke(bitmap);
                java.lang.reflect.Field hx = iconClass.getDeclaredField("mHotSpotX");
                java.lang.reflect.Field hy = iconClass.getDeclaredField("mHotSpotY");
                hx.setAccessible(true); hy.setAccessible(true);
                float x = hx.getFloat(icon), y = hy.getFloat(icon);
                if (w <= 0 || h <= 0 || w > 512 || h > 512 || !Float.isFinite(x) || !Float.isFinite(y)
                    || x < 0 || y < 0 || x >= w || y >= h) return;
                int[] pixels = new int[w * h];
                bitmapClass.getMethod("getPixels", int[].class, int.class, int.class, int.class, int.class, int.class, int.class)
                    .invoke(bitmap, pixels, 0, w, 0, 0, w, h);
                boolean visible = false;
                for (int pixel : pixels) if ((pixel >>> 24) != 0) { visible = true; break; }
                if (!visible) {
                    // Some vendor PointerIcon adapters return a transparent placeholder.
                    // Load the matching framework arrow pixels, keeping the supplied hotspot.
                    Object resources = Class.forName("android.content.Context").getMethod("getResources").invoke(context);
                    Class<?> resourcesClass = Class.forName("android.content.res.Resources");
                    int resourceId = (Integer)resourcesClass.getMethod("getIdentifier", String.class, String.class, String.class)
                        .invoke(resources, "pointer_arrow", "drawable", "android");
                    if (resourceId != 0) {
                        Object drawable = resourcesClass.getMethod("getDrawable", int.class).invoke(resources, resourceId);
                        Class<?> drawableClass = Class.forName("android.graphics.drawable.BitmapDrawable");
                        if (drawableClass.isInstance(drawable)) {
                            Object alternative = drawableClass.getMethod("getBitmap").invoke(drawable);
                            if ((Integer)bitmapClass.getMethod("getWidth").invoke(alternative) == w
                                && (Integer)bitmapClass.getMethod("getHeight").invoke(alternative) == h) {
                                bitmapClass.getMethod("getPixels", int[].class, int.class, int.class, int.class, int.class, int.class, int.class)
                                    .invoke(alternative, pixels, 0, w, 0, 0, w, h);
                                for (int pixel : pixels) if ((pixel >>> 24) != 0) { visible = true; break; }
                                if (visible) bitmap = alternative;
                            }
                        }
                    }
                }
                if (!visible) {
                    System.out.println("DRAG_CURSOR system-style transparent; using visible fallback");
                    return;
                }
                cursorBitmap = bitmap; imageWidth = w; imageHeight = h; hotSpotX = x; hotSpotY = y;
                System.out.println("DRAG_CURSOR system-style loaded");
            } catch (Exception unsupported) { }
              catch (LinkageError unsupported) { }
        }

        void drawArrow() throws Exception {
            Class<?> canvasClass = Class.forName("android.graphics.Canvas");
            Class<?> rectClass = Class.forName("android.graphics.Rect");
            Class<?> pathClass = Class.forName("android.graphics.Path");
            Class<?> paintClass = Class.forName("android.graphics.Paint");
            Class<?> styleClass = Class.forName("android.graphics.Paint$Style");
            Class<?> modeClass = Class.forName("android.graphics.PorterDuff$Mode");
            Object canvas = surfaceClass.getMethod("lockCanvas", rectClass).invoke(surface, new Object[] {null});
            try {
                canvasClass.getMethod("drawColor", int.class, modeClass)
                    .invoke(canvas, 0, modeClass.getField("CLEAR").get(null));
                if (cursorBitmap != null) {
                    // Explicit destination bounds preserve pixels regardless of canvas density.
                    Object destination = rectClass.getConstructor(int.class, int.class, int.class, int.class)
                        .newInstance(0, 0, imageWidth, imageHeight);
                    canvasClass.getMethod("drawBitmap", Class.forName("android.graphics.Bitmap"),
                        rectClass, rectClass, paintClass).invoke(canvas, cursorBitmap, null, destination, null);
                    return;
                }
                Object path = pathClass.getConstructor().newInstance();
                pathClass.getMethod("moveTo", float.class, float.class).invoke(path, 0f, 0f);
                Method line = pathClass.getMethod("lineTo", float.class, float.class);
                float[][] points = {{0, 34}, {8, 26}, {15, 43}, {22, 40}, {15, 23}, {29, 23}};
                for (float[] point : points) line.invoke(path, point[0], point[1]);
                pathClass.getMethod("close").invoke(path);
                Object paint = paintClass.getConstructor(int.class).newInstance(1); // anti-alias
                Method color = paintClass.getMethod("setColor", int.class);
                Method style = paintClass.getMethod("setStyle", styleClass);
                Method draw = canvasClass.getMethod("drawPath", pathClass, paintClass);
                color.invoke(paint, 0xffffffff); style.invoke(paint, styleClass.getField("FILL").get(null));
                draw.invoke(canvas, path, paint);
                color.invoke(paint, 0xff000000); style.invoke(paint, styleClass.getField("STROKE").get(null));
                paintClass.getMethod("setStrokeWidth", float.class).invoke(paint, 2f);
                draw.invoke(canvas, path, paint);
            } finally { surfaceClass.getMethod("unlockCanvasAndPost", canvasClass).invoke(surface, canvas); }
        }
        void show(int x, int y) throws Exception {
            setPosition.invoke(transaction, control, x - hotSpotX, y - hotSpotY);
            show.invoke(transaction, control); apply.invoke(transaction);
        }
        void move(int x, int y) throws Exception {
            setPosition.invoke(transaction, control, x - hotSpotX, y - hotSpotY); apply.invoke(transaction);
        }
        void hide() throws Exception { hide.invoke(transaction, control); apply.invoke(transaction); }

        void close() throws Exception {
            // Framework icon images may be shared; release our reference without recycling them.
            cursorBitmap = null;
            Throwable failure = null;
            if (control != null && transaction != null) {
                try {
                    transactionClass.getMethod("remove", controlClass).invoke(transaction, control);
                    transactionClass.getMethod("apply").invoke(transaction);
                } catch (Exception error) { failure = error; }
                  catch (LinkageError error) { failure = error; }
            }
            // Each native reference is released independently, even if layer removal failed.
            if (surface != null) {
                try { surfaceClass.getMethod("release").invoke(surface); surface = null; }
                catch (Exception error) { if (failure == null) failure = error; }
                catch (LinkageError error) { if (failure == null) failure = error; }
            }
            if (control != null) {
                try { controlClass.getMethod("release").invoke(control); control = null; }
                catch (Exception error) { if (failure == null) failure = error; }
                catch (LinkageError error) { if (failure == null) failure = error; }
            }
            if (transaction != null) {
                try { transactionClass.getMethod("close").invoke(transaction); transaction = null; }
                catch (Exception error) { if (failure == null) failure = error; }
                catch (LinkageError error) { if (failure == null) failure = error; }
            }
            if (failure instanceof Exception) throw (Exception)failure;
            if (failure instanceof LinkageError) throw (LinkageError)failure;
        }
    }
}
